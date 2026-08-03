#nullable enable

using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Compares two SQL queries for semantic equivalence (schema, row count, and database-side EXCEPT set difference).
/// </summary>
public interface IQueryComparisonService
{
    /// <summary>
    /// Compares two SQL queries using a parameter object.
    /// </summary>
    /// <param name="args">Comparison arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Result{T}"/> containing <see cref="QueryComparisonResult"/>.</returns>
    Task<Result<QueryComparisonResult>> CompareQueriesAsync(
        QueryComparisonArgs args,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares two unparameterized SQL queries.
    /// </summary>
    /// <param name="databaseName">Target database name.</param>
    /// <param name="queryA">Baseline SQL query A.</param>
    /// <param name="queryB">Candidate SQL query B.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Result{T}"/> containing <see cref="QueryComparisonResult"/>.</returns>
    Task<Result<QueryComparisonResult>> CompareQueriesAsync(
        string databaseName,
        string queryA,
        string queryB,
        CancellationToken cancellationToken = default);
}
