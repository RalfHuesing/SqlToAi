#nullable enable

using SqlToAi.Domain;
using SqlToAi.Mcp;
using static SqlToAi.Tests.Mcp.ToolDispatcherTestHelper;

namespace SqlToAi.Tests.Mcp;

/// <summary>
/// Unit tests for <see cref="ToolDispatcher"/> query execution output formatting, redaction blocks, and token info.
/// </summary>
public sealed class ToolDispatcherExecutionTests
{

    [Fact]
    public async Task ExecuteQuery_ShouldReturnExecutionInfoAndData_WhenNotAnonymized()
    {
        var exec = new FakeQueryExecutionService(wasAnonymized: false);
        var dispatcher = BuildDispatcher(exec: exec);

        var result = await dispatcher.DispatchAsync(
            Call(McpConstants.ToolExecuteQuery,
                (McpConstants.ArgQuery, "SELECT 1"),
                (McpConstants.ArgDatabase, TestConstants.DatabaseName)),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Content.Count);
        Assert.Contains("Execution Info:", result.Content[0].Text);
        Assert.Equal("{\"Col\":1}", result.Content[1].Text);
    }

    [Fact]
    public async Task ExecuteQuery_ShouldReturnThreeContentBlocks_WhenAnonymized()
    {
        var exec = new FakeQueryExecutionService(wasAnonymized: true);
        var dispatcher = BuildDispatcher(exec: exec);

        var result = await dispatcher.DispatchAsync(
            Call(McpConstants.ToolExecuteQuery,
                (McpConstants.ArgQuery, "SELECT 1"),
                (McpConstants.ArgDatabase, TestConstants.DatabaseName)),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(3, result.Content.Count);
        Assert.Contains("anonymized (Mode: ScramblePattern)", result.Content[0].Text);
        Assert.Contains("columns were anonymized: FirstName, Email", result.Content[0].Text);
        Assert.Contains("Execution Info:", result.Content[1].Text);
        Assert.Equal("{\"Col\":1}", result.Content[2].Text);
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
    public async Task ExecuteQuery_ShouldIncludeCpuAndLogicalReads_InExecutionInfoText()
    {
        var exec = new FakeQueryExecutionService(cpuTimeMs: 12, logicalReads: 34);
        var dispatcher = BuildDispatcher(exec: exec);

        var result = await dispatcher.DispatchAsync(
            Call(McpConstants.ToolExecuteQuery,
                (McpConstants.ArgQuery, "SELECT 1"),
                (McpConstants.ArgDatabase, TestConstants.DatabaseName)),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains("cpu: 12 ms | logical reads: 34.", result.Content[0].Text);
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
}
