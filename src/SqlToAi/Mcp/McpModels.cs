using System.Text.Json.Serialization;

namespace SqlToAi.Mcp;

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
    public string? Type { get; init; } = "string";

    [JsonPropertyName("anyOf")]
    public IReadOnlyList<ToolParameterDefinition>? AnyOf { get; init; }

    [JsonPropertyName("items")]
    public ToolParameterDefinition? Items { get; init; }

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
