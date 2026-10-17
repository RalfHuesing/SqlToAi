using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;

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
        public int Calls { get; private set; }

        public Task<Result<PerformanceMeasurementResult>> MeasurePerformanceAsync(QueryPerformanceArgs args, CancellationToken ct = default)
        {
            Calls++;
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
        Assert.Equal(BenchmarkVerdict.Recommended, result.Value.Verdict);
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
        Assert.Equal(BenchmarkVerdict.UnsafeDueToDataMismatch, result.Value.Verdict);
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
        Assert.Equal(BenchmarkVerdict.NotRecommended, result.Value.Verdict);
    }

    [Fact]
    public async Task BenchmarkOptimizationAsync_PropagatesComparisonParameterConflictWithoutMeasuring()
    {
        var options = new SqlToAiOptions();
        var comparison = new QueryComparisonService(
            new QueryExecutionDependencies(
                new ValidationMockConnectionFactory(),
                FakeQuerySafetyValidator.Create(isAllowed: true, accessLevel: AccessLevel.ReadOnly),
                Options.Create(options)),
            NullLogger<QueryComparisonService>.Instance);
        var performance = new FakePerfService(
            new PerformanceMetrics(100, 120, 500, 0, 0),
            new PerformanceMetrics(40, 45, 100, 0, 0));
        var service = new OptimizationBenchmarkService(
            comparison, performance, NullLogger<OptimizationBenchmarkService>.Instance);

        var result = await service.BenchmarkOptimizationAsync(
            new QueryBenchmarkArgs("TestDb", "A", "B", ParametersA: new Dictionary<string, object?> { ["p"] = 1 },
                ParametersB: new Dictionary<string, object?> { ["@P"] = 2 }),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Equal(0, performance.Calls);
    }
}
