#nullable enable

using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Executes a single SQL statement. Runs inside an explicit rollback transaction and rejects
/// mutating keywords unless the database is at <c>AccessLevel.ReadWrite</c> with the global
/// read-only override off, in which case the transaction is committed instead.
/// Applies row limits and optional PII anonymization based on the database access level.
/// </summary>
public interface IQueryExecutionService
{
    /// <summary>
    /// Executes a single SQL statement against the specified database.
    /// </summary>
    /// <param name="databaseName">The name of the target database.</param>
    /// <param name="query">The SQL query to execute (SELECT, or DML/EXEC when write-allowed).</param>
    /// <param name="requestedRowLimit">
    /// Optional caller-supplied row limit. Capped by the configured maximum.
    /// When null, the configured default row limit applies.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> containing a JSON-lines string (one JSON object per row) on success,
    /// or a structured error on failure.
    /// </returns>
    Task<Result<string>> ExecuteQueryAsync(
        string databaseName,
        string query,
        int? requestedRowLimit,
        CancellationToken cancellationToken = default);
}
