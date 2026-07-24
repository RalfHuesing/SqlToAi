#nullable enable

using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Mcp;
using SqlToAi.Security;

namespace SqlToAi.Database;

/// <summary>
/// Bundles the anonymization dependencies for <see cref="QueryExecutionService"/> to keep
/// the constructor parameter count within architectural limits.
/// </summary>
/// <param name="Anonymizer">The anonymizer used for PII column masking.</param>
/// <param name="ExclusionProvider">Optional provider that returns table/column exclusions for anonymization.</param>
/// <param name="RuleProvider">Optional central, cross-database rule provider (see <see cref="IAnonymizationRuleProvider"/>).</param>
/// <param name="TokenResolver">Optional resolver that substitutes previously issued anonymization tokens back into their real values before a query executes (see <see cref="IQueryTokenResolver"/>).</param>
public sealed record AnonymizationDependencies(
    IAnonymizer Anonymizer,
    IAnonymizerExclusionProvider? ExclusionProvider = null,
    IAnonymizationRuleProvider? RuleProvider = null,
    IQueryTokenResolver? TokenResolver = null);

/// <summary>
/// Executes a single SQL statement. For every database except those at
/// <see cref="AccessLevel.ReadWrite"/>, the statement runs inside an explicit rollback
/// transaction and mutating keywords are rejected outright. Applies row limits and on-the-fly
/// PII anonymization for <see cref="AccessLevel.ReadOnlyAnonymized"/> databases.
/// </summary>
public sealed class QueryExecutionService : IQueryExecutionService
{
    private static readonly Action<ILogger, string, string, Exception?> LogQueryFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1, "QueryFailed"),
            "Query execution failed for database {Database}. Query: {Query}");

    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly ISecurityGuard _securityGuard;
    private readonly IAccessLevelProvider _accessLevelProvider;
    private readonly IReadOnlyGuard _readOnlyGuard;
    private readonly IAnonymizer _anonymizer;
    private readonly IAnonymizerExclusionProvider? _anonymizerExclusionProvider;
    private readonly IAnonymizationRuleProvider? _anonymizationRuleProvider;
    private readonly IQueryTokenResolver? _queryTokenResolver;
    private readonly QueryExecutionOptions _options;
    private readonly string _anonymizationMode;
    private readonly TokenizationOptions _tokenizationOptions;
    private readonly ILogger<QueryExecutionService> _logger;

    /// <summary>Initializes a new instance of <see cref="QueryExecutionService"/>.</summary>
    public QueryExecutionService(
        IDatabaseConnectionFactory connectionFactory,
        ISecurityGuard securityGuard,
        IAccessLevelProvider accessLevelProvider,
        IReadOnlyGuard readOnlyGuard,
        AnonymizationDependencies anonymization,
        IOptions<SqlToAiOptions> options,
        ILogger<QueryExecutionService> logger)
    {
        _connectionFactory = connectionFactory;
        _securityGuard = securityGuard;
        _accessLevelProvider = accessLevelProvider;
        _readOnlyGuard = readOnlyGuard;
        _anonymizer = anonymization.Anonymizer;
        _anonymizerExclusionProvider = anonymization.ExclusionProvider;
        _anonymizationRuleProvider = anonymization.RuleProvider;
        _queryTokenResolver = anonymization.TokenResolver;
        _options = options.Value.QueryExecution;
        _anonymizationMode = options.Value.Anonymizer.DefaultMode;
        _tokenizationOptions = options.Value.Anonymizer.Tokenization;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<QueryExecutionResult>> ExecuteQueryAsync(
        string databaseName,
        string query,
        int? requestedRowLimit,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate inputs
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return SqlToAiError.InvalidParameters("Database name must not be empty.");
        }
        if (string.IsNullOrWhiteSpace(query))
        {
            return SqlToAiError.InvalidParameters("Query must not be empty.");
        }

        // 2. Static whitelist check
        if (!_securityGuard.IsDatabaseAllowed(databaseName))
        {
            return SqlToAiError.SafetyCheckFailed(databaseName);
        }

        // 3. Dynamic access level check
        var accessLevel = await _accessLevelProvider.GetAccessLevelAsync(databaseName, cancellationToken);

        if (accessLevel == AccessLevel.None || accessLevel == AccessLevel.SchemaOnly)
        {
            return SqlToAiError.WriteOperationBlocked($"Database '{databaseName}' does not permit query execution (AccessLevel: {accessLevel}).");
        }

        // 4. Read-only guard: reject mutating statements, unless this database is fully
        //    unlocked via AccessCheckSql returning ReadWrite.
        bool writeAllowed = accessLevel == AccessLevel.ReadWrite;

        if (!writeAllowed && !_readOnlyGuard.IsQuerySafe(query))
        {
            return SqlToAiError.WriteOperationBlocked("The query contains mutating SQL keywords and was rejected.");
        }

        // 5. Single-statement validation — always enforced, write-allowed or not, to keep
        //    the blast radius of a single call limited to one statement.
        if (ContainsMultipleStatements(query))
        {
            return SqlToAiError.MultipleStatementsForbidden();
        }

        // 6. Resolve effective row limit (caller request capped by configured maximum)
        int effectiveLimit = requestedRowLimit.HasValue
            ? Math.Min(requestedRowLimit.Value, _options.MaxRowLimit)
            : _options.DefaultRowLimit;

        bool anonymize = accessLevel == AccessLevel.ReadOnlyAnonymized;

        // Detokenization runs only for anonymized databases (the only place tokens are ever
        // issued) and after the safety/multi-statement checks above, so it substitutes literal
        // values only — it never changes the query's structure.
        string effectiveQuery = anonymize && _queryTokenResolver != null
            ? _queryTokenResolver.ResolveTokens(query)
            : query;

        return await ExecuteQueryInTransactionAsync(
            databaseName, effectiveQuery, effectiveLimit, anonymize, writeAllowed, cancellationToken);
    }

    private async Task<Result<QueryExecutionResult>> ExecuteQueryInTransactionAsync(
        string databaseName,
        string query,
        int effectiveLimit,
        bool anonymize,
        bool writeAllowed,
        CancellationToken cancellationToken)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            Result<QueryExecutionResult> result;
            try
            {
                result = await ExecuteAndSerializeAsync(connection, transaction, query, effectiveLimit, anonymize, databaseName, cancellationToken);
            }
            catch
            {
                // Roll back any partial state before the outer catch reports the error.
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }

            // Only a write-allowed database persists changes; everything else stays a dry run
            // (guarantees zero side-effects even for accidental DML that slips through).
            if (writeAllowed)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogQueryFailed(_logger, databaseName, query, ex);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task<Result<QueryExecutionResult>> ExecuteAndSerializeAsync(
        DbConnection connection,
        DbTransaction transaction,
        string query,
        int rowLimit,
        bool anonymize,
        string databaseName,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = query;
        command.Transaction = transaction;
        command.CommandTimeout = 0; // governed by caller's CancellationToken

        using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.KeyInfo, cancellationToken);

        var columnNames = GetColumnNames(reader);
        var anonCtx = await ResolveAnonymizationContextAsync(reader, columnNames, anonymize, databaseName, cancellationToken);

        var sb = new StringBuilder();
        int rowCount = 0;
        var tracker = new RowAnonymizationTracker();

        while (rowCount < rowLimit && await reader.ReadAsync(cancellationToken))
        {
            AppendSerializedRow(sb, reader, columnNames, anonCtx, tracker);
            rowCount++;
        }

        if (rowCount == 0)
        {
            return new QueryExecutionResult("[]", false, Array.Empty<string>(), _anonymizationMode);
        }

        return new QueryExecutionResult(
            sb.ToString().TrimEnd(), tracker.WasAnonymized, tracker.AnonymizedColumns, _anonymizationMode, tracker.SearchableTokenColumns);
    }

    /// <summary>Accumulates per-row anonymization outcomes while serializing a result set.</summary>
    private sealed class RowAnonymizationTracker
    {
        public bool WasAnonymized { get; set; }
        public List<string> AnonymizedColumns { get; } = [];
        public List<string> SearchableTokenColumns { get; } = [];

        public void RecordAnonymizedColumn(string qualifiedName, bool searchable)
        {
            WasAnonymized = true;
            if (!AnonymizedColumns.Contains(qualifiedName))
            {
                AnonymizedColumns.Add(qualifiedName);
            }
            if (searchable && !SearchableTokenColumns.Contains(qualifiedName))
            {
                SearchableTokenColumns.Add(qualifiedName);
            }
        }
    }

    /// <summary>Bundles per-query anonymization context for passing between internal helpers.</summary>
    private sealed record AnonymizationContext(
        bool Anonymize,
        HashSet<string>? Exclusions,
        string?[]? BaseTableNames,
        bool[]? CentralExclusions,
        bool UseTokenization);

    private async Task<AnonymizationContext> ResolveAnonymizationContextAsync(
        DbDataReader reader,
        string[] columnNames,
        bool anonymize,
        string databaseName,
        CancellationToken cancellationToken)
    {
        if (!anonymize)
        {
            return new AnonymizationContext(false, null, null, null, false);
        }

        HashSet<string>? exclusions = _anonymizerExclusionProvider != null
            ? await _anonymizerExclusionProvider.GetExclusionsAsync(databaseName, cancellationToken)
            : null;
        var baseTableNames = GetBaseTableNames(reader);
        bool[]? centralExclusions = _anonymizationRuleProvider != null
            ? await ResolveCentralExclusionsAsync(databaseName, columnNames, baseTableNames, cancellationToken)
            : null;

        return new AnonymizationContext(true, exclusions, baseTableNames, centralExclusions, _tokenizationOptions.IsUsable);
    }

    /// <summary>
    /// Resolves the central rule provider's exclusion decision once per column ordinal (not per
    /// row), so a 1000-row result only pays for N rule lookups instead of N × rowCount.
    /// </summary>
    private async Task<bool[]> ResolveCentralExclusionsAsync(
        string databaseName, string[] columnNames, string?[] baseTableNames, CancellationToken cancellationToken)
    {
        var result = new bool[columnNames.Length];
        for (int i = 0; i < columnNames.Length; i++)
        {
            string tableName = i < baseTableNames.Length ? baseTableNames[i] ?? string.Empty : string.Empty;
            result[i] = await _anonymizationRuleProvider!.IsExcludedAsync(databaseName, tableName, columnNames[i], cancellationToken);
        }
        return result;
    }

    private void AppendSerializedRow(
        StringBuilder sb,
        DbDataReader reader,
        string[] columnNames,
        AnonymizationContext anonCtx,
        RowAnonymizationTracker tracker)
    {
        var rowDict = new Dictionary<string, object?>(columnNames.Length, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < columnNames.Length; i++)
        {
            object? raw = reader.IsDBNull(i) ? null : reader.GetValue(i);
            raw = AnonymizeCell(columnNames[i], raw, anonCtx, i, tracker);
            rowDict[columnNames[i]] = raw;
        }

        sb.AppendLine(JsonSerializer.Serialize(rowDict, typeof(Dictionary<string, object?>), McpJsonContext.Default));
    }

    private object? AnonymizeCell(
        string columnName,
        object? raw,
        AnonymizationContext anonCtx,
        int columnIndex,
        RowAnonymizationTracker tracker)
    {
        if (!anonCtx.Anonymize || raw is not string strVal)
        {
            return raw;
        }

        if (IsFlagSet(anonCtx.CentralExclusions, columnIndex))
        {
            return raw;
        }

        string? tableName = anonCtx.BaseTableNames != null && columnIndex < anonCtx.BaseTableNames.Length ? anonCtx.BaseTableNames[columnIndex] : null;
        string anonymizedValue = anonCtx.UseTokenization
            ? _anonymizer.Tokenize(columnName, strVal, tableName, anonCtx.Exclusions)
            : _anonymizer.Anonymize(columnName, strVal, tableName, anonCtx.Exclusions);

        if (anonymizedValue != strVal)
        {
            // Qualify with the resolved base table when known, so the LLM (and the human it
            // reports to) can act on a concrete "TableName.ColumnName" instead of a bare alias.
            string qualifiedName = string.IsNullOrEmpty(tableName) ? columnName : $"{tableName}.{columnName}";
            tracker.RecordAnonymizedColumn(qualifiedName, anonCtx.UseTokenization);
        }

        return anonymizedValue;
    }

    private static bool IsFlagSet(bool[]? flags, int index) =>
        flags != null && index < flags.Length && flags[index];

    private static string?[] GetBaseTableNames(DbDataReader reader)
    {
        var names = new string?[reader.FieldCount];
        try
        {
            var schemaTable = reader.GetSchemaTable();
            if (schemaTable != null)
            {
                bool hasOrdinal = schemaTable.Columns.Contains("ColumnOrdinal");
                bool hasBaseTable = schemaTable.Columns.Contains("BaseTableName");

                for (int i = 0; i < schemaTable.Rows.Count; i++)
                {
                    var row = schemaTable.Rows[i];
                    int ordinal = hasOrdinal ? Convert.ToInt32(row["ColumnOrdinal"], System.Globalization.CultureInfo.InvariantCulture) : i;

                    if (ordinal >= 0 && ordinal < names.Length && hasBaseTable)
                    {
                        names[ordinal] = row["BaseTableName"]?.ToString();
                    }
                }
            }
        }
        catch (Exception ignored)
        {
            _ = ignored; // Safe fallback: schema table not available for this provider
        }
        return names;
    }

    private static string[] GetColumnNames(DbDataReader reader)
    {
        var names = new string[reader.FieldCount];
        for (int i = 0; i < reader.FieldCount; i++)
        {
            names[i] = reader.GetName(i);
        }
        return names;
    }

    /// <summary>
    /// Detects multiple SQL statements by scanning for semicolons outside
    /// string literals (<c>'...'</c>), bracket identifiers (<c>[...]</c>), and comments (<c>--</c>, <c>/* */</c>).
    /// </summary>
    private enum SqlParserState
    {
        Normal,
        LineComment,
        BlockComment,
        SingleQuote,
        Bracket
    }

    private static bool ContainsMultipleStatements(string query)
    {
        var state = SqlParserState.Normal;
        ReadOnlySpan<char> span = query.AsSpan();

        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            char next = i + 1 < span.Length ? span[i + 1] : '\0';

            state = Transition(state, c, next, ref i);

            if (state == SqlParserState.Normal && c == ';')
            {
                // Allow trailing semicolon at end (after trimming whitespace)
                string remaining = query[(i + 1)..].TrimEnd();
                if (remaining.Length > 0)
                {
                    return true; // text after semicolon → second statement
                }
            }
        }

        return false;
    }

    private static SqlParserState Transition(SqlParserState state, char c, char next, ref int i)
    {
        switch (state)
        {
            case SqlParserState.LineComment:
                if (c == '\n') return SqlParserState.Normal;
                return SqlParserState.LineComment;

            case SqlParserState.BlockComment:
                if (c == '*' && next == '/')
                {
                    i++; // skip '/'
                    return SqlParserState.Normal;
                }
                return SqlParserState.BlockComment;

            case SqlParserState.SingleQuote:
                if (c == '\'' && next == '\'')
                {
                    i++; // escaped quote
                    return SqlParserState.SingleQuote;
                }
                if (c == '\'') return SqlParserState.Normal;
                return SqlParserState.SingleQuote;

            case SqlParserState.Bracket:
                if (c == ']') return SqlParserState.Normal;
                return SqlParserState.Bracket;

            default:
                return TransitionFromNormal(c, next, ref i);
        }
    }

    private static SqlParserState TransitionFromNormal(char c, char next, ref int i)
    {
        if (c == '-' && next == '-')
        {
            i++;
            return SqlParserState.LineComment;
        }
        if (c == '/' && next == '*')
        {
            i++;
            return SqlParserState.BlockComment;
        }
        if (c == '\'') return SqlParserState.SingleQuote;
        if (c == '[') return SqlParserState.Bracket;
        return SqlParserState.Normal;
    }
}
