#nullable enable

using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.Logging;
using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Layer-2 defense in depth against transaction tampering, independent of any specific mutating
/// keyword: captures <c>@@TRANCOUNT</c> before and after a query executes inside an ambient
/// ADO.NET transaction, and — for non-write-allowed databases — treats any change as a
/// transaction-integrity violation (e.g. an embedded <c>COMMIT</c> deep inside dynamic SQL
/// silently committing the ambient transaction) rather than trusting the query's result.
/// Extracted from <see cref="QueryExecutionService"/> so that file stays within the project's
/// line-count budget; this guard has no other dependency on the rest of that class.
/// </summary>
internal static class TransactionIntegrityGuard
{
    private static readonly Action<ILogger, string, Exception?> LogViolation =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2, "TransactionIntegrityViolation"),
            "Transaction integrity violation for database {Database}: the ambient transaction's state changed unexpectedly during query execution (possible read-only guard bypass). The result was discarded and no commit was performed.");

    private static readonly Action<ILogger, string, Exception?> LogRollbackAfterViolationFailed =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(3, "RollbackAfterIntegrityViolationFailed"),
            "Rollback after a transaction-integrity violation failed for database {Database} — expected when the ambient transaction no longer exists.");

    /// <summary>Reads the current <c>@@TRANCOUNT</c> on the given connection/transaction.</summary>
    public static async Task<int> GetTranCountAsync(DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT @@TRANCOUNT";
        command.Transaction = transaction;
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Logs the violation, attempts a best-effort rollback (swallowing failure, since the
    /// underlying transaction is often already gone by this point), and returns the rejection
    /// result the caller must return instead of the query's own (untrustworthy) result.
    /// </summary>
    public static async Task<Result<QueryExecutionResult>> RejectViolationAsync(
        ILogger logger, string databaseName, DbTransaction transaction, CancellationToken cancellationToken)
    {
        LogViolation(logger, databaseName, null);
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (Exception rollbackEx)
        {
            // Expected in this anomaly path: the ambient transaction may already be gone (e.g.
            // committed server-side by the statement itself). Logged at Debug so it never masks
            // the Error above, and deliberately not rethrown — that would replace the
            // integrity-violation error below with a confusing "no transaction" exception.
            LogRollbackAfterViolationFailed(logger, databaseName, rollbackEx);
        }

        return SqlToAiError.QueryError(
            "Transaction state was altered during query execution (a possible read-only guard bypass); the operation was rejected and no data was returned.");
    }
}
