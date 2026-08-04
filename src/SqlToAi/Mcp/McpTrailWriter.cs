#nullable enable

using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
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
/// A helper type representing the serialized shape of a trail record.
/// </summary>
public sealed record McpCallRecordShape(
    [property: JsonPropertyName("ts")] string Ts,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("tool")] string? Tool,
    [property: JsonPropertyName("args")] string? Args,
    [property: JsonPropertyName("response")] string? Response,
    [property: JsonPropertyName("duration_ms")] long DurationMs,
    [property: JsonPropertyName("success")] bool Success
);

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
    private static readonly JsonSerializerOptions CompactJsonOptions = McpTrailJsonOptions.Compact;
    private static readonly JsonSerializerOptions PrettyJsonOptions = McpTrailJsonOptions.Pretty;
    private static readonly McpJsonContext CompactContext = McpTrailJsonOptions.CompactContext;
    private static readonly McpJsonContext PrettyContext = McpTrailJsonOptions.PrettyContext;

    private static readonly Action<ILogger, string, Exception?> LogWriteFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(1, "WriteFailed"),
            "Failed to write MCP trail record for {CorrelationId}.");

    /// <summary>
    /// JSON-RPC envelope keys whose values are excluded from redaction, but only when found
    /// directly on the envelope root of an envelope document (<see cref="RedactionContext.IsEnvelopeRoot"/>)
    /// — never for a same-named property found elsewhere in the tree (e.g. inside <c>arguments</c>).
    /// </summary>
    private static readonly HashSet<string> EnvelopeKeys = new(StringComparer.Ordinal)
    {
        "jsonrpc", "id", "method",
    };

    /// <summary>Discriminator key of an MCP content block, exempt only inside a direct <c>content[]</c> element.</summary>
    private const string ContentBlockTypeKey = "type";

    private const string ArrayElementPlaceholderName = "value";

    /// <summary>
    /// Carries positional context through the redaction recursion so the structural-key
    /// exemption can no longer be decided by name alone (the CRITICAL fix for the
    /// audit-hardening EPIC-03 step-003 review finding): a same-named property found deeper
    /// in free-form, LLM-authored content (e.g. an <c>arguments</c> value named <c>id</c>)
    /// must still be redacted.
    /// </summary>
    /// <param name="IsEnvelopeRoot">True only for the root object of an envelope document (<see cref="RawRequestJson"/>/<see cref="ResponseJson"/>), never for <see cref="ArgumentsJson"/> or any nested object.</param>
    /// <param name="IsContentBlock">True only for a direct object element of a <c>content</c> array, one recursion level deep, and only when that array was found directly on <see cref="IsResultObject"/> (i.e. <c>result.content[]</c>) — never for a same-named <c>content</c> array found anywhere else in the tree (audit-hardening TD-003).</param>
    /// <param name="IsResultObject">True only for the <c>result</c> object found as a direct child of the envelope root, one recursion level deep — never for any deeper descendant.</param>
    private readonly record struct RedactionContext(bool IsEnvelopeRoot, bool IsContentBlock, bool IsResultObject);

    private readonly LoggingOptions _options;
    private readonly ILogger<McpTrailWriter> _logger;
    private readonly IAnonymizer _anonymizer;
    private readonly object _writeLock = new();

    /// <summary>Initializes a new instance of <see cref="McpTrailWriter"/>.</summary>
    public McpTrailWriter(IOptions<SqlToAiOptions> options, ILogger<McpTrailWriter> logger, IAnonymizer anonymizer)
    {
        _options = options.Value.Logging;
        _logger = logger;
        _anonymizer = anonymizer;
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

            // Redact before any of the four output files are produced, so the JSONL metadata
            // line and the pretty-printed companions are all fed from the same anonymized
            // source — a local agent reading the trail files directly must never see more
            // than what a ReadOnlyAnonymized database would expose via the MCP channel.
            string? anonymizedArgs = AnonymizeJsonStrings(record.ArgumentsJson, isEnvelope: false);
            string? anonymizedRequest = AnonymizeJsonStrings(record.RawRequestJson, isEnvelope: true);
            string? anonymizedResponse = AnonymizeJsonStrings(record.ResponseJson, isEnvelope: true);

            // The metadata line — one JSONL row per call, never truncated.
            string line = JsonSerializer.Serialize(ToJsonShape(record, anonymizedArgs, anonymizedResponse), typeof(McpCallRecordShape), CompactContext);
            string jsonlPath = Path.Combine(dateDir, $"{filePrefix}-call.jsonl");

            // Pretty-printed companions for humans / editors / syntax highlighters.
            string? requestPath = !string.IsNullOrEmpty(anonymizedRequest)
                ? Path.Combine(dateDir, $"{filePrefix}-request.json")
                : null;
            string? responsePath = !string.IsNullOrEmpty(anonymizedResponse)
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
                WritePrettyJson(requestPath, anonymizedRequest);
                WritePrettyJson(responsePath, anonymizedResponse);
                WriteMarkdownCompanion(responseMdPath, anonymizedResponse);
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
            File.WriteAllText(path, JsonSerializer.Serialize(doc.RootElement, typeof(JsonElement), PrettyContext));
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

        string? text = ExtractMarkdownText(content);
        if (text != null && LooksLikeMarkdown(text))
        {
            File.WriteAllText(path, text);
        }
    }

    private static string? ExtractMarkdownText(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (!root.TryGetProperty("result", out var result)) return null;
            if (!result.TryGetProperty("content", out var contentArr)) return null;
            if (contentArr.ValueKind != JsonValueKind.Array) return null;
            if (contentArr.GetArrayLength() != 1) return null;

            var first = contentArr[0];
            if (!first.TryGetProperty("type", out var typeEl) || typeEl.GetString() != "text") return null;
            if (!first.TryGetProperty("text", out var textEl) || textEl.ValueKind != JsonValueKind.String) return null;

            return textEl.GetString();
        }
        catch (JsonException ignored)
        {
            _ = ignored;
            return null;
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

    private static McpCallRecordShape ToJsonShape(McpCallRecord r, string? anonymizedArgs, string? anonymizedResponse) => new(
        DateTime.UtcNow.ToString("O"),
        r.CorrelationId,
        r.Method,
        r.Tool,
        anonymizedArgs,
        anonymizedResponse,
        r.DurationMs,
        r.Success
    );

    // -------------------------------------------------------------------------
    // Redaction — reuses IAnonymizer, the same masking applied to
    // ReadOnlyAnonymized query results, with the JSON property name standing
    // in for the (unavailable) column name.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses <paramref name="json"/> and replaces every string leaf value with its
    /// <see cref="IAnonymizer"/>-masked counterpart. Fails safe: if the input is not valid
    /// JSON, the original string is returned unchanged (the trail must never break on
    /// malformed JSON).
    /// </summary>
    /// <param name="isEnvelope">
    /// True for the two JSON-RPC envelope documents (<see cref="McpCallRecord.RawRequestJson"/>,
    /// <see cref="McpCallRecord.ResponseJson"/>) whose root object carries the structural
    /// <c>jsonrpc</c>/<c>id</c>/<c>method</c> keys. False for <see cref="McpCallRecord.ArgumentsJson"/>,
    /// whose root is already the free-form, LLM-authored <c>arguments</c> object — no envelope
    /// exemption applies there, even at the root.
    /// </param>
    private string? AnonymizeJsonStrings(string? json, bool isEnvelope)
    {
        if (string.IsNullOrEmpty(json)) return json;

        try
        {
            JsonNode? node = JsonNode.Parse(json);
            AnonymizeContainer(node, new RedactionContext(IsEnvelopeRoot: isEnvelope, IsContentBlock: false, IsResultObject: false));
            return node?.ToJsonString(CompactJsonOptions) ?? json;
        }
        catch (JsonException)
        {
            return json;
        }
    }

    /// <summary>
    /// Redacts every string leaf reachable from <paramref name="node"/>. Object properties
    /// are replaced in place via the object indexer (using the property name as the
    /// alias-only <c>columnName</c>); array elements have no property name of their own, so
    /// they use the fixed <see cref="ArrayElementPlaceholderName"/> instead. Scalars at the
    /// root (neither object nor array) are left as-is — the trail always wraps them in a
    /// JSON-RPC envelope, so this case does not occur in practice.
    /// </summary>
    private void AnonymizeContainer(JsonNode? node, RedactionContext context)
    {
        switch (node)
        {
            case JsonObject obj:
                AnonymizeObjectProperties(obj, context);
                break;
            case JsonArray arr:
                AnonymizeArrayElements(arr, context);
                break;
        }
    }

    private void AnonymizeObjectProperties(JsonObject obj, RedactionContext context)
    {
        foreach (string key in obj.Select(static kvp => kvp.Key).ToList())
        {
            if (IsExemptStructuralKey(key, context)) continue;

            if (obj[key] is JsonValue value && value.TryGetValue(out string? stringValue))
            {
                obj[key] = _anonymizer.Anonymize(key, stringValue);
            }
            else if (context.IsResultObject && key == "content" && obj[key] is JsonArray contentArray)
            {
                AnonymizeArrayElements(contentArray, default(RedactionContext) with { IsContentBlock = true });
            }
            else
            {
                AnonymizeContainer(obj[key], ChildContextFor(key, context));
            }
        }
    }

    /// <summary>
    /// Decides the recursion context for descending into <c>obj[key]</c> from
    /// <paramref name="context"/>: only the <c>result</c> property found directly on the
    /// envelope root carries <see cref="RedactionContext.IsResultObject"/> forward — every
    /// other descent resets to a plain, unmarked context (audit-hardening TD-003: the
    /// content-block exemption must trace back to <c>result.content[]</c>, not to the bare
    /// property name <c>content</c> anywhere in the tree).
    /// </summary>
    private static RedactionContext ChildContextFor(string key, RedactionContext context) =>
        context.IsEnvelopeRoot && key == "result"
            ? default(RedactionContext) with { IsResultObject = true }
            : default;

    /// <summary>
    /// Decides whether <paramref name="key"/> is exempt from redaction at the current
    /// position: only a true JSON-RPC envelope key on the actual envelope root, or the
    /// <c>type</c> discriminator directly inside a <c>content[]</c> element — never a
    /// same-named property found anywhere else in the tree (e.g. inside <c>arguments</c>).
    /// </summary>
    private static bool IsExemptStructuralKey(string key, RedactionContext context) =>
        (context.IsEnvelopeRoot && EnvelopeKeys.Contains(key))
        || (context.IsContentBlock && key == ContentBlockTypeKey);

    private void AnonymizeArrayElements(JsonArray arr, RedactionContext context)
    {
        // The IsContentBlock marker applies only to the direct elements of this array (one
        // recursion level deep) — children below that must not inherit it.
        RedactionContext childContext = default;

        for (int i = 0; i < arr.Count; i++)
        {
            if (arr[i] is JsonValue value && value.TryGetValue(out string? stringValue))
            {
                arr[i] = _anonymizer.Anonymize(ArrayElementPlaceholderName, stringValue);
            }
            else if (arr[i] is JsonObject elementObj && context.IsContentBlock)
            {
                AnonymizeObjectProperties(elementObj, context with { IsEnvelopeRoot = false });
            }
            else
            {
                AnonymizeContainer(arr[i], childContext);
            }
        }
    }
}
