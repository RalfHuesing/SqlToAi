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
/// Executes a single SQL statement with row limits. Writable access levels commit changes;
/// read-only levels reject mutations and roll back. Anonymized access levels mask string results.
/// </summary>
public sealed partial class QueryExecutionService : IQueryExecutionService, IQueryBatchExecutor
{
    private static readonly Action<ILogger, string, string, Exception?> LogQueryFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1, "QueryFailed"),
            "Query execution failed for database {Database}. Query: {Query}");

    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly IQuerySafetyValidator _querySafetyValidator;
    private readonly IAnonymizer _anonymizer;
    private readonly IAnonymizationRuleProvider? _anonymizationRuleProvider;
    private readonly IQueryTokenResolver? _queryTokenResolver;
    private readonly QueryExecutionOptions _options;
    private readonly string _anonymizationMode;
    private readonly TokenizationOptions _tokenizationOptions;
    private readonly DatabaseTargetResolver _targets;
    private readonly ILogger<QueryExecutionService> _logger;

    /// <summary>Initializes a new instance of <see cref="QueryExecutionService"/>.</summary>
    public QueryExecutionService(
        IDatabaseConnectionFactory connectionFactory,
        IQuerySafetyValidator querySafetyValidator,
        AnonymizationDependencies anonymization,
        IOptions<SqlToAiOptions> options,
        ILogger<QueryExecutionService> logger)
    {
        _connectionFactory = connectionFactory;
        _querySafetyValidator = querySafetyValidator;
        _anonymizer = anonymization.Anonymizer;
        _anonymizationRuleProvider = anonymization.RuleProvider;
        _queryTokenResolver = anonymization.TokenResolver;
        _options = options.Value.QueryExecution;
        _anonymizationMode = options.Value.Anonymizer.DefaultMode;
        _tokenizationOptions = options.Value.Anonymizer.Tokenization;
        _targets = new DatabaseTargetResolver(options.Value);
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
        // Resolve the caller's qualified target once; malformed, unqualified or unknown targets
        // are rejected with SQL-AI-0001 before any validation or database work.
        var targetResult = _targets.Resolve(databaseName);
        if (targetResult.IsFailure)
        {
            return targetResult.Error;
        }
        DatabaseTargetContext target = targetResult.Value;

        // 1-5. Run the shared 6-stage guardrail pipeline (single source of truth).
        var safetyResult = await _querySafetyValidator
            .ValidateTargetQuerySafetyAsync(target, query, allowSchemaOnly: false, cancellationToken)
            .ConfigureAwait(false);
        if (safetyResult.IsFailure)
        {
            return safetyResult.Error;
        }
        var accessLevel = safetyResult.Value.AccessLevel;
        var writeAllowed = safetyResult.Value.IsWriteAllowed;

        // 6. Resolve effective row limit (caller request capped by configured maximum)
        int effectiveLimit = requestedRowLimit.HasValue
            ? Math.Min(requestedRowLimit.Value, _options.MaxRowLimit)
            : _options.DefaultRowLimit;

        bool anonymize = accessLevel.RequiresAnonymization();

        // Detokenization runs only for anonymized databases (the only place tokens are ever
        // issued) and after the safety/multi-statement checks above, so it substitutes literal
        // values only — it never changes the query's structure. Resolution is scoped to the
        // resolved target, so foreign tokens stay literal SQL text.
        string effectiveQuery = anonymize && _queryTokenResolver != null
            ? _queryTokenResolver.ResolveTokens(query, target)
            : query;

        var result = await ExecuteQueryInTransactionAsync(
            target, effectiveQuery, effectiveLimit, anonymize, writeAllowed, parameters, cancellationToken);
        return result.IsSuccess ? result.Value with { AccessLevel = accessLevel } : result;
    }

    private async Task<Result<QueryExecutionResult>> ExecuteQueryInTransactionAsync(
        DatabaseTargetContext target,
        string query,
        int effectiveLimit,
        bool anonymize,
        bool writeAllowed,
        object? parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = _connectionFactory.CreateTargetConnection(target);
            await PrimaryConnection.OpenTargetAsync(connection, target, cancellationToken);

            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

            int baselineTranCount;
            Result<QueryExecutionResult> result;
            bool tranCountChanged;
            try
            {
                baselineTranCount = await TransactionIntegrityGuard.GetTranCountAsync(connection, transaction, _options.CommandTimeoutSeconds, cancellationToken);
                bool anonymizeResult = anonymize && !SqlColumnReferenceDetector.IsColumnIndependent(query);
                var execArgs = new QueryBatchExecutionArgs(
                    connection, transaction, target.QualifiedName, query, effectiveLimit, anonymizeResult, parameters, target);
                result = await ((IQueryBatchExecutor)this).ExecuteBatchAsync(execArgs, cancellationToken);
                int tranCountAfterExecution = await TransactionIntegrityGuard.GetTranCountAsync(connection, transaction, _options.CommandTimeoutSeconds, cancellationToken);
                tranCountChanged = tranCountAfterExecution != baselineTranCount;
            }
            catch
            {
                await DatabaseCleanup.RollbackAsync(transaction, _options.CommandTimeoutSeconds, _logger, preservePrimaryFailure: true);
                throw;
            }

            if (!writeAllowed && tranCountChanged)
            {
                return await TransactionIntegrityGuard.RejectViolationAsync(_logger, target.QualifiedName, transaction, _options.CommandTimeoutSeconds);
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
                await DatabaseCleanup.RollbackAsync(transaction, _options.CommandTimeoutSeconds, _logger);
                cancellationToken.ThrowIfCancellationRequested();
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogQueryFailed(_logger, target.QualifiedName, query, ex);
            string? anonymizedMessage = anonymize ? BuildAnonymizedQueryErrorMessage(ex) : null;
            return SqlToAiErrorMapper.MapException(ex, anonymizedMessage);
        }
    }

    private static string BuildAnonymizedQueryErrorMessage(Exception ex) => ex switch
    {
        SqlException sqlEx => $"(SQL error {sqlEx.Number}) the query failed during execution. Check syntax and column types; see server-side logs for details.",
        _ => "the query failed during execution.",
    };

    Task<Result<QueryExecutionResult>> IQueryBatchExecutor.ExecuteBatchAsync(
        QueryBatchExecutionArgs args,
        CancellationToken cancellationToken) =>
        ExecuteAndSerializeAsync(args, cancellationToken);

    private async Task EnableOptionsAsync(
        DbConnection connection,
        DbTransaction? transaction,
        int rowLimit,
        CancellationToken cancellationToken)
    {
        await DatabaseCommandExecutor.ExecuteSetOptionAsync(connection, transaction, "SET STATISTICS IO ON", _options.CommandTimeoutSeconds, cancellationToken);
        await DatabaseCommandExecutor.ExecuteSetOptionAsync(connection, transaction, "SET STATISTICS TIME ON", _options.CommandTimeoutSeconds, cancellationToken);
        await DatabaseCommandExecutor.ExecuteSetOptionAsync(connection, transaction, $"SET ROWCOUNT {rowLimit}", _options.CommandTimeoutSeconds, cancellationToken);
    }

    private async Task<Result<QueryExecutionResult>> ExecuteAndSerializeAsync(
        QueryBatchExecutionArgs args,
        CancellationToken cancellationToken)
    {
        var messages = new List<string>();
        var transaction = args.Transaction;
        var sqlConn = args.Connection as SqlConnection;
        SqlInfoMessageEventHandler infoMessageHandler = (_, e) => messages.Add(e.Message);
        if (sqlConn is not null)
        {
            sqlConn.InfoMessage += infoMessageHandler;
        }
        try
        {
            await EnableOptionsAsync(args.Connection, transaction, args.RowLimit, cancellationToken);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            await using var command = args.Connection.CreateCommand();
            command.CommandText = args.Query;
            command.Transaction = transaction;
            command.CommandTimeout = _options.CommandTimeoutSeconds;
            SqlParameterBinder.BindParameters(command, args.Parameters);

            var sb = new StringBuilder();
            int rowCount = 0;
            bool hasResultSet = false;
            int recordsAffected;
            var tracker = new RowAnonymizationTracker();
            bool executionFailed = false;

            try
            {
                CommandBehavior behavior = args.Anonymize
                    ? CommandBehavior.SequentialAccess | CommandBehavior.KeyInfo
                    : CommandBehavior.SequentialAccess;

                var reader = await command.ExecuteReaderAsync(behavior, cancellationToken);
                await using (reader)
                {
                    do
                    {
                        hasResultSet |= reader.FieldCount > 0;
                        var columns = GetColumnNames(reader);
                        var anonCtx = await ResolveAnonymizationContextAsync(
                            reader, columns, args.Anonymize, args.Target, cancellationToken);

                        while (rowCount < args.RowLimit && await reader.ReadAsync(cancellationToken))
                        {
                            AppendSerializedRow(sb, reader, columns, anonCtx, tracker);
                            rowCount++;
                        }
                    }
                    while ((rowCount < args.RowLimit || !hasResultSet) && await reader.NextResultAsync(cancellationToken));
                }
                // ADO.NET exposes the final affected count on the reader, after disposal.
                recordsAffected = reader.RecordsAffected;
            }
            catch
            {
                executionFailed = true;
                throw;
            }
            finally
            {
                await DatabaseCleanup.ResetRowCountAsync(args.Connection, transaction, _options.CommandTimeoutSeconds, _logger, executionFailed);
            }
            cancellationToken.ThrowIfCancellationRequested();

            stopwatch.Stop();

            var (cpu, _, logical, _, _, _) = PerformanceMetricsCalculator.ParseRunMessages(messages);

            if (rowCount == 0)
            {
                return new QueryExecutionResult("[]", false, Array.Empty<string>(), _anonymizationMode, ElapsedMs: stopwatch.ElapsedMilliseconds, RowCount: 0, CpuTimeMs: cpu, LogicalReads: logical)
                { HasResultSet = hasResultSet, RecordsAffected = recordsAffected };
            }

            return new QueryExecutionResult(
                sb.ToString().TrimEnd(), tracker.WasAnonymized, tracker.AnonymizedColumns, _anonymizationMode, tracker.SearchableTokenColumns,
                ElapsedMs: stopwatch.ElapsedMilliseconds, RowCount: rowCount, CpuTimeMs: cpu, LogicalReads: logical)
            { HasResultSet = hasResultSet, RecordsAffected = recordsAffected };
        }
        finally
        {
            if (sqlConn is not null)
            {
                sqlConn.InfoMessage -= infoMessageHandler;
            }
        }
    }

}
