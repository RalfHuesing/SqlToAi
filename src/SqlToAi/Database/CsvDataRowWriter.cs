using System.Globalization;
using System.Text;

namespace SqlToAi.Database;

/// <summary>
/// Streams tabular result rows into RFC 4180 compliant CSV formatted output.
/// </summary>
public sealed class CsvDataRowWriter : IDataRowWriter
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly char[] CharsToEscape = [',', '"', '\r', '\n'];

    private readonly StreamWriter _writer;

    /// <summary>Initializes a new instance of <see cref="CsvDataRowWriter"/>.</summary>
    public CsvDataRowWriter(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _writer = new StreamWriter(stream, Utf8WithoutBom, bufferSize: 65536, leaveOpen: true)
        {
            NewLine = "\r\n"
        };
    }

    /// <inheritdoc/>
    public async Task WriteHeaderAsync(IReadOnlyList<ExportColumnInfo> columns, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(columns);
        for (int i = 0; i < columns.Count; i++)
        {
            if (i > 0)
            {
                await _writer.WriteAsync(',').ConfigureAwait(false);
            }
            await _writer.WriteAsync(EscapeField(columns[i].Name)).ConfigureAwait(false);
        }
        await _writer.WriteLineAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task WriteRowAsync(IReadOnlyList<ExportColumnInfo> columns, object?[] values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(values);

        for (int i = 0; i < columns.Count; i++)
        {
            if (i > 0)
            {
                await _writer.WriteAsync(',').ConfigureAwait(false);
            }
            object? val = i < values.Length ? values[i] : null;
            await _writer.WriteAsync(FormatValue(val)).ConfigureAwait(false);
        }
        await _writer.WriteLineAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync().ConfigureAwait(false);
    }

    private static string FormatValue(object? val)
    {
        if (val is null or DBNull)
        {
            return string.Empty;
        }

        if (val is string s)
        {
            if (s.Length == 0)
            {
                return "\"\"";
            }
            return EscapeField(s);
        }

        if (val is bool b)
        {
            return b ? "true" : "false";
        }

        if (val is DateTime dt)
        {
            return dt.ToString("O", CultureInfo.InvariantCulture);
        }

        if (val is DateTimeOffset dto)
        {
            return dto.ToString("O", CultureInfo.InvariantCulture);
        }

        if (val is byte[] bytes)
        {
            return Convert.ToBase64String(bytes);
        }

        if (val is IFormattable formattable)
        {
            string formatted = formattable.ToString(null, CultureInfo.InvariantCulture);
            return EscapeField(formatted);
        }

        string str = Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty;
        if (str.Length == 0)
        {
            return "\"\"";
        }
        return EscapeField(str);
    }

    private static string EscapeField(string text)
    {
        if (text.IndexOfAny(CharsToEscape) >= 0)
        {
            return $"\"{text.Replace("\"", "\"\"")}\"";
        }
        return text;
    }
}
