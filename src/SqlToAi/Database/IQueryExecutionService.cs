using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Executes a single SQL statement with row limits. Writable access levels commit changes;
/// read-only levels reject mutations and roll back. Anonymized access levels mask string results.
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
    Task<Result<QueryExecutionResult>> ExecuteQueryAsync(
        string databaseName,
        string query,
        int? requestedRowLimit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a single SQL statement against the specified database with optional parameter binding.
    /// </summary>
    /// <param name="databaseName">The name of the target database.</param>
    /// <param name="query">The SQL query to execute.</param>
    /// <param name="requestedRowLimit">Optional row limit.</param>
    /// <param name="parameters">Optional parameters container (dictionary, JSON element, or string).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result.</returns>
    Task<Result<QueryExecutionResult>> ExecuteQueryAsync(
        string databaseName,
        string query,
        int? requestedRowLimit,
        object? parameters,
        CancellationToken cancellationToken = default);
}
