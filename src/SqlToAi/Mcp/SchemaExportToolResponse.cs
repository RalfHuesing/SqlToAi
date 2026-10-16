using System.Text.Json;
using System.Text.Json.Serialization;
using SqlToAi.Cli;

namespace SqlToAi.Mcp;

internal sealed record SchemaExportToolResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("output_path")] string? OutputPath,
    [property: JsonPropertyName("partial_output")] bool PartialOutput,
    [property: JsonPropertyName("error")] SchemaExportToolError? Error,
    [property: JsonPropertyName("reason")] string? Reason)
{
    internal static ToolCallResult Render(SchemaExportOutcome outcome) => new()
    {
        Content = [new ToolContent { Text = JsonSerializer.Serialize(new SchemaExportToolResponse(
            outcome.Status.ToString().ToLowerInvariant(), outcome.OutputPath, outcome.PartialOutput,
            outcome.Error is { } error ? new SchemaExportToolError(error.Code, error.Message) : null,
            outcome.Reason), McpJsonContext.Default.SchemaExportToolResponse) }],
        IsError = outcome.Status != SchemaExportStatus.Completed
    };

    internal static bool IsQualifiedWindowsDirectory(string path)
    {
        string normalized = path.Replace('/', '\\');
        if (normalized.StartsWith(@"\\?\", StringComparison.Ordinal) || normalized.StartsWith(@"\\.\", StringComparison.Ordinal))
            return false;
        if (normalized.Length >= 3 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':' && normalized[2] == '\\')
            return true;
        if (!normalized.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        string[] parts = normalized[2..].Split('\\');
        return parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1])
            && parts[0] is not ("?" or ".") && !normalized.Contains(':', StringComparison.Ordinal);
    }
}

internal sealed record SchemaExportToolError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);
