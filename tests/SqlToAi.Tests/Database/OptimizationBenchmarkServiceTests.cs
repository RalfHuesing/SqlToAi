using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Unit tests for <see cref="OptimizationBenchmarkService"/>, validating verdict determination and metric deltas.
/// </summary>
public sealed class OptimizationBenchmarkServiceTests
{
    private sealed class FakeComparisonService(bool isEqual) : IQueryComparisonService
    {
        public List<DatabaseTargetContext> Targets { get; } = [];
        public List<string> Databases { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public Task<Result<QueryComparisonResult>> CompareQueriesAsync(DatabaseTargetContext target, QueryComparisonArgs args, CancellationToken ct = default)
        {
            Targets.Add(target);
            Databases.Add(args.DatabaseName);
            Tokens.Add(ct);
            return CompareQueriesAsync(args with { DatabaseName = target.QualifiedName }, ct);
        }

        public SqlToAiError? Failure { get; init; }
        public Task<Result<QueryComparisonResult>> CompareQueriesAsync(QueryComparisonArgs args, CancellationToken ct = default)
        {
            if (Failure is not null) return Task.FromResult(Result<QueryComparisonResult>.Failure(Failure));
            var res = new QueryComparisonResult(isEqual, isEqual, isEqual, 10, 10, Array.Empty<string>(), "[]", "[]");
            return Task.FromResult(Result<QueryComparisonResult>.Success(res));
        }

        public Task<Result<QueryComparisonResult>> CompareQueriesAsync(string db, string qA, string qB, CancellationToken ct = default)
            => CompareQueriesAsync(new QueryComparisonArgs(db, qA, qB), ct);
    }

    private sealed class FakePerfService(PerformanceMetrics metricsA, PerformanceMetrics metricsB) : IPerformanceMeasurementService
    {
        public List<DatabaseTargetContext> Targets { get; } = [];
        public List<string> Databases { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public Task<Result<PerformanceMeasurementResult>> MeasurePerformanceAsync(DatabaseTargetContext target, QueryPerformanceArgs args, CancellationToken ct = default)
        {
            Targets.Add(target);
            Databases.Add(args.DatabaseName);
            Tokens.Add(ct);
            return MeasurePerformanceAsync(args with { DatabaseName = target.QualifiedName }, ct);
        }

        public int Calls { get; private set; }
        public int FailOnCall { get; init; }
        public SqlToAiError Failure { get; } = SqlToAiError.QueryError("measurement failure");

        public Task<Result<PerformanceMeasurementResult>> MeasurePerformanceAsync(QueryPerformanceArgs args, CancellationToken ct = default)
        {
            Calls++;
            if (Calls == FailOnCall) return Task.FromResult(Result<PerformanceMeasurementResult>.Failure(Failure));
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

        var service = new OptimizationBenchmarkService(comp, perf, TestTargets.BoundOptions);
        var result = await service.BenchmarkOptimizationAsync(new QueryBenchmarkArgs(TestConstants.QualifiedDatabaseName, "A", "B"), TestContext.Current.CancellationToken);

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

        var service = new OptimizationBenchmarkService(comp, perf, TestTargets.BoundOptions);
        var result = await service.BenchmarkOptimizationAsync(new QueryBenchmarkArgs(TestConstants.QualifiedDatabaseName, "A", "B"), TestContext.Current.CancellationToken);

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

        var service = new OptimizationBenchmarkService(comp, perf, TestTargets.BoundOptions);
        var result = await service.BenchmarkOptimizationAsync(new QueryBenchmarkArgs(TestConstants.QualifiedDatabaseName, "A", "B"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(BenchmarkVerdict.NotRecommended, result.Value.Verdict);
    }

    [Fact]
    public async Task BenchmarkOptimizationAsync_PropagatesComparisonParameterConflictWithoutMeasuring()
    {
        var options = new SqlToAiOptions().WithNamedServers(TestTargets.ServerAlias);
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
            comparison, performance, Options.Create(options));

        var result = await service.BenchmarkOptimizationAsync(
            new QueryBenchmarkArgs(TestConstants.QualifiedDatabaseName, "A", "B", ParametersA: new Dictionary<string, object?> { ["p"] = 1 },
                ParametersB: new Dictionary<string, object?> { ["@P"] = 2 }),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Equal(0, performance.Calls);
    }
    [Fact]
    public async Task Benchmark_SharesOneCanonicalContextAndCancellationAcrossAllChildren()
    {
        var comparison = new FakeComparisonService(true);
        var metrics = new PerformanceMetrics(1, 1, 1, 0, 0);
        var performance = new FakePerfService(metrics, metrics);
        var service = new OptimizationBenchmarkService(comparison, performance, TestTargets.BoundOptions);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        const string rawDatabase = " Raw/Db Å ";
        var result = await service.BenchmarkOptimizationAsync("TEST/" + rawDatabase, "A", "B", cancellation.Token);
        Assert.True(result.IsSuccess);
        DatabaseTargetContext target = Assert.Single(comparison.Targets);
        Assert.Equal(rawDatabase, target.DatabaseName);
        Assert.Equal("test/" + rawDatabase, result.Value.Database);
        Assert.Equal(2, performance.Targets.Count);
        Assert.All(performance.Targets, child => Assert.Same(target, child));
        Assert.All(comparison.Databases.Concat(performance.Databases), database => Assert.Equal(target.QualifiedName, database));
        Assert.All(comparison.Tokens.Concat(performance.Tokens), token => Assert.Equal(cancellation.Token, token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Db")]
    [InlineData("bad alias/Db")]
    [InlineData("unknown/Db")]
    public async Task Benchmark_InvalidTargetStopsBeforeAnyChild(string? database)
    {
        var comparison = new FakeComparisonService(true);
        var metrics = new PerformanceMetrics(1, 1, 1, 0, 0);
        var performance = new FakePerfService(metrics, metrics);
        var service = new OptimizationBenchmarkService(comparison, performance, TestTargets.BoundOptions);
        var result = await service.BenchmarkOptimizationAsync(database!, "A", "B", TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Empty(comparison.Targets);
        Assert.Empty(performance.Targets);
        Assert.Equal(0, performance.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Benchmark_ChildFailureStopsLaterWorkAndRetainsItsError(int failingStage)
    {
        var failure = SqlToAiError.QueryError("comparison failure");
        var comparison = new FakeComparisonService(true) { Failure = failingStage == 0 ? failure : null };
        var metrics = new PerformanceMetrics(1, 1, 1, 0, 0);
        var performance = new FakePerfService(metrics, metrics) { FailOnCall = failingStage };
        var service = new OptimizationBenchmarkService(comparison, performance, TestTargets.BoundOptions);
        var result = await service.BenchmarkOptimizationAsync(TestConstants.QualifiedDatabaseName, "A", "B", TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(failingStage == 0 ? failure : performance.Failure, result.Error);
        DatabaseTargetContext target = Assert.Single(comparison.Targets);
        Assert.Equal(failingStage, performance.Calls);
        Assert.All(performance.Targets, child => Assert.Same(target, child));
    }
}
