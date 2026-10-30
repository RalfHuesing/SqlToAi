namespace SqlToAi.Database;

/// <summary>
/// Writes schema header and row data to an underlying export stream.
/// </summary>
public interface IDataRowWriter : IAsyncDisposable
{
    /// <summary>Writes the header or column metadata row to the output stream.</summary>
    Task WriteHeaderAsync(IReadOnlyList<ExportColumnInfo> columns, CancellationToken cancellationToken = default);

    /// <summary>Writes an individual data row to the output stream.</summary>
    Task WriteRowAsync(IReadOnlyList<ExportColumnInfo> columns, object?[] values, CancellationToken cancellationToken = default);

    /// <summary>Flushes buffered row data to the destination stream.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
