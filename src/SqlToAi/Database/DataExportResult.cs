using System.Text.Json.Serialization;

namespace SqlToAi.Database;

/// <summary>
/// Column metadata returned as part of a file export result.
/// </summary>
/// <param name="Name">Column name.</param>
/// <param name="Type">Normalized data type string.</param>
public sealed record ExportColumnInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type);

/// <summary>
/// Metadata result produced by a successful data export operation.
/// </summary>
public sealed record DataExportResult(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("file_path")] string FilePath,
    [property: JsonPropertyName("format")] string Format,
    [property: JsonPropertyName("row_count")] int RowCount,
    [property: JsonPropertyName("file_size_bytes")] long FileSizeBytes,
    [property: JsonPropertyName("columns")] IReadOnlyList<ExportColumnInfo> Columns,
    [property: JsonPropertyName("elapsed_ms")] long ElapsedMs,
    [property: JsonPropertyName("cpu_ms")] long CpuMs,
    [property: JsonPropertyName("logical_reads")] long LogicalReads,
    [property: JsonPropertyName("was_anonymized")] bool WasAnonymized,
    [property: JsonPropertyName("anonymized_columns")] IReadOnlyList<string> AnonymizedColumns);
