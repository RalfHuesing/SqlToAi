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
        var (preamble, body) = ExtractPreambleAndBody(query);
        string countSql = string.IsNullOrEmpty(preamble)
            ? $"SELECT COUNT_BIG(*) FROM ({body}) AS SqlToAiCountSubQuery"
            : $"{preamble}\nSELECT COUNT_BIG(*) FROM ({body}) AS SqlToAiCountSubQuery";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = countSql;
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
        var (preambleA, bodyA) = ExtractPreambleAndBody(args.QueryA);
        var (preambleB, bodyB) = ExtractPreambleAndBody(args.QueryB);
        string combinedPreamble = CombinePreambles(preambleA, preambleB);

        string sqlExceptAnotB = string.IsNullOrEmpty(combinedPreamble)
            ? $"SELECT * FROM ({bodyA}) AS QA EXCEPT SELECT * FROM ({bodyB}) AS QB"
            : $"{combinedPreamble}\nSELECT * FROM ({bodyA}) AS QA EXCEPT SELECT * FROM ({bodyB}) AS QB";

        string sqlExceptBnotA = string.IsNullOrEmpty(combinedPreamble)
            ? $"SELECT * FROM ({bodyB}) AS QB EXCEPT SELECT * FROM ({bodyA}) AS QA"
            : $"{combinedPreamble}\nSELECT * FROM ({bodyB}) AS QB EXCEPT SELECT * FROM ({bodyA}) AS QA";

        string diffAnotB = await ExecuteDiffQueryAsync(connection, transaction, sqlExceptAnotB, paramsA, paramsB, args.MaxDiffRows, ct);
        string diffBnotA = await ExecuteDiffQueryAsync(connection, transaction, sqlExceptBnotA, paramsB, paramsA, args.MaxDiffRows, ct);

        return (diffAnotB, diffBnotA);
    }

    private static (string Preamble, string Body) ExtractPreambleAndBody(string query)
    {
        var semicolonIndices = GetSemicolonIndices(query);
        if (semicolonIndices.Count == 0)
        {
            return (string.Empty, query.Trim());
        }

        var segments = GetSegmentsFromIndices(query, semicolonIndices);
        int lastNonEmptyIndex = GetLastNonEmptyIndex(segments);

        if (lastNonEmptyIndex <= 0)
        {
            return (string.Empty, query.Trim());
        }

        return BuildPreambleAndBody(segments, lastNonEmptyIndex);
    }

    private static List<int> GetSemicolonIndices(string query)
    {
        var indices = new List<int>();
        foreach (var ev in SqlCharScanner.Scan(query))
        {
            if (ev.State == SqlCharState.Normal && ev.Character == ';')
            {
                indices.Add(ev.Index);
            }
        }
        return indices;
    }

    private static List<string> GetSegmentsFromIndices(string query, List<int> semicolonIndices)
    {
        var segments = new List<string>();
        int lastIndex = 0;
        foreach (int idx in semicolonIndices)
        {
            segments.Add(query[lastIndex..idx]);
            lastIndex = idx + 1;
        }
        if (lastIndex <= query.Length)
        {
            segments.Add(query[lastIndex..]);
        }
        return segments;
    }

    private static int GetLastNonEmptyIndex(List<string> segments)
    {
        int index = segments.Count - 1;
        while (index >= 0 && string.IsNullOrWhiteSpace(segments[index]))
        {
            index--;
        }
        return index;
    }

    private static (string Preamble, string Body) BuildPreambleAndBody(List<string> segments, int lastNonEmptyIndex)
    {
        var preambleParts = new List<string>();
        for (int i = 0; i < lastNonEmptyIndex; i++)
        {
            if (!string.IsNullOrWhiteSpace(segments[i]))
            {
                preambleParts.Add(segments[i].Trim() + ";");
            }
        }

        var bodyParts = new List<string>();
        for (int i = lastNonEmptyIndex; i < segments.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(segments[i]))
            {
                bodyParts.Add(segments[i].Trim());
            }
        }

        return (string.Join("\n", preambleParts), string.Join(";\n", bodyParts));
    }

    private static string CombinePreambles(string preambleA, string preambleB)
    {
        if (string.IsNullOrWhiteSpace(preambleA)) return preambleB;
        if (string.IsNullOrWhiteSpace(preambleB)) return preambleA;

        var lines = new List<string>();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in new[] { preambleA, preambleB })
        {
            var parts = p.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (!string.IsNullOrWhiteSpace(part) && set.Add(part))
                {
                    lines.Add(part + ";");
                }
            }
        }

        return string.Join("\n", lines);
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
