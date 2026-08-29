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
public sealed class ToolDispatcherTests : IDisposable
{
    private readonly string _tempDirectory;

    public ToolDispatcherTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "SqlToAiDispatcherTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
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
    public async Task ExecuteFile_ShouldForwardFileRequestAndRenderReport()
    {
        string filePath = WriteScript("SELECT @CustomerId;");
        var parameters = new Dictionary<string, object?> { ["CustomerId"] = 42 };
        var report = CreateReport(ScriptExecutionStatus.Success, ScriptTransactionMode.ReadWriteProviderAutocommit);
        var script = new FakeScriptExecutionService(report);
        var dispatcher = BuildDispatcher(script: script);

        var result = await dispatcher.DispatchAsync(
            Call(McpConstants.ToolExecuteFile,
                (McpConstants.ArgFilePath, filePath),
                (McpConstants.ArgDatabase, TestConstants.DatabaseName),
                (McpConstants.ArgUseTransaction, false),
                (McpConstants.ArgRequestedRowLimit, (object)25),
                (McpConstants.ArgParameters, parameters)),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Single(result.Content);
        Assert.Contains("# SQL Script Execution Report", result.Content[0].Text);
        Assert.True(script.ExecuteCalled);
        Assert.Equal(Path.GetFullPath(filePath), script.LastRequest?.ScriptFile.ResolvedPath);
        Assert.Equal("SELECT @CustomerId;", script.LastRequest?.ScriptFile.Text);
        Assert.Equal(TestConstants.DatabaseName, script.LastRequest?.DatabaseName);
        Assert.Equal(25, script.LastRequest?.RequestedRowLimit);
        Assert.Same(parameters, script.LastRequest?.Parameters);
        Assert.False(script.LastRequest?.UseTransaction ?? true);
    }

    [Fact]
    public async Task ExecuteFile_ShouldDefaultToTransaction()
    {
        string filePath = WriteScript("SELECT 1;");
        var script = new FakeScriptExecutionService();
        var dispatcher = BuildDispatcher(script: script);

        await dispatcher.DispatchAsync(
            Call(McpConstants.ToolExecuteFile,
                (McpConstants.ArgFilePath, filePath),
                (McpConstants.ArgDatabase, TestConstants.DatabaseName)),
            TestContext.Current.CancellationToken);

        Assert.True(script.LastRequest?.UseTransaction ?? false);
    }

    [Fact]
    public async Task ExecuteFile_ShouldReturnRenderedFailureReport()
    {
        string filePath = WriteScript("SELECT 1;");
        var error = SqlToAiError.QueryError("syntax failure");
        var batch = new SqlBatch("SELECT BAD", 3, 3);
        var failedBatch = ScriptExecutionReportFactory.BuildFailedBatch(2, batch, [], error);
        var report = ScriptExecutionReportFactory.BuildReport(new ScriptExecutionReportInput(
            new SqlScriptFile(filePath, "SELECT 1;", "UTF-8"),
            TestConstants.DatabaseName,
            ScriptTransactionMode.ReadWriteAtomic,
            [failedBatch],
            error));
        var dispatcher = BuildDispatcher(script: new FakeScriptExecutionService(report));

        var result = await dispatcher.DispatchAsync(
            Call(McpConstants.ToolExecuteFile,
                (McpConstants.ArgFilePath, filePath),
                (McpConstants.ArgDatabase, TestConstants.DatabaseName)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("# SQL Script Execution Report", result.Content[0].Text);
        Assert.Contains("failed_batch: 2", result.Content[0].Text);
        Assert.Contains("failed_source_lines: 3-3", result.Content[0].Text);
        Assert.Contains(SqlToAiError.QueryErrorCode, result.Content[0].Text);
        Assert.Contains("syntax failure", result.Content[0].Text);
    }

    [Fact]
    public async Task ExecuteFile_ShouldReturnFileErrorWithoutExecuting()
    {
        string filePath = Path.Combine(_tempDirectory, "missing.sql");
        var script = new FakeScriptExecutionService();
        var dispatcher = BuildDispatcher(script: script);

        var result = await dispatcher.DispatchAsync(
            Call(McpConstants.ToolExecuteFile,
                (McpConstants.ArgFilePath, filePath),
                (McpConstants.ArgDatabase, TestConstants.DatabaseName)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains(SqlToAiError.FileNotFoundCode, result.Content[0].Text);
        Assert.False(script.ExecuteCalled);
    }

    private string WriteScript(string content)
    {
        string filePath = Path.Combine(_tempDirectory, Guid.NewGuid().ToString("N") + ".sql");
        File.WriteAllText(filePath, content);
        return filePath;
    }

    private static ScriptExecutionReport CreateReport(
        ScriptExecutionStatus status,
        ScriptTransactionMode mode) =>
        new("script.sql", "UTF-8", TestConstants.DatabaseName, status, mode, 0, 0, 0, []);
}
