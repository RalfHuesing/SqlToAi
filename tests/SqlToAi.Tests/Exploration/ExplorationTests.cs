using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Exploration;
using SqlToAi.Exploration.Scenarios;
using SqlToAi.Mcp;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Exploration;

public sealed class ExplorationTests
{
    [Fact]
    public async Task CallAsync_PreservesTypedInputsAndAllContent_ContinuesAfterToolErrors()
    {
        using var temp = TestTempDirectory.Create();
        var calls = new List<ToolCallParams>();
        var dispatcher = new FakeDispatcher((request, _) =>
        {
            calls.Add(request);
            return Task.FromResult(calls.Count == 1
                ? ToolCallResult.Failure("SQL-AI-TEST", "Observed failure")
                : new ToolCallResult { Content = [new() { Text = "Notice" }, new() { Text = "Metrics" }, new() { Text = "Rows" }] });
        });
        var context = new ExplorationContext(dispatcher, temp.DirectoryPath, temp.DirectoryPath, TestContext.Current.CancellationToken);
        await context.CallAsync("../../unknown", new { parameters = new { Value = 42 }, requested_row_limit = 3 });
        var result = await context.CallAsync("sql_execute_query", new { query = "SELECT 1" });
        Assert.NotNull(result);
        Assert.False(result.IsError);
        using var request = JsonDocument.Parse(await File.ReadAllTextAsync(temp.GetPath("01/request.json"), TestContext.Current.CancellationToken));
        Assert.Equal(42, request.RootElement.GetProperty("arguments").GetProperty("parameters").GetProperty("Value").GetInt32());
        using var response = JsonDocument.Parse(await File.ReadAllTextAsync(temp.GetPath("01/response.json"), TestContext.Current.CancellationToken));
        Assert.True(response.RootElement.GetProperty("isError").GetBoolean());
        Assert.Equal(string.Join(Environment.NewLine, "Notice", "Metrics", "Rows"), await File.ReadAllTextAsync(temp.GetPath("02/response.txt"), TestContext.Current.CancellationToken));
        Assert.Equal("../../unknown", calls[0].Name);
        Assert.Equal(JsonValueKind.Number, ((JsonElement)calls[0].Arguments["requested_row_limit"]!).ValueKind);
    }

    [Fact]
    public async Task CallAsync_RecordsExceptionsAndContinues()
    {
        using var temp = TestTempDirectory.Create();
        var number = 0;
        var dispatcher = new FakeDispatcher((_, _) => ++number == 1
            ? throw new InvalidOperationException("Exploration exception")
            : Task.FromResult(ToolCallResult.Success("After exception")));
        var context = new ExplorationContext(dispatcher, temp.DirectoryPath, temp.DirectoryPath, TestContext.Current.CancellationToken);
        Assert.Null(await context.CallAsync("first", new { }));
        Assert.Contains("Exploration exception", await File.ReadAllTextAsync(temp.GetPath("01/error.txt"), TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.NotNull(await context.CallAsync("second", new { }));
        Assert.True(File.Exists(temp.GetPath("02/response.json")));
    }

    [Fact]
    public async Task CallAsync_PropagatesTimeoutInsteadOfContinuing()
    {
        using var temp = TestTempDirectory.Create();
        using var cancellation = new CancellationTokenSource();
        var dispatcher = new FakeDispatcher(async (_, token) =>
        {
            await cancellation.CancelAsync();
            return await Task.FromCanceled<ToolCallResult>(token);
        });
        var context = new ExplorationContext(dispatcher, temp.DirectoryPath, temp.DirectoryPath, cancellation.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.CallAsync("timeout", new { }));
        Assert.True(File.Exists(temp.GetPath("01/request.json")));
        Assert.False(File.Exists(temp.GetPath("01/response.json")));
    }

    [Fact]
    public async Task CallAsync_ArtifactFailurePropagatesBeforeDispatch()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("01", "Block the first call directory.");
        var called = false;
        var dispatcher = new FakeDispatcher((_, _) =>
        {
            called = true;
            return Task.FromResult(ToolCallResult.Success("Unexpected"));
        });
        var context = new ExplorationContext(dispatcher, temp.DirectoryPath, temp.DirectoryPath, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => context.CallAsync("sql_execute_query", new { }));
        Assert.False(called);
    }

    [Fact]
    public async Task ProductionConfigurationAndServices_AreReusedWithoutDatabaseConnection()
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("appsettings.json", """{"SqlToAi":{"Servers":{"primary":{"Server":"localhost"}},"Databases":{"ReadOnly":["primary/ExplorationTestDatabase"]}}}""");
        using var startup = UserConfiguration.Load(temp.DirectoryPath);
        await using var services = SqlToAi.Program.BuildServiceProvider(startup.Configuration, startup.Options);
        Assert.Contains("primary/ExplorationTestDatabase", services.GetRequiredService<IOptions<SqlToAiOptions>>().Value.Databases.ReadOnly);
        var dispatcher = services.GetRequiredService<IToolDispatcher>();
        var result = await dispatcher.DispatchAsync(new ToolCallParams { Name = "exploration_unknown_tool" }, TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        Assert.Single(result.Content);
    }

    [Fact]
    public void Registry_DiscoversPermanentScenarioCaseInsensitively()
    {
        Assert.IsType<ExploreSqlTools>(ScenarioRegistry.All["exploresqltools"]);
    }

    [Theory]
    [InlineData(false, "primary")]
    [InlineData(true, "primary")]
    [InlineData(false, "PRIMARY")]
    [InlineData(true, "PRIMARY")]
    public async Task BaselineManifest_UsesScenarioTargetRegardlessOfAliasOrderOrCasing(bool primaryFirst, string primaryAlias)
    {
        var options = new SqlToAiOptions();
        string[] aliases = primaryFirst ? [primaryAlias, "alpha"] : ["alpha", primaryAlias];
        foreach (string alias in aliases) options.Servers.Add(alias, new NamedServerOptions { Server = alias + "-host" });
        options.Databases.SchemaOnly.Add("primary/DemoDB");
        options.Databases.ReadWrite.Add("alpha/DemoDB");
        var access = new AccessLevelProvider(Options.Create(options));
        var baseline = await ExplorationRunner.DescribeBaselineAsync(options, access, TestContext.Current.CancellationToken);
        Assert.Equal(ExploreSqlTools.Database, baseline.Target);
        Assert.Equal(AccessLevel.SchemaOnly, baseline.AccessLevel);
    }

    [Fact]
    public async Task BaselineManifest_UnconfiguredPrimaryDoesNotSelectAnotherServer()
    {
        var options = new SqlToAiOptions();
        options.Servers.Add("alpha", new NamedServerOptions { Server = "alpha-host" });
        options.Databases.ReadWrite.Add("alpha/DemoDB");
        var baseline = await ExplorationRunner.DescribeBaselineAsync(options, new AccessLevelProvider(Options.Create(options)),
            TestContext.Current.CancellationToken);
        Assert.Equal(ExploreSqlTools.Database, baseline.Target);
        Assert.Equal(AccessLevel.None, baseline.AccessLevel);
    }

    [Fact]
    public async Task BaselineScenario_UsesItsManifestTargetForEveryDatabaseCall()
    {
        using var temp = TestTempDirectory.Create();
        var calls = new List<ToolCallParams>();
        var dispatcher = new FakeDispatcher((request, _) =>
        {
            calls.Add(request);
            return Task.FromResult(ToolCallResult.Success("observed"));
        });
        var context = new ExplorationContext(dispatcher, temp.DirectoryPath, temp.DirectoryPath, TestContext.Current.CancellationToken);
        await new ExploreSqlTools().RunAsync(context);
        var targets = calls.Where(call => call.Arguments.ContainsKey(McpConstants.ArgDatabase))
            .Select(call => Assert.IsType<JsonElement>(call.Arguments[McpConstants.ArgDatabase]).GetString()).ToArray();
        Assert.NotEmpty(targets);
        Assert.All(targets, target => Assert.Equal(ExploreSqlTools.Database, target));
        Assert.All(calls.Where(call => call.Arguments.ContainsKey(McpConstants.ArgServer)), call =>
            Assert.Equal(ExploreSqlTools.Server, Assert.IsType<JsonElement>(call.Arguments[McpConstants.ArgServer]).GetString()));
    }

    private sealed class FakeDispatcher(Func<ToolCallParams, CancellationToken, Task<ToolCallResult>> dispatch) : IToolDispatcher
    {
        public Task<ToolCallResult> DispatchAsync(ToolCallParams callParams, CancellationToken cancellationToken = default) => dispatch(callParams, cancellationToken);
    }
}
