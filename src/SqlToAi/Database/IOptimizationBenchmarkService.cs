#nullable enable

using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Service for running combined optimization benchmarks (equivalence check + performance deltas + verdict).
/// </summary>
public interface IOptimizationBenchmarkService
{
    /// <summary>
    /// Benchmarks baseline vs candidate query using a parameter object.
    /// </summary>
    /// <param name="args">Benchmark arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Result{T}"/> containing <see cref="OptimizationBenchmarkResult"/>.</returns>
    Task<Result<OptimizationBenchmarkResult>> BenchmarkOptimizationAsync(
        QueryBenchmarkArgs args,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Benchmarks baseline vs candidate unparameterized queries with default settings.
    /// </summary>
    /// <param name="databaseName">Target database name.</param>
    /// <param name="queryA">Baseline query A.</param>
    /// <param name="queryB">Candidate query B.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Result{T}"/> containing <see cref="OptimizationBenchmarkResult"/>.</returns>
    Task<Result<OptimizationBenchmarkResult>> BenchmarkOptimizationAsync(
        string databaseName,
        string queryA,
        string queryB,
        CancellationToken cancellationToken = default);
}
