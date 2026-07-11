#nullable enable

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;

namespace SqlToAi.Mcp;

/// <summary>
/// Records one entry in the structured MCP call trail. Every method that crosses the
/// <c>McpHost</c> boundary produces exactly one <see cref="McpCallRecord"/>; the
/// <see cref="IMcpTrailWriter"/> turns it into a JSONL metadata file plus, when meaningful,
/// pretty-printed <c>request.json</c> / <c>response.json</c> companions and an optional
/// <c>response.md</c> for Markdown payloads.
/// </summary>
/// <param name="CorrelationId">The JSON-RPC <c>id</c> (or a generated UUID if the request had none).</param>
/// <param name="Method">JSON-RPC method, e.g. <c>initialize</c>, <c>tools/list</c>, <c>tools/call</c>.</param>
/// <param name="Tool">For <c>tools/call</c>: the tool name (e.g. <c>sql_get_schema</c>). Null for other methods.</param>
/// <param name="RawRequestJson">The full JSON-RPC request line as it arrived on stdin (or null for synthetic records).</param>
/// <param name="ArgumentsJson">Raw JSON arguments extracted from the request <c>params</c> field.</param>
/// <param name="ResponseJson">Exact JSON that the server sent back to the LLM — this is 1:1 what the LLM saw, including any anonymization.</param>
/// <param name="DurationMs">Wall-clock duration of the call in milliseconds.</param>
/// <param name="Success">True when the call returned a <c>result</c>, false when it returned an <c>error</c>.</param>
public sealed record McpCallRecord(
    string CorrelationId,
    string Method,
    string? Tool,
    string? RawRequestJson,
    string? ArgumentsJson,
    string? ResponseJson,
    long DurationMs,
    bool Success);

/// <summary>
/// Writes MCP call records to a per-day directory. For each call, the writer produces:
/// <list type="bullet">
///   <item><c>HH-mm-ss-{id}-call.jsonl</c> — one compact metadata line for <c>jq</c> / <c>grep</c>.</item>
///   <item><c>HH-mm-ss-{id}-request.json</c> — pretty-printed full JSON-RPC request (what the LLM sent).</item>
///   <item><c>HH-mm-ss-{id}-response.json</c> — pretty-printed full JSON-RPC response (what the LLM got back).</item>
///   <item><c>HH-mm-ss-{id}-response.md</c> — only when the response carries a Markdown <c>text</c> content block.</item>
/// </list>
/// </summary>
public interface IMcpTrailWriter
{
    /// <summary>Records a single call. Fire-and-forget by design — never throws into the MCP loop.</summary>
    void Record(McpCallRecord record);
}

/// <inheritdoc/>
public sealed class McpTrailWriter : IMcpTrailWriter, IDisposable
{
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly Action<ILogger, string, Exception?> LogWriteFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(1, "WriteFailed"),
            "Failed to write MCP trail record for {CorrelationId}.");

    private readonly LoggingOptions _options;
    private readonly ILogger<McpTrailWriter> _logger;
    private readonly object _writeLock = new();

    /// <summary>Initializes a new instance of <see cref="McpTrailWriter"/>.</summary>
    public McpTrailWriter(IOptions<SqlToAiOptions> options, ILogger<McpTrailWriter> logger)
    {
        _options = options.Value.Logging;
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Record(McpCallRecord record)
    {
        if (!_options.McpTrail.Enabled) return;

        try
        {
            // Per-day subdirectory under {log-root}/mcp
            string dateDir = Path.Combine(_options.GetAbsoluteRoot(), _options.McpTrail.Directory, DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(dateDir);

            // The correlation id originates from the client-supplied JSON-RPC "id" field,
            // so it must be sanitized before it can be used as a path segment — otherwise
            // a crafted id (e.g. containing "..", "/", or "\") could write the trail file
            // outside dateDir (path traversal).
            string safeCorrelationId = SanitizeForFileName(record.CorrelationId);
            string filePrefix = $"{DateTime.UtcNow:HH-mm-ss}-{safeCorrelationId}";

            // The metadata line — one JSONL row per call, never truncated.
            string line = JsonSerializer.Serialize(ToJsonShape(record), CompactJsonOptions);
            string jsonlPath = Path.Combine(dateDir, $"{filePrefix}-call.jsonl");

            // Pretty-printed companions for humans / editors / syntax highlighters.
            string? requestPath = !string.IsNullOrEmpty(record.RawRequestJson)
                ? Path.Combine(dateDir, $"{filePrefix}-request.json")
                : null;
            string? responsePath = !string.IsNullOrEmpty(record.ResponseJson)
                ? Path.Combine(dateDir, $"{filePrefix}-response.json")
                : null;
            string? responseMdPath = responsePath is not null
                ? Path.Combine(dateDir, $"{filePrefix}-response.md")
                : null;

            // Lock so concurrent calls cannot interleave bytes in the same file. Defensive
            // even though the filenames are unique per call.
            lock (_writeLock)
            {
                File.AppendAllText(jsonlPath, line + Environment.NewLine);
                WritePrettyJson(requestPath, record.RawRequestJson);
                WritePrettyJson(responsePath, record.ResponseJson);
                WriteMarkdownCompanion(responseMdPath, record.ResponseJson);
            }
        }
        catch (Exception ex)
        {
            // The trail must never break the MCP loop. If writing fails, log and move on.
            LogWriteFailed(_logger, record.CorrelationId, ex);
        }
    }

    /// <summary>Disposes nothing — the writer holds no unmanaged resources.</summary>
    public void Dispose() { }

    // -------------------------------------------------------------------------
    // File writers
    // -------------------------------------------------------------------------

    private static void WritePrettyJson(string? path, string? content)
    {
        if (path is null || string.IsNullOrEmpty(content)) return;

        // Try to re-parse and pretty-print; fall back to the raw text if it is not valid
        // JSON for any reason (defensive — the trail is a debugging aid, not a sanitizer).
        try
        {
            using var doc = JsonDocument.Parse(content);
            File.WriteAllText(path, JsonSerializer.Serialize(doc.RootElement, PrettyJsonOptions));
        }
        catch (JsonException)
        {
            File.WriteAllText(path, content);
        }
    }

    /// <summary>
    /// If the response is a JSON-RPC success containing a single MCP <c>text</c> content
    /// block whose <c>text</c> looks like Markdown (starts with a Markdown marker or
    /// contains a markdown table separator), write a companion <c>.md</c> file with the
    /// inner text verbatim. Otherwise, no file is written.
    /// </summary>
    private static void WriteMarkdownCompanion(string? path, string? content)
    {
        if (path is null || string.IsNullOrEmpty(content)) return;

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (!root.TryGetProperty("result", out var result)) return;
            if (!result.TryGetProperty("content", out var contentArr)) return;
            if (contentArr.ValueKind != JsonValueKind.Array) return;
            if (contentArr.GetArrayLength() != 1) return;

            var first = contentArr[0];
            if (!first.TryGetProperty("type", out var typeEl) || typeEl.GetString() != "text") return;
            if (!first.TryGetProperty("text", out var textEl) || textEl.ValueKind != JsonValueKind.String) return;

            string text = textEl.GetString() ?? string.Empty;
            if (LooksLikeMarkdown(text))
            {
                File.WriteAllText(path, text);
            }
        }
        catch (JsonException)
        {
            // Not a valid JSON response — nothing to extract.
        }
    }

    private static bool LooksLikeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        // Cheap heuristics that cover the schema-discoverable MCP tools (sql_get_schema,
        // sql_get_routine_parameters, ...). Markdown responses are always multi-line and
        // start with a header, a list, or a code block.
        ReadOnlySpan<char> span = text.AsSpan();
        int newline = span.IndexOf('\n');
        ReadOnlySpan<char> firstLine = newline < 0 ? span : span[..newline];
        firstLine = firstLine.TrimStart();

        if (firstLine.Length == 0) return false;
        if (firstLine[0] is '#' or '-' or '*' or '>') return true;          // header / list / blockquote
        if (firstLine.StartsWith("```")) return true;                      // fenced code block
        if (text.Contains("|---", StringComparison.Ordinal)) return true;   // markdown table

        return false;
    }

    /// <summary>
    /// Restricts a value to characters safe for use as a single path segment: ASCII letters,
    /// digits, hyphen, and underscore. Everything else (including <c>..</c>, <c>/</c>, and
    /// <c>\</c>) is replaced with an underscore, and the result is capped in length.
    /// </summary>
    private static string SanitizeForFileName(string value)
    {
        const int maxLength = 80;
        Span<char> buffer = value.Length <= maxLength ? stackalloc char[value.Length] : stackalloc char[maxLength];

        for (int i = 0; i < buffer.Length; i++)
        {
            char c = value[i];
            buffer[i] = char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_';
        }

        return buffer.IsEmpty ? "unknown" : new string(buffer);
    }

    // -------------------------------------------------------------------------
    // JSON shape (snake_case-ish, compact, human-readable)
    // -------------------------------------------------------------------------

    private static object ToJsonShape(McpCallRecord r) => new
    {
        ts          = DateTime.UtcNow.ToString("O"),
        id          = r.CorrelationId,
        method      = r.Method,
        tool        = r.Tool,
        args        = r.ArgumentsJson,
        response    = r.ResponseJson,
        duration_ms = r.DurationMs,
        success     = r.Success
    };
}
