#nullable enable

using System.CommandLine;
using SqlToAi.Cli;
using SqlToAi.Mcp;

namespace SqlToAi.Tests.Cli;

/// <summary>
/// Unit tests for <see cref="ToolCommandFactory"/> — verifies the generated `query` command tree
/// mirrors <see cref="ToolRegistry"/> and correctly maps parsed CLI options into dispatcher arguments,
/// without touching <see cref="IToolDispatcher"/> or DI.
/// </summary>
public sealed class ToolCommandFactoryTests
{
    private static readonly IReadOnlyList<ToolDefinition> Tools = new ToolRegistry().GetAll();

    private static Command BuildQueryCommand(Func<string, Dictionary<string, object?>, CancellationToken, Task<int>> execute) =>
        ToolCommandFactory.BuildQueryCommand(Tools, execute);

    private static RootCommand WrapInRoot(Command queryCommand)
    {
        var root = new RootCommand("test root");
        root.Add(queryCommand);
        return root;
    }

    [Fact]
    public void BuildQueryCommand_ShouldCreate_OneSubcommandPerTool()
    {
        Command queryCommand = BuildQueryCommand((_, _, _) => Task.FromResult(0));

        var subcommandNames = queryCommand.Subcommands.Select(c => c.Name).ToHashSet();
        var toolNames = Tools.Select(t => t.Name).ToHashSet();

        Assert.Equal(toolNames, subcommandNames);
    }

    [Fact]
    public async Task ExecuteQuery_ShouldPassParsedStringArguments_ToExecuteCallback()
    {
        string? capturedTool = null;
        Dictionary<string, object?>? capturedArgs = null;

        Command queryCommand = BuildQueryCommand((toolName, args, _) =>
        {
            capturedTool = toolName;
            capturedArgs = args;
            return Task.FromResult(0);
        });

        RootCommand root = WrapInRoot(queryCommand);
        int exitCode = await root
            .Parse(["query", "sql_execute_query", "--database", "DemoDB", "--query", "SELECT 1"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Equal(McpConstants.ToolExecuteQuery, capturedTool);
        Assert.Equal("DemoDB", capturedArgs?[McpConstants.ArgDatabase]);
        Assert.Equal("SELECT 1", capturedArgs?[McpConstants.ArgQuery]);
        Assert.False(capturedArgs!.ContainsKey(McpConstants.ArgRequestedRowLimit));
    }

    [Fact]
    public async Task ExecuteQuery_ShouldParseIntegerOption_WhenProvided()
    {
        Dictionary<string, object?>? capturedArgs = null;

        Command queryCommand = BuildQueryCommand((_, args, _) =>
        {
            capturedArgs = args;
            return Task.FromResult(0);
        });

        RootCommand root = WrapInRoot(queryCommand);
        await root
            .Parse(["query", "sql_execute_query", "--database", "DemoDB", "--query", "SELECT 1", "--requested_row_limit", "5"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(5, capturedArgs?[McpConstants.ArgRequestedRowLimit]);
    }

    [Fact]
    public async Task ExecuteQuery_ShouldFailParsing_WhenRequiredOptionMissing()
    {
        bool executed = false;

        Command queryCommand = BuildQueryCommand((_, _, _) =>
        {
            executed = true;
            return Task.FromResult(0);
        });

        RootCommand root = WrapInRoot(queryCommand);
        ParseResult parseResult = root.Parse(["query", "sql_execute_query", "--query", "SELECT 1"]);

        Assert.NotEmpty(parseResult.Errors);

        await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(executed);
    }

    [Fact]
    public async Task SearchObjects_ShouldSucceed_WhenOptionalArgumentsOmitted()
    {
        Dictionary<string, object?>? capturedArgs = null;

        Command queryCommand = BuildQueryCommand((_, args, _) =>
        {
            capturedArgs = args;
            return Task.FromResult(0);
        });

        RootCommand root = WrapInRoot(queryCommand);
        ParseResult parseResult = root.Parse(
            ["query", "sql_search_objects", "--database", "DemoDB", "--search_term", "Customer"]);

        Assert.Empty(parseResult.Errors);

        int exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.False(capturedArgs!.ContainsKey(McpConstants.ArgMaxResults));
        Assert.False(capturedArgs.ContainsKey(McpConstants.ArgObjectType));
    }

    [Fact]
    public void SearchObjects_ShouldFailParsing_WhenSearchTermMissing()
    {
        Command queryCommand = BuildQueryCommand((_, _, _) => Task.FromResult(0));
        RootCommand root = WrapInRoot(queryCommand);

        ParseResult parseResult = root.Parse(["query", "sql_search_objects", "--database", "DemoDB"]);

        Assert.NotEmpty(parseResult.Errors);
    }
}
