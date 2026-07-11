#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace SqlToAi.Mcp;

/// <summary>
/// Stdio-based MCP server host. Reads newline-delimited JSON-RPC 2.0 messages from
/// <see cref="Console.In"/>, dispatches them through <see cref="IToolDispatcher"/>,
/// and writes responses to <see cref="Console.Out"/>.
/// </summary>
public sealed class McpHost : IMcpHost
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Action<ILogger, string, Exception?> LogMethodReceived =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1, "MethodReceived"),
            "Received MCP method: {Method}");

    private static readonly Action<ILogger, Exception?> LogParseError =
        LoggerMessage.Define(LogLevel.Warning, new EventId(2, "ParseError"),
            "Failed to parse incoming JSON-RPC message.");

    private static readonly Action<ILogger, Exception?> LogServerStarted =
        LoggerMessage.Define(LogLevel.Information, new EventId(3, "ServerStarted"),
            "SqlToAi MCP server started on stdio.");

    private static readonly Action<ILogger, Exception?> LogServerStopped =
        LoggerMessage.Define(LogLevel.Information, new EventId(4, "ServerStopped"),
            "SqlToAi MCP server stopped.");

    private readonly IToolDispatcher _dispatcher;
    private readonly ToolRegistry _toolRegistry;
    private readonly ILogger<McpHost> _logger;

    /// <summary>Initializes a new instance of <see cref="McpHost"/>.</summary>
    public McpHost(
        IToolDispatcher dispatcher,
        ToolRegistry toolRegistry,
        ILogger<McpHost> logger)
    {
        _dispatcher = dispatcher;
        _toolRegistry = toolRegistry;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Ensure stdout uses UTF-8 without BOM for cross-platform JSON compatibility
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding  = System.Text.Encoding.UTF8;

        LogServerStarted(_logger, null);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await Console.In.ReadLineAsync(cancellationToken);
                if (line is null) break; // stdin closed — client disconnected

                if (string.IsNullOrWhiteSpace(line)) continue;

                await HandleMessageAsync(line, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        finally
        {
            LogServerStopped(_logger, null);
        }
    }

    // -------------------------------------------------------------------------
    // Message handling
    // -------------------------------------------------------------------------

    private async Task HandleMessageAsync(string rawJson, CancellationToken cancellationToken)
    {
        JsonRpcRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<JsonRpcRequest>(rawJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            LogParseError(_logger, ex);
            WriteError(null, JsonRpcError.ParseError, "Parse error: invalid JSON.");
            return;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Method))
        {
            WriteError(null, JsonRpcError.InvalidRequest, "Invalid request: missing method.");
            return;
        }

        LogMethodReceived(_logger, request.Method, null);

        switch (request.Method)
        {
            case McpConstants.MethodInitialize:
                HandleInitialize(request);
                break;

            case McpConstants.MethodInitialized:
                // Notification — no response required
                break;

            case McpConstants.MethodPing:
                WriteResult(request.Id, new { });
                break;

            case McpConstants.MethodToolsList:
                HandleToolsList(request);
                break;

            case McpConstants.MethodToolsCall:
                await HandleToolsCallAsync(request, cancellationToken);
                break;

            default:
                WriteError(request.Id, JsonRpcError.MethodNotFound, $"Method not found: {request.Method}");
                break;
        }
    }

    private static void HandleInitialize(JsonRpcRequest request)
    {
        var result = new InitializeResult();
        WriteResult(request.Id, result);
    }

    private void HandleToolsList(JsonRpcRequest request)
    {
        var result = new ToolListResult { Tools = _toolRegistry.GetAll() };
        WriteResult(request.Id, result);
    }

    private async Task HandleToolsCallAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        ToolCallParams? callParams;
        try
        {
            callParams = request.Params.HasValue
                ? JsonSerializer.Deserialize<ToolCallParams>(request.Params.Value.GetRawText(), JsonOptions)
                : null;
        }
        catch (JsonException)
        {
            WriteError(request.Id, JsonRpcError.InvalidParams, "Invalid tool call parameters.");
            return;
        }

        if (callParams is null || string.IsNullOrWhiteSpace(callParams.Name))
        {
            WriteError(request.Id, JsonRpcError.InvalidParams, "Missing tool name in call parameters.");
            return;
        }

        ToolCallResult toolResult = await _dispatcher.DispatchAsync(callParams, cancellationToken);
        WriteResult(request.Id, toolResult);
    }

    // -------------------------------------------------------------------------
    // Response writers
    // -------------------------------------------------------------------------

    private static void WriteResult(System.Text.Json.JsonElement? id, object result)
    {
        var response = new JsonRpcResponse { Id = id, Result = result };
        Console.WriteLine(JsonSerializer.Serialize(response, JsonOptions));
        Console.Out.Flush();
    }

    private static void WriteError(System.Text.Json.JsonElement? id, int code, string message)
    {
        var response = new JsonRpcErrorResponse
        {
            Id = id,
            Error = new JsonRpcError { Code = code, Message = message }
        };
        Console.WriteLine(JsonSerializer.Serialize(response, JsonOptions));
        Console.Out.Flush();
    }
}
