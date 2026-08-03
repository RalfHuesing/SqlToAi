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
    IAnonymizationRuleProvider? RuleProvider = null,
    IQueryTokenResolver? TokenResolver = null);

/// <summary>
/// Executes a single SQL statement. For every database except those at
/// <see cref="AccessLevel.ReadWrite"/>, the statement runs inside an explicit rollback
/// transaction and mutating keywords are rejected outright. Applies row limits and on-the-fly
/// PII anonymization for <see cref="AccessLevel.ReadOnlyAnonymized"/> databases.
/// </summary>
public sealed partial class QueryExecutionService : IQueryExecutionService
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
        _anonymizationRuleProvider = anonymization.RuleProvider;
        _queryTokenResolver = anonymization.TokenResolver;
        _options = options.Value.QueryExecution;
        _anonymizationMode = options.Value.Anonymizer.DefaultMode;
        _tokenizationOptions = options.Value.Anonymizer.Tokenization;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<Result<QueryExecutionResult>> ExecuteQueryAsync(
        string databaseName,
        string query,
        int? requestedRowLimit,
        CancellationToken cancellationToken = default)
    {
        return ExecuteQueryAsync(databaseName, query, requestedRowLimit, parameters: null, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<QueryExecutionResult>> ExecuteQueryAsync(
        string databaseName,
        string query,
        int? requestedRowLimit,
        object? parameters,
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
            databaseName, effectiveQuery, effectiveLimit, anonymize, writeAllowed, parameters, cancellationToken);
    }

    private async Task<Result<QueryExecutionResult>> ExecuteQueryInTransactionAsync(
        string databaseName,
        string query,
        int effectiveLimit,
        bool anonymize,
        bool writeAllowed,
        object? parameters,
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
                var execArgs = new ExecutionArgs(connection, transaction, query, effectiveLimit, anonymize, databaseName, parameters);
                result = await ExecuteAndSerializeAsync(execArgs, cancellationToken);
                int tranCountAfterExecution = await TransactionIntegrityGuard.GetTranCountAsync(connection, transaction, cancellationToken);
                tranCountChanged = tranCountAfterExecution != baselineTranCount;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }

            if (!writeAllowed && tranCountChanged)
            {
                return await TransactionIntegrityGuard.RejectViolationAsync(_logger, databaseName, transaction, cancellationToken);
            }

            if (writeAllowed)
            {
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
            LogQueryFailed(_logger, databaseName, query, ex);
            string? anonymizedMessage = anonymize ? BuildAnonymizedQueryErrorMessage(ex) : null;
            return SqlToAiErrorMapper.MapException(ex, anonymizedMessage);
        }
    }

    private static string BuildAnonymizedQueryErrorMessage(Exception ex) => ex switch
    {
        SqlException sqlEx => $"(SQL error {sqlEx.Number}) the query failed during execution. Check syntax and column types; see server-side logs for details.",
        _ => "the query failed during execution.",
    };

    private sealed record ExecutionArgs(
        DbConnection Connection,
        DbTransaction Transaction,
        string Query,
        int RowLimit,
        bool Anonymize,
        string DatabaseName,
        object? Parameters);

    private async Task<Result<QueryExecutionResult>> ExecuteAndSerializeAsync(
        ExecutionArgs args,
        CancellationToken cancellationToken)
    {
        var messages = new List<string>();
        if (args.Connection is SqlConnection sqlConn)
        {
            sqlConn.InfoMessage += (_, e) => messages.Add(e.Message);
        }
        await ExecuteSetOptionAsync(args.Connection, args.Transaction, "SET STATISTICS IO ON", cancellationToken);
        await ExecuteSetOptionAsync(args.Connection, args.Transaction, "SET STATISTICS TIME ON", cancellationToken);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        using var command = args.Connection.CreateCommand();
        command.CommandText = args.Query;
        command.Transaction = args.Transaction;
        command.CommandTimeout = 0;
        SqlParameterBinder.BindParameters(command, args.Parameters);

        using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.KeyInfo, cancellationToken);

        var columnNames = GetColumnNames(reader);
        var anonCtx = await ResolveAnonymizationContextAsync(reader, columnNames, args.Anonymize, args.DatabaseName, cancellationToken);

        var sb = new StringBuilder();
        int rowCount = 0;
        var tracker = new RowAnonymizationTracker();

        while (rowCount < args.RowLimit && await reader.ReadAsync(cancellationToken))
        {
            AppendSerializedRow(sb, reader, columnNames, anonCtx, tracker);
            rowCount++;
        }

        stopwatch.Stop();

        var (cpu, _, logical, _, _, _) = PerformanceMetricsCalculator.ParseRunMessages(messages);

        if (rowCount == 0)
        {
            return new QueryExecutionResult("[]", false, Array.Empty<string>(), _anonymizationMode, ElapsedMs: stopwatch.ElapsedMilliseconds, RowCount: 0, CpuTimeMs: cpu, LogicalReads: logical);
        }

        return new QueryExecutionResult(
            sb.ToString().TrimEnd(), tracker.WasAnonymized, tracker.AnonymizedColumns, _anonymizationMode, tracker.SearchableTokenColumns,
            ElapsedMs: stopwatch.ElapsedMilliseconds, RowCount: rowCount, CpuTimeMs: cpu, LogicalReads: logical);
    }

    /// <summary>
    /// Executes a single <c>SET ...</c> statement on the given connection/transaction. Mirrors
    /// the identical helper in <see cref="PerformanceMeasurementService"/> — deliberately
    /// duplicated locally rather than shared, since the method is too small to justify coupling
    /// the two service classes together (see step-002 JIT context).
    /// </summary>
    private static async Task ExecuteSetOptionAsync(DbConnection connection, DbTransaction transaction, string sql, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = transaction;
        await cmd.ExecuteNonQueryAsync(ct);
    }

}
