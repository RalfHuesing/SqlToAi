#nullable enable

using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Measures server-side CPU, IO, and Execution Plan metrics for SQL queries.
/// </summary>
public interface IPerformanceMeasurementService
{
    /// <summary>
    /// Measures performance using a parameter object.
    /// </summary>
    /// <param name="args">Measurement options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Result{T}"/> containing <see cref="PerformanceMeasurementResult"/>.</returns>
    Task<Result<PerformanceMeasurementResult>> MeasurePerformanceAsync(
        QueryPerformanceArgs args,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Measures performance for an unparameterized query with default settings.
    /// </summary>
    /// <param name="databaseName">Target database name.</param>
    /// <param name="query">SQL query string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Result{T}"/> containing <see cref="PerformanceMeasurementResult"/>.</returns>
    Task<Result<PerformanceMeasurementResult>> MeasurePerformanceAsync(
        string databaseName,
        string query,
        CancellationToken cancellationToken = default);
}
