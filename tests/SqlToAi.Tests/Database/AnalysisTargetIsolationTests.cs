using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Milestone 3.2 acceptance tests: verifies database target qualification, security policy pre-checks,
/// SchemaOnly rejection/acceptance, cross-server same-database isolation, and canonical target reporting
/// across <see cref="PerformanceMeasurementService"/>, <see cref="QueryComparisonService"/>,
/// <see cref="OptimizationBenchmarkService"/>, and <see cref="IndexSuggestionService"/>.
/// </summary>
public sealed class AnalysisTargetIsolationTests
{
    private sealed class TrackingAnalysisConnectionFactory : IDatabaseConnectionFactory
    {
        public List<DatabaseTargetContext> OpenedTargets { get; } = [];
        public int ConnectionCount { get; private set; }

        public DbConnection CreateTargetConnection(DatabaseTargetContext target)
        {
            ConnectionCount++;
            OpenedTargets.Add(target);
            return new FakeDbConnection(
                conn => new FakeDbCommand(conn, new FakeDbCommandHandlers(
                    ExecuteReader: cmd => HandleReader(cmd),
                    ExecuteScalar: cmd => HandleScalar(cmd),
                    ExecuteNonQuery: cmd => 1)),
                new FakeDbConnectionOptions(
                    Database: target.DatabaseName,
                    DataSource: target.ServerAlias,
                    ServerVersion: "16.0",
                    BeginTransaction: (conn, _) => new FakeDbTransaction(conn)));
        }

        public Result<DbConnection> CreateCatalogConnection(string? serverAlias) => throw new NotSupportedException();
        public DbConnection CreateConnection(string? databaseName = null) => throw new NotSupportedException();

        private static FakeDbDataReader HandleReader(FakeDbCommand cmd)
        {
            if (cmd.CommandText.Contains("sys.dm_db_missing_index_details", StringComparison.Ordinal))
            {
                return new FakeDbDataReader(
                    ["Statement", "IndexHandle", "UserSeeks", "UserScans", "LastUserSeek", "AvgTotalUserCost", "AvgUserImpact", "ColumnId", "ColumnUsage"],
                    []);
            }
            if (cmd.CommandText.Contains(" EXCEPT ", StringComparison.Ordinal))
            {
                return new FakeDbDataReader(["Value"], []);
            }
            return new FakeDbDataReader(["Value"], [[1]]);
        }

        private static object? HandleScalar(FakeDbCommand cmd) => 1L;
    }

    private static (DatabaseAnalysisServices Services, TrackingAnalysisConnectionFactory Factory) CreateServices(
        Action<SqlToAiOptions>? configure = null)
    {
        var options = new SqlToAiOptions()
            .WithNamedServers("srv1", "srv2");
        options.Databases.ReadOnly = ["srv1/SharedDb", "srv2/SharedDb", "srv1/WhitelistedDb"];
        options.Databases.SchemaOnly = ["srv1/SchemaDb"];
        configure?.Invoke(options);

        var bound = Options.Create(options);
        var factory = new TrackingAnalysisConnectionFactory();
        var guard = new SecurityGuard(bound);
        var accessProvider = new AccessLevelProvider(factory, bound, NullLogger<AccessLevelProvider>.Instance);
        var readOnlyGuard = new ReadOnlyGuard();
        var safetyValidator = new QuerySafetyValidator(guard, accessProvider, readOnlyGuard);
        var dependencies = new QueryExecutionDependencies(factory, safetyValidator, bound);

        var perf = new PerformanceMeasurementService(dependencies, NullLogger<PerformanceMeasurementService>.Instance);
        var comp = new QueryComparisonService(dependencies, NullLogger<QueryComparisonService>.Instance);
        var bench = new OptimizationBenchmarkService(comp, perf, NullLogger<OptimizationBenchmarkService>.Instance);
        var idx = new IndexSuggestionService(factory, guard, accessProvider, bound, NullLogger<IndexSuggestionService>.Instance);

        var services = new DatabaseAnalysisServices(perf, comp, bench, idx);
        return (services, factory);
    }

    [Theory]
    [InlineData("SharedDb")]
    [InlineData("srv1//SharedDb")]
    [InlineData("unknown_alias/SharedDb")]
    [InlineData("   ")]
    public async Task PerformanceMeasurement_RejectsUnqualifiedOrUnknownTargetBeforeConnecting(string invalidTarget)
    {
        var (services, factory) = CreateServices();
        var result = await services.PerformanceMeasurement.MeasurePerformanceAsync(
            new QueryPerformanceArgs(invalidTarget, "SELECT 1"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(0, factory.ConnectionCount);
        Assert.Empty(factory.OpenedTargets);
    }

    [Theory]
    [InlineData("SharedDb")]
    [InlineData("srv1//SharedDb")]
    [InlineData("unknown_alias/SharedDb")]
    [InlineData("   ")]
    public async Task QueryComparison_RejectsUnqualifiedOrUnknownTargetBeforeConnecting(string invalidTarget)
    {
        var (services, factory) = CreateServices();
        var result = await services.QueryComparison.CompareQueriesAsync(
            new QueryComparisonArgs(invalidTarget, "SELECT 1", "SELECT 2"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(0, factory.ConnectionCount);
        Assert.Empty(factory.OpenedTargets);
    }

    [Theory]
    [InlineData("SharedDb")]
    [InlineData("srv1//SharedDb")]
    [InlineData("unknown_alias/SharedDb")]
    [InlineData("   ")]
    public async Task IndexSuggestions_RejectsUnqualifiedOrUnknownTargetBeforeConnecting(string invalidTarget)
    {
        var (services, factory) = CreateServices();
        var result = await services.IndexSuggestion.SuggestIndexesAsync(
            new IndexSuggestionArgs(invalidTarget),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(0, factory.ConnectionCount);
        Assert.Empty(factory.OpenedTargets);
    }

    [Fact]
    public async Task Analysis_RejectsTargetBlockedByStaticWhitelistBeforeConnecting()
    {
        var (services, factory) = CreateServices();

        var perfResult = await services.PerformanceMeasurement.MeasurePerformanceAsync(
            new QueryPerformanceArgs("srv1/BlockedDb", "SELECT 1"),
            TestContext.Current.CancellationToken);
        Assert.True(perfResult.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, perfResult.Error.Code);

        var compResult = await services.QueryComparison.CompareQueriesAsync(
            new QueryComparisonArgs("srv1/BlockedDb", "SELECT 1", "SELECT 2"),
            TestContext.Current.CancellationToken);
        Assert.True(compResult.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, compResult.Error.Code);

        var idxResult = await services.IndexSuggestion.SuggestIndexesAsync(
            new IndexSuggestionArgs("srv1/BlockedDb"),
            TestContext.Current.CancellationToken);
        Assert.True(idxResult.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, idxResult.Error.Code);

        Assert.Equal(0, factory.ConnectionCount);
    }

    [Fact]
    public async Task SchemaOnlyTarget_RejectedByExecutionBasedAnalysis_AcceptedByIndexSuggestions()
    {
        var (services, factory) = CreateServices();

        // Execution-based services must reject SchemaOnly access
        var perfResult = await services.PerformanceMeasurement.MeasurePerformanceAsync(
            new QueryPerformanceArgs("srv1/SchemaDb", "SELECT 1"),
            TestContext.Current.CancellationToken);
        Assert.True(perfResult.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, perfResult.Error.Code);

        var compResult = await services.QueryComparison.CompareQueriesAsync(
            new QueryComparisonArgs("srv1/SchemaDb", "SELECT 1", "SELECT 2"),
            TestContext.Current.CancellationToken);
        Assert.True(compResult.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, compResult.Error.Code);

        // Index suggestions reads DMVs and only requires accessLevel != None
        var idxResult = await services.IndexSuggestion.SuggestIndexesAsync(
            new IndexSuggestionArgs("srv1/SchemaDb"),
            TestContext.Current.CancellationToken);
        Assert.True(idxResult.IsSuccess);
        Assert.Contains("# Missing Index Recommendations — srv1/SchemaDb", idxResult.Value);
    }

    [Fact]
    public async Task Analysis_SameNamedDatabaseOnDifferentServers_RoutesToCorrectContext()
    {
        var (services, factory) = CreateServices();

        var perf1 = await services.PerformanceMeasurement.MeasurePerformanceAsync(
            new QueryPerformanceArgs("srv1/SharedDb", "SELECT 1"),
            TestContext.Current.CancellationToken);
        var perf2 = await services.PerformanceMeasurement.MeasurePerformanceAsync(
            new QueryPerformanceArgs("srv2/SharedDb", "SELECT 1"),
            TestContext.Current.CancellationToken);

        Assert.True(perf1.IsSuccess);
        Assert.True(perf2.IsSuccess);
        Assert.Equal("srv1/SharedDb", perf1.Value.Database);
        Assert.Equal("srv2/SharedDb", perf2.Value.Database);

        var idx1 = await services.IndexSuggestion.SuggestIndexesAsync(
            new IndexSuggestionArgs("srv1/SharedDb"),
            TestContext.Current.CancellationToken);
        var idx2 = await services.IndexSuggestion.SuggestIndexesAsync(
            new IndexSuggestionArgs("srv2/SharedDb"),
            TestContext.Current.CancellationToken);

        Assert.True(idx1.IsSuccess);
        Assert.True(idx2.IsSuccess);
        Assert.Contains("# Missing Index Recommendations — srv1/SharedDb", idx1.Value);
        Assert.Contains("# Missing Index Recommendations — srv2/SharedDb", idx2.Value);

        Assert.Contains(factory.OpenedTargets, t => t.ServerAlias == "srv1" && t.DatabaseName == "SharedDb");
        Assert.Contains(factory.OpenedTargets, t => t.ServerAlias == "srv2" && t.DatabaseName == "SharedDb");
    }

    [Fact]
    public async Task BenchmarkOptimization_CarriesSameTargetContextAndCanonicalReporting()
    {
        var (services, factory) = CreateServices();

        var bench = await services.Benchmark.BenchmarkOptimizationAsync(
            "srv1/WhitelistedDb", "SELECT 1", "SELECT 2",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(bench.IsSuccess);
        Assert.Equal("srv1/WhitelistedDb", bench.Value.Database);
        Assert.All(factory.OpenedTargets, t =>
        {
            Assert.Equal("srv1", t.ServerAlias);
            Assert.Equal("WhitelistedDb", t.DatabaseName);
        });
    }
}
