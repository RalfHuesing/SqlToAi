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
    private readonly IQuerySafetyValidator _querySafetyValidator;
    private readonly QueryExecutionOptions _options;
    private readonly ILogger<QueryComparisonService> _logger;

    /// <summary>Initializes a new instance of <see cref="QueryComparisonService"/>.</summary>
    public QueryComparisonService(
        IDatabaseConnectionFactory connectionFactory,
        IQuerySafetyValidator querySafetyValidator,
        IOptions<SqlToAiOptions> options,
        ILogger<QueryComparisonService> logger)
    {
        _connectionFactory = connectionFactory;
        _querySafetyValidator = querySafetyValidator;
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

        // 1-5. Run the shared 6-stage guardrail pipeline once per query — the pipeline is
        // single-query, so QueryComparisonService is the only consumer that calls it twice
        // (once for QueryA, once for QueryB) and short-circuits on the first failure.
        var safetyResultA = await _querySafetyValidator
            .ValidateQuerySafetyAsync(args.DatabaseName, args.QueryA, allowSchemaOnly: false, cancellationToken)
            .ConfigureAwait(false);
        if (safetyResultA.IsFailure)
        {
            return safetyResultA.Error;
        }

        var safetyResultB = await _querySafetyValidator
            .ValidateQuerySafetyAsync(args.DatabaseName, args.QueryB, allowSchemaOnly: false, cancellationToken)
            .ConfigureAwait(false);
        if (safetyResultB.IsFailure)
        {
            return safetyResultB.Error;
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
            connection, transaction, args, paramsA, paramsB, ct);

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

        var colsA = await GetSchemaColumnsAsync(connection, transaction, queryA, paramsA, ct);
        var colsB = await GetSchemaColumnsAsync(connection, transaction, queryB, paramsB, ct);

        if (colsA.Count != colsB.Count)
        {
            diffs.Add($"Column count mismatch: Query A has {colsA.Count} columns, Query B has {colsB.Count} columns.");
            return (false, diffs);
        }

        for (int i = 0; i < colsA.Count; i++)
        {
            var (nameA, typeA) = colsA[i];
            var (nameB, typeB) = colsB[i];

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

    private static async Task<List<(string Name, string Type)>> GetSchemaColumnsAsync(
        DbConnection connection,
        DbTransaction transaction,
        string query,
        object? parameters,
        CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = query;
        cmd.Transaction = transaction;
        SqlParameterBinder.BindParameters(cmd, parameters);

        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, ct);
        var list = new List<(string Name, string Type)>(reader.FieldCount);
        for (int i = 0; i < reader.FieldCount; i++)
        {
            list.Add((reader.GetName(i), reader.GetDataTypeName(i)));
        }
        return list;
    }

    private static async Task<long> ExecuteCountAsync(
        DbConnection connection,
        DbTransaction transaction,
        string query,
        object? parameters,
        CancellationToken ct)
    {
        var d = QueryDeconstructor.Deconstruct(query);
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(d.Preamble))
        {
            sb.AppendLine(d.Preamble);
        }
        if (!string.IsNullOrEmpty(d.Ctes))
        {
            sb.AppendLine(";" + d.Ctes);
        }
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT_BIG(*) FROM ({d.MainSelect}) AS SqlToAiCountSubQuery"));

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sb.ToString();
        cmd.Transaction = transaction;
        SqlParameterBinder.BindParameters(cmd, parameters);

        object? result = await cmd.ExecuteScalarAsync(ct);
        return result is long l ? l : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static async Task<(string RowsInANotB, string RowsInBNotA)> ExecuteExceptDiffsAsync(
        DbConnection connection,
        DbTransaction transaction,
        QueryComparisonArgs args,
        object? paramsA,
        object? paramsB,
        CancellationToken ct)
    {
        var dA = QueryDeconstructor.Deconstruct(args.QueryA);
        var dB = QueryDeconstructor.Deconstruct(args.QueryB);

        string preamble = QueryDeconstructor.CombinePreambles(dA.Preamble, dB.Preamble);
        string ctes = QueryDeconstructor.CombineCtes(dA.Ctes, dB.Ctes);

        var prefixSb = new StringBuilder();
        if (!string.IsNullOrEmpty(preamble))
        {
            prefixSb.AppendLine(preamble);
        }
        if (!string.IsNullOrEmpty(ctes))
        {
            prefixSb.AppendLine(";" + ctes);
        }
        string prefix = prefixSb.ToString();

        string sqlExceptAnotB = $"{prefix}SELECT * FROM ({dA.MainSelect}) AS QA EXCEPT SELECT * FROM ({dB.MainSelect}) AS QB";
        string sqlExceptBnotA = $"{prefix}SELECT * FROM ({dB.MainSelect}) AS QB EXCEPT SELECT * FROM ({dA.MainSelect}) AS QA";

        string diffAnotB = await ExecuteDiffQueryAsync(connection, transaction, sqlExceptAnotB, paramsA, paramsB, args.MaxDiffRows, ct);
        string diffBnotA = await ExecuteDiffQueryAsync(connection, transaction, sqlExceptBnotA, paramsB, paramsA, args.MaxDiffRows, ct);

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
