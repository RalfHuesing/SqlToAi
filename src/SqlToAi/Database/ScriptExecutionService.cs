#nullable enable

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;

namespace SqlToAi.Database;

internal sealed class ScriptExecutionService : IScriptExecutionService
{
    private static readonly Action<ILogger, string, Exception?> LogScriptFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(4, "ScriptExecutionFailed"),
            "Script execution failed for database {Database}.");

    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly IQuerySafetyValidator _querySafetyValidator;
    private readonly IQueryBatchExecutor _batchExecutor;
    private readonly QueryExecutionOptions _options;
    private readonly ILogger<ScriptExecutionService> _logger;

    public ScriptExecutionService(
        IDatabaseConnectionFactory connectionFactory,
        IQuerySafetyValidator querySafetyValidator,
        IQueryBatchExecutor batchExecutor,
        IOptions<SqlToAiOptions> options,
        ILogger<ScriptExecutionService> logger)
    {
        _connectionFactory = connectionFactory;
        _querySafetyValidator = querySafetyValidator;
        _batchExecutor = batchExecutor;
        _options = options.Value.QueryExecution;
        _logger = logger;
    }

    public async Task<ScriptExecutionReport> ExecuteAsync(
        ScriptExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SqlBatch> batches = SqlScriptBatchSplitter.Split(request.ScriptFile.Text);
        if (batches.Count == 0)
        {
            return BuildEmptyScriptReport(request);
        }

        ScriptTransactionMode mode = ScriptTransactionMode.NotStarted;
        try
        {
            ScriptPreflightResult preflight = await PreflightAsync(
                request.DatabaseName,
                batches,
                cancellationToken).ConfigureAwait(false);
            if (preflight.Error is not null)
            {
                return BuildPreflightFailureReport(request, batches, preflight);
            }

            QuerySafetyCheckResult safety = preflight.Safety!;
            mode = ResolveTransactionMode(safety, request.UseTransaction);
            ScriptExecutionOutcome outcome = mode == ScriptTransactionMode.ReadWriteProviderAutocommit
                ? await ExecuteWithoutTransactionAsync(request, batches, safety, cancellationToken)
                    .ConfigureAwait(false)
                : await ExecuteWithTransactionAsync(request, batches, safety, cancellationToken)
                    .ConfigureAwait(false);

            return ScriptExecutionReportFactory.BuildReport(new ScriptExecutionReportInput(
                request.ScriptFile,
                request.DatabaseName,
                mode,
                outcome.Batches,
                outcome.Error));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BuildUnexpectedFailureReport(request, batches, mode, ex);
        }
    }

    private static ScriptExecutionReport BuildEmptyScriptReport(ScriptExecutionRequest request)
    {
        return ScriptExecutionReportFactory.BuildReport(new ScriptExecutionReportInput(
            request.ScriptFile,
            request.DatabaseName,
            ScriptTransactionMode.NotStarted,
            [],
            SqlToAiError.InvalidParameters("SQL script must contain at least one batch.")));
    }

    private static ScriptExecutionReport BuildPreflightFailureReport(
        ScriptExecutionRequest request,
        IReadOnlyList<SqlBatch> batches,
        ScriptPreflightResult preflight)
    {
        SqlToAiError error = preflight.Error!;
        return ScriptExecutionReportFactory.BuildReport(new ScriptExecutionReportInput(
            request.ScriptFile,
            request.DatabaseName,
            ScriptTransactionMode.NotStarted,
            ScriptExecutionReportFactory.BuildFailureBatches(batches, preflight.FailedBatchNumber, error),
            error));
    }

    private ScriptExecutionReport BuildUnexpectedFailureReport(
        ScriptExecutionRequest request,
        IReadOnlyList<SqlBatch> batches,
        ScriptTransactionMode mode,
        Exception exception)
    {
        LogScriptFailed(_logger, request.DatabaseName, exception);
        SqlToAiError error = SqlToAiErrorMapper.MapException(exception);
        return ScriptExecutionReportFactory.BuildReport(new ScriptExecutionReportInput(
            request.ScriptFile,
            request.DatabaseName,
            mode,
            ScriptExecutionReportFactory.BuildFailureBatches(batches, null, error),
            error));
    }

    private async Task<ScriptExecutionOutcome> ExecuteWithoutTransactionAsync(
        ScriptExecutionRequest request,
        IReadOnlyList<SqlBatch> batches,
        QuerySafetyCheckResult safety,
        CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection(request.DatabaseName);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var args = new QueryBatchExecutionArgs(
            connection,
            null,
            request.DatabaseName,
            string.Empty,
            ResolveRowLimit(request.RequestedRowLimit),
            safety.AccessLevel == AccessLevel.ReadOnlyAnonymized,
            request.Parameters);
        var context = new ScriptTransactionContext(args, safety.IsWriteAllowed, false, 0);
        return await ExecuteBatchesAsync(batches, context, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ScriptPreflightResult> PreflightAsync(
        string databaseName,
        IReadOnlyList<SqlBatch> batches,
        CancellationToken cancellationToken)
    {
        QuerySafetyCheckResult? firstOutcome = null;
        var validatedBatches = new HashSet<SqlBatch>();
        for (int index = 0; index < batches.Count; index++)
        {
            SqlBatch batch = batches[index];
            if (!validatedBatches.Add(batch))
            {
                continue;
            }

            var safetyResult = await _querySafetyValidator
                .ValidateBatchSafetyAsync(databaseName, batch.Text, cancellationToken)
                .ConfigureAwait(false);
            if (safetyResult.IsFailure)
            {
                return new ScriptPreflightResult(null, index + 1, safetyResult.Error);
            }

            firstOutcome ??= safetyResult.Value;
        }

        return firstOutcome is null
            ? new ScriptPreflightResult(
                null,
                null,
                SqlToAiError.InvalidParameters("SQL script must contain at least one batch."))
            : new ScriptPreflightResult(firstOutcome, null, null);
    }

    private async Task<ScriptExecutionOutcome> ExecuteWithTransactionAsync(
        ScriptExecutionRequest request,
        IReadOnlyList<SqlBatch> batches,
        QuerySafetyCheckResult safety,
        CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection(request.DatabaseName);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        ScriptExecutionOutcome? outcome = null;

        try
        {
            int baselineTranCount = await TransactionIntegrityGuard
                .GetTranCountAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            var args = new QueryBatchExecutionArgs(
                connection,
                transaction,
                request.DatabaseName,
                string.Empty,
                ResolveRowLimit(request.RequestedRowLimit),
                safety.AccessLevel == AccessLevel.ReadOnlyAnonymized,
                request.Parameters);
            var context = new ScriptTransactionContext(args, safety.IsWriteAllowed, true, baselineTranCount);
            outcome = await ExecuteBatchesAsync(batches, context, cancellationToken).ConfigureAwait(false);
            if (outcome.Error is not null)
            {
                return outcome;
            }

            if (context.WriteAllowed)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }

            return outcome;
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            LogScriptFailed(_logger, request.DatabaseName, ex);
            IReadOnlyList<ScriptBatchReport> reports = outcome?.Batches
                ?? ScriptExecutionReportFactory.BuildFailureBatches(batches, null, SqlToAiError.QueryError(ex.Message));
            return new ScriptExecutionOutcome(reports, SqlToAiErrorMapper.MapException(ex));
        }
    }

    private async Task<ScriptExecutionOutcome> ExecuteBatchesAsync(
        IReadOnlyList<SqlBatch> batches,
        ScriptTransactionContext context,
        CancellationToken cancellationToken)
    {
        var reports = new List<ScriptBatchReport>(batches.Count);
        for (int index = 0; index < batches.Count; index++)
        {
            SqlBatch batch = batches[index];
            BatchExecutionOutcome batchOutcome = await ExecuteBatchRepetitionsAsync(
                batch,
                context,
                cancellationToken).ConfigureAwait(false);
            if (batchOutcome.Error is not null)
            {
                reports.Add(ScriptExecutionReportFactory.BuildFailedBatch(
                    index + 1,
                    batch,
                    batchOutcome.Executions,
                    batchOutcome.Error));
                AppendNotExecutedBatches(reports, batches, index + 1);
                return new ScriptExecutionOutcome(reports, batchOutcome.Error);
            }

            reports.Add(ScriptExecutionReportFactory.BuildSucceededBatch(
                index + 1,
                batch,
                batchOutcome.Executions));
        }

        return new ScriptExecutionOutcome(reports, null);
    }

    private static void AppendNotExecutedBatches(
        List<ScriptBatchReport> reports,
        IReadOnlyList<SqlBatch> batches,
        int failedBatchNumber)
    {
        for (int index = failedBatchNumber; index < batches.Count; index++)
        {
            reports.Add(ScriptExecutionReportFactory.BuildNotExecutedBatch(index + 1, batches[index]));
        }
    }

    private async Task<BatchExecutionOutcome> ExecuteBatchRepetitionsAsync(
        SqlBatch batch,
        ScriptTransactionContext context,
        CancellationToken cancellationToken)
    {
        var executions = new List<QueryExecutionResult>(batch.RepeatCount);
        QueryBatchExecutionArgs args = context.ExecutionArgs with { Query = batch.Text };
        try
        {
            for (int repetition = 0; repetition < batch.RepeatCount; repetition++)
            {
                var executionResult = await _batchExecutor
                    .ExecuteBatchAsync(args, cancellationToken)
                    .ConfigureAwait(false);
                if (executionResult.IsFailure)
                {
                    await RollbackFailedBatchAsync(context, cancellationToken).ConfigureAwait(false);
                    return new BatchExecutionOutcome(executions, executionResult.Error);
                }

                if (context.CheckTransactionIntegrity)
                {
                    DbTransaction transaction = args.Transaction
                        ?? throw new InvalidOperationException("An explicit transaction is required for integrity checks.");
                    if (await HasTransactionChangedAsync(context, transaction, cancellationToken).ConfigureAwait(false))
                    {
                        var violation = await TransactionIntegrityGuard
                            .RejectViolationAsync(_logger, args.DatabaseName, transaction, cancellationToken)
                            .ConfigureAwait(false);
                        return new BatchExecutionOutcome(executions, violation.Error);
                    }
                }

                executions.Add(executionResult.Value);
            }

            return new BatchExecutionOutcome(executions, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await RollbackFailedBatchAsync(context, cancellationToken).ConfigureAwait(false);
            LogScriptFailed(_logger, context.ExecutionArgs.DatabaseName, ex);
            return new BatchExecutionOutcome(executions, SqlToAiErrorMapper.MapException(ex));
        }
    }

    private static async Task RollbackFailedBatchAsync(
        ScriptTransactionContext context,
        CancellationToken cancellationToken)
    {
        if (context.ExecutionArgs.Transaction is { } transaction)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<bool> HasTransactionChangedAsync(
        ScriptTransactionContext context,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        int currentTranCount = await TransactionIntegrityGuard
            .GetTranCountAsync(
                context.ExecutionArgs.Connection,
                transaction,
                cancellationToken)
            .ConfigureAwait(false);
        return currentTranCount != context.BaselineTranCount;
    }

    private static ScriptTransactionMode ResolveTransactionMode(
        QuerySafetyCheckResult safety,
        bool useTransaction)
    {
        if (safety.AccessLevel == AccessLevel.ReadWrite)
        {
            return useTransaction
                ? ScriptTransactionMode.ReadWriteAtomic
                : ScriptTransactionMode.ReadWriteProviderAutocommit;
        }

        return safety.AccessLevel == AccessLevel.ReadOnlyAnonymized
            ? ScriptTransactionMode.ReadOnlyAnonymizedRollback
            : ScriptTransactionMode.ReadOnlyRollback;
    }

    private int ResolveRowLimit(int? requestedRowLimit) => requestedRowLimit.HasValue
        ? Math.Min(requestedRowLimit.Value, _options.MaxRowLimit)
        : _options.DefaultRowLimit;

    private sealed record ScriptPreflightResult(
        QuerySafetyCheckResult? Safety,
        int? FailedBatchNumber,
        SqlToAiError? Error);

    private sealed record BatchExecutionOutcome(
        IReadOnlyList<QueryExecutionResult> Executions,
        SqlToAiError? Error);

    private sealed record ScriptExecutionOutcome(
        IReadOnlyList<ScriptBatchReport> Batches,
        SqlToAiError? Error);

    private sealed record ScriptTransactionContext(
        QueryBatchExecutionArgs ExecutionArgs,
        bool WriteAllowed,
        bool CheckTransactionIntegrity,
        int BaselineTranCount);
}
