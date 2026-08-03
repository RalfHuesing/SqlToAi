#nullable enable

using Microsoft.Extensions.Logging;
using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Service that orchestrates equivalence checks and performance measurements to generate an optimization benchmark report.
/// </summary>
public sealed class OptimizationBenchmarkService : IOptimizationBenchmarkService
{
    private readonly IQueryComparisonService _comparisonService;
    private readonly IPerformanceMeasurementService _performanceService;
    private readonly ILogger<OptimizationBenchmarkService> _logger;

    /// <summary>Initializes a new instance of <see cref="OptimizationBenchmarkService"/>.</summary>
    public OptimizationBenchmarkService(
        IQueryComparisonService comparisonService,
        IPerformanceMeasurementService performanceService,
        ILogger<OptimizationBenchmarkService> logger)
    {
        _comparisonService = comparisonService;
        _performanceService = performanceService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<Result<OptimizationBenchmarkResult>> BenchmarkOptimizationAsync(
        string databaseName,
        string queryA,
        string queryB,
        CancellationToken cancellationToken = default)
    {
        return BenchmarkOptimizationAsync(new QueryBenchmarkArgs(databaseName, queryA, queryB), cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<OptimizationBenchmarkResult>> BenchmarkOptimizationAsync(
        QueryBenchmarkArgs args,
        CancellationToken cancellationToken = default)
    {
        var compArgs = new QueryComparisonArgs(
            args.DatabaseName, args.QueryA, args.QueryB, args.ParametersA, args.ParametersB, args.SharedParameters);

        var compResult = await _comparisonService.CompareQueriesAsync(compArgs, cancellationToken);
        if (compResult.IsFailure)
        {
            return compResult.Error;
        }

        var perfArgsA = new QueryPerformanceArgs(
            args.DatabaseName, args.QueryA, args.ParametersA ?? args.SharedParameters, args.WarmupRuns, args.ExecutionRuns);

        var perfResultA = await _performanceService.MeasurePerformanceAsync(perfArgsA, cancellationToken);
        if (perfResultA.IsFailure)
        {
            return perfResultA.Error;
        }

        var perfArgsB = new QueryPerformanceArgs(
            args.DatabaseName, args.QueryB, args.ParametersB ?? args.SharedParameters, args.WarmupRuns, args.ExecutionRuns);

        var perfResultB = await _performanceService.MeasurePerformanceAsync(perfArgsB, cancellationToken);
        if (perfResultB.IsFailure)
        {
            return perfResultB.Error;
        }

        var deltas = CalculateMetricsDelta(perfResultA.Value.Metrics, perfResultB.Value.Metrics);
        var (verdict, summary) = DetermineVerdictAndSummary(compResult.Value, deltas);

        return new OptimizationBenchmarkResult(
            Database: args.DatabaseName,
            Verdict: verdict,
            Summary: summary,
            Comparison: compResult.Value,
            PerformanceA: perfResultA.Value,
            PerformanceB: perfResultB.Value,
            Deltas: deltas);
    }

    private static BenchmarkMetricsDelta CalculateMetricsDelta(PerformanceMetrics baseline, PerformanceMetrics candidate)
    {
        return new BenchmarkMetricsDelta(
            CpuTime: CreateMetricDelta(baseline.CpuTimeMs, candidate.CpuTimeMs),
            ElapsedTime: CreateMetricDelta(baseline.ElapsedTimeMs, candidate.ElapsedTimeMs),
            LogicalReads: CreateMetricDelta(baseline.LogicalReads, candidate.LogicalReads),
            PhysicalReads: CreateMetricDelta(baseline.PhysicalReads, candidate.PhysicalReads));
    }

    private static MetricDelta CreateMetricDelta(long baseline, long candidate)
    {
        long diff = candidate - baseline;
        double pct = baseline == 0 ? 0.0 : Math.Round((double)diff / baseline * 100.0, 2);
        return new MetricDelta(baseline, candidate, diff, pct);
    }

    private static (string Verdict, string Summary) DetermineVerdictAndSummary(QueryComparisonResult comparison, BenchmarkMetricsDelta deltas)
    {
        if (!comparison.IsEqual)
        {
            return (
                Verdict: "UnsafeDueToDataMismatch",
                Summary: "Candidate query (Query B) produces different results or schema compared to baseline query (Query A). Cannot replace query."
            );
        }

        bool cpuImprovedOrSame = deltas.CpuTime.AbsoluteDelta <= 0;
        bool readsImprovedOrSame = deltas.LogicalReads.AbsoluteDelta <= 0;

        if (cpuImprovedOrSame && readsImprovedOrSame && (deltas.CpuTime.AbsoluteDelta < 0 || deltas.LogicalReads.AbsoluteDelta < 0))
        {
            return (
                Verdict: "Recommended",
                Summary: $"Candidate query is 100% equivalent and reduces CPU time by {Math.Abs(deltas.CpuTime.PercentageDelta):F1}% and logical reads by {Math.Abs(deltas.LogicalReads.PercentageDelta):F1}%."
            );
        }

        if (deltas.CpuTime.AbsoluteDelta > 0 || deltas.LogicalReads.AbsoluteDelta > 0)
        {
            return (
                Verdict: "NotRecommended",
                Summary: "Candidate query is equivalent, but consumed more CPU time or logical reads than baseline query."
            );
        }

        return (
            Verdict: "Neutral",
            Summary: "Candidate query is equivalent and showed identical resource utilization."
        );
    }
}
