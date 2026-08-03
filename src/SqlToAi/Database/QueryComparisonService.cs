#nullable enable

using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Database;

/// <summary>
/// Service for comparing two SQL queries on a target database for schema, count, and content equivalence.
/// Executes DB-side set differences (<c>EXCEPT</c>) to avoid streaming large result sets to the client.
/// </summary>
public sealed class QueryComparisonService : IQueryComparisonService
{
    private static readonly Action<ILogger, string, Exception?> LogComparisonFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, "ComparisonFailed"),
            "Query comparison failed for database {Database}.");

    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly ISecurityGuard _securityGuard;
    private readonly IAccessLevelProvider _accessLevelProvider;
    private readonly IReadOnlyGuard _readOnlyGuard;
    private readonly QueryExecutionOptions _options;
    private readonly ILogger<QueryComparisonService> _logger;

    /// <summary>Initializes a new instance of <see cref="QueryComparisonService"/>.</summary>
    public QueryComparisonService(
        IDatabaseConnectionFactory connectionFactory,
        ISecurityGuard securityGuard,
        IAccessLevelProvider accessLevelProvider,
        IReadOnlyGuard readOnlyGuard,
        IOptions<SqlToAiOptions> options,
        ILogger<QueryComparisonService> logger)
    {
        _connectionFactory = connectionFactory;
        _securityGuard = securityGuard;
        _accessLevelProvider = accessLevelProvider;
        _readOnlyGuard = readOnlyGuard;
        _options = options.Value.QueryExecution;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<Result<QueryComparisonResult>> CompareQueriesAsync(
        string databaseName,
        string queryA,
        string queryB,
        CancellationToken cancellationToken = default)
    {
        return CompareQueriesAsync(new QueryComparisonArgs(databaseName, queryA, queryB), cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<QueryComparisonResult>> CompareQueriesAsync(
        QueryComparisonArgs args,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateArgs(args);
        if (validationError != null)
        {
            return validationError;
        }

        if (!_securityGuard.IsDatabaseAllowed(args.DatabaseName))
        {
            return SqlToAiError.SafetyCheckFailed(args.DatabaseName);
        }

        var accessLevel = await _accessLevelProvider.GetAccessLevelAsync(args.DatabaseName, cancellationToken);
        var guardError = ValidateSecurityGuards(args, accessLevel);
        if (guardError != null)
        {
            return guardError;
        }

        object? effectiveParamsA = args.ParametersA ?? args.SharedParameters;
        object? effectiveParamsB = args.ParametersB ?? args.SharedParameters;
        int effectiveMaxDiff = Math.Clamp(args.MaxDiffRows, 1, _options.MaxRowLimit);

        try
        {
            using var connection = _connectionFactory.CreateConnection(args.DatabaseName);
            await connection.OpenAsync(cancellationToken);

            using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

            try
            {
                var comparisonResult = await PerformComparisonAsync(
                    connection, transaction, args, effectiveParamsA, effectiveParamsB, effectiveMaxDiff, cancellationToken);

                await transaction.RollbackAsync(cancellationToken);
                return comparisonResult;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogComparisonFailed(_logger, args.DatabaseName, ex);
            return SqlToAiErrorMapper.MapException(ex);
        }
    }

    private static SqlToAiError? ValidateArgs(QueryComparisonArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.DatabaseName))
        {
            return SqlToAiError.InvalidParameters("Database name must not be empty.");
        }
        if (string.IsNullOrWhiteSpace(args.QueryA) || string.IsNullOrWhiteSpace(args.QueryB))
        {
            return SqlToAiError.InvalidParameters("Both Query A and Query B must be specified.");
        }
        return null;
    }

    private SqlToAiError? ValidateSecurityGuards(QueryComparisonArgs args, AccessLevel accessLevel)
    {
        if (accessLevel == AccessLevel.None || accessLevel == AccessLevel.SchemaOnly)
        {
            return SqlToAiError.WriteOperationBlocked($"Database '{args.DatabaseName}' does not permit query comparison (AccessLevel: {accessLevel}).");
        }

        bool writeAllowed = accessLevel == AccessLevel.ReadWrite;
        if (!writeAllowed && (!_readOnlyGuard.IsQuerySafe(args.QueryA) || !_readOnlyGuard.IsQuerySafe(args.QueryB)))
        {
            return SqlToAiError.WriteOperationBlocked("One or both queries contain mutating SQL keywords and were rejected.");
        }

        if (SqlMultiStatementDetector.ContainsMultipleStatements(args.QueryA) || SqlMultiStatementDetector.ContainsMultipleStatements(args.QueryB))
        {
            return SqlToAiError.MultipleStatementsForbidden();
        }

        return null;
    }

    private static async Task<QueryComparisonResult> PerformComparisonAsync(
        DbConnection connection,
        DbTransaction transaction,
        QueryComparisonArgs args,
        object? paramsA,
        object? paramsB,
        int maxDiffRows,
        CancellationToken ct)
    {
        var (schemaMatch, schemaDiffs) = await CompareSchemasAsync(connection, transaction, args.QueryA, args.QueryB, paramsA, paramsB, ct);

        long countA = await ExecuteCountAsync(connection, transaction, args.QueryA, paramsA, ct);
        long countB = await ExecuteCountAsync(connection, transaction, args.QueryB, paramsB, ct);
        bool countMatch = countA == countB;

        if (!schemaMatch)
        {
            return new QueryComparisonResult(
                IsEqual: false,
                SchemaMatch: false,
                CountMatch: countMatch,
                RowCountA: countA,
                RowCountB: countB,
                SchemaDifferences: schemaDiffs,
                RowsInANotInB: "[]",
                RowsInBNotInA: "[]");
        }

        var (rowsInANotB, rowsInBNotA) = await ExecuteExceptDiffsAsync(
            connection, transaction, args.QueryA, args.QueryB, paramsA, paramsB, maxDiffRows, ct);

        bool isEqual = countMatch && rowsInANotB == "[]" && rowsInBNotA == "[]";

        return new QueryComparisonResult(
            IsEqual: isEqual,
            SchemaMatch: true,
            CountMatch: countMatch,
            RowCountA: countA,
            RowCountB: countB,
            SchemaDifferences: Array.Empty<string>(),
            RowsInANotInB: rowsInANotB,
            RowsInBNotInA: rowsInBNotA);
    }

    private static async Task<(bool Match, List<string> Diffs)> CompareSchemasAsync(
        DbConnection connection,
        DbTransaction transaction,
        string queryA,
        string queryB,
        object? paramsA,
        object? paramsB,
        CancellationToken ct)
    {
        var diffs = new List<string>();

        using var cmdA = connection.CreateCommand();
        cmdA.CommandText = queryA;
        cmdA.Transaction = transaction;
        SqlParameterBinder.BindParameters(cmdA, paramsA);
        using var readerA = await cmdA.ExecuteReaderAsync(CommandBehavior.SchemaOnly, ct);

        using var cmdB = connection.CreateCommand();
        cmdB.CommandText = queryB;
        cmdB.Transaction = transaction;
        SqlParameterBinder.BindParameters(cmdB, paramsB);
        using var readerB = await cmdB.ExecuteReaderAsync(CommandBehavior.SchemaOnly, ct);

        if (readerA.FieldCount != readerB.FieldCount)
        {
            diffs.Add($"Column count mismatch: Query A has {readerA.FieldCount} columns, Query B has {readerB.FieldCount} columns.");
            return (false, diffs);
        }

        for (int i = 0; i < readerA.FieldCount; i++)
        {
            string nameA = readerA.GetName(i);
            string nameB = readerB.GetName(i);
            string typeA = readerA.GetDataTypeName(i);
            string typeB = readerB.GetDataTypeName(i);

            if (!string.Equals(nameA, nameB, StringComparison.OrdinalIgnoreCase))
            {
                diffs.Add($"Column {i + 1} name mismatch: Query A='{nameA}', Query B='{nameB}'.");
            }
            if (!string.Equals(typeA, typeB, StringComparison.OrdinalIgnoreCase))
            {
                diffs.Add($"Column {i + 1} type mismatch: Query A='{typeA}', Query B='{typeB}'.");
            }
        }

        return (diffs.Count == 0, diffs);
    }

    private static async Task<long> ExecuteCountAsync(
        DbConnection connection,
        DbTransaction transaction,
        string query,
        object? parameters,
        CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT_BIG(*) FROM ({query}) AS SqlToAiCountSubQuery";
        cmd.Transaction = transaction;
        SqlParameterBinder.BindParameters(cmd, parameters);

        object? result = await cmd.ExecuteScalarAsync(ct);
        return result is long l ? l : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static async Task<(string RowsInANotB, string RowsInBNotA)> ExecuteExceptDiffsAsync(
        DbConnection connection,
        DbTransaction transaction,
        string queryA,
        string queryB,
        object? paramsA,
        object? paramsB,
        int maxDiffRows,
        CancellationToken ct)
    {
        string sqlExceptAnotB = $"SELECT * FROM ({queryA}) AS QA EXCEPT SELECT * FROM ({queryB}) AS QB";
        string sqlExceptBnotA = $"SELECT * FROM ({queryB}) AS QB EXCEPT SELECT * FROM ({queryA}) AS QA";

        string diffAnotB = await ExecuteDiffQueryAsync(connection, transaction, sqlExceptAnotB, paramsA, paramsB, maxDiffRows, ct);
        string diffBnotA = await ExecuteDiffQueryAsync(connection, transaction, sqlExceptBnotA, paramsB, paramsA, maxDiffRows, ct);

        return (diffAnotB, diffBnotA);
    }

    private static async Task<string> ExecuteDiffQueryAsync(
        DbConnection connection,
        DbTransaction transaction,
        string exceptQuery,
        object? primaryParams,
        object? secondaryParams,
        int maxDiffRows,
        CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = exceptQuery;
        cmd.Transaction = transaction;

        SqlParameterBinder.BindParameters(cmd, primaryParams);
        if (secondaryParams != null && secondaryParams != primaryParams)
        {
            SqlParameterBinder.BindParameters(cmd, secondaryParams);
        }

        using var reader = await cmd.ExecuteReaderAsync(ct);

        var names = new string[reader.FieldCount];
        for (int i = 0; i < reader.FieldCount; i++)
        {
            names[i] = reader.GetName(i);
        }

        var sb = new StringBuilder();
        int count = 0;

        while (count < maxDiffRows && await reader.ReadAsync(ct))
        {
            var rowDict = new Dictionary<string, object?>(names.Length, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < names.Length; i++)
            {
                rowDict[names[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            sb.AppendLine(JsonSerializer.Serialize(rowDict, typeof(Dictionary<string, object?>), SqlToAi.Mcp.McpJsonContext.Default));
            count++;
        }

        return count == 0 ? "[]" : sb.ToString().TrimEnd();
    }
}
