#nullable enable

using System.Diagnostics;
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

    private static readonly Action<ILogger, string, Exception?> LogUnhandledError =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(5, "UnhandledError"),
            "Unhandled error while processing MCP method {Method}.");

    private readonly IToolDispatcher _dispatcher;
    private readonly ToolRegistry _toolRegistry;
    private readonly IMcpTrailWriter _trail;
    private readonly ILogger<McpHost> _logger;

    /// <summary>Initializes a new instance of <see cref="McpHost"/>.</summary>
    public McpHost(
        IToolDispatcher dispatcher,
        ToolRegistry toolRegistry,
        IMcpTrailWriter trail,
        ILogger<McpHost> logger)
    {
        _dispatcher = dispatcher;
        _toolRegistry = toolRegistry;
        _trail = trail;
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

                await HandleMessageAsync(line, output, cancellationToken);
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

    private async Task HandleMessageAsync(string rawJson, TextWriter output, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? toolName = null;
        string? argsJson = null;
        string correlationId = Guid.NewGuid().ToString("N");
        bool success = false;
        string? responseJson = null;

        // 1) Parse request
        JsonRpcRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<JsonRpcRequest>(rawJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            LogParseError(_logger, ex);
            responseJson = WriteErrorAndCapture(output, null, JsonRpcError.ParseError, "Parse error: invalid JSON.");
            _trail.Record(new McpCallRecord(correlationId, "<unparseable>", null, rawJson, null, responseJson, sw.ElapsedMilliseconds, false));
            return;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Method))
        {
            responseJson = WriteErrorAndCapture(output, null, JsonRpcError.InvalidRequest, "Invalid request: missing method.");
            _trail.Record(new McpCallRecord(correlationId, "<invalid>", null, rawJson, null, responseJson, sw.ElapsedMilliseconds, false));
            return;
        }

        correlationId = ResolveCorrelationId(request.Id);
        LogMethodReceived(_logger, request.Method, null);

        // 2) Dispatch
        try
        {
            switch (request.Method)
            {
                case McpConstants.MethodInitialize:
                    responseJson = HandleInitialize(output, request);
                    success = true;
                    break;

                case McpConstants.MethodInitialized:
                    // Notification — no response required
                    success = true;
                    break;

                case McpConstants.MethodPing:
                    responseJson = WriteResultAndCapture(output, request.Id, new { });
                    success = true;
                    break;

                case McpConstants.MethodToolsList:
                    responseJson = HandleToolsList(output, request);
                    success = true;
                    break;

                case McpConstants.MethodToolsCall:
                    (toolName, argsJson) = ExtractToolCallMetadata(request);
                    responseJson = await HandleToolsCallAsync(output, request, cancellationToken);
                    success = true;
                    break;

                default:
                    responseJson = WriteErrorAndCapture(output, request.Id, JsonRpcError.MethodNotFound, $"Method not found: {request.Method}");
                    break;
            }
        }
        catch (Exception ex)
        {
            LogUnhandledError(_logger, request.Method, ex);
            responseJson = WriteErrorAndCapture(output, request.Id, JsonRpcError.InternalError, $"Internal error: {ex.Message}");
        }

        // 3) Trail (fire-and-forget)
        _trail.Record(new McpCallRecord(
            correlationId,
            request.Method,
            toolName,
            rawJson,
            argsJson,
            responseJson,
            sw.ElapsedMilliseconds,
            success));
    }

    private static string HandleInitialize(TextWriter output, JsonRpcRequest request)
    {
        var result = new InitializeResult();
        return WriteResultAndCapture(output, request.Id, result);
    }

    private string HandleToolsList(TextWriter output, JsonRpcRequest request)
    {
        var result = new ToolListResult { Tools = _toolRegistry.GetAll() };
        return WriteResultAndCapture(output, request.Id, result);
    }

    private async Task<string> HandleToolsCallAsync(TextWriter output, JsonRpcRequest request, CancellationToken cancellationToken)
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
            return WriteErrorAndCapture(output, request.Id, JsonRpcError.InvalidParams, "Invalid tool call parameters.");
        }

        if (callParams is null || string.IsNullOrWhiteSpace(callParams.Name))
        {
            return WriteErrorAndCapture(output, request.Id, JsonRpcError.InvalidParams, "Missing tool name in call parameters.");
        }

        ToolCallResult toolResult = await _dispatcher.DispatchAsync(callParams, cancellationToken);
        return WriteResultAndCapture(output, request.Id, toolResult);
    }

    // -------------------------------------------------------------------------
    // Response writers (tee: serialize once, write to stdio and trail)
    // -------------------------------------------------------------------------

    private static string WriteResultAndCapture(TextWriter output, System.Text.Json.JsonElement? id, object result)
    {
        var response = new JsonRpcResponse { Id = id, Result = result };
        string json = JsonSerializer.Serialize(response, JsonOptions);
        output.WriteLine(json);
        output.Flush();
        return json;
    }

    private static string WriteErrorAndCapture(TextWriter output, System.Text.Json.JsonElement? id, int code, string message)
    {
        var response = new JsonRpcErrorResponse
        {
            Id = id,
            Error = new JsonRpcError { Code = code, Message = message }
        };
        string json = JsonSerializer.Serialize(response, JsonOptions);
        output.WriteLine(json);
        output.Flush();
        return json;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string ResolveCorrelationId(System.Text.Json.JsonElement? id)
    {
        if (!id.HasValue) return Guid.NewGuid().ToString("N");
        var element = id.Value;
        if (element.ValueKind == JsonValueKind.String)
        {
            string? s = element.GetString();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        return element.GetRawText();
    }

    private static (string? toolName, string? argsJson) ExtractToolCallMetadata(JsonRpcRequest request)
    {
        if (!request.Params.HasValue) return (null, null);
        try
        {
            // Pass the raw JSON params through verbatim — it is 1:1 what the LLM sent.
            string raw = request.Params.Value.GetRawText();
            // Best-effort pull of the tool name for nicer filtering.
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("name", out var nameEl)
                && nameEl.ValueKind == JsonValueKind.String)
            {
                return (nameEl.GetString(), raw);
            }
            return (null, raw);
        }
        catch
        {
            return (null, null);
        }
    }
}
