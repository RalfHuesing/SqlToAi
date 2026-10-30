using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Resolves export format identifiers and creates format-specific <see cref="IDataRowWriter"/> instances.
/// </summary>
public static class DataWriterFactory
{
    public const string CsvFormat = "csv";
    public const string JsonlFormat = "jsonl";

    /// <summary>
    /// Resolves the effective format from an explicit argument or output path file extension.
    /// </summary>
    public static Result<string> ResolveFormat(string? format, string outputPath, string? defaultFormat = null)
    {
        if (!string.IsNullOrWhiteSpace(format))
        {
            string trimmed = format.Trim().ToLowerInvariant();
            if (trimmed is CsvFormat or JsonlFormat)
            {
                return trimmed;
            }
            return SqlToAiError.InvalidParameters($"Unsupported export format '{format}'. Supported formats are 'csv' and 'jsonl'.");
        }

        string extension = Path.GetExtension(outputPath);
        if (string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
        {
            return CsvFormat;
        }
        if (string.Equals(extension, ".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            return JsonlFormat;
        }

        if (!string.IsNullOrWhiteSpace(defaultFormat))
        {
            string normalizedDefault = defaultFormat.Trim().ToLowerInvariant();
            if (normalizedDefault is CsvFormat or JsonlFormat)
            {
                return normalizedDefault;
            }
        }

        return CsvFormat;
    }

    /// <summary>
    /// Creates a new <see cref="IDataRowWriter"/> for the specified format over the target stream.
    /// </summary>
    public static IDataRowWriter CreateWriter(string format, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(stream);

        return format.ToLowerInvariant() switch
        {
            CsvFormat => new CsvDataRowWriter(stream),
            JsonlFormat => new JsonlDataRowWriter(stream),
            _ => throw new ArgumentException($"Unsupported format '{format}'.", nameof(format))
        };
    }
}
