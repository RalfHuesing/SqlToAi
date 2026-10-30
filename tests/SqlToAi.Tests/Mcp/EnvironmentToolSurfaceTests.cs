using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Mcp;
using SqlToAi.Security;
using SqlToAi.Tests.Database;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Mcp;

public sealed class EnvironmentToolSurfaceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<CallToolResult> InvokeRegisteredAsync(ToolDispatcher dispatcher, Dictionary<string, object?> arguments)
    {
        await using var transport = new StreamServerTransport(new MemoryStream(), new MemoryStream());
        await using var server = McpServer.Create(transport, new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "environment-binding-test", Version = "1" }
        }, serviceProvider: new ServiceCollection().BuildServiceProvider());

        var tool = SqlMcpToolRegistrations.BuildToolCollection(dispatcher).Single(t => t.ProtocolTool.Name == McpConstants.ToolGetEnvironment);
        var jsonArgs = arguments.ToDictionary(
            kvp => kvp.Key,
            kvp => JsonSerializer.SerializeToElement(kvp.Value));

        var parameters = new CallToolRequestParams { Name = McpConstants.ToolGetEnvironment, Arguments = jsonArgs };
        var context = new RequestContext<CallToolRequestParams>(server,
            new JsonRpcRequest { Id = new RequestId(1), Method = "tools/call" }, parameters);
        return await tool.InvokeAsync(context, Token);
    }

    [Fact]
    public async Task RegisteredSdk_ReturnsEnvironmentJsonOnSuccess()
    {
        var fixture = new ComposedTargetFixture();
        var result = await InvokeRegisteredAsync(fixture.Dispatcher, new()
        {
            [McpConstants.ArgDatabase] = "alpha/SharedDb"
        });

        Assert.False(result.IsError);
        string text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;

        Assert.Equal("alpha/SharedDb", root.GetProperty("target").GetString());
        Assert.Equal("16.0.4180.1", root.GetProperty("product_version").GetString());
        Assert.Equal(16, root.GetProperty("product_major_version").GetInt32());
        Assert.Equal("Developer Edition (64-bit)", root.GetProperty("edition").GetString());
        Assert.Equal(3, root.GetProperty("engine_edition").GetInt32());
        Assert.Equal(160, root.GetProperty("compatibility_level").GetByte());
        Assert.Equal("Latin1_General_CI_AS", root.GetProperty("collation").GetString());
        Assert.Equal("ONLINE", root.GetProperty("state_desc").GetString());
        Assert.False(root.GetProperty("is_read_only").GetBoolean());
    }

    [Fact]
    public async Task RegisteredSdk_MissingDatabase_ReturnsInvalidParametersBeforeConnection()
    {
        var fixture = new ComposedTargetFixture();
        var result = await InvokeRegisteredAsync(fixture.Dispatcher, []);

        Assert.True(result.IsError);
        string text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains(SqlToAiError.InvalidParametersCode, text);
        Assert.Empty(fixture.Factory.Connections);
    }

    [Theory]
    [InlineData("unqualified")]
    [InlineData("unknown_alias/Db")]
    [InlineData(" ")]
    public async Task RegisteredSdk_MalformedOrUnknownDatabase_ReturnsInvalidParametersBeforeConnection(string database)
    {
        var fixture = new ComposedTargetFixture();
        var result = await InvokeRegisteredAsync(fixture.Dispatcher, new()
        {
            [McpConstants.ArgDatabase] = database
        });

        Assert.True(result.IsError);
        string text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains(SqlToAiError.InvalidParametersCode, text);
        Assert.Empty(fixture.Factory.Connections);
    }

    [Fact]
    public async Task RegisteredSdk_DeniedOrExcludedDatabase_ReturnsSafetyCheckFailedBeforeConnection()
    {
        var options = ComposedTargetFixture.CreateConfiguration();
        // Exclude alpha/SharedDb
        options.Servers["ALPHA"].ExcludedDatabases.Add("SharedDb");
        var fixture = new ComposedTargetFixture(options);

        var result = await InvokeRegisteredAsync(fixture.Dispatcher, new()
        {
            [McpConstants.ArgDatabase] = "alpha/SharedDb"
        });

        Assert.True(result.IsError);
        string text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains(SqlToAiError.SafetyCheckFailedCode, text);
        Assert.Empty(fixture.Factory.Connections);
    }

    [Fact]
    public async Task RegisteredSdk_SchemaOnlyAccess_AllowsEnvironmentRetrieval()
    {
        var options = new SqlToAiOptions();
        options.Servers["test"] = new NamedServerOptions
        {
            Server = "test.invalid",
            IntegratedSecurity = true
        };
        options.Databases.SchemaOnly = ["test/SchemaOnlyDb"];

        var fixture = new ComposedTargetFixture(options);
        var result = await InvokeRegisteredAsync(fixture.Dispatcher, new()
        {
            [McpConstants.ArgDatabase] = "test/SchemaOnlyDb"
        });

        Assert.False(result.IsError);
        string text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        using var doc = JsonDocument.Parse(text);
        Assert.Equal("test/SchemaOnlyDb", doc.RootElement.GetProperty("target").GetString());
    }

    [Fact]
    public async Task RegisteredSdk_SameDatabaseNameDifferentAliases_DoNotCollide()
    {
        var fixture = new ComposedTargetFixture();

        var resultAlpha = await InvokeRegisteredAsync(fixture.Dispatcher, new()
        {
            [McpConstants.ArgDatabase] = "alpha/SharedDb"
        });
        var resultBeta = await InvokeRegisteredAsync(fixture.Dispatcher, new()
        {
            [McpConstants.ArgDatabase] = "beta/SharedDb"
        });

        Assert.False(resultAlpha.IsError);
        Assert.False(resultBeta.IsError);

        var connectionAlpha = fixture.Factory.Connections.First(c => c.Target?.ServerAlias == "alpha");
        var connectionBeta = fixture.Factory.Connections.First(c => c.Target?.ServerAlias == "beta");

        Assert.Equal("alpha.invalid", connectionAlpha.Settings.DataSource);
        Assert.Equal("beta.invalid", connectionBeta.Settings.DataSource);
        Assert.Equal("SharedDb", connectionAlpha.Settings.InitialCatalog);
        Assert.Equal("SharedDb", connectionBeta.Settings.InitialCatalog);
    }
}
