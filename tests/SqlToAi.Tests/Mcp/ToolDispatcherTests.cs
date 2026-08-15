#nullable enable

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Mcp;

using static SqlToAi.Tests.Mcp.ToolDispatcherTestHelper;

namespace SqlToAi.Tests.Mcp;

/// <summary>
/// Unit tests for <see cref="ToolDispatcher"/> — verifies routing, argument extraction,
/// and error translation without hitting a real database.
/// </summary>
public sealed class ToolDispatcherTests
{
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
}
