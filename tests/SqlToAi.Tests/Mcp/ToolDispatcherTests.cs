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
        string defaultDatabase = "DemoDb")
    {
        var options = new SqlToAiOptions { Databases = new DatabasesOptions { Default = defaultDatabase } };
        return new ToolDispatcher(
            schema ?? new FakeSchemaService(),
            exec   ?? new FakeQueryExecutionService(),
            validation ?? new FakeQueryValidationService(),
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
            Call(McpConstants.ToolValidateQuery, (McpConstants.ArgQuery, "SELECT 1")),
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
            Call(McpConstants.ToolExecuteQuery, (McpConstants.ArgQuery, "SELECT 1")),
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
            Call(McpConstants.ToolGetSchema, (McpConstants.ArgObjectName, "Customers")),
            TestContext.Current.CancellationToken);

        Assert.True(schema.GetSchemaCalled);
        Assert.False(result.IsError);
    }

    // -------------------------------------------------------------------------
    // Default database fallback
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenDatabaseArgMissing_ShouldUseDefaultDatabase()
    {
        var schema = new FakeSchemaService();
        var dispatcher = BuildDispatcher(schema, defaultDatabase: "FallbackDb");

        await dispatcher.DispatchAsync(
            Call(McpConstants.ToolGetSchema, (McpConstants.ArgObjectName, "Orders")),
            TestContext.Current.CancellationToken);

        Assert.Equal("FallbackDb", schema.LastDatabase);
    }

    [Fact]
    public async Task WhenDatabaseArgProvided_ShouldUseItOverDefault()
    {
        var schema = new FakeSchemaService();
        var dispatcher = BuildDispatcher(schema, defaultDatabase: "FallbackDb");

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
            Call(McpConstants.ToolExecuteQuery, (McpConstants.ArgQuery, "SELECT 1")),
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
                (McpConstants.ArgRequestedRowLimit, (object)50)),
            TestContext.Current.CancellationToken);

        Assert.Equal(50, exec.LastRowLimit);
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
            return Task.FromResult(Result<IReadOnlyList<string>>.Success(["DemoDb"]));
        }

        public Task<Result<IReadOnlyList<string>>> SearchDatabasesAsync(string searchTerm, CancellationToken ct = default)
        {
            SearchDatabasesCalled = true;
            return Task.FromResult(Result<IReadOnlyList<string>>.Success(["DemoDb"]));
        }

        public Task<Result<string>> SearchObjectsAsync(string db, string searchTerm, int? maxResults = null, CancellationToken ct = default)
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

    private sealed class FakeQueryExecutionService(bool fail = false) : IQueryExecutionService
    {
        public bool ExecuteCalled { get; private set; }
        public int? LastRowLimit { get; private set; }

        public Task<Result<string>> ExecuteQueryAsync(string db, string query, int? requestedRowLimit, CancellationToken ct = default)
        {
            ExecuteCalled = true;
            LastRowLimit = requestedRowLimit;
            return fail
                ? Task.FromResult<Result<string>>(SqlToAiError.SafetyCheckFailed(db))
                : Task.FromResult(Result<string>.Success("{\"Col\":1}"));
        }
    }

    private sealed class FakeQueryValidationService : IQueryValidationService
    {
        public bool ValidateCalled { get; private set; }

        public Task<Result<string>> ValidateQueryAsync(string db, string query, CancellationToken ct = default)
        {
            ValidateCalled = true;
            return Task.FromResult(Result<string>.Success("Query syntax is valid."));
        }
    }
}
