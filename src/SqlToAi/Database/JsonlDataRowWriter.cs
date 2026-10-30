using System.Buffers;
using System.Globalization;
using System.Text.Json;

namespace SqlToAi.Database;

/// <summary>
/// Streams tabular result rows into Newline-Delimited JSON (JSONL) formatted output.
/// </summary>
public sealed class JsonlDataRowWriter : IDataRowWriter
{
    private static readonly byte[] Newline = [(byte)'\n'];

    private readonly Stream _stream;
    private readonly ArrayBufferWriter<byte> _buffer = new(4096);
    private readonly Utf8JsonWriter _writer;

    /// <summary>Initializes a new instance of <see cref="JsonlDataRowWriter"/>.</summary>
    public JsonlDataRowWriter(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _writer = new Utf8JsonWriter(_buffer, new JsonWriterOptions { SkipValidation = true });
    }

    /// <inheritdoc/>
    public Task WriteHeaderAsync(IReadOnlyList<ExportColumnInfo> columns, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task WriteRowAsync(IReadOnlyList<ExportColumnInfo> columns, object?[] values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(values);

        _buffer.Clear();
        _writer.Reset(_buffer);

        _writer.WriteStartObject();
        for (int i = 0; i < columns.Count; i++)
        {
            _writer.WritePropertyName(columns[i].Name);
            object? val = i < values.Length ? values[i] : null;
            WriteJsonValue(_writer, val);
        }
        _writer.WriteEndObject();
        await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);

        await _stream.WriteAsync(_buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        await _stream.WriteAsync(Newline, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync().ConfigureAwait(false);
        await _stream.FlushAsync().ConfigureAwait(false);
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, object? val)
    {
        if (val is null or DBNull)
        {
            writer.WriteNullValue();
            return;
        }

        switch (val)
        {
            case string s:
                writer.WriteStringValue(s);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case short sh:
                writer.WriteNumberValue(sh);
                break;
            case byte by:
                writer.WriteNumberValue(by);
                break;
            case sbyte sby:
                writer.WriteNumberValue(sby);
                break;
            case uint ui:
                writer.WriteNumberValue(ui);
                break;
            case ulong ul:
                writer.WriteNumberValue(ul);
                break;
            case ushort ush:
                writer.WriteNumberValue(ush);
                break;
            case decimal dec:
                writer.WriteNumberValue(dec);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            case DateTime dt:
                writer.WriteStringValue(dt.ToString("O", CultureInfo.InvariantCulture));
                break;
            case DateTimeOffset dto:
                writer.WriteStringValue(dto.ToString("O", CultureInfo.InvariantCulture));
                break;
            case TimeSpan ts:
                writer.WriteStringValue(ts.ToString("c", CultureInfo.InvariantCulture));
                break;
            case Guid g:
                writer.WriteStringValue(g.ToString("D"));
                break;
            case byte[] bytes:
                writer.WriteBase64StringValue(bytes);
                break;
            case JsonElement je:
                je.WriteTo(writer);
                break;
            default:
                writer.WriteStringValue(Convert.ToString(val, CultureInfo.InvariantCulture));
                break;
        }
    }
}
