using System.CommandLine;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SqlToAi.Cli;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Mcp;
using SqlToAi.Tests.Database;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Mcp;

public sealed class ComposedTargetBindingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly IReadOnlyList<ToolDefinition> Definitions = new ToolRegistry().GetAll();

    public static TheoryData<string, string?> InvalidSdkTargets
    {
        get
        {
            var cases = new TheoryData<string, string?>();
            foreach (var definition in Definitions)
            {
                string? argument = TargetArgument(definition);
                if (argument is null) continue;
                string?[] values = argument == McpConstants.ArgDatabase
                    ? [null, "null", "\"\"", "\" \"", "\"Db\"", "\"/Db\"", "\"bad alias/Db\"", "\"unknown/SharedDb\"", "42", "{}", "[]"]
                    : [null, "null", "\"\"", "\" \"", "\"bad alias\"", "\"unknown\"", "42", "{}", "[]"];
                foreach (string? value in values) cases.Add(definition.Name, value);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(InvalidSdkTargets))]
    public async Task RegisteredSdk_InvalidOrMissingTargetsReturnCatalogErrorBeforeAnyConnection(string toolName, string? targetJson)
    {
        using var temp = TestTempDirectory.Create();
        var fixture = new ComposedTargetFixture();
        var definition = Definitions.Single(tool => tool.Name == toolName);
        var arguments = BaseArguments(definition, temp);
        string argument = TargetArgument(definition)!;
        if (targetJson is null) arguments.Remove(argument);
        else arguments[argument] = JsonSerializer.Deserialize<JsonElement>(targetJson);
        var result = await InvokeSdkAsync(fixture, toolName, arguments);
        Assert.True(result.IsError);
        Assert.Contains(SqlToAiError.InvalidParametersCode, Text(result), StringComparison.Ordinal);
        Assert.Empty(fixture.Factory.Connections);
        Assert.Empty(fixture.Factory.RuleLoads);
        Assert.False(Directory.Exists(temp.GetPath("dump")));
    }

    [Fact]
    public async Task RegisteredSdk_DiscoveryAndQueriesUseRealRoutingRulesAndCanonicalNames()
    {
        var fixture = new ComposedTargetFixture();
        var servers = await InvokeSdkAsync(fixture, McpConstants.ToolListServers, []);
        Assert.False(servers.IsError);
        Assert.Equal(["alpha", "beta"], JsonSerializer.Deserialize<string[]>(Text(servers))!);
        Assert.Empty(fixture.Factory.Connections);

        var databases = await InvokeSdkAsync(fixture, McpConstants.ToolListDatabases,
            new() { [McpConstants.ArgServer] = Json("ALPHA") });
        Assert.False(databases.IsError);
        Assert.Equal(["alpha/SharedDb"], JsonSerializer.Deserialize<string[]>(Text(databases))!);
        var catalog = Assert.Single(fixture.Factory.Connections);
        Assert.Equal("alpha.invalid", catalog.Settings.DataSource);
        Assert.Equal("", catalog.Settings.InitialCatalog);
        var search = await InvokeSdkAsync(fixture, McpConstants.ToolSearchDatabases,
            new() { [McpConstants.ArgServer] = Json("BETA"), [McpConstants.ArgSearchTerm] = Json("Shared") });
        Assert.Equal(["beta/SharedDb"], JsonSerializer.Deserialize<string[]>(Text(search))!);
        Assert.Equal("beta.invalid", fixture.Factory.Connections.Last().Settings.DataSource);

        foreach (string owner in new[] { "ALPHA", "BETA" })
        {
            var query = await InvokeSdkAsync(fixture, McpConstants.ToolExecuteQuery,
                new() { [McpConstants.ArgDatabase] = Json(owner + "/SharedDb"), [McpConstants.ArgQuery] = Json(ComposedTargetFixture.DataQuery) });
            Assert.False(query.IsError);
            using var data = JsonDocument.Parse(Assert.IsType<TextContentBlock>(query.Content.Last()).Text.Trim());
            string value = data.RootElement.GetProperty("Value").GetString()!;
            if (owner == "ALPHA") Assert.Equal("alpha/SharedDb-value", value);
            else Assert.StartsWith("§§§", value, StringComparison.Ordinal);
        }
        Assert.Equal(1, fixture.Factory.RuleLoads["alpha/SharedDb"]);
        Assert.Equal(1, fixture.Factory.RuleLoads["beta/SharedDb"]);
        Assert.DoesNotContain("synthetic-", Text(servers), StringComparison.Ordinal);
        Assert.DoesNotContain(".invalid", Text(servers), StringComparison.Ordinal);
    }

    public static TheoryData<string, string?> CliTargetValues
    {
        get
        {
            var cases = new TheoryData<string, string?>();
            foreach (var definition in Definitions.Where(tool => TargetArgument(tool) is not null))
            {
                cases.Add(definition.Name, null);
                cases.Add(definition.Name, "unknown" + (TargetArgument(definition) == McpConstants.ArgDatabase ? "/SharedDb" : ""));
                cases.Add(definition.Name, "bad alias");
                if (TargetArgument(definition) == McpConstants.ArgDatabase) cases.Add(definition.Name, "UnqualifiedDb");
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(CliTargetValues))]
    public async Task GeneratedCli_TargetFailuresPreserveParseAndCatalogFailureForms(string toolName, string? target)
    {
        using var temp = TestTempDirectory.Create();
        var fixture = new ComposedTargetFixture();
        var definition = Definitions.Single(tool => tool.Name == toolName);
        string argument = TargetArgument(definition)!;
        var arguments = BaseArguments(definition, temp);
        if (target is null) arguments.Remove(argument);
        else arguments[argument] = Json(target);
        ToolCallResult? result = null;
        var root = Cli(fixture, response => result = response);
        var parsed = root.Parse(Tokens(toolName, arguments));
        if (target is null)
        {
            Assert.NotEmpty(parsed.Errors);
            Assert.Null(result);
        }
        else
        {
            Assert.Empty(parsed.Errors);
            Assert.Equal(1, await parsed.InvokeAsync(cancellationToken: Token));
            Assert.NotNull(result);
            Assert.True(result.IsError);
            Assert.Contains(SqlToAiError.InvalidParametersCode, Assert.Single(result.Content).Text, StringComparison.Ordinal);
        }
        Assert.Empty(fixture.Factory.Connections);
        Assert.False(Directory.Exists(temp.GetPath("dump")));
    }

    [Fact]
    public async Task GeneratedCli_InvokesRealDiscoveryAndQueryServices()
    {
        var fixture = new ComposedTargetFixture();
        ToolCallResult? response = null;
        var root = Cli(fixture, result => response = result);
        Assert.Equal(0, await root.Parse(["query", McpConstants.ToolListServers]).InvokeAsync(cancellationToken: Token));
        Assert.Equal(["alpha", "beta"], JsonSerializer.Deserialize<string[]>(Assert.Single(response!.Content).Text!)!);
        Assert.Empty(fixture.Factory.Connections);
        Assert.Equal(0, await root.Parse(["query", McpConstants.ToolListDatabases, "--server", "ALPHA"]).InvokeAsync(cancellationToken: Token));
        Assert.Equal(["alpha/SharedDb"], JsonSerializer.Deserialize<string[]>(Assert.Single(response!.Content).Text!)!);
        Assert.Equal(0, await root.Parse(["query", McpConstants.ToolExecuteQuery, "--database", "ALPHA/SharedDb",
            "--query", ComposedTargetFixture.DataQuery]).InvokeAsync(cancellationToken: Token));
        using var data = JsonDocument.Parse(response!.Content.Last().Text!.Trim());
        Assert.Equal("alpha/SharedDb-value", data.RootElement.GetProperty("Value").GetString());
        Assert.StartsWith("§§§", data.RootElement.GetProperty("Secret").GetString()!, StringComparison.Ordinal);
        Assert.Equal(1, fixture.Factory.RuleLoads["alpha/SharedDb"]);
        Assert.All(fixture.Factory.Connections, connection => Assert.Equal("alpha.invalid", connection.Settings.DataSource));
    }

    [Theory]
    [InlineData(" Archive/Å ")]
    [InlineData("[Sales.Db]")]
    [InlineData(" ")]
    public async Task SdkAndCli_PreserveRawDatabaseCharactersThroughProductionConnectionConstruction(string rawDatabase)
    {
        foreach (bool sdk in new[] { true, false })
        {
            var configuration = ComposedTargetFixture.CreateConfiguration();
            configuration.Databases.ReadOnlyAnonymized.Add("alpha/" + rawDatabase);
            var fixture = new ComposedTargetFixture(configuration);
            fixture.Factory.ExemptValues["alpha/" + rawDatabase] = true;
            string data;
            if (sdk)
            {
                var result = await InvokeSdkAsync(fixture, McpConstants.ToolExecuteQuery,
                    new() { [McpConstants.ArgDatabase] = Json("ALPHA/" + rawDatabase), [McpConstants.ArgQuery] = Json(ComposedTargetFixture.DataQuery) });
                Assert.False(result.IsError);
                data = Assert.IsType<TextContentBlock>(result.Content.Last()).Text;
            }
            else
            {
                ToolCallResult? response = null;
                var root = Cli(fixture, result => response = result);
                Assert.Equal(0, await root.Parse(["query", McpConstants.ToolExecuteQuery, "--database", "ALPHA/" + rawDatabase,
                    "--query", ComposedTargetFixture.DataQuery]).InvokeAsync(cancellationToken: Token));
                data = response!.Content.Last().Text!;
            }
            using var json = JsonDocument.Parse(data.Trim());
            Assert.Equal("alpha/" + rawDatabase + "-value", json.RootElement.GetProperty("Value").GetString());
            Assert.Equal(2, fixture.Factory.Connections.Count);
            Assert.All(fixture.Factory.Connections, connection => Assert.Equal(rawDatabase, connection.Settings.InitialCatalog));
            Assert.Equal(1, fixture.Factory.RuleLoads["alpha/" + rawDatabase]);
        }
    }

    [Fact]
    public async Task PublicSchemasAndGeneratedHelpAdvertiseRequiredQualifiedTargets()
    {
        var fixture = new ComposedTargetFixture();
        var tools = SqlMcpToolRegistrations.BuildToolCollection(fixture.Dispatcher);
        Assert.Equal(22, tools.Count);
        foreach (var tool in tools)
        {
            var definition = Definitions.Single(item => item.Name == tool.ProtocolTool.Name);
            Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(definition.InputSchema, McpJsonContext.Default.ToolInputSchema),
                tool.ProtocolTool.InputSchema));
            string? target = TargetArgument(definition);
            if (target is null) continue;
            Assert.Contains(target, definition.InputSchema.Required);
            if (target == McpConstants.ArgDatabase)
                Assert.Equal(McpConstants.DatabaseTargetDescription,
                    tool.ProtocolTool.InputSchema.GetProperty("properties").GetProperty(target).GetProperty("description").GetString());
        }
        using var help = new StringWriter();
        var root = Cli(fixture, _ => throw new InvalidOperationException("Help must not dispatch a tool."));
        Assert.Equal(0, await root.Parse(["query", McpConstants.ToolExecuteQuery, "--help"])
            .InvokeAsync(new InvocationConfiguration { Output = help, Error = help }, Token));
        Assert.Contains("alias/database", help.ToString(), StringComparison.Ordinal);
        Assert.Contains("test/AppDb", help.ToString(), StringComparison.Ordinal);
        Assert.Contains(McpConstants.ToolListServers, help.ToString(), StringComparison.Ordinal);
        Assert.Contains(McpConstants.ToolListDatabases, help.ToString(), StringComparison.Ordinal);
        Assert.Empty(fixture.Factory.Connections);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispatcher_UnsupportedBoxedSelectionCannotBecomeAnAbsentFilter(bool export)
    {
        using var temp = TestTempDirectory.Create();
        foreach (object unsupported in new object[] { new object(), 1m, new[] { 1 }, new Dictionary<string, object?>() })
        {
            var fixture = new ComposedTargetFixture();
            var arguments = new Dictionary<string, object?>
            {
                [McpConstants.ArgDatabase] = "alpha/SharedDb", [McpConstants.ArgObjectName] = "dbo.T",
                [McpConstants.ArgObjectNames] = unsupported
            };
            if (export)
            {
                arguments[McpConstants.ArgScope] = "selected";
                arguments[McpConstants.ArgOutputPath] = temp.GetPath("dump");
            }
            var result = await fixture.Dispatcher.DispatchAsync(new ToolCallParams
            { Name = export ? McpConstants.ToolExportSchema : McpConstants.ToolGetSchema, Arguments = arguments }, Token);
            Assert.True(result.IsError);
            Assert.Contains(SqlToAiError.InvalidParametersCode, Assert.Single(result.Content).Text, StringComparison.Ordinal);
            Assert.Empty(fixture.Factory.Connections);
            Assert.False(Directory.Exists(temp.GetPath("dump")));
        }
    }

    private static RootCommand Cli(ComposedTargetFixture fixture, Action<ToolCallResult> receive)
    {
        var root = new RootCommand();
        root.Add(ToolCommandFactory.BuildQueryCommand(Definitions, async (name, arguments, ct) =>
        {
            var result = await fixture.Dispatcher.DispatchAsync(new ToolCallParams { Name = name, Arguments = arguments }, ct);
            receive(result);
            return result.IsError ? 1 : 0;
        }));
        return root;
    }

    private static string[] Tokens(string toolName, Dictionary<string, JsonElement> arguments) =>
        new[] { "query", toolName }.Concat(arguments.SelectMany(pair => new[] { "--" + pair.Key, pair.Value.GetString()! })).ToArray();

    private static string? TargetArgument(ToolDefinition tool) => tool.InputSchema.Properties.ContainsKey(McpConstants.ArgDatabase)
        ? McpConstants.ArgDatabase : tool.InputSchema.Properties.ContainsKey(McpConstants.ArgServer) ? McpConstants.ArgServer : null;

    private static Dictionary<string, JsonElement> BaseArguments(ToolDefinition tool, TestTempDirectory temp)
    {
        var all = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [McpConstants.ArgDatabase] = "alpha/SharedDb", [McpConstants.ArgServer] = "alpha", [McpConstants.ArgQuery] = ComposedTargetFixture.DataQuery,
            [McpConstants.ArgQueryA] = ComposedTargetFixture.DataQuery, [McpConstants.ArgQueryB] = ComposedTargetFixture.DataQuery,
            [McpConstants.ArgObjectName] = "dbo.T", [McpConstants.ArgTriggerName] = "Changed", [McpConstants.ArgSearchTerm] = "Shared",
            [McpConstants.ArgScope] = "all", [McpConstants.ArgOutputPath] = temp.GetPath("dump"),
            [McpConstants.ArgFilePath] = temp.CreateFile("input.sql", ComposedTargetFixture.DataQuery)
        };
        return all.Where(pair => tool.InputSchema.Properties.ContainsKey(pair.Key)
            && !(tool.Name == McpConstants.ToolExportSchema && pair.Key == McpConstants.ArgObjectName))
            .ToDictionary(pair => pair.Key, pair => Json(pair.Value), StringComparer.Ordinal);
    }

    private static JsonElement Json(string value) => JsonSerializer.SerializeToElement(value);
    private static string Text(CallToolResult result) => string.Join("\n", result.Content.Select(block => Assert.IsType<TextContentBlock>(block).Text));

    private static async Task<CallToolResult> InvokeSdkAsync(ComposedTargetFixture fixture, string toolName, Dictionary<string, JsonElement> arguments)
    {
        await using var transport = new StreamServerTransport(new MemoryStream(), new MemoryStream());
        await using var server = McpServer.Create(transport, new McpServerOptions { ServerInfo = new Implementation { Name = "composed", Version = "1" } });
        var tool = SqlMcpToolRegistrations.BuildToolCollection(fixture.Dispatcher).Single(item => item.ProtocolTool.Name == toolName);
        var context = new RequestContext<CallToolRequestParams>(server,
            new JsonRpcRequest { Id = new RequestId(1), Method = "tools/call" },
            new CallToolRequestParams { Name = toolName, Arguments = arguments });
        return await tool.InvokeAsync(context, Token);
    }
}
