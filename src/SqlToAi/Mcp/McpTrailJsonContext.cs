#nullable enable

using System.Text.Json.Serialization;

namespace SqlToAi.Mcp;

/// <summary>
/// Dedicated source generator context for <see cref="McpCallRecordShape"/> serialization in MCP trail logging.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(McpCallRecordShape))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
internal sealed partial class McpTrailJsonContext : JsonSerializerContext;
