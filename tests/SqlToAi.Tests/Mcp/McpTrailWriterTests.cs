#nullable enable

using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Mcp;

namespace SqlToAi.Tests.Mcp;

/// <summary>
/// Unit tests for <see cref="McpTrailWriter"/>. Each test uses a fresh, isolated log
/// root directory under <c>%TEMP%</c> so the tests do not touch any real log files.
/// </summary>
public sealed class McpTrailWriterTests : IDisposable
{
    private readonly string _logRoot;

    public McpTrailWriterTests()
    {
        _logRoot = Path.Combine(Path.GetTempPath(), "SqlToAiMcpTrailTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_logRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_logRoot, recursive: true); } catch { /* best effort */ }
    }

    private string GetDayDir() =>
        Path.Combine(_logRoot, "mcp", DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Record_ShouldCreatePerDayDirectory_AndWriteJsonlFile()
    {
        var writer = CreateWriter(enabled: true);
        var record = new McpCallRecord(
            CorrelationId: "abc123",
            Method: "tools/call",
            Tool: "sql_get_schema",
            RawRequestJson: "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\"}",
            ArgumentsJson: "{\"object_name\":\"dbo.Foo\"}",
            ResponseJson: "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}",
            DurationMs: 42,
            Success: true);

        writer.Record(record);

        string dayDir = GetDayDir();
        Assert.True(Directory.Exists(dayDir), $"Expected day directory {dayDir} to exist.");

        var files = Directory.GetFiles(dayDir, "*-call.jsonl");
        Assert.Single(files);
        var content = File.ReadAllText(files[0]);
        Assert.Contains("\"method\":\"tools/call\"", content);
        Assert.Contains("\"tool\":\"sql_get_schema\"", content);
        Assert.Contains("\"success\":true", content);
        Assert.Contains("\"duration_ms\":42", content);
    }

    [Fact]
    public void Record_ShouldWriteCompanionRequestJsonAndResponseJson()
    {
        var writer = CreateWriter(enabled: true);
        var record = new McpCallRecord(
            CorrelationId: "id-with-companion",
            Method: "tools/call",
            Tool: "sql_get_schema",
            RawRequestJson: "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\"sql_get_schema\",\"arguments\":{\"object_name\":\"dbo.Foo\"}}}",
            ArgumentsJson: "{\"name\":\"sql_get_schema\",\"arguments\":{\"object_name\":\"dbo.Foo\"}}",
            ResponseJson: "{\"jsonrpc\":\"2.0\",\"id\":7,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"# Schema for Table/View: \\\"dbo.Foo\\\"\"}],\"isError\":false}}",
            DurationMs: 5,
            Success: true);

        writer.Record(record);

        string dayDir = GetDayDir();

        string requestFile = Directory.GetFiles(dayDir, "*-request.json").Single();
        string requestText = File.ReadAllText(requestFile);
        // Pretty-printed (indented), with a top-level newline before the closing brace.
        Assert.Contains("\"jsonrpc\": \"2.0\"", requestText);
        Assert.Contains("\"id\": 7", requestText);
        Assert.Contains("\"object_name\": \"dbo.Foo\"", requestText);

        string responseFile = Directory.GetFiles(dayDir, "*-response.json").Single();
        string responseText = File.ReadAllText(responseFile);
        Assert.Contains("\"jsonrpc\": \"2.0\"", responseText);
        Assert.Contains("\"isError\": false", responseText);
    }

    [Fact]
    public void Record_ShouldWriteResponseMd_WhenResponseIsMarkdown()
    {
        var writer = CreateWriter(enabled: true);
        string markdown = "# Schema for Table/View: `dbo.Customers`\n| Column | Type |\n| --- | --- |\n| Id | int |\n| Name | varchar |\n";
        string responseJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"" + markdown.Replace("\n", "\\n").Replace("\"", "\\\"") + "\"}],\"isError\":false}}";

        var record = new McpCallRecord(
            CorrelationId: "md-1",
            Method: "tools/call",
            Tool: "sql_get_schema",
            RawRequestJson: "{}",
            ArgumentsJson: "{}",
            ResponseJson: responseJson,
            DurationMs: 1,
            Success: true);

        writer.Record(record);

        string dayDir = GetDayDir();
        string mdFile = Directory.GetFiles(dayDir, "*-response.md").Single();
        string content = File.ReadAllText(mdFile);

        // The .md companion contains the Markdown text verbatim (no JSON escaping, no quotes).
        Assert.Contains("# Schema for Table/View: `dbo.Customers`", content);
        Assert.Contains("| Column | Type |", content);
        Assert.Contains("| Id | int |", content);
    }

    [Fact]
    public void Record_ShouldNotWriteResponseMd_WhenResponseIsJsonText()
    {
        var writer = CreateWriter(enabled: true);
        // sql_execute_query returns JSON in the text block — that is NOT Markdown.
        string jsonPayload = "{\"Mandant\":1,\"ProjektID\":42}";
        string responseJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"" + jsonPayload + "\"}],\"isError\":false}}";

        var record = new McpCallRecord(
            CorrelationId: "json-1",
            Method: "tools/call",
            Tool: "sql_execute_query",
            RawRequestJson: "{}",
            ArgumentsJson: "{}",
            ResponseJson: responseJson,
            DurationMs: 1,
            Success: true);

        writer.Record(record);

        string dayDir = GetDayDir();
        Assert.Empty(Directory.GetFiles(dayDir, "*-response.md"));
    }

    [Fact]
    public void Record_ShouldNotWriteRequestJson_WhenRawRequestIsNull()
    {
        var writer = CreateWriter(enabled: true);
        var record = new McpCallRecord(
            CorrelationId: "no-req",
            Method: "tools/list",
            Tool: null,
            RawRequestJson: null,
            ArgumentsJson: null,
            ResponseJson: "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}",
            DurationMs: 1,
            Success: true);

        writer.Record(record);

        string dayDir = GetDayDir();
        Assert.Empty(Directory.GetFiles(dayDir, "*-request.json"));
        // response.json still gets written
        Assert.Single(Directory.GetFiles(dayDir, "*-response.json"));
    }

    [Fact]
    public void Record_ShouldIncludeRawArgsAndResponse_Verbatim()
    {
        var writer = CreateWriter(enabled: true);
        string longQuery = "SELECT TOP 1 * FROM Customers WHERE Name = 'O''Brien' AND City LIKE '%München%'";
        string longResponse = "{\"data\":\"" + new string('x', 5_000) + "\"}";

        var record = new McpCallRecord(
            CorrelationId: "x1",
            Method: "tools/call",
            Tool: "sql_execute_query",
            RawRequestJson: "{}",
            ArgumentsJson: "{\"query\":\"" + longQuery + "\"}",
            ResponseJson: longResponse,
            DurationMs: 1,
            Success: true);

        writer.Record(record);

        string file = Directory.GetFiles(Path.Combine(_logRoot, "mcp", DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)), "*-call.jsonl").Single();
        string content = File.ReadAllText(file);

        // The response and args are stored verbatim — no truncation. The whole line is
        // JSON-encoded, so we check for the JSON-escaped form of the long content.
        Assert.Contains("\\\"data\\\":\\\"" + new string('x', 5_000) + "\\\"}", content);
        Assert.Contains("\\\"query\\\":\\\"" + longQuery + "\\\"}", content);
    }

    [Fact]
    public void Record_ShouldDoNothing_WhenMcpTrailDisabled()
    {
        var writer = CreateWriter(enabled: false);
        writer.Record(new McpCallRecord("c1", "tools/list", null, null, null, null, 1, true));

        Assert.False(Directory.Exists(Path.Combine(_logRoot, "mcp")));
    }

    [Fact]
    public void Record_ShouldNeverThrow_EvenIfTargetDirIsLocked()
    {
        // Point the writer at a path under a read-only location to force a write failure.
        var badRoot = Path.Combine(_logRoot, "this-path-cannot-be-created");
        Directory.CreateDirectory(badRoot);
        // Create a file with the same name as the would-be mcp subdir to block creation.
        File.WriteAllText(Path.Combine(badRoot, "mcp"), "blocker");

        var options = new SqlToAiOptions
        {
            Logging = new LoggingOptions { Directory = badRoot, McpTrail = new McpTrailOptions { Enabled = true, Directory = "mcp" } }
        };
        var writer = new McpTrailWriter(
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<McpTrailWriter>.Instance,
            CreateAnonymizer(anonymizerEnabled: false));

        // Must not throw — fire-and-forget contract.
        var ex = Record.Exception(() => writer.Record(new McpCallRecord("c1", "tools/list", null, null, null, null, 1, true)));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("../../../../evil")]
    [InlineData("..\\..\\..\\evil")]
    [InlineData("/etc/passwd")]
    [InlineData("a/../../b")]
    public void Record_ShouldSanitizeCorrelationId_AndStayInsideDayDirectory(string maliciousId)
    {
        var writer = CreateWriter(enabled: true);
        var record = new McpCallRecord(maliciousId, "tools/call", "sql_get_schema", null, null, "{}", 1, true);

        writer.Record(record);

        string dayDir = GetDayDir();
        var files = Directory.GetFiles(dayDir, "*-call.jsonl");
        Assert.Single(files);

        // The written file must be a direct child of dayDir — no path traversal outside it.
        Assert.Equal(dayDir, Path.GetDirectoryName(files[0]));
    }

    [Fact]
    public void Record_ShouldBeThreadSafe_AcrossParallelCalls()
    {
        var writer = CreateWriter(enabled: true);
        Parallel.For(0, 50, i =>
        {
            writer.Record(new McpCallRecord(
                CorrelationId: $"id-{i}",
                Method: "tools/call",
                Tool: "sql_list_databases",
                RawRequestJson: "{}",
                ArgumentsJson: null,
                ResponseJson: $"{{\"id\":{i}}}",
                DurationMs: i,
                Success: true));
        });

        string dayDir = GetDayDir();
        var files = Directory.GetFiles(dayDir, "*-call.jsonl");
        Assert.Equal(50, files.Length);
    }

    /// <summary>
    /// Default <paramref name="anonymizerEnabled"/> is false: the tests in this class assert
    /// unmodified trail content (verbatim payloads, Markdown detection, sanitization,
    /// thread-safety) and are unrelated to redaction. Redaction-specific tests live in the
    /// sibling <see cref="McpTrailWriterRedactionTests"/> class, which opts into a real,
    /// enabled <see cref="Anonymizer"/>.
    /// </summary>
    private McpTrailWriter CreateWriter(bool enabled, bool anonymizerEnabled = false)
    {
        var options = new SqlToAiOptions
        {
            Logging = new LoggingOptions
            {
                Directory = _logRoot,
                McpTrail = new McpTrailOptions { Enabled = enabled, Directory = "mcp", RetainedDays = 14 }
            }
        };
        return new McpTrailWriter(
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<McpTrailWriter>.Instance,
            CreateAnonymizer(anonymizerEnabled));
    }

    private static Anonymizer CreateAnonymizer(bool anonymizerEnabled)
    {
        var options = new SqlToAiOptions
        {
            Anonymizer = new AnonymizerOptions { Enabled = anonymizerEnabled, DefaultMode = "ScramblePattern" },
        };
        return new Anonymizer(Microsoft.Extensions.Options.Options.Create(options), new TokenVault());
    }
}
