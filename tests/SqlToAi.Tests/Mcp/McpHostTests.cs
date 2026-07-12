#nullable enable

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SqlToAi.Domain;
using SqlToAi.Mcp;

namespace SqlToAi.Tests.Mcp;

/// <summary>
/// Unit tests for <see cref="McpHost"/> message routing logic.
/// Uses a fake dispatcher and captures stdout via Console redirection.
/// </summary>
public sealed class McpHostTests
{
    private static McpHost BuildHost(FakeMcpDispatcher? dispatcher = null)
        => new(
            dispatcher ?? new FakeMcpDispatcher(),
            new ToolRegistry(),
            new NoopMcpTrailWriter(),
            NullLogger<McpHost>.Instance);

    /// <summary>Sends a single JSON-RPC line to the host and returns the single response line.</summary>
    private static async Task<string> SendAsync(McpHost host, string json)
    {
        using var inputReader = new StringReader(json + "\n");
        var outputBuilder = new System.Text.StringBuilder();
        using var outputWriter = new StringWriter(outputBuilder);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await host.RunAsync(inputReader, outputWriter, cts.Token);

        return outputBuilder.ToString().Trim();
    }

    // -------------------------------------------------------------------------
    // initialize
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Initialize_ShouldReturn_ServerInfoAndProtocolVersion()
    {
        var host = BuildHost();
        string raw = await SendAsync(host, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");

        using var doc = JsonDocument.Parse(raw);
        var result = doc.RootElement.GetProperty("result");
        Assert.Equal(McpConstants.ProtocolVersion, result.GetProperty("protocolVersion").GetString());
        Assert.Equal(McpConstants.ServerName, result.GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Initialize_WithLeadingUtf8Bom_ShouldParseAndRespond()
    {
        // Arrange: a client (e.g. PowerShell) prepends a decoded UTF-8 BOM character (U+FEFF)
        // to the first line it writes. McpHost.HandleMessageAsync must strip it explicitly —
        // StreamReader's own byte-level BOM auto-detection is unreliable across a redirected
        // pipe, so the host cannot depend on it alone.
        var host = BuildHost();
        const string json = "﻿{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}";

        // Act
        string raw = await SendAsync(host, json);

        // Assert: response must be a valid initialize response, not a parse error
        using var doc = JsonDocument.Parse(raw);
        Assert.True(doc.RootElement.TryGetProperty("result", out var result),
            "Expected a 'result' property — got an error response instead.");
        Assert.Equal(McpConstants.ProtocolVersion, result.GetProperty("protocolVersion").GetString());
    }

    // -------------------------------------------------------------------------
    // tools/list
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ToolsList_ShouldReturn_TwelveTools()
    {
        var host = BuildHost();
        string raw = await SendAsync(host, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

        using var doc = JsonDocument.Parse(raw);
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        Assert.Equal(12, tools.GetArrayLength());
    }

    // -------------------------------------------------------------------------
    // ping
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Ping_ShouldReturn_EmptyResult()
    {
        var host = BuildHost();
        string raw = await SendAsync(host, """{"jsonrpc":"2.0","id":3,"method":"ping"}""");

        using var doc = JsonDocument.Parse(raw);
        Assert.True(doc.RootElement.TryGetProperty("result", out _));
    }

    // -------------------------------------------------------------------------
    // tools/call
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ToolsCall_ShouldRouteToDispatcher_AndReturnContent()
    {
        var dispatcher = new FakeMcpDispatcher();
        var host = BuildHost(dispatcher);
        string raw = await SendAsync(host, """
            {"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"sql_list_databases","arguments":{}}}
            """);

        Assert.True(dispatcher.DispatchCalled);
        using var doc = JsonDocument.Parse(raw);
        var content = doc.RootElement.GetProperty("result").GetProperty("content");
        Assert.True(content.GetArrayLength() > 0);
    }

    // -------------------------------------------------------------------------
    // Error cases
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvalidJson_ShouldReturn_ParseError()
    {
        var host = BuildHost();
        string raw = await SendAsync(host, "{ not valid json }");

        using var doc = JsonDocument.Parse(raw);
        int code = doc.RootElement.GetProperty("error").GetProperty("code").GetInt32();
        Assert.Equal(JsonRpcError.ParseError, code);
    }

    [Fact]
    public async Task UnknownMethod_ShouldReturn_MethodNotFound()
    {
        var host = BuildHost();
        string raw = await SendAsync(host, """{"jsonrpc":"2.0","id":5,"method":"unknown/method"}""");

        using var doc = JsonDocument.Parse(raw);
        int code = doc.RootElement.GetProperty("error").GetProperty("code").GetInt32();
        Assert.Equal(JsonRpcError.MethodNotFound, code);
    }

    [Fact]
    public async Task Notification_ShouldProduceNoResponse()
    {
        // notifications/initialized has no id and should produce no output
        var host = BuildHost();
        string raw = await SendAsync(host, """{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        Assert.Empty(raw);
    }

    // =========================================================================
    // Fake
    // =========================================================================

    private sealed class FakeMcpDispatcher : IToolDispatcher
    {
        public bool DispatchCalled { get; private set; }

        public Task<ToolCallResult> DispatchAsync(ToolCallParams callParams, CancellationToken cancellationToken = default)
        {
            DispatchCalled = true;
            return Task.FromResult(ToolCallResult.Success("[\"DemoDb\"]"));
        }
    }

    private sealed class NoopMcpTrailWriter : IMcpTrailWriter
    {
        public void Record(McpCallRecord record) { /* intentionally empty */ }
    }
}
