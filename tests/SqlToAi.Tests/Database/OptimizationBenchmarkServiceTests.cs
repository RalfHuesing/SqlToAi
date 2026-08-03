#nullable enable

using Microsoft.Extensions.Logging.Abstractions;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Unit tests for <see cref="OptimizationBenchmarkService"/>, validating verdict determination and metric deltas.
/// </summary>
public sealed class OptimizationBenchmarkServiceTests
{
    private sealed class FakeComparisonService(bool isEqual) : IQueryComparisonService
    {
        public Task<Result<QueryComparisonResult>> CompareQueriesAsync(QueryComparisonArgs args, CancellationToken ct = default)
        {
            var res = new QueryComparisonResult(isEqual, isEqual, isEqual, 10, 10, Array.Empty<string>(), "[]", "[]");
            return Task.FromResult(Result<QueryComparisonResult>.Success(res));
        }

        public Task<Result<QueryComparisonResult>> CompareQueriesAsync(string db, string qA, string qB, CancellationToken ct = default)
            => CompareQueriesAsync(new QueryComparisonArgs(db, qA, qB), ct);
    }

    private sealed class FakePerfService(PerformanceMetrics metricsA, PerformanceMetrics metricsB) : IPerformanceMeasurementService
    {
        public Task<Result<PerformanceMeasurementResult>> MeasurePerformanceAsync(QueryPerformanceArgs args, CancellationToken ct = default)
        {
            var metrics = args.Query == "A" ? metricsA : metricsB;
            var res = new PerformanceMeasurementResult(args.DatabaseName, 1, 1, metrics, Array.Empty<PerformancePlanWarning>(), true, null);
            return Task.FromResult(Result<PerformanceMeasurementResult>.Success(res));
        }

        public Task<Result<PerformanceMeasurementResult>> MeasurePerformanceAsync(string db, string query, CancellationToken ct = default)
            => MeasurePerformanceAsync(new QueryPerformanceArgs(db, query), ct);
    }

    [Fact]
    public async Task BenchmarkOptimizationAsync_EqualQueriesWithImprovement_ReturnsRecommended()
    {
        var comp = new FakeComparisonService(isEqual: true);
        var perfA = new PerformanceMetrics(CpuTimeMs: 100, ElapsedTimeMs: 120, LogicalReads: 500, PhysicalReads: 0, ReadAheadReads: 0);
        var perfB = new PerformanceMetrics(CpuTimeMs: 40, ElapsedTimeMs: 45, LogicalReads: 100, PhysicalReads: 0, ReadAheadReads: 0);
        var perf = new FakePerfService(perfA, perfB);

        var service = new OptimizationBenchmarkService(comp, perf, NullLogger<OptimizationBenchmarkService>.Instance);
        var result = await service.BenchmarkOptimizationAsync(new QueryBenchmarkArgs("TestDb", "A", "B"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("Recommended", result.Value.Verdict);
        Assert.Equal(-60.0, result.Value.Deltas.CpuTime.PercentageDelta);
        Assert.Equal(-80.0, result.Value.Deltas.LogicalReads.PercentageDelta);
    }

    [Fact]
    public async Task BenchmarkOptimizationAsync_UnequalQueries_ReturnsUnsafeDueToDataMismatch()
    {
        var comp = new FakeComparisonService(isEqual: false);
        var perfA = new PerformanceMetrics(CpuTimeMs: 100, ElapsedTimeMs: 120, LogicalReads: 500, PhysicalReads: 0, ReadAheadReads: 0);
        var perfB = new PerformanceMetrics(CpuTimeMs: 40, ElapsedTimeMs: 45, LogicalReads: 100, PhysicalReads: 0, ReadAheadReads: 0);
        var perf = new FakePerfService(perfA, perfB);

        var service = new OptimizationBenchmarkService(comp, perf, NullLogger<OptimizationBenchmarkService>.Instance);
        var result = await service.BenchmarkOptimizationAsync(new QueryBenchmarkArgs("TestDb", "A", "B"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("UnsafeDueToDataMismatch", result.Value.Verdict);
    }

    [Fact]
    public async Task BenchmarkOptimizationAsync_HigherResourceConsumption_ReturnsNotRecommended()
    {
        var comp = new FakeComparisonService(isEqual: true);
        var perfA = new PerformanceMetrics(CpuTimeMs: 50, ElapsedTimeMs: 60, LogicalReads: 100, PhysicalReads: 0, ReadAheadReads: 0);
        var perfB = new PerformanceMetrics(CpuTimeMs: 150, ElapsedTimeMs: 160, LogicalReads: 800, PhysicalReads: 0, ReadAheadReads: 0);
        var perf = new FakePerfService(perfA, perfB);

        var service = new OptimizationBenchmarkService(comp, perf, NullLogger<OptimizationBenchmarkService>.Instance);
        var result = await service.BenchmarkOptimizationAsync(new QueryBenchmarkArgs("TestDb", "A", "B"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("NotRecommended", result.Value.Verdict);
    }
}
