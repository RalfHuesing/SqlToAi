using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SqlToAi.Domain;
using SqlToAi.Mcp;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Mcp;

public sealed class ManagedMcpTargetBindingTests
{
    [Fact]
    public async Task ManagedHost_MissingTargetsKeepCatalogCodesAcrossActualStdioBinding()
    {
        using var temp = TestTempDirectory.Create();
        const string settings = """{"SqlToAi":{"Servers":{"ALPHA":{"Server":"never-contact.invalid","IntegratedSecurity":true}},"Databases":{"ReadOnly":[]}}}""";
        string file = temp.CreateFile("user/appsettings.json", settings);
        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        environment["DOTNET_ENVIRONMENT"] = "StartupTest";
        environment["LOCALAPPDATA"] = temp.GetPath("local-app-data");
        environment["SqlToAi__Logging__Directory"] = temp.GetPath("logs");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet", WorkingDirectory = temp.DirectoryPath, InheritEnvironmentVariables = false,
            EnvironmentVariables = environment,
            Arguments = ["exec", "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "SqlToAi.Tests.runtimeconfig.json"),
                "--depsfile", Path.Combine(AppContext.BaseDirectory, "SqlToAi.Tests.deps.json"),
                typeof(StartupHarness.StartupHarness).Assembly.Location, temp.GetPath("user"), "server", "server"]
        }), cancellationToken: deadline.Token);
        Assert.Equal(McpConstants.ServerInstructions, client.ServerInstructions);
        Assert.Contains("exact raw database name, compared ordinally", client.ServerInstructions, StringComparison.Ordinal);
        Assert.Contains("can match actual stored text", client.ServerInstructions, StringComparison.Ordinal);
        Assert.Equal(20, (await client.ListToolsAsync(cancellationToken: deadline.Token)).Count);
        var servers = await client.CallToolAsync(McpConstants.ToolListServers, cancellationToken: deadline.Token);
        Assert.False(servers.IsError);
        Assert.Equal(["alpha"], JsonSerializer.Deserialize<string[]>(Text(servers))!);

        var cases = new (string Tool, Dictionary<string, object?> Args, string Code)[]
        {
            (McpConstants.ToolListDatabases, [], SqlToAiError.InvalidParametersCode),
            (McpConstants.ToolListDatabases, new() { ["server"] = null }, SqlToAiError.InvalidParametersCode),
            (McpConstants.ToolSearchDatabases, new() { ["search_term"] = "Db" }, SqlToAiError.InvalidParametersCode),
            (McpConstants.ToolExecuteQuery, new() { ["query"] = "SELECT 1" }, SqlToAiError.InvalidParametersCode),
            (McpConstants.ToolExecuteQuery, new() { ["query"] = "SELECT 1", ["database"] = null }, SqlToAiError.InvalidParametersCode),
            (McpConstants.ToolExecuteQuery, new() { ["query"] = "SELECT 1", ["database"] = "unknown/Db" }, SqlToAiError.InvalidParametersCode),
            (McpConstants.ToolExecuteQuery, new() { ["query"] = "SELECT 1", ["database"] = "ALPHA/Db" }, SqlToAiError.SafetyCheckFailedCode)
        };
        foreach (var item in cases)
        {
            var result = await client.CallToolAsync(item.Tool, item.Args, cancellationToken: deadline.Token);
            Assert.True(result.IsError);
            Assert.Contains(item.Code, Text(result), StringComparison.Ordinal);
            Assert.DoesNotContain("An error occurred invoking", Text(result), StringComparison.Ordinal);
        }
        Assert.Equal(settings, await File.ReadAllTextAsync(file, deadline.Token));
    }

    private static string Text(CallToolResult result) => string.Join("\n", result.Content.Select(block => Assert.IsType<TextContentBlock>(block).Text));
}
