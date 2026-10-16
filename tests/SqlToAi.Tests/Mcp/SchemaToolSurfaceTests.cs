using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SqlToAi.Cli;
using SqlToAi.Mcp;
using SqlToAi.Tests.Database;

namespace SqlToAi.Tests.Mcp;

public sealed class SchemaToolSurfaceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<CallToolResult> InvokeRegisteredAsync(ToolDispatcher dispatcher, string selection)
    {
        await using var transport = new StreamServerTransport(new MemoryStream(), new MemoryStream());
        await using var server = McpServer.Create(transport, new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "schema-binding-test", Version = "1" }
        }, serviceProvider: new ServiceCollection().BuildServiceProvider());
        var tool = SqlMcpToolRegistrations.BuildToolCollection(dispatcher).Single(t => t.ProtocolTool.Name == McpConstants.ToolGetSchema);
        var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(selection)!;
        arguments["database"] = JsonSerializer.SerializeToElement("DemoDB");
        var parameters = new CallToolRequestParams { Name = McpConstants.ToolGetSchema, Arguments = arguments };
        var context = new RequestContext<CallToolRequestParams>(server,
            new JsonRpcRequest { Id = new RequestId(1), Method = "tools/call" }, parameters);
        return await tool.InvokeAsync(context, Token);
    }

    [Theory]
    [InlineData("{\"object_name\":\"Orders\"}")]
    [InlineData("{\"object_name\":\"Orders\",\"object_names\":null}")]
    public async Task RegisteredSdk_OmittedOrNullArray_PreservesScalarMarkdown(string selection)
    {
        var factory = new SchemaBatchTestFactory();
        var result = await InvokeRegisteredAsync(ToolDispatcherTestHelper.BuildDispatcher(schema: factory.Service()), selection);
        Assert.False(result.IsError);
        string markdown = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.StartsWith("#", markdown);
        var scalar = await factory.Service().GetSchemaAsync("DemoDB", "Orders", Token);
        Assert.Equal(scalar.Value, markdown);
    }

    [Theory]
    [InlineData("{\"object_names\":[\"Orders\"]}", "Orders")]
    [InlineData("{\"object_names\":[\"Orders\",\"Customers\"]}", "Orders,Customers")]
    [InlineData("{\"object_name\":\" Orders \",\"object_names\":[null,\"\",\" \",\"Customers\",\"Orders\"]}", "Orders,Customers,Orders")]
    [InlineData("{\"object_name\":\"Orders\",\"object_names\":[]}", "Orders")]
    [InlineData("{\"object_name\":null,\"object_names\":[\"Orders\"]}", "Orders")]
    public async Task RegisteredSdk_SuppliedArrays_SelectOrderedBatch(string selection, string expectedNames)
    {
        var factory = new SchemaBatchTestFactory();
        var result = await InvokeRegisteredAsync(ToolDispatcherTestHelper.BuildDispatcher(schema: factory.Service()), selection);
        Assert.False(result.IsError);
        using var json = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.True(json.RootElement.GetProperty("complete").GetBoolean());
        var entries = json.RootElement.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(expectedNames.Split(','), entries.Select(e => e.GetProperty("object_name").GetString()));
        Assert.All(entries, e => Assert.Equal("ok", e.GetProperty("status").GetString()));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"object_names\":null}")]
    [InlineData("{\"object_name\":null}")]
    [InlineData("{\"object_name\":\" \"}")]
    [InlineData("{\"object_name\":42}")]
    [InlineData("{\"object_names\":[]}")]
    [InlineData("{\"object_names\":[null,\" \",\"\"]}")]
    [InlineData("{\"object_names\":\"Orders\"}")]
    [InlineData("{\"object_names\":42}")]
    [InlineData("{\"object_names\":{}}")]
    [InlineData("{\"object_names\":[\"Orders\",3]}")]
    [InlineData("{\"object_names\":[true]}")]
    [InlineData("{\"object_name\":{},\"object_names\":[\"Orders\"]}")]
    public async Task RegisteredSdk_InvalidSelections_FailBeforeDatabaseWork(string selection)
    {
        var factory = new SchemaBatchTestFactory();
        var result = await InvokeRegisteredAsync(ToolDispatcherTestHelper.BuildDispatcher(schema: factory.Service()), selection);
        Assert.True(result.IsError);
        Assert.Contains("SQL-AI-0001", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Equal(0, factory.ConnectionCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegisteredSdk_IncompleteBatch_PreservesEnvelopeAndSetsError(bool omit)
    {
        var factory = new SchemaBatchTestFactory();
        if (omit) factory.Options.SchemaBatch.MaxResponseBytes = 350;
        var result = await InvokeRegisteredAsync(ToolDispatcherTestHelper.BuildDispatcher(schema: factory.Service()),
            "{\"object_names\":[\"Orders\",\"missing\",\"Customers\"]}");
        Assert.True(result.IsError);
        using var json = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.False(json.RootElement.GetProperty("complete").GetBoolean());
        var entries = json.RootElement.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(3, entries.Length);
        Assert.Equal(omit ? new[] { "not_returned", "not_returned", "not_returned" } : ["ok", "error", "ok"],
            entries.Select(e => e.GetProperty("status").GetString()));
        if (omit) Assert.All(entries, e => Assert.Equal("response_limit", e.GetProperty("reason").GetString()));
        else Assert.Equal("SQL-AI-0103", entries[1].GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegisteredSdk_RequestWideFailures_SetErrorBeforeConnections(bool denied)
    {
        var factory = new SchemaBatchTestFactory();
        if (denied) factory.Options.Databases.SchemaOnly.Clear();
        else factory.Options.SchemaBatch.MaxInputCount = 1;
        var result = await InvokeRegisteredAsync(ToolDispatcherTestHelper.BuildDispatcher(schema: factory.Service()),
            "{\"object_names\":[\"Orders\",\"Orders\"]}");
        Assert.True(result.IsError);
        Assert.Contains(denied ? "SQL-AI-0104" : "SQL-AI-0001", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Equal(0, factory.ConnectionCount);
    }

    [Fact]
    public async Task Dispatcher_NativeStringArray_ReachesBatchService()
    {
        var factory = new SchemaBatchTestFactory();
        var result = await ToolDispatcherTestHelper.BuildDispatcher(schema: factory.Service()).DispatchAsync(new ToolCallParams
        {
            Name = McpConstants.ToolGetSchema,
            Arguments = new() { ["database"] = "DemoDB", ["object_names"] = new[] { "Orders" } }
        }, Token);
        Assert.False(result.IsError);
        using var json = JsonDocument.Parse(Assert.Single(result.Content).Text!);
        Assert.True(json.RootElement.GetProperty("complete").GetBoolean());
    }

    [Fact]
    public void RegisteredSdkSchema_EqualsRegistry_AndAdvertisesNullableSelections()
    {
        var definition = new ToolRegistry().GetAll().Single(t => t.Name == McpConstants.ToolGetSchema);
        var sdk = SqlMcpToolRegistrations.BuildToolCollection(ToolDispatcherTestHelper.BuildDispatcher())
            .Single(t => t.ProtocolTool.Name == definition.Name).ProtocolTool;
        var registry = JsonSerializer.SerializeToElement(definition.InputSchema, McpJsonContext.Default.ToolInputSchema);
        Assert.True(JsonElement.DeepEquals(registry, sdk.InputSchema));
        Assert.Equal(definition.Description, sdk.Description);
        Assert.Equal(["database"], definition.InputSchema.Required);
        var properties = sdk.InputSchema.GetProperty("properties");
        Assert.Equal(["string", "null"], properties.GetProperty("object_name").GetProperty("anyOf").EnumerateArray().Select(e => e.GetProperty("type").GetString()));
        var shapes = properties.GetProperty("object_names").GetProperty("anyOf").EnumerateArray().ToArray();
        Assert.Equal("null", shapes[1].GetProperty("type").GetString());
        Assert.Equal("array", shapes[0].GetProperty("type").GetString());
        Assert.Equal(["string", "null"], shapes[0].GetProperty("items").GetProperty("anyOf").EnumerateArray().Select(e => e.GetProperty("type").GetString()));
    }

    [Theory]
    [InlineData("Orders", null, false)]
    [InlineData("Orders", "null", false)]
    [InlineData(null, "[\"Orders\",\"Customers\"]", true)]
    [InlineData("Orders", "[]", true)]
    [InlineData(" Orders ", "[null,\" \",\"Customers\",\"Orders\"]", true)]
    public async Task Cli_DocumentedSelectionForms_ReachDispatcher(string? scalar, string? array, bool batch)
    {
        var factory = new SchemaBatchTestFactory();
        var dispatcher = ToolDispatcherTestHelper.BuildDispatcher(schema: factory.Service());
        ToolCallResult? response = null;
        var query = ToolCommandFactory.BuildQueryCommand(new ToolRegistry().GetAll(), async (name, arguments, ct) =>
        {
            response = await dispatcher.DispatchAsync(new ToolCallParams { Name = name, Arguments = arguments }, ct);
            return response.IsError ? 1 : 0;
        });
        var root = new RootCommand();
        root.Add(query);
        var tokens = new List<string> { "query", "sql_get_schema", "--database", "DemoDB" };
        if (scalar is not null) tokens.AddRange(["--object_name", scalar]);
        if (array is not null) tokens.AddRange(["--object_names", array]);
        var parsed = root.Parse(tokens.ToArray());
        Assert.Empty(parsed.Errors);
        int exit = await parsed.InvokeAsync(cancellationToken: Token);
        Assert.True(exit == 0, response?.Content.FirstOrDefault()?.Text);
        Assert.NotNull(response);
        string text = Assert.Single(response.Content).Text!;
        Assert.Equal(batch ? '{' : '#', text[0]);
    }

    [Theory]
    [InlineData("Orders")]
    [InlineData("{}")]
    [InlineData("42")]
    [InlineData("[invalid]")]
    public void Cli_InvalidArrayToken_IsParseError(string array)
    {
        var root = new RootCommand();
        root.Add(ToolCommandFactory.BuildQueryCommand(new ToolRegistry().GetAll(), (_, _, _) => throw new InvalidOperationException("Must not execute")));
        Assert.NotEmpty(root.Parse(["query", "sql_get_schema", "--database", "DemoDB", "--object_names", array]).Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cli_MissingOrMultipleArrayTokens_IsParseError(bool multiple)
    {
        var root = new RootCommand();
        root.Add(ToolCommandFactory.BuildQueryCommand(new ToolRegistry().GetAll(), (_, _, _) => throw new InvalidOperationException("Must not execute")));
        var tokens = new List<string> { "query", "sql_get_schema", "--database", "DemoDB", "--object_names" };
        if (multiple) tokens.AddRange(["[]", "[]"]);
        Assert.NotEmpty(root.Parse(tokens.ToArray()).Errors);
    }
}
