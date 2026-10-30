using System.Text.Json.Serialization;

namespace SqlToAi.Domain;

/// <summary>
/// Server and database environment metadata returned by the environment diagnostic service.
/// </summary>
/// <param name="Target">Canonical qualified alias/database target name.</param>
/// <param name="ProductVersion">SQL Server version string (e.g. '16.0.4180.1').</param>
/// <param name="ProductMajorVersion">Numeric major version (e.g. 16).</param>
/// <param name="Edition">SQL Server edition string (e.g. 'Developer Edition (64-bit)').</param>
/// <param name="EngineEdition">Numeric engine edition (e.g. 3 for Enterprise/Developer, 5 for Azure SQL Database).</param>
/// <param name="CompatibilityLevel">Target database compatibility level (e.g. 160).</param>
/// <param name="Collation">Database collation name (e.g. 'Latin1_General_CI_AS').</param>
/// <param name="StateDesc">Database state (e.g. 'ONLINE').</param>
/// <param name="IsReadOnly">Whether the SQL Server database itself is read-only.</param>
/// <param name="UnavailableFields">Names of allowlisted fields the server did not provide.</param>
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
