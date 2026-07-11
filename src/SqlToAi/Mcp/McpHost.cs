#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace SqlToAi.Mcp;

/// <summary>
/// Stdio-based MCP server host. Reads newline-delimited JSON-RPC 2.0 messages from
/// a caller-supplied <see cref="TextReader"/>, dispatches them through
/// <see cref="IToolDispatcher"/>, and writes responses to a caller-supplied
/// <see cref="TextWriter"/>.
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
    public async Task RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        LogServerStarted(_logger, null);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await input.ReadLineAsync(cancellationToken);
                if (line is null) break; // stdin closed — client disconnected

                if (string.IsNullOrWhiteSpace(line)) continue;

                await HandleMessageAsync(line, input, output, cancellationToken);
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

    private async Task HandleMessageAsync(string rawJson, TextReader input, TextWriter output, CancellationToken cancellationToken)
    {
        JsonRpcRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<JsonRpcRequest>(rawJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            LogParseError(_logger, ex);
            WriteError(output, null, JsonRpcError.ParseError, "Parse error: invalid JSON.");
            return;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Method))
        {
            WriteError(output, null, JsonRpcError.InvalidRequest, "Invalid request: missing method.");
            return;
        }

        LogMethodReceived(_logger, request.Method, null);

        switch (request.Method)
        {
            case McpConstants.MethodInitialize:
                HandleInitialize(output, request);
                break;

            case McpConstants.MethodInitialized:
                // Notification — no response required
                break;

            case McpConstants.MethodPing:
                WriteResult(output, request.Id, new { });
                break;

            case McpConstants.MethodToolsList:
                HandleToolsList(output, request);
                break;

            case McpConstants.MethodToolsCall:
                await HandleToolsCallAsync(output, request, cancellationToken);
                break;

            default:
                WriteError(output, request.Id, JsonRpcError.MethodNotFound, $"Method not found: {request.Method}");
                break;
        }
    }

    private static void HandleInitialize(TextWriter output, JsonRpcRequest request)
    {
        var result = new InitializeResult();
        WriteResult(output, request.Id, result);
    }

    private void HandleToolsList(TextWriter output, JsonRpcRequest request)
    {
        var result = new ToolListResult { Tools = _toolRegistry.GetAll() };
        WriteResult(output, request.Id, result);
    }

    private async Task HandleToolsCallAsync(TextWriter output, JsonRpcRequest request, CancellationToken cancellationToken)
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
            WriteError(output, request.Id, JsonRpcError.InvalidParams, "Invalid tool call parameters.");
            return;
        }

        if (callParams is null || string.IsNullOrWhiteSpace(callParams.Name))
        {
            WriteError(output, request.Id, JsonRpcError.InvalidParams, "Missing tool name in call parameters.");
            return;
        }

        ToolCallResult toolResult = await _dispatcher.DispatchAsync(callParams, cancellationToken);
        WriteResult(output, request.Id, toolResult);
    }

    // -------------------------------------------------------------------------
    // Response writers
    // -------------------------------------------------------------------------

    private static void WriteResult(TextWriter output, System.Text.Json.JsonElement? id, object result)
    {
        var response = new JsonRpcResponse { Id = id, Result = result };
        output.WriteLine(JsonSerializer.Serialize(response, JsonOptions));
        output.Flush();
    }

    private static void WriteError(TextWriter output, System.Text.Json.JsonElement? id, int code, string message)
    {
        var response = new JsonRpcErrorResponse
        {
            Id = id,
            Error = new JsonRpcError { Code = code, Message = message }
        };
        output.WriteLine(JsonSerializer.Serialize(response, JsonOptions));
        output.Flush();
    }
}
