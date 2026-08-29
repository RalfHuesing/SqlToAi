#nullable enable

using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RalfHuesing.Mcp.Observability;
using SqlToAi.Domain;
using SqlToAi.Mcp;

namespace SqlToAi.Tests.Mcp;

public sealed class McpObservabilityIntegrationTests : IDisposable
{
    private readonly string _tempDirectory;

    public McpObservabilityIntegrationTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "SqlToAi_ObsTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignored on cleanup
        }
    }

    [Fact]
    public async Task ListTools_ShouldReturn_AllSqlToolsAndFeedbackTool()
    {
        var ct = TestContext.Current.CancellationToken;
        var (clientRead, clientWrite, serverRead, serverWrite) = CreateDuplexPipes();

        var obsOptions = new McpObservabilityOptions
        {
            LogDirectory = _tempDirectory
        };

        var dispatcher = new FakeTestDispatcher();
        var host = CreateTestHost(dispatcher, obsOptions, serverRead, serverWrite);
        await host.StartAsync(ct);

        await using var client = await CreateClientAsync(clientWrite, clientRead, ct);
        var tools = await client.ListToolsAsync(cancellationToken: ct);

        Assert.Equal(18, tools.Count);
        Assert.Contains(tools, t => t.Name == McpConstants.ToolListDatabases);
        Assert.Contains(tools, t => t.Name == McpConstants.ToolExecuteQuery);
        Assert.Contains(tools, t => t.Name == McpConstants.ToolExecuteFile);
        Assert.Contains(tools, t => t.Name == McpConstants.ToolGetSchema);
        Assert.Contains(tools, t => t.Name == McpConstants.ToolCompareQueries);
        Assert.Contains(tools, t => t.Name == McpConstants.ToolMeasurePerformance);
        Assert.Contains(tools, t => t.Name == McpConstants.ToolBenchmarkOptimization);
        Assert.Contains(tools, t => t.Name == McpConstants.ToolSuggestIndexes);
        Assert.Contains(tools, t => t.Name == "report_observability_feedback");

        await host.StopAsync(ct);
    }

    [Fact]
    public async Task ExecuteFileTool_Call_ShouldForwardArgumentsToDispatcher()
    {
        var ct = TestContext.Current.CancellationToken;
        var (clientRead, clientWrite, serverRead, serverWrite) = CreateDuplexPipes();
        var dispatcher = new FakeTestDispatcher();
        var host = CreateTestHost(dispatcher, new McpObservabilityOptions { Enabled = false }, serverRead, serverWrite);
        await host.StartAsync(ct);

        await using var client = await CreateClientAsync(clientWrite, clientRead, ct);
        var result = await client.CallToolAsync(new CallToolRequestParams
        {
            Name = McpConstants.ToolExecuteFile,
            Arguments = new Dictionary<string, JsonElement>
            {
                [McpConstants.ArgFilePath] = JsonSerializer.SerializeToElement("scripts/report.sql"),
                [McpConstants.ArgDatabase] = JsonSerializer.SerializeToElement(TestConstants.DatabaseName),
                [McpConstants.ArgUseTransaction] = JsonSerializer.SerializeToElement(false),
                [McpConstants.ArgRequestedRowLimit] = JsonSerializer.SerializeToElement(7),
                [McpConstants.ArgParameters] = JsonSerializer.SerializeToElement(new { CustomerId = 42 })
            }
        }, cancellationToken: ct);

        Assert.NotNull(result);
        Assert.False(result.IsError ?? false);
        Assert.NotNull(dispatcher.LastCall);
        Assert.Equal("scripts/report.sql", dispatcher.LastCall!.Arguments[McpConstants.ArgFilePath]);
        Assert.Equal(TestConstants.DatabaseName, dispatcher.LastCall.Arguments[McpConstants.ArgDatabase]);
        Assert.Equal(false, dispatcher.LastCall.Arguments[McpConstants.ArgUseTransaction]);
        Assert.Equal(7, dispatcher.LastCall.Arguments[McpConstants.ArgRequestedRowLimit]);
        Assert.True(dispatcher.LastCall.Arguments.ContainsKey(McpConstants.ArgParameters));

        await host.StopAsync(ct);
    }

    [Fact]
    public async Task ToolCall_ShouldWrite_ToolCallRecordToJsonl_WithSanitizedArguments()
    {
        var ct = TestContext.Current.CancellationToken;
        var (clientRead, clientWrite, serverRead, serverWrite) = CreateDuplexPipes();

        var obsOptions = new McpObservabilityOptions
        {
            LogDirectory = _tempDirectory,
            ServerName = "SqlToAi"
        };

        var dispatcher = new FakeTestDispatcher();
        var host = CreateTestHost(dispatcher, obsOptions, serverRead, serverWrite);
        await host.StartAsync(ct);

        await using var client = await CreateClientAsync(clientWrite, clientRead, ct);

        var result = await client.CallToolAsync(new CallToolRequestParams
        {
            Name = McpConstants.ToolExecuteQuery,
            Arguments = new Dictionary<string, JsonElement>
            {
                ["query"] = JsonSerializer.SerializeToElement("SELECT * FROM Customers"),
                ["database"] = JsonSerializer.SerializeToElement("DemoDB"),
                ["password"] = JsonSerializer.SerializeToElement("secret123")
            }
        }, cancellationToken: ct);

        Assert.NotNull(result);
        Assert.False(result.IsError ?? false);

        var obsService = host.Services.GetRequiredService<IMcpObservabilityService>();
        await obsService.FlushAsync(ct);

        Assert.NotNull(obsService.CurrentLogFilePath);
        Assert.True(File.Exists(obsService.CurrentLogFilePath));

        string[] lines = await ReadAllLinesSharedAsync(obsService.CurrentLogFilePath, ct);
        Assert.Single(lines);

        using var doc = JsonDocument.Parse(lines[0]);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("tool_call", root.GetProperty("recordType").GetString());
        Assert.Equal("SqlToAi", root.GetProperty("serverName").GetString());
        Assert.Equal(McpConstants.ToolExecuteQuery, root.GetProperty("toolName").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.False(root.GetProperty("isErrorResult").GetBoolean());

        var args = root.GetProperty("arguments");
        Assert.Equal("SELECT * FROM Customers", args.GetProperty("query").GetString());
        Assert.Equal("DemoDB", args.GetProperty("database").GetString());
        Assert.Equal("***REDACTED***", args.GetProperty("password").GetString());

        await host.StopAsync(ct);
    }

    [Fact]
    public async Task FeedbackTool_ShouldWrite_FeedbackRecordToJsonl()
    {
        var ct = TestContext.Current.CancellationToken;
        var (clientRead, clientWrite, serverRead, serverWrite) = CreateDuplexPipes();

        var obsOptions = new McpObservabilityOptions
        {
            LogDirectory = _tempDirectory,
            ServerName = "SqlToAi"
        };

        var dispatcher = new FakeTestDispatcher();
        var host = CreateTestHost(dispatcher, obsOptions, serverRead, serverWrite);
        await host.StartAsync(ct);

        await using var client = await CreateClientAsync(clientWrite, clientRead, ct);

        var result = await client.CallToolAsync(new CallToolRequestParams
        {
            Name = "report_observability_feedback",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["feedbackType"] = JsonSerializer.SerializeToElement("issue"),
                ["title"] = JsonSerializer.SerializeToElement("Index suggestion issue"),
                ["description"] = JsonSerializer.SerializeToElement("Missing index on large table"),
                ["severity"] = JsonSerializer.SerializeToElement("medium")
            }
        }, cancellationToken: ct);

        Assert.NotNull(result);
        Assert.False(result.IsError ?? false);
        var text = result.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        Assert.Equal(McpObservabilityOptions.DefaultFeedbackConfirmationMessage, text);

        var obsService = host.Services.GetRequiredService<IMcpObservabilityService>();
        await obsService.FlushAsync(ct);

        string[] lines = await ReadAllLinesSharedAsync(obsService.CurrentLogFilePath!, ct);
        Assert.Equal(2, lines.Length);

        using var feedbackDoc = lines.Select(l => JsonDocument.Parse(l))
            .First(d => d.RootElement.GetProperty("recordType").GetString() == "feedback");
        var root = feedbackDoc.RootElement;
        Assert.Equal("feedback", root.GetProperty("recordType").GetString());
        Assert.Equal("issue", root.GetProperty("feedbackType").GetString());
        Assert.Equal("Index suggestion issue", root.GetProperty("title").GetString());
        Assert.Equal("medium", root.GetProperty("severity").GetString());

        await host.StopAsync(ct);
    }

    [Fact]
    public async Task DisabledObservability_ShouldOmitFeedbackTool_AndNotLog()
    {
        var ct = TestContext.Current.CancellationToken;
        var (clientRead, clientWrite, serverRead, serverWrite) = CreateDuplexPipes();

        var obsOptions = new McpObservabilityOptions
        {
            Enabled = false,
            LogDirectory = _tempDirectory
        };

        var dispatcher = new FakeTestDispatcher();
        var host = CreateTestHost(dispatcher, obsOptions, serverRead, serverWrite);
        await host.StartAsync(ct);

        await using var client = await CreateClientAsync(clientWrite, clientRead, ct);
        var tools = await client.ListToolsAsync(cancellationToken: ct);

        Assert.Equal(17, tools.Count);
        Assert.DoesNotContain(tools, t => t.Name == "report_observability_feedback");

        var obsService = host.Services.GetRequiredService<IMcpObservabilityService>();
        Assert.False(obsService.IsEnabled);
        Assert.Null(obsService.CurrentLogFilePath);

        await host.StopAsync(ct);
    }

    [Fact]
    public async Task MaxResponseLength_ShouldTruncateResponse_WhenExceeded()
    {
        var ct = TestContext.Current.CancellationToken;
        var (clientRead, clientWrite, serverRead, serverWrite) = CreateDuplexPipes();

        var obsOptions = new McpObservabilityOptions
        {
            LogDirectory = _tempDirectory,
            MaxResponseLength = 10
        };

        var dispatcher = new FakeTestDispatcher
        {
            ResponseText = "This is a very long response that exceeds ten characters."
        };

        var host = CreateTestHost(dispatcher, obsOptions, serverRead, serverWrite);
        await host.StartAsync(ct);

        await using var client = await CreateClientAsync(clientWrite, clientRead, ct);

        await client.CallToolAsync(new CallToolRequestParams
        {
            Name = McpConstants.ToolListDatabases,
            Arguments = new Dictionary<string, JsonElement>()
        }, cancellationToken: ct);

        var obsService = host.Services.GetRequiredService<IMcpObservabilityService>();
        await obsService.FlushAsync(ct);

        string[] lines = await ReadAllLinesSharedAsync(obsService.CurrentLogFilePath!, ct);
        Assert.Single(lines);

        using var doc = JsonDocument.Parse(lines[0]);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("responseTruncated").GetBoolean());
        Assert.StartsWith("This is a ", root.GetProperty("response").GetString()!);

        await host.StopAsync(ct);
    }

    // -------------------------------------------------------------------------
    // Helpers & Fakes
    // -------------------------------------------------------------------------

    private static IHost CreateTestHost(
        IToolDispatcher dispatcher,
        McpObservabilityOptions obsOptions,
        Stream serverRead,
        Stream serverWrite)
    {
        var builder = Host.CreateEmptyApplicationBuilder(null);

        builder.Services.AddMcpServer(serverOptions =>
        {
            serverOptions.ServerInfo = new Implementation
            {
                Name = "SqlToAi",
                Version = "1.0.0"
            };
            serverOptions.ToolCollection = SqlMcpToolRegistrations.BuildToolCollection(dispatcher);
        })
        .WithStreamServerTransport(serverRead, serverWrite)
        .WithObservability(obsOptions);

        return builder.Build();
    }

    private static async Task<McpClient> CreateClientAsync(
        Stream clientWrite,
        Stream clientRead,
        CancellationToken ct)
    {
        var transport = new StreamClientTransport(clientWrite, clientRead);
        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }

    private static (Stream ClientRead, Stream ClientWrite, Stream ServerRead, Stream ServerWrite) CreateDuplexPipes()
    {
        var clientPipe = new Pipe();
        var serverPipe = new Pipe();

        var clientRead = serverPipe.Reader.AsStream();
        var clientWrite = clientPipe.Writer.AsStream();
        var serverRead = clientPipe.Reader.AsStream();
        var serverWrite = serverPipe.Writer.AsStream();

        return (clientRead, clientWrite, serverRead, serverWrite);
    }

    private static async Task<string[]> ReadAllLinesSharedAsync(string filePath, CancellationToken ct)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = new List<string>();
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            lines.Add(line);
        }
        return lines.ToArray();
    }

    private sealed class FakeTestDispatcher : IToolDispatcher
    {
        public string ResponseText { get; set; } = "[\"DemoDB\"]";
        public ToolCallParams? LastCall { get; private set; }

        public Task<ToolCallResult> DispatchAsync(ToolCallParams callParams, CancellationToken cancellationToken = default)
        {
            LastCall = callParams;
            return Task.FromResult(ToolCallResult.Success(ResponseText));
        }
    }
}
