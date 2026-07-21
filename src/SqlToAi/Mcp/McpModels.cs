#nullable enable

using System.Text.Json.Serialization;

namespace SqlToAi.Mcp;

// ---------------------------------------------------------------------------
// initialize
// ---------------------------------------------------------------------------

/// <summary>Parameters for the MCP <c>initialize</c> request.</summary>
public sealed class InitializeParams
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; init; } = string.Empty;

    [JsonPropertyName("clientInfo")]
    public ClientInfo? ClientInfo { get; init; }

    [JsonPropertyName("capabilities")]
    public ClientCapabilities? Capabilities { get; init; }
}

/// <summary>Info block about the connecting MCP client.</summary>
public sealed class ClientInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;
}

/// <summary>Client-declared capabilities (currently unused by this server).</summary>
public sealed class ClientCapabilities;

/// <summary>Result of the MCP <c>initialize</c> request.</summary>
public sealed class InitializeResult
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; init; } = McpConstants.ProtocolVersion;

    [JsonPropertyName("serverInfo")]
    public ServerInfo ServerInfo { get; init; } = new();

    [JsonPropertyName("capabilities")]
    public ServerCapabilities Capabilities { get; init; } = new();

    /// <summary>
    /// One-time behavioral guidance for the connecting agent (see <see cref="McpConstants.ServerInstructions"/>).
    /// </summary>
    [JsonPropertyName("instructions")]
    public string Instructions { get; init; } = McpConstants.ServerInstructions;
}

/// <summary>Metadata about this MCP server.</summary>
public sealed class ServerInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = McpConstants.ServerName;

    [JsonPropertyName("version")]
    public string Version { get; init; } = McpConstants.ServerVersion;
}

/// <summary>Server-declared capabilities advertised to the MCP client.</summary>
public sealed class ServerCapabilities
{
    [JsonPropertyName("tools")]
    public ToolsCapability Tools { get; init; } = new();
}

/// <summary>Declares that the server supports the <c>tools/list</c> method.</summary>
public sealed class ToolsCapability
{
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; init; } = false;
}

// ---------------------------------------------------------------------------
// tools/list
// ---------------------------------------------------------------------------

/// <summary>Result payload for the MCP <c>tools/list</c> method.</summary>
public sealed class ToolListResult
{
    [JsonPropertyName("tools")]
    public IReadOnlyList<ToolDefinition> Tools { get; init; } = [];
}

/// <summary>Describes a single MCP tool exposed by this server.</summary>
public sealed class ToolDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("inputSchema")]
    public ToolInputSchema InputSchema { get; init; } = new();
}

/// <summary>JSON Schema definition for a tool's input arguments.</summary>
public sealed class ToolInputSchema
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, ToolParameterDefinition> Properties { get; init; } = [];

    [JsonPropertyName("required")]
    public IReadOnlyList<string> Required { get; init; } = [];
}

/// <summary>JSON Schema definition for a single tool parameter.</summary>
public sealed class ToolParameterDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "string";

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}

// ---------------------------------------------------------------------------
// tools/call
// ---------------------------------------------------------------------------

/// <summary>Parameters for the MCP <c>tools/call</c> request.</summary>
public sealed class ToolCallParams
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("arguments")]
    public Dictionary<string, object?> Arguments { get; init; } = [];
}

/// <summary>Result payload for the MCP <c>tools/call</c> method.</summary>
public sealed class ToolCallResult
{
    [JsonPropertyName("content")]
    public IReadOnlyList<ToolContent> Content { get; init; } = [];

    [JsonPropertyName("isError")]
    public bool IsError { get; init; }

    /// <summary>Creates a successful text result.</summary>
    public static ToolCallResult Success(string text) => new()
    {
        Content = [new ToolContent { Type = "text", Text = text }],
        IsError = false
    };

    /// <summary>Creates an error result carrying the SqlToAi error code and message.</summary>
    public static ToolCallResult Failure(string errorCode, string message) => new()
    {
        Content = [new ToolContent { Type = "text", Text = $"[{errorCode}] {message}" }],
        IsError = true
    };
}

/// <summary>A single content block inside a tool result (text or image).</summary>
public sealed class ToolContent
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "text";

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}
