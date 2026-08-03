#nullable enable

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Mcp;

namespace SqlToAi.Tests.Mcp;

/// <summary>
/// Unit tests for <see cref="ToolDispatcher"/> — verifies routing, argument extraction,
/// and error translation without hitting a real database.
/// </summary>
public sealed class ToolDispatcherTests
{
    private static ToolDispatcher BuildDispatcher(
        FakeSchemaService? schema = null,
        FakeQueryExecutionService? exec = null,
        FakeQueryValidationService? validation = null,
        FakeQueryComparisonService? comparison = null,
        FakePerformanceMeasurementService? perf = null,
        FakeOptimizationBenchmarkService? benchmark = null)
    {
        var options = new SqlToAiOptions();
        return new ToolDispatcher(
            schema ?? new FakeSchemaService(),
            exec   ?? new FakeQueryExecutionService(),
            validation ?? new FakeQueryValidationService(),
            comparison ?? new FakeQueryComparisonService(),
            perf       ?? new FakePerformanceMeasurementService(),
            benchmark  ?? new FakeOptimizationBenchmarkService(),
            Options.Create(options),
            NullLogger<ToolDispatcher>.Instance);
    }

    // -------------------------------------------------------------------------
    // Routing: each tool name dispatches to the correct service
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ListDatabases_ShouldRouteToSchemaService()
    {
        var schema = new FakeSchemaService();
        var dispatcher = BuildDispatcher(schema);

        var result = await dispatcher.DispatchAsync(Call(McpConstants.ToolListDatabases), TestContext.Current.CancellationToken);

        Assert.True(schema.ListDatabasesCalled);
        Assert.False(result.IsError);
    }

    [Fact]
    public async Task SearchDatabases_ShouldRouteToSchemaService()
    {
        var schema = new FakeSchemaService();
        var dispatcher = BuildDispatcher(schema);

        var result = await dispatcher.DispatchAsync(
            Call(McpConstants.ToolSearchDatabases, (McpConstants.ArgSearchTerm, "Demo")),
            TestContext.Current.CancellationToken);

        Assert.True(schema.SearchDatabasesCalled);
        Assert.False(result.IsError);
    }

    [Fact]
    public async Task ValidateQuery_ShouldRouteToValidationService()
    {
        var validation = new FakeQueryValidationService();
        var dispatcher = BuildDispatcher(validation: validation);

        var result = await dispatcher.DispatchAsync(
            Call(McpConstants.ToolValidateQuery, (McpConstants.ArgQuery, "SELECT 1"), (McpConstants.ArgDatabase, TestConstants.DatabaseName)),
            TestContext.Current.CancellationToken);

        Assert.True(validation.ValidateCalled);
        Assert.False(result.IsError);
    }

    [Fact]
    public async Task ExecuteQuery_ShouldRouteToExecutionService()
    {
        var exec = new FakeQueryExecutionService();
        var dispatcher = BuildDispatcher(exec: exec);

        var result = await dispatcher.DispatchAsync(
            Call(McpConstants.ToolExecuteQuery, (McpConstants.ArgQuery, "SELECT 1"), (McpConstants.ArgDatabase, TestConstants.DatabaseName)),
            TestContext.Current.CancellationToken);

        Assert.True(exec.ExecuteCalled);
        Assert.False(result.IsError);
    }

    [Fact]
    public async Task GetSchema_ShouldRouteToSchemaService()
    {
        var schema = new FakeSchemaService();
        var dispatcher = BuildDispatcher(schema);

        var result = await dispatcher.DispatchAsync(
            Call(McpConstants.ToolGetSchema, (McpConstants.ArgObjectName, "Customers"), (McpConstants.ArgDatabase, TestConstants.DatabaseName)),
            TestContext.Current.CancellationToken);

        Assert.True(schema.GetSchemaCalled);
        Assert.False(result.IsError);
    }

    // -------------------------------------------------------------------------
    // Database check
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenDatabaseArgMissing_ShouldReturnError()
    {
        var schema = new FakeSchemaService();
        var dispatcher = BuildDispatcher(schema);

        var result = await dispatcher.DispatchAsync(
            Call(McpConstants.ToolGetSchema, (McpConstants.ArgObjectName, "Orders")),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("Database name must be explicitly specified", result.Content[0].Text);
    }

    [Fact]
    public async Task WhenDatabaseArgProvided_ShouldUseIt()
    {
        var schema = new FakeSchemaService();
        var dispatcher = BuildDispatcher(schema);

        await dispatcher.DispatchAsync(
            Call(McpConstants.ToolGetSchema,
                (McpConstants.ArgObjectName, "Orders"),
                (McpConstants.ArgDatabase, "ExplicitDb")),
            TestContext.Current.CancellationToken);

        Assert.Equal("ExplicitDb", schema.LastDatabase);
    }

    // -------------------------------------------------------------------------
    // Error handling
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UnknownTool_ShouldReturnError()
    {
        var dispatcher = BuildDispatcher();
        var result = await dispatcher.DispatchAsync(
            Call("sql_nonexistent_tool"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        Assert.Contains("Unknown tool", result.Content[0].Text);
    }

    [Fact]
    public async Task MissingRequiredArg_ShouldReturnInvalidParametersError()
    {
        var dispatcher = BuildDispatcher();
        var result = await dispatcher.DispatchAsync(
            Call(McpConstants.ToolGetSchema),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        Assert.Contains(SqlToAiError.InvalidParametersCode, result.Content[0].Text);
    }

    [Fact]
    public async Task ServiceReturnsFailure_ShouldPropagateErrorCode()
    {
        var exec = new FakeQueryExecutionService(fail: true);
        var dispatcher = BuildDispatcher(exec: exec);

        var result = await dispatcher.DispatchAsync(
            Call(McpConstants.ToolExecuteQuery, (McpConstants.ArgQuery, "SELECT 1"), (McpConstants.ArgDatabase, TestConstants.DatabaseName)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains(SqlToAiError.SafetyCheckFailedCode, result.Content[0].Text);
    }

    // -------------------------------------------------------------------------
    // Argument extraction: integer
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQuery_ShouldForwardRowLimit()
    {
        var exec = new FakeQueryExecutionService();
        var dispatcher = BuildDispatcher(exec: exec);

        await dispatcher.DispatchAsync(
            Call(McpConstants.ToolExecuteQuery,
                (McpConstants.ArgQuery, "SELECT 1"),
                (McpConstants.ArgDatabase, TestConstants.DatabaseName),
                (McpConstants.ArgRequestedRowLimit, (object)50)),
            TestContext.Current.CancellationToken);

        Assert.Equal(50, exec.LastRowLimit);
    }

    [Fact]
    public async Task ExecuteQuery_ShouldReturnSingleContentBlock_WhenNotAnonymized()
    {
        var exec = new FakeQueryExecutionService(wasAnonymized: false);
        var dispatcher = BuildDispatcher(exec: exec);

        var result = await dispatcher.DispatchAsync(
            Call(McpConstants.ToolExecuteQuery,
                (McpConstants.ArgQuery, "SELECT 1"),
                (McpConstants.ArgDatabase, TestConstants.DatabaseName)),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Single(result.Content);
        Assert.Equal("{\"Col\":1}", result.Content[0].Text);
    }

    [Fact]
    public async Task ExecuteQuery_ShouldReturnTwoContentBlocks_WhenAnonymized()
    {
        var exec = new FakeQueryExecutionService(wasAnonymized: true);
        var dispatcher = BuildDispatcher(exec: exec);

        var result = await dispatcher.DispatchAsync(
            Call(McpConstants.ToolExecuteQuery,
                (McpConstants.ArgQuery, "SELECT 1"),
                (McpConstants.ArgDatabase, TestConstants.DatabaseName)),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Content.Count);
        Assert.Contains("anonymized (Mode: ScramblePattern)", result.Content[0].Text);
        Assert.Contains("columns were anonymized: FirstName, Email", result.Content[0].Text);
        Assert.Equal("{\"Col\":1}", result.Content[1].Text);
    }

    [Fact]
    public async Task ExecuteQuery_ShouldExplainReusableTokens_WhenSearchableColumnsPresent()
    {
        var exec = new FakeQueryExecutionService(wasAnonymized: true, withSearchableTokens: true);
        var dispatcher = BuildDispatcher(exec: exec);

        var result = await dispatcher.DispatchAsync(
            Call(McpConstants.ToolExecuteQuery,
                (McpConstants.ArgQuery, "SELECT 1"),
                (McpConstants.ArgDatabase, TestConstants.DatabaseName)),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains("Email", result.Content[0].Text);
        Assert.Contains("searchable tokens", result.Content[0].Text);
        Assert.Contains("reuse it verbatim", result.Content[0].Text);
    }

    [Fact]
    public async Task ExecuteQuery_ShouldNotMentionTokens_WhenNoSearchableColumns()
    {
        var exec = new FakeQueryExecutionService(wasAnonymized: true, withSearchableTokens: false);
        var dispatcher = BuildDispatcher(exec: exec);

        var result = await dispatcher.DispatchAsync(
            Call(McpConstants.ToolExecuteQuery,
                (McpConstants.ArgQuery, "SELECT 1"),
                (McpConstants.ArgDatabase, TestConstants.DatabaseName)),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.DoesNotContain("searchable tokens", result.Content[0].Text);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static ToolCallParams Call(string toolName, params (string key, object value)[] args)
        => new()
        {
            Name = toolName,
            Arguments = args.ToDictionary(a => a.key, a => (object?)a.value)
        };

    // -------------------------------------------------------------------------
    // Fakes
    // -------------------------------------------------------------------------

    private sealed class FakeSchemaService : ISchemaService
    {
        public bool ListDatabasesCalled { get; private set; }
        public bool SearchDatabasesCalled { get; private set; }
        public bool GetSchemaCalled { get; private set; }
        public string? LastDatabase { get; private set; }

        public Task<Result<IReadOnlyList<string>>> ListDatabasesAsync(CancellationToken ct = default)
        {
            ListDatabasesCalled = true;
            return Task.FromResult(Result<IReadOnlyList<string>>.Success([TestConstants.DatabaseName]));
        }

        public Task<Result<IReadOnlyList<string>>> SearchDatabasesAsync(string searchTerm, CancellationToken ct = default)
        {
            SearchDatabasesCalled = true;
            return Task.FromResult(Result<IReadOnlyList<string>>.Success([TestConstants.DatabaseName]));
        }

        public Task<Result<string>> SearchObjectsAsync(string db, string searchTerm, int? maxResults = null, string? objectType = null, CancellationToken ct = default)
        { LastDatabase = db; return Task.FromResult(Result<string>.Success("# Results")); }

        public Task<Result<string>> GetSchemaAsync(string db, string objectName, CancellationToken ct = default)
        { GetSchemaCalled = true; LastDatabase = db; return Task.FromResult(Result<string>.Success("# Schema")); }

        public Task<Result<string>> GetSchemaForeignKeysAsync(string db, string tableName, CancellationToken ct = default)
        { LastDatabase = db; return Task.FromResult(Result<string>.Success("# FK")); }

        public Task<Result<string>> GetSchemaIndexesAsync(string db, string tableName, CancellationToken ct = default)
        { LastDatabase = db; return Task.FromResult(Result<string>.Success("# Idx")); }

        public Task<Result<string>> GetSchemaConstraintsAsync(string db, string tableName, CancellationToken ct = default)
        { LastDatabase = db; return Task.FromResult(Result<string>.Success("# Constraints")); }

        public Task<Result<string>> GetTriggerDefinitionAsync(string db, string tableName, string triggerName, CancellationToken ct = default)
        { LastDatabase = db; return Task.FromResult(Result<string>.Success("# Trigger")); }

        public Task<Result<string>> GetObjectReferencesAsync(string db, string objectName, CancellationToken ct = default)
        { LastDatabase = db; return Task.FromResult(Result<string>.Success("# Refs")); }

        public Task<Result<string>> GetRoutineParametersAsync(string db, string routineName, CancellationToken ct = default)
        { LastDatabase = db; return Task.FromResult(Result<string>.Success("# Params")); }
    }

    private sealed class FakeQueryExecutionService(bool fail = false, bool wasAnonymized = false, bool withSearchableTokens = false) : IQueryExecutionService
    {
        private static readonly string[] AnonymizedColumnsSample = new[] { "FirstName", "Email" };
        private static readonly string[] SearchableTokenColumnsSample = new[] { "Email" };

        public bool ExecuteCalled { get; private set; }
        public int? LastRowLimit { get; private set; }

        public Task<Result<QueryExecutionResult>> ExecuteQueryAsync(string db, string query, int? requestedRowLimit, CancellationToken ct = default)
            => ExecuteQueryAsync(db, query, requestedRowLimit, parameters: null, ct);

        public Task<Result<QueryExecutionResult>> ExecuteQueryAsync(string db, string query, int? requestedRowLimit, object? parameters, CancellationToken ct = default)
        {
            ExecuteCalled = true;
            LastRowLimit = requestedRowLimit;

            if (fail)
            {
                return Task.FromResult<Result<QueryExecutionResult>>(SqlToAiError.SafetyCheckFailed(db));
            }

            var result = wasAnonymized
                ? new QueryExecutionResult("{\"Col\":1}", true, AnonymizedColumnsSample, "ScramblePattern")
                {
                    SearchableTokenColumns = withSearchableTokens ? SearchableTokenColumnsSample : Array.Empty<string>()
                }
                : new QueryExecutionResult("{\"Col\":1}", false, Array.Empty<string>(), "ScramblePattern");

            return Task.FromResult(Result<QueryExecutionResult>.Success(result));
        }
    }

    private sealed class FakeQueryValidationService : IQueryValidationService
    {
        public bool ValidateCalled { get; private set; }

        public Task<Result<string>> ValidateQueryAsync(string db, string query, CancellationToken ct = default)
            => ValidateQueryAsync(db, query, parameters: null, ct);

        public Task<Result<string>> ValidateQueryAsync(string db, string query, object? parameters, CancellationToken ct = default)
        {
            ValidateCalled = true;
            return Task.FromResult(Result<string>.Success("Query syntax is valid."));
        }
    }

    private sealed class FakeQueryComparisonService : IQueryComparisonService
    {
        public bool CompareCalled { get; private set; }

        public Task<Result<QueryComparisonResult>> CompareQueriesAsync(
            string databaseName, string queryA, string queryB, CancellationToken cancellationToken = default)
            => CompareQueriesAsync(new QueryComparisonArgs(databaseName, queryA, queryB), cancellationToken);

        public Task<Result<QueryComparisonResult>> CompareQueriesAsync(
            QueryComparisonArgs args, CancellationToken cancellationToken = default)
        {
            CompareCalled = true;
            var res = new QueryComparisonResult(true, true, true, 10, 10, Array.Empty<string>(), "[]", "[]");
            return Task.FromResult(Result<QueryComparisonResult>.Success(res));
        }
    }

    private sealed class FakePerformanceMeasurementService : IPerformanceMeasurementService
    {
        public bool MeasureCalled { get; private set; }

        public Task<Result<PerformanceMeasurementResult>> MeasurePerformanceAsync(
            string databaseName, string query, CancellationToken cancellationToken = default)
            => MeasurePerformanceAsync(new QueryPerformanceArgs(databaseName, query), cancellationToken);

        public Task<Result<PerformanceMeasurementResult>> MeasurePerformanceAsync(
            QueryPerformanceArgs args, CancellationToken cancellationToken = default)
        {
            MeasureCalled = true;
            var res = new PerformanceMeasurementResult(
                args.DatabaseName, 1, 1, new PerformanceMetrics(10, 15, 100, 0, 0), Array.Empty<PerformancePlanWarning>(), true, null);
            return Task.FromResult(Result<PerformanceMeasurementResult>.Success(res));
        }
    }

    private sealed class FakeOptimizationBenchmarkService : IOptimizationBenchmarkService
    {
        public bool BenchmarkCalled { get; private set; }

        public Task<Result<OptimizationBenchmarkResult>> BenchmarkOptimizationAsync(
            string databaseName, string queryA, string queryB, CancellationToken cancellationToken = default)
            => BenchmarkOptimizationAsync(new QueryBenchmarkArgs(databaseName, queryA, queryB), cancellationToken);

        public Task<Result<OptimizationBenchmarkResult>> BenchmarkOptimizationAsync(
            QueryBenchmarkArgs args, CancellationToken cancellationToken = default)
        {
            BenchmarkCalled = true;
            var comp = new QueryComparisonResult(true, true, true, 10, 10, Array.Empty<string>(), "[]", "[]");
            var perf = new PerformanceMeasurementResult(args.DatabaseName, 1, 1, new PerformanceMetrics(10, 15, 100, 0, 0), Array.Empty<PerformancePlanWarning>(), true, null);
            var deltas = new BenchmarkMetricsDelta(new MetricDelta(10, 5, -5, -50.0), new MetricDelta(15, 10, -5, -33.3), new MetricDelta(100, 50, -50, -50.0), new MetricDelta(0, 0, 0, 0.0));
            var res = new OptimizationBenchmarkResult(args.DatabaseName, "Recommended", "Summary", comp, perf, perf, deltas);
            return Task.FromResult(Result<OptimizationBenchmarkResult>.Success(res));
        }
    }
}
