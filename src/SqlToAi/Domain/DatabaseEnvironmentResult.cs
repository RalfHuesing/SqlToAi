using System.Text.Json.Serialization;

namespace SqlToAi.Domain;

/// <summary>
/// Server and database environment metadata returned by the environment diagnostic service.
/// </summary>
public sealed record DatabaseEnvironmentResult(
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("product_version")] string? ProductVersion,
    [property: JsonPropertyName("product_major_version")] int? ProductMajorVersion,
    [property: JsonPropertyName("edition")] string? Edition,
    [property: JsonPropertyName("engine_edition")] int? EngineEdition,
    [property: JsonPropertyName("compatibility_level")] byte? CompatibilityLevel,
    [property: JsonPropertyName("collation")] string? Collation,
    [property: JsonPropertyName("state_desc")] string? StateDesc,
    [property: JsonPropertyName("is_read_only")] bool? IsReadOnly,
    [property: JsonPropertyName("unavailable_fields")] IReadOnlyList<string> UnavailableFields);
