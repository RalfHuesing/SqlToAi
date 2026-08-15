#nullable enable

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Mcp;

namespace SqlToAi.Tests.Mcp;

internal static class ToolDispatcherTestHelper
{
    public static ToolDispatcher BuildDispatcher(
        FakeSchemaService? schema = null,
        FakeQueryExecutionService? exec = null,
        FakeQueryValidationService? validation = null,
        DatabaseAnalysisServices? analysis = null)
    {
        var options = new SqlToAiOptions();
        return new ToolDispatcher(
            schema ?? new FakeSchemaService(),
            exec   ?? new FakeQueryExecutionService(),
            validation ?? new FakeQueryValidationService(),
            analysis ?? new DatabaseAnalysisServices(
                new FakePerformanceMeasurementService(),
                new FakeQueryComparisonService(),
                new FakeOptimizationBenchmarkService(),
                new FakeIndexSuggestionService()),
            Options.Create(options),
            NullLogger<ToolDispatcher>.Instance);
    }

    public static ToolCallParams Call(string toolName, params (string key, object value)[] args)
        => new()
        {
            Name = toolName,
            Arguments = args.ToDictionary(a => a.key, a => (object?)a.value)
        };
}

internal sealed class FakeSchemaService : ISchemaService
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

internal sealed class FakeQueryExecutionService(
    bool fail = false, bool wasAnonymized = false, bool withSearchableTokens = false,
    long cpuTimeMs = 0, long logicalReads = 0) : IQueryExecutionService
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
            ? new QueryExecutionResult("{\"Col\":1}", true, AnonymizedColumnsSample, "ScramblePattern", CpuTimeMs: cpuTimeMs, LogicalReads: logicalReads)
            {
                SearchableTokenColumns = withSearchableTokens ? SearchableTokenColumnsSample : Array.Empty<string>()
            }
            : new QueryExecutionResult("{\"Col\":1}", false, Array.Empty<string>(), "ScramblePattern", CpuTimeMs: cpuTimeMs, LogicalReads: logicalReads);

        return Task.FromResult(Result<QueryExecutionResult>.Success(result));
    }
}

internal sealed class FakeQueryValidationService : IQueryValidationService
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

internal sealed class FakeQueryComparisonService : IQueryComparisonService
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

internal sealed class FakePerformanceMeasurementService : IPerformanceMeasurementService
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

internal sealed class FakeOptimizationBenchmarkService : IOptimizationBenchmarkService
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

internal sealed class FakeIndexSuggestionService : IIndexSuggestionService
{
    public bool SuggestCalled { get; private set; }
    public IndexSuggestionArgs? LastArgs { get; private set; }

    public Task<Result<string>> SuggestIndexesAsync(
        string databaseName,
        string? tableName = null,
        double? minScore = null,
        int? top = null,
        CancellationToken cancellationToken = default)
        => SuggestIndexesAsync(new IndexSuggestionArgs(databaseName, tableName, minScore, top ?? 10), cancellationToken);

    public Task<Result<string>> SuggestIndexesAsync(
        IndexSuggestionArgs args,
        CancellationToken cancellationToken = default)
    {
        SuggestCalled = true;
        LastArgs = args;
        return Task.FromResult(Result<string>.Success("# Missing Index Recommendations"));
    }
}
