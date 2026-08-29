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

    public async Task<Result<IReadOnlyList<ScriptBatchExecutionResult>>> ExecuteAsync(
        ScriptExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<SqlBatch> batches = SqlScriptBatchSplitter.Split(request.ScriptFile.Text);
            if (batches.Count == 0)
            {
                return SqlToAiError.InvalidParameters("SQL script must contain at least one batch.");
            }

            var safetyResult = await PreflightAsync(request.DatabaseName, batches, cancellationToken);
            if (safetyResult.IsFailure)
            {
                return safetyResult.Error;
            }

            QuerySafetyCheckResult safety = safetyResult.Value;
            if (safety.AccessLevel == AccessLevel.ReadWrite && !request.UseTransaction)
            {
                return await ExecuteWithoutTransactionAsync(request, batches, safety, cancellationToken);
            }

            return await ExecuteWithTransactionAsync(request, batches, safety, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogScriptFailed(_logger, request.DatabaseName, ex);
            return SqlToAiErrorMapper.MapException(ex);
        }
    }

    private async Task<Result<IReadOnlyList<ScriptBatchExecutionResult>>> ExecuteWithoutTransactionAsync(
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

    private async Task<Result<QuerySafetyCheckResult>> PreflightAsync(
        string databaseName,
        IReadOnlyList<SqlBatch> batches,
        CancellationToken cancellationToken)
    {
        QuerySafetyCheckResult? firstOutcome = null;
        foreach (SqlBatch batch in batches.Distinct())
        {
            var safetyResult = await _querySafetyValidator
                .ValidateBatchSafetyAsync(databaseName, batch.Text, cancellationToken)
                .ConfigureAwait(false);
            if (safetyResult.IsFailure)
            {
                return safetyResult.Error;
            }

            firstOutcome ??= safetyResult.Value;
        }

        if (firstOutcome is null)
        {
            return SqlToAiError.InvalidParameters("SQL script must contain at least one batch.");
        }

        return firstOutcome;
    }

    private async Task<Result<IReadOnlyList<ScriptBatchExecutionResult>>> ExecuteWithTransactionAsync(
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
            return await ExecuteBatchesAsync(batches, context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<Result<IReadOnlyList<ScriptBatchExecutionResult>>> ExecuteBatchesAsync(
        IReadOnlyList<SqlBatch> batches,
        ScriptTransactionContext context,
        CancellationToken cancellationToken)
    {
        var results = new List<ScriptBatchExecutionResult>(batches.Count);
        foreach (SqlBatch batch in batches)
        {
            var batchExecutions = await ExecuteBatchRepetitionsAsync(batch, context, cancellationToken);
            if (batchExecutions.IsFailure)
            {
                return Result<IReadOnlyList<ScriptBatchExecutionResult>>.Failure(batchExecutions.Error);
            }

            results.Add(new ScriptBatchExecutionResult(batch, batchExecutions.Value));
        }

        if (context.ExecutionArgs.Transaction is not { } transaction)
        {
            return Result<IReadOnlyList<ScriptBatchExecutionResult>>.Success(results);
        }

        if (context.WriteAllowed)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result<IReadOnlyList<ScriptBatchExecutionResult>>.Success(results);
    }

    private async Task<Result<IReadOnlyList<QueryExecutionResult>>> ExecuteBatchRepetitionsAsync(
        SqlBatch batch,
        ScriptTransactionContext context,
        CancellationToken cancellationToken)
    {
        var executions = new List<QueryExecutionResult>(batch.RepeatCount);
        QueryBatchExecutionArgs args = context.ExecutionArgs with { Query = batch.Text };
        for (int repetition = 0; repetition < batch.RepeatCount; repetition++)
        {
            var executionResult = await _batchExecutor
                .ExecuteBatchAsync(args, cancellationToken)
                .ConfigureAwait(false);
            if (executionResult.IsFailure)
            {
                if (context.ExecutionArgs.Transaction is { } transaction)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }

                return Result<IReadOnlyList<QueryExecutionResult>>.Failure(executionResult.Error);
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
                    return Result<IReadOnlyList<QueryExecutionResult>>.Failure(violation.Error);
                }
            }

            executions.Add(executionResult.Value);
        }

        return Result<IReadOnlyList<QueryExecutionResult>>.Success(executions);
    }

    private static async Task<bool> HasTransactionChangedAsync(
        ScriptTransactionContext context,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        int currentTranCount = await TransactionIntegrityGuard
            .GetTranCountAsync(
                context.ExecutionArgs.Connection, transaction,
                cancellationToken)
            .ConfigureAwait(false);
        return currentTranCount != context.BaselineTranCount;
    }

    private int ResolveRowLimit(int? requestedRowLimit) => requestedRowLimit.HasValue
        ? Math.Min(requestedRowLimit.Value, _options.MaxRowLimit)
        : _options.DefaultRowLimit;

    private sealed record ScriptTransactionContext(
        QueryBatchExecutionArgs ExecutionArgs,
        bool WriteAllowed,
        bool CheckTransactionIntegrity,
        int BaselineTranCount);
}
