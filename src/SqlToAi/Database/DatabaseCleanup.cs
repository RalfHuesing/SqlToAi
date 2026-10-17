using System.Data.Common;
using Microsoft.Extensions.Logging;
using SqlToAi.Configuration;

namespace SqlToAi.Database;

internal static class DatabaseCleanup
{
    private static readonly Action<ILogger, string, Exception?> LogCleanupFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(5, "DatabaseCleanupFailed"),
            "Database cleanup {Operation} failed; preserving the original operation failure.");

    internal static int GetTimeoutSeconds(int commandTimeout) =>
        Math.Min(commandTimeout > 0 ? commandTimeout : new QueryExecutionOptions().CommandTimeoutSeconds,
            (int)((uint.MaxValue - 1) / 1000));

    internal static async Task RollbackAsync(DbTransaction transaction, int commandTimeout,
        ILogger logger, bool preservePrimaryFailure = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(GetTimeoutSeconds(commandTimeout)));
        try
        {
            // Rollback cancellation depends on the provider; never abandon an in-flight operation.
            await transaction.RollbackAsync(timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
        }
        catch (Exception exception) when (preservePrimaryFailure)
        {
            LogCleanupFailed(logger, "transaction rollback", exception);
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("Database rollback exceeded its cleanup timeout.", exception);
        }
        catch (OperationCanceledException exception)
        {
            throw new InvalidOperationException("Database rollback was cancelled without a cleanup timeout.", exception);
        }
    }

    internal static Task ResetRowCountAsync(DbConnection connection, DbTransaction? transaction,
        int commandTimeout, ILogger logger, bool preservePrimaryFailure = false) =>
        ResetOptionAsync(connection, transaction, "SET ROWCOUNT 0", commandTimeout, logger, preservePrimaryFailure);

    internal static Task ResetNoexecAsync(DbConnection connection, DbTransaction transaction,
        int commandTimeout, ILogger logger, bool preservePrimaryFailure = false) =>
        ResetOptionAsync(connection, transaction, "SET NOEXEC OFF", commandTimeout, logger, preservePrimaryFailure);

    private static async Task ResetOptionAsync(DbConnection connection, DbTransaction? transaction,
        string sql, int commandTimeout, ILogger logger, bool preservePrimaryFailure)
    {
        int cleanupTimeout = GetTimeoutSeconds(commandTimeout);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(cleanupTimeout));
        try
        {
            await DatabaseCommandExecutor.ExecuteSetOptionAsync(connection, transaction, sql, cleanupTimeout, timeout.Token)
                .ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
        }
        catch (Exception exception) when (preservePrimaryFailure)
        {
            LogCleanupFailed(logger, sql, exception);
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("Database session reset exceeded its cleanup timeout.", exception);
        }
        catch (OperationCanceledException exception)
        {
            throw new InvalidOperationException("Database session reset was cancelled without a cleanup timeout.", exception);
        }
    }
}
