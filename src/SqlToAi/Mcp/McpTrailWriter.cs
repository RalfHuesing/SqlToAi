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
/// <see cref="IMcpTrailWriter"/> turns it into a JSONL file on disk.
/// </summary>
/// <param name="CorrelationId">The JSON-RPC <c>id</c> (or a generated UUID if the request had none).</param>
/// <param name="Method">JSON-RPC method, e.g. <c>initialize</c>, <c>tools/list</c>, <c>tools/call</c>.</param>
/// <param name="Tool">For <c>tools/call</c>: the tool name (e.g. <c>sql_get_schema</c>). Null for other methods.</param>
/// <param name="ArgumentsJson">Raw JSON arguments as received from the client (or null if not applicable).</param>
/// <param name="ResponseJson">Exact JSON that the server sent back to the LLM — this is 1:1 what the LLM saw, including any anonymization.</param>
/// <param name="DurationMs">Wall-clock duration of the call in milliseconds.</param>
/// <param name="Success">True when the call returned a <c>result</c>, false when it returned an <c>error</c>.</param>
/// <param name="ErrorCode">Error catalog code (e.g. <c>SQL-AI-0107</c>) when <paramref name="Success"/> is false.</param>
public sealed record McpCallRecord(
    string CorrelationId,
    string Method,
    string? Tool,
    string? ArgumentsJson,
    string? ResponseJson,
    long DurationMs,
    bool Success);

/// <summary>
/// Writes MCP call records to a per-day directory of JSONL files. One file per call, so
/// even large responses are written without truncation. The directory layout is:
/// <c>{log-root}/mcp/YYYY-MM-DD/HH-MM-SS-{uuid}-call.jsonl</c>.
/// </summary>
public interface IMcpTrailWriter
{
    /// <summary>Records a single call. Fire-and-forget by design — never throws into the MCP loop.</summary>
    void Record(McpCallRecord record);
}

/// <inheritdoc/>
public sealed class McpTrailWriter : IMcpTrailWriter, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
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

            // Filename: HH-MM-SS-{uuid}-call.jsonl
            string fileName = $"{DateTime.UtcNow:HH-mm-ss}-{record.CorrelationId}-call.jsonl";
            string filePath = Path.Combine(dateDir, fileName);

            string line = JsonSerializer.Serialize(ToJsonShape(record), JsonOptions);

            // Lock so concurrent calls cannot interleave bytes in the same file (which is
            // impossible by construction — one file per call — but defensive for future
            // changes that batch multiple records per file).
            lock (_writeLock)
            {
                File.AppendAllText(filePath, line + Environment.NewLine);
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
