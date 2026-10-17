using System.CommandLine;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SqlToAi.Cli;
using SqlToAi.Database;
using SqlToAi.Mcp;
using SqlToAi.Tests.Cli;
using SqlToAi.Tests.TestSupport;
using static SqlToAi.Tests.Database.SchemaObjectSelectionTests;

namespace SqlToAi.Tests.Mcp;

public sealed class ExportToolSurfaceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static (ToolDispatcher Dispatcher, SchemaExportTests.ExportSchemaProxy Proxy) Create(Func<string, Stream>? open = null)
    {
        var schema = DispatchProxy.Create<ISchemaService, SchemaExportTests.ExportSchemaProxy>();
        var proxy = (SchemaExportTests.ExportSchemaProxy)schema;
        proxy.Objects = [new(new(1, "dbo", "Orders", SchemaObjectKind.Table)), new(new(2, "dbo", "Customers", SchemaObjectKind.Table))];
        var catalog = new CatalogFactory([new(1, "dbo", "Orders"), new(2, "dbo", "Customers")]);
        proxy.ResolveSelected = (names, ct) => catalog.Service().GetSelectedExportObjectsAsync(TestConstants.QualifiedDatabaseName, names, ct);
        var export = open is null ? new SchemaExportService(schema, TestTargets.BoundOptions, NullLogger<SchemaExportService>.Instance)
            : new SchemaExportService(schema, TestTargets.BoundOptions, NullLogger<SchemaExportService>.Instance, open);
        return (ToolDispatcherTestHelper.BuildDispatcher(schema: schema, export: export), proxy);
    }

    private static async Task<ToolCallResult> Invoke(ToolDispatcher dispatcher, string selection, string output, string route,
        CancellationToken ct = default, bool addRequired = true)
    {
        var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(selection)!;
        if (addRequired)
        {
            arguments["database"] = JsonSerializer.SerializeToElement(TestConstants.QualifiedDatabaseName);
            arguments["output_path"] = JsonSerializer.SerializeToElement(output);
        }
        if (route == "sdk")
        {
            await using var transport = new StreamServerTransport(new MemoryStream(), new MemoryStream());
            await using var server = McpServer.Create(transport, new McpServerOptions
            { ServerInfo = new Implementation { Name = "export-test", Version = "1" } }, serviceProvider: new ServiceCollection().BuildServiceProvider());
            var tool = SqlMcpToolRegistrations.BuildToolCollection(dispatcher).Single(t => t.ProtocolTool.Name == "sql_export_schema");
            var parameters = new CallToolRequestParams { Name = "sql_export_schema", Arguments = arguments };
            var context = new RequestContext<CallToolRequestParams>(server,
                new JsonRpcRequest { Id = new RequestId(1), Method = "tools/call" }, parameters);
            var result = await tool.InvokeAsync(context, ct);
            return new ToolCallResult { IsError = result.IsError ?? false,
                Content = result.Content.Cast<TextContentBlock>().Select(b => new ToolContent { Text = b.Text }).ToList() };
        }
        return await dispatcher.DispatchAsync(new ToolCallParams { Name = "sql_export_schema",
            Arguments = arguments.ToDictionary(p => p.Key, p => (object?)p.Value) }, ct);
    }

    public static TheoryData<string, string, bool> SelectionCases => new()
    {
        { """{"scope":"selected","object_name":"Orders"}""", "completed", true },
        { """{"scope":"selected","object_names":["Orders"]}""", "completed", true },
        { """{"scope":"selected","object_names":["Orders","Customers"]}""", "completed", true },
        { """{"scope":"selected","object_name":" Orders ","object_names":[null," ","Customers","[dbo].[Orders]"]}""", "completed", true },
        { """{"scope":"all"}""", "completed", true },
        { """{"scope":"all","object_name":null,"object_names":null}""", "completed", true },
        { """{"scope":"all","object_name":" ","object_names":[null,""," "]}""", "completed", true },
        { """{"scope":"all","object_names":[]}""", "completed", true },
        { """{"scope":"all","object_names":["Orders"]}""", "failed", false },
        { """{"scope":"all","object_name":"Orders"}""", "failed", false },
        { """{"scope":"selected"}""", "failed", false },
        { """{"scope":"selected","object_names":[null," "]}""", "failed", false },
        { """{"scope":"selected","object_name":42}""", "failed", false },
        { """{"scope":"selected","object_names":"Orders"}""", "failed", false },
        { """{"scope":"selected","object_names":[true]}""", "failed", false },
        { """{"scope":"all","object_names":{}}""", "failed", false },
        { """{"scope":"invalid"}""", "failed", false }
    };

    [Theory]
    [MemberData(nameof(SelectionCases))]
    public async Task Sdk_AndDispatcher_SelectionContract(string selection, string status, bool writes)
    {
        using var temp = TestTempDirectory.Create();
        foreach (string route in new[] { "sdk", "dispatcher" })
        {
            var (dispatcher, _) = Create();
            string output = temp.GetPath(route);
            var result = await Invoke(dispatcher, selection, output, route, Token);
            using var json = JsonDocument.Parse(Assert.Single(result.Content).Text!);
            Assert.Equal(status, json.RootElement.GetProperty("status").GetString());
            Assert.Equal(!writes, result.IsError);
            Assert.False(json.RootElement.GetProperty("partial_output").GetBoolean());
            Assert.Equal(writes, File.Exists(Path.Combine(output, "README.md")));
            if (!writes) Assert.Equal("SQL-AI-0001", json.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.DoesNotContain("markdown", json.RootElement.EnumerateObject().Select(p => p.Name));
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"scope":null}""")]
    [InlineData("""{"scope":42}""")]
    public async Task Dispatcher_MissingInvalidScope_NeverExports(string input)
    {
        using var temp = TestTempDirectory.Create();
        var (dispatcher, proxy) = Create();
        var result = await Invoke(dispatcher, input, temp.GetPath("dump"), "dispatcher", Token);
        Assert.True(result.IsError);
        Assert.Empty(proxy.Calls);
        Assert.Contains("SQL-AI-0001", Assert.Single(result.Content).Text);
        using var json = JsonDocument.Parse(Assert.Single(result.Content).Text!);
        Assert.Equal(temp.GetPath("dump"), json.RootElement.GetProperty("output_path").GetString());
    }

    [Fact]
    public async Task Sdk_MissingRequiredScope_ReturnsCatalogOutcomeBeforeServices()
    {
        using var temp = TestTempDirectory.Create();
        var (dispatcher, proxy) = Create();
        var result = await Invoke(dispatcher, "{}", temp.GetPath("dump"), "sdk", Token);
        Assert.True(result.IsError);
        using var json = JsonDocument.Parse(Assert.Single(result.Content).Text!);
        Assert.Equal("failed", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("SQL-AI-0001", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(proxy.Calls);
        Assert.False(Directory.Exists(temp.GetPath("dump")));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"database\":null,\"scope\":null,\"output_path\":null}")]
    [InlineData("{\"database\":42,\"scope\":42,\"output_path\":42}")]
    [InlineData("{\"database\":\"DemoDB\",\"scope\":\"all\"}")]
    public async Task Sdk_InvalidRequiredShapes_ReturnCompactCatalogFailure(string input)
    {
        var (dispatcher, proxy) = Create();
        var result = await Invoke(dispatcher, input, "unused", "sdk", Token, addRequired: false);
        Assert.True(result.IsError);
        using var json = JsonDocument.Parse(Assert.Single(result.Content).Text!);
        Assert.Equal("failed", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("SQL-AI-0001", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.False(json.RootElement.GetProperty("partial_output").GetBoolean());
        Assert.Empty(proxy.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispatcher_MissingDatabaseOrOutput_FailsWithoutWrites(bool missingDatabase)
    {
        using var temp = TestTempDirectory.Create();
        var (dispatcher, proxy) = Create();
        var arguments = new Dictionary<string, object?> { ["scope"] = "all" };
        if (missingDatabase) arguments["output_path"] = temp.GetPath("dump");
        else arguments["database"] = "DemoDB";
        var result = await dispatcher.DispatchAsync(new ToolCallParams { Name = "sql_export_schema", Arguments = arguments }, Token);
        Assert.True(result.IsError);
        using var json = JsonDocument.Parse(Assert.Single(result.Content).Text!);
        Assert.Equal("failed", json.RootElement.GetProperty("status").GetString());
        if (missingDatabase) Assert.Equal(temp.GetPath("dump"), json.RootElement.GetProperty("output_path").GetString());
        else Assert.False(json.RootElement.TryGetProperty("output_path", out _));
        Assert.Empty(proxy.Calls);
    }

    [Fact]
    public async Task CancellationBeforeWrites_ReportsEmptyOutput()
    {
        using var temp = TestTempDirectory.Create();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var result = await Invoke(Create().Dispatcher, "{\"scope\":\"all\"}", temp.GetPath("dump"), "dispatcher", cancellation.Token);
        Assert.True(result.IsError);
        using var json = JsonDocument.Parse(Assert.Single(result.Content).Text!);
        Assert.Equal("cancelled", json.RootElement.GetProperty("status").GetString());
        Assert.False(json.RootElement.GetProperty("partial_output").GetBoolean());
        Assert.Equal(temp.GetPath("dump"), json.RootElement.GetProperty("output_path").GetString());
        Assert.False(Directory.Exists(temp.GetPath("dump")));
    }

    public static TheoryData<string, string> EarlyFailureDestinations
    {
        get
        {
            var cases = new TheoryData<string, string>();
            foreach (string failure in new[] { "missing_scope", "invalid_scope", "missing_database" })
                foreach (string destination in new[] { "absent", "empty", "file", "files", "children" })
                    cases.Add(failure, destination);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(EarlyFailureDestinations))]
    public async Task EarlyFailures_InspectRetainedOutputAcrossEntryPoints(string failure, string destination)
    {
        using var temp = TestTempDirectory.Create();
        foreach (string route in new[] { "sdk", "dispatcher", "cli" })
        {
            string output = temp.GetPath(route);
            if (destination == "file") await File.WriteAllTextAsync(output, "preserve", Token);
            else if (destination != "absent")
            {
                Directory.CreateDirectory(output);
                if (destination == "files") await File.WriteAllTextAsync(Path.Combine(output, "README.md"), "preserve", Token);
                if (destination == "children") Directory.CreateDirectory(Path.Combine(output, "tables"));
            }
            string[] before = Directory.Exists(output) ? Directory.GetFileSystemEntries(output) : [];
            var (dispatcher, proxy) = Create();
            var args = new Dictionary<string, string> { ["output_path"] = output };
            if (failure != "missing_database") args["database"] = "DemoDB";
            if (failure != "missing_scope") args["scope"] = failure == "invalid_scope" ? "invalid" : "all";
            ToolCallResult result;
            if (route == "cli")
            {
                ToolCallResult? response = null;
                var root = new RootCommand();
                root.Add(ToolCommandFactory.BuildQueryCommand(new ToolRegistry().GetAll(), async (name, arguments, ct) =>
                {
                    response = await dispatcher.DispatchAsync(new ToolCallParams { Name = name, Arguments = arguments }, ct);
                    return response.IsError ? 1 : 0;
                }));
                var tokens = new List<string> { "query", "sql_export_schema" };
                foreach (var argument in args) tokens.AddRange(["--" + argument.Key, argument.Value]);
                if (failure != "invalid_scope")
                {
                    // Omitted required CLI options fail parsing before dispatch.
                    Assert.NotEmpty(root.Parse(tokens.ToArray()).Errors);
                    // An explicitly empty required value exercises the shared missing-value boundary.
                    tokens.AddRange([failure == "missing_scope" ? "--scope" : "--database", ""]);
                }
                var parsed = root.Parse(tokens.ToArray());
                Assert.Empty(parsed.Errors);
                Assert.Equal(1, await parsed.InvokeAsync(cancellationToken: Token));
                result = Assert.IsType<ToolCallResult>(response);
            }
            else result = await Invoke(dispatcher, JsonSerializer.Serialize(args), output, route, Token, addRequired: false);
            Assert.True(result.IsError);
            using var json = JsonDocument.Parse(Assert.Single(result.Content).Text!);
            Assert.Equal("failed", json.RootElement.GetProperty("status").GetString());
            Assert.Equal(output, json.RootElement.GetProperty("output_path").GetString());
            Assert.Equal(destination is "files" or "children", json.RootElement.GetProperty("partial_output").GetBoolean());
            Assert.Equal("SQL-AI-0001", json.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.Empty(proxy.Calls);
            Assert.Equal(before, Directory.Exists(output) ? Directory.GetFileSystemEntries(output) : []);
            Assert.Equal(destination is not ("absent" or "file"), Directory.Exists(output));
            if (destination is "file" or "files")
                Assert.Equal("preserve", await File.ReadAllTextAsync(destination == "file" ? output : Path.Combine(output, "README.md"), Token));
        }
    }

    [Theory]
    [InlineData(@"C:\dump", true)]
    [InlineData("C:/dump", true)]
    [InlineData(@"\\server\share", true)]
    [InlineData("//server/share/dump", true)]
    [InlineData("dump", false)]
    [InlineData(@"C:dump", false)]
    [InlineData(@"\dump", false)]
    [InlineData("/dump", false)]
    [InlineData("file:///C:/dump", false)]
    [InlineData("https://server/dump", false)]
    [InlineData(@"\\?\C:\dump", false)]
    [InlineData(@"\\.\C:\dump", false)]
    [InlineData("//?/C:/dump", false)]
    [InlineData("//./C:/dump", false)]
    [InlineData(@"\\server", false)]
    public void WindowsPathQualification_DoesNotRequireReachableShare(string path, bool valid)
        => Assert.Equal(valid, SchemaExportToolResponse.IsQualifiedWindowsDirectory(path));

    [Theory]
    [InlineData("relative")]
    [InlineData(@"C:dump")]
    [InlineData(@"\dump")]
    [InlineData("https://host/dump")]
    [InlineData("//?/C:/dump")]
    public async Task Sdk_AndDispatcher_RejectPathsBeforeServices(string path)
    {
        foreach (string route in new[] { "sdk", "dispatcher" })
        {
            var (dispatcher, proxy) = Create();
            var result = await Invoke(dispatcher, """{"scope":"all"}""", path, route, Token);
            Assert.True(result.IsError);
            using var json = JsonDocument.Parse(Assert.Single(result.Content).Text!);
            Assert.Equal("failed", json.RootElement.GetProperty("status").GetString());
            Assert.False(json.RootElement.TryGetProperty("output_path", out _));
            Assert.False(json.RootElement.GetProperty("partial_output").GetBoolean());
            Assert.Empty(proxy.Calls);
        }
    }

    [Theory]
    [InlineData("file")]
    [InlineData("child")]
    [InlineData("empty")]
    public async Task Destinations_KeepExistingContents(string kind)
    {
        using var temp = TestTempDirectory.Create();
        string output = temp.GetPath("dump");
        if (kind == "file") await File.WriteAllTextAsync(output, "keep", Token);
        else { Directory.CreateDirectory(output); if (kind == "child") Directory.CreateDirectory(Path.Combine(output, "child")); }
        var (dispatcher, _) = Create();
        var result = await Invoke(dispatcher, """{"scope":"all"}""", output, "sdk", Token);
        Assert.Equal(kind != "empty", result.IsError);
        if (kind == "file") Assert.Equal("keep", await File.ReadAllTextAsync(output, Token));
        if (kind == "child") Assert.True(Directory.Exists(Path.Combine(output, "child")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureAndCancellation_ReportPartialOutput(bool cancel)
    {
        using var temp = TestTempDirectory.Create();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var (dispatcher, _) = Create(path =>
        {
            if (cancel) { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); }
            throw new IOException("fixed write failure");
        });
        var result = await Invoke(dispatcher, """{"scope":"all"}""", temp.GetPath("dump"), "sdk", cancellation.Token);
        Assert.True(result.IsError);
        using var json = JsonDocument.Parse(Assert.Single(result.Content).Text!);
        Assert.Equal(cancel ? "cancelled" : "failed", json.RootElement.GetProperty("status").GetString());
        Assert.True(json.RootElement.GetProperty("partial_output").GetBoolean());
        if (cancel) Assert.Equal("cancelled", json.RootElement.GetProperty("reason").GetString());
        else Assert.Equal("SQL-AI-0105", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task DeniedAccess_FailsBeforeConnectionsAndWrites()
    {
        using var temp = TestTempDirectory.Create();
        var catalog = new CatalogFactory([new(1, "dbo", "Orders")]);
        var schema = catalog.Service(allowed: false);
        var dispatcher = ToolDispatcherTestHelper.BuildDispatcher(schema: schema);
        var result = await Invoke(dispatcher, """{"scope":"selected","object_name":"Orders"}""", temp.GetPath("dump"), "sdk", Token);
        Assert.True(result.IsError);
        Assert.Contains("SQL-AI-0104", Assert.Single(result.Content).Text);
        Assert.Equal(0, catalog.ConnectionCount);
        Assert.False(Directory.Exists(temp.GetPath("dump")));
    }

    [Fact]
    public void RegistryAndSdk_AgreeOnContract()
    {
        var definition = new ToolRegistry().GetAll().Single(t => t.Name == "sql_export_schema");
        var sdk = SqlMcpToolRegistrations.BuildToolCollection(Create().Dispatcher).Single(t => t.ProtocolTool.Name == definition.Name).ProtocolTool;
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(definition.InputSchema, McpJsonContext.Default.ToolInputSchema), sdk.InputSchema));
        Assert.Equal(definition.Description, sdk.Description);
        Assert.Equal(["database", "scope", "output_path"], definition.InputSchema.Required);
        Assert.Contains("persistent", sdk.Description);
        Assert.Contains("SERVER filesystem", sdk.Description);
        Assert.Contains("substantial time", sdk.Description);
    }

    [Theory]
    [InlineData("selected", "Orders", null, 0)]
    [InlineData("selected", null, """["Orders","Customers"]""", 0)]
    [InlineData("selected", "Orders", """[null,"Customers"]""", 0)]
    [InlineData("all", null, "[]", 0)]
    [InlineData("selected", null, "[]", 1)]
    [InlineData("invalid", null, null, 1)]
    public async Task RegistryCli_InvokesExport(string scope, string? scalar, string? list, int expected)
    {
        using var temp = TestTempDirectory.Create();
        var (dispatcher, _) = Create();
        var root = new RootCommand();
        root.Add(ToolCommandFactory.BuildQueryCommand(new ToolRegistry().GetAll(), async (name, args, ct) =>
            (await dispatcher.DispatchAsync(new ToolCallParams { Name = name, Arguments = args }, ct)).IsError ? 1 : 0));
        var tokens = new List<string> { "query", "sql_export_schema", "--database", TestConstants.QualifiedDatabaseName, "--scope", scope, "--output_path", temp.GetPath("dump") };
        if (scalar is not null) tokens.AddRange(["--object_name", scalar]);
        if (list is not null) tokens.AddRange(["--object_names", list]);
        var parsed = root.Parse(tokens.ToArray());
        Assert.Empty(parsed.Errors);
        Assert.Equal(expected, await parsed.InvokeAsync(cancellationToken: Token));
        Assert.NotEmpty(root.Parse(["query", "sql_export_schema", "--database", "DemoDB", "--output_path", temp.GetPath("other")]).Errors);
    }
}
