#nullable enable

using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
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
        if (SqlMultiStatementDetector.ContainsMultipleStatements(query))
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
            int baselineTranCount = await TransactionIntegrityGuard.GetTranCountAsync(connection, transaction, cancellationToken);

            Result<QueryExecutionResult> result;
            bool tranCountChanged;
            try
            {
                result = await ExecuteAndSerializeAsync(connection, transaction, query, effectiveLimit, anonymize, databaseName, cancellationToken);
                int tranCountAfterExecution = await TransactionIntegrityGuard.GetTranCountAsync(connection, transaction, cancellationToken);
                tranCountChanged = tranCountAfterExecution != baselineTranCount;
            }
            catch
            {
                // Roll back any partial state before the outer catch reports the error.
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }

            // A statement that itself alters the ambient transaction's state (e.g. an embedded
            // COMMIT deep inside dynamic SQL) breaks the isolation guarantee the whole
            // rollback-by-default design rests on — the result can no longer be trusted,
            // regardless of what keyword caused it, so it must never reach the caller.
            if (!writeAllowed && tranCountChanged)
            {
                return await TransactionIntegrityGuard.RejectViolationAsync(_logger, databaseName, transaction, cancellationToken);
            }

            // Only a write-allowed database persists changes; everything else stays a dry run
            // (guarantees zero side-effects even for accidental DML that slips through).
            if (writeAllowed)
            {
                // If the trancount already changed, the transaction is already gone (e.g.
                // committed by the statement itself) — nothing left to commit on a fully
                // authorized database; calling Commit again would only throw a confusing
                // "no transaction" error.
                if (!tranCountChanged)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
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
            // The log always gets the full, untouched exception message (and the already
            // detokenized query) — the admin has direct SQL Server access anyway and needs the
            // real values to verify reported errors. This is a deliberate, accepted design
            // choice and must never change (see audit-2026-07-24/01-security-guardrails.md,
            // Finding "Detokenisierte Klartextwerte leaken über Fehlerpfad").
            LogQueryFailed(_logger, databaseName, query, ex);

            // What goes back to the AI is different: for an anonymized/tokenized database,
            // `query` may contain a value that was just detokenized back to its real, cleartext
            // form (see ResolveTokens above), and SQL Server routinely quotes the offending
            // literal verbatim in ex.Message (e.g. type-conversion errors) — returning that
            // verbatim would leak the exact real values tokenization exists to hide. A
            // non-anonymized database has no such secrecy promise, so ex.Message keeps its
            // full diagnostic value there, unchanged.
            string? anonymizedMessage = anonymize ? BuildAnonymizedQueryErrorMessage(ex) : null;
            return SqlToAiErrorMapper.MapException(ex, anonymizedMessage);
        }
    }

    /// <summary>
    /// Builds a generic, non-quoting error message for an anonymized/tokenized database's query
    /// failure — deliberately never includes <c>ex.Message</c>'s free text, since SQL Server
    /// routinely embeds the offending literal value directly in it for common errors (e.g.
    /// type-conversion failures), which for a just-detokenized query could be a real PII value.
    /// </summary>
    private static string BuildAnonymizedQueryErrorMessage(Exception ex) => ex switch
    {
        SqlException sqlEx => $"(SQL error {sqlEx.Number}) the query failed during execution. Check syntax and column types; see server-side logs for details.",
        _ => "the query failed during execution.",
    };

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

    /// <summary>
    /// The real source of an output column, resolved once per ordinal via the reader's schema
    /// table (<c>BaseSchemaName</c>/<c>BaseTableName</c>/<c>BaseColumnName</c>) — never the query's
    /// output alias. Any part is null when the provider can't resolve it (e.g. unsupported
    /// provider, or a computed/literal/aggregate expression with no traceable source column).
    /// </summary>
    private sealed record ColumnOrigin(string? TableName, string? ColumnName, string? SchemaName);

    /// <summary>Bundles per-query anonymization context for passing between internal helpers.</summary>
    private sealed record AnonymizationContext(
        bool Anonymize,
        AnonymizerExclusionSet? Exclusions,
        ColumnOrigin?[]? ColumnOrigins,
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

        AnonymizerExclusionSet? exclusions = _anonymizerExclusionProvider != null
            ? await _anonymizerExclusionProvider.GetExclusionsAsync(databaseName, cancellationToken)
            : null;
        var columnOrigins = GetColumnOrigins(reader);
        bool[]? centralExclusions = _anonymizationRuleProvider != null
            ? await ResolveCentralExclusionsAsync(databaseName, columnNames, columnOrigins, cancellationToken)
            : null;

        return new AnonymizationContext(true, exclusions, columnOrigins, centralExclusions, _tokenizationOptions.IsUsable);
    }

    /// <summary>
    /// Resolves the central rule provider's exclusion decision once per column ordinal (not per
    /// row), so a 1000-row result only pays for N rule lookups instead of N × rowCount. Passes the
    /// resolved base schema alongside the base table, so a same-named table in a different schema
    /// never inherits a rule scoped to another schema.
    /// </summary>
    private async Task<bool[]> ResolveCentralExclusionsAsync(
        string databaseName, string[] columnNames, ColumnOrigin?[] columnOrigins, CancellationToken cancellationToken)
    {
        var result = new bool[columnNames.Length];
        for (int i = 0; i < columnNames.Length; i++)
        {
            ColumnOrigin? origin = i < columnOrigins.Length ? columnOrigins[i] : null;
            string tableName = origin?.TableName ?? string.Empty;
            string schemaName = origin?.SchemaName ?? string.Empty;
            result[i] = await _anonymizationRuleProvider!.IsExcludedAsync(databaseName, schemaName, tableName, columnNames[i], cancellationToken);
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

        ColumnOrigin? origin = anonCtx.ColumnOrigins != null && columnIndex < anonCtx.ColumnOrigins.Length ? anonCtx.ColumnOrigins[columnIndex] : null;
        string? tableName = origin?.TableName;
        var columnContext = new AnonymizationColumnContext(tableName, origin?.ColumnName, origin?.SchemaName, anonCtx.Exclusions);
        string anonymizedValue = anonCtx.UseTokenization
            ? _anonymizer.Tokenize(strVal, columnContext)
            : _anonymizer.Anonymize(strVal, columnContext);

        if (anonymizedValue != strVal)
        {
            // Qualify with the resolved base table when known, so the LLM (and the human it
            // reports to) can act on a concrete "TableName.ColumnName" instead of a bare alias.
            // Deliberately reports the alias (columnName), not the resolved origin column, since
            // the alias is the JSON key the AI actually sees in the result — only the exclusion
            // *decision* above is origin-based, not this display name.
            string qualifiedName = string.IsNullOrEmpty(tableName) ? columnName : $"{tableName}.{columnName}";
            tracker.RecordAnonymizedColumn(qualifiedName, anonCtx.UseTokenization);
        }

        return anonymizedValue;
    }

    private static bool IsFlagSet(bool[]? flags, int index) =>
        flags != null && index < flags.Length && flags[index];

    /// <summary>
    /// Resolves each output column's real source (base schema + base table + base column) via the
    /// reader's schema table, so the anonymization exclusion decision can be based on where a value
    /// actually comes from instead of the query's output alias (e.g. <c>SELECT SSN AS RecordId</c>
    /// must never be judged by the alias <c>RecordId</c>). The resolved schema lets two same-named
    /// tables in different schemas be told apart, so an exclusion/rule scoped to one schema never
    /// silently applies to the other. Tolerates providers where <see cref="DbDataReader.GetSchemaTable"/>
    /// is unavailable or incomplete — any column whose origin can't be determined simply gets a null
    /// <see cref="ColumnOrigin"/>, which the anonymizer then treats fail-safe (never excluded via
    /// the plain pattern list).
    /// </summary>
    private static ColumnOrigin?[] GetColumnOrigins(DbDataReader reader)
    {
        var origins = new ColumnOrigin?[reader.FieldCount];
        try
        {
            var schemaTable = reader.GetSchemaTable();
            if (schemaTable != null)
            {
                PopulateColumnOrigins(schemaTable, origins);
            }
        }
        catch (Exception ignored)
        {
            _ = ignored; // Safe fallback: schema table not available for this provider
        }
        return origins;
    }

    private static void PopulateColumnOrigins(DataTable schemaTable, ColumnOrigin?[] origins)
    {
        bool hasOrdinal = schemaTable.Columns.Contains("ColumnOrdinal");
        bool hasBaseTable = schemaTable.Columns.Contains("BaseTableName");
        bool hasBaseColumn = schemaTable.Columns.Contains("BaseColumnName");
        bool hasBaseSchema = schemaTable.Columns.Contains("BaseSchemaName");

        for (int i = 0; i < schemaTable.Rows.Count; i++)
        {
            var row = schemaTable.Rows[i];
            int ordinal = hasOrdinal ? Convert.ToInt32(row["ColumnOrdinal"], System.Globalization.CultureInfo.InvariantCulture) : i;
            if (ordinal < 0 || ordinal >= origins.Length)
            {
                continue;
            }

            origins[ordinal] = new ColumnOrigin(
                ReadOriginValue(row, "BaseTableName", hasBaseTable),
                ReadOriginValue(row, "BaseColumnName", hasBaseColumn),
                ReadOriginValue(row, "BaseSchemaName", hasBaseSchema));
        }
    }

    /// <summary>Reads a normalized (null-if-empty) schema table value, or null when the column itself is unavailable.</summary>
    private static string? ReadOriginValue(DataRow row, string columnName, bool columnAvailable)
    {
        if (!columnAvailable)
        {
            return null;
        }
        string? value = row[columnName]?.ToString();
        return string.IsNullOrEmpty(value) ? null : value;
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
}
