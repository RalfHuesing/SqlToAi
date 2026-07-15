#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlToAi.Mcp;

/// <summary>
/// Provides the two <see cref="JsonSerializerOptions"/> instances used by
/// <see cref="McpTrailWriter"/>: one compact (JSONL lines) and one pretty-printed (companion files).
/// Centralizing these options decouples <see cref="McpTrailWriter"/> from the large
/// <see cref="McpJsonContext"/> type graph and keeps the transitive AIContextFootprint within limits.
/// </summary>
internal static class McpTrailJsonOptions
{
    /// <summary>Compact options: no indentation, nulls omitted, relaxed escaping.</summary>
    internal static readonly JsonSerializerOptions Compact = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = McpJsonContext.Default
    };

    /// <summary>Pretty options: indented, relaxed escaping.</summary>
    internal static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = McpJsonContext.Default
    };

    /// <summary>Compact context instance derived from <see cref="Compact"/>.</summary>
    internal static readonly McpJsonContext CompactContext = new(Compact);

    /// <summary>Pretty context instance derived from <see cref="Pretty"/>.</summary>
    internal static readonly McpJsonContext PrettyContext = new(Pretty);
}
