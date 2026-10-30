using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Request parameters for exporting query results directly to a local file.
/// </summary>
public sealed record DataExportRequest(
    string Database,
    string Query,
    string OutputPath,
    string? Format = null,
    bool Overwrite = false,
    int? RequestedRowLimit = null,
    object? Parameters = null);

/// <summary>
/// Streams query results directly to local files in CSV or JSONL format.
/// </summary>
public interface IDataExportService
{
    /// <summary>
    /// Executes a read-only query and streams its results to a local file.
    /// </summary>
    Task<Result<DataExportResult>> ExportDataAsync(DataExportRequest request, CancellationToken cancellationToken = default);
}
