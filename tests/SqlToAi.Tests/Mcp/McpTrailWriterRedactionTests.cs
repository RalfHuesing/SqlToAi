#nullable enable

using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Mcp;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Mcp;

/// <summary>
/// Redaction-specific unit tests for <see cref="McpTrailWriter"/> (IAnonymizer reuse,
/// audit-hardening EPIC-03 step-003 / EPIC-06 step-006). Split out of
/// <see cref="McpTrailWriterTests"/> to keep both files under <c>MaxLineCount</c> — this class
/// has its own isolated log-root fixture rather than sharing one across xUnit test classes.
/// </summary>
public sealed class McpTrailWriterRedactionTests : IDisposable
{
    private readonly string _logRoot;

    public McpTrailWriterRedactionTests()
    {
        _logRoot = Path.Combine(Path.GetTempPath(), "SqlToAiMcpTrailRedactionTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_logRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_logRoot, recursive: true); } catch { /* best effort */ }
    }

    private string GetDayDir() =>
        Path.Combine(_logRoot, "mcp", DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

    private McpTrailWriter CreateWriter(bool enabled, bool anonymizerEnabled = false) =>
        McpTrailTestHelper.CreateWriter(_logRoot, enabled, anonymizerEnabled);

    [Fact]
    public void Record_ShouldRedactResponseText_AcrossAllCompanionFiles()
    {
        var writer = CreateWriter(enabled: true, anonymizerEnabled: true);
        const string markdown = "# Report for Max Mustermann\n- Status: active\n";
        string escaped = markdown.Replace("\n", "\\n", StringComparison.Ordinal);
        string responseJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"" + escaped + "\"}],\"isError\":false}}";

        var record = new McpCallRecord("pii-1", "tools/call", "sql_get_schema", "{}", "{}", responseJson, 1, true);

        writer.Record(record);

        string dayDir = GetDayDir();
        string jsonlContent = File.ReadAllText(Directory.GetFiles(dayDir, "*-call.jsonl").Single());
        string responseJsonContent = File.ReadAllText(Directory.GetFiles(dayDir, "*-response.json").Single());
        string responseMdContent = File.ReadAllText(Directory.GetFiles(dayDir, "*-response.md").Single());

        Assert.DoesNotContain("Mustermann", jsonlContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Mustermann", responseJsonContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Mustermann", responseMdContent, StringComparison.Ordinal);
        // Structural characters of the Markdown (not letters) still identify it as Markdown.
        Assert.StartsWith("#", responseMdContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_ShouldRedactRequestArguments_InRequestJsonAndJsonl()
    {
        var writer = CreateWriter(enabled: true, anonymizerEnabled: true);
        var record = new McpCallRecord(
            "pii-2", "tools/call", "sql_search_objects",
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"sql_search_objects\",\"arguments\":{\"search_term\":\"SensitiveSecretTerm\"}}}",
            "{\"search_term\":\"SensitiveSecretTerm\"}",
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{}}", 1, true);

        writer.Record(record);

        string dayDir = GetDayDir();
        string requestContent = File.ReadAllText(Directory.GetFiles(dayDir, "*-request.json").Single());
        string jsonlContent = File.ReadAllText(Directory.GetFiles(dayDir, "*-call.jsonl").Single());

        Assert.DoesNotContain("SensitiveSecretTerm", requestContent, StringComparison.Ordinal);
        Assert.DoesNotContain("SensitiveSecretTerm", jsonlContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_ShouldKeepStructuralKeys_UnredactedAndReadable()
    {
        var writer = CreateWriter(enabled: true, anonymizerEnabled: true);
        var record = new McpCallRecord(
            "pii-3", "tools/call", "sql_get_schema", "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\"}", "{}",
            "{\"jsonrpc\":\"2.0\",\"id\":3,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"SomeSecretValue\"}]}}", 1, true);

        writer.Record(record);

        string dayDir = GetDayDir();
        string requestContent = File.ReadAllText(Directory.GetFiles(dayDir, "*-request.json").Single());
        string responseContent = File.ReadAllText(Directory.GetFiles(dayDir, "*-response.json").Single());

        Assert.Contains("\"jsonrpc\": \"2.0\"", requestContent, StringComparison.Ordinal);
        Assert.Contains("\"id\": 3", requestContent, StringComparison.Ordinal);
        Assert.Contains("\"method\": \"tools/call\"", requestContent, StringComparison.Ordinal);
        Assert.Contains("\"jsonrpc\": \"2.0\"", responseContent, StringComparison.Ordinal);
        Assert.Contains("\"id\": 3", responseContent, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"text\"", responseContent, StringComparison.Ordinal);
        Assert.DoesNotContain("SomeSecretValue", responseContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_ShouldLeaveNumbersAndBooleans_Unredacted()
    {
        var writer = CreateWriter(enabled: true, anonymizerEnabled: true);
        var record = new McpCallRecord(
            "pii-4", "tools/call", "sql_execute_query", "{}", "{\"row_limit\":42,\"strict\":true}",
            "{\"jsonrpc\":\"2.0\",\"id\":4,\"result\":{\"rows\":123,\"ok\":false}}", 1, true);

        writer.Record(record);

        string dayDir = GetDayDir();
        string jsonlContent = File.ReadAllText(Directory.GetFiles(dayDir, "*-call.jsonl").Single());

        Assert.Contains("row_limit\\\":42", jsonlContent, StringComparison.Ordinal);
        Assert.Contains("strict\\\":true", jsonlContent, StringComparison.Ordinal);
        Assert.Contains("rows\\\":123", jsonlContent, StringComparison.Ordinal);
        Assert.Contains("ok\\\":false", jsonlContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_ShouldLeaveTrailUnredacted_WhenAnonymizerDisabled()
    {
        var writer = CreateWriter(enabled: true, anonymizerEnabled: false);
        var record = new McpCallRecord(
            "pii-5", "tools/call", "sql_get_schema", "{}", "{\"search_term\":\"PlainTextSecret\"}",
            "{\"jsonrpc\":\"2.0\",\"id\":5,\"result\":{}}", 1, true);

        writer.Record(record);

        string dayDir = GetDayDir();
        string jsonlContent = File.ReadAllText(Directory.GetFiles(dayDir, "*-call.jsonl").Single());

        Assert.Contains("PlainTextSecret", jsonlContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_ShouldRedactArgumentsProperties_NamedLikeStructuralKeys()
    {
        // CRITICAL fix regression test (audit-hardening EPIC-03 step-003 fix-01): `arguments`
        // properties whose names collide with JSON-RPC structural keys (e.g. the LLM picks
        // "id" as a bind-parameter name) must still be redacted — the exemption is positional
        // (envelope root only / content-block only), not name-based.
        var writer = CreateWriter(enabled: true, anonymizerEnabled: true);
        const string idValue = "123-45-6789";
        const string typeValue = "SuperSecretTypeValue";
        const string methodValue = "AlsoSecretMethodValue";
        string argsJson = "{\"id\":\"" + idValue + "\",\"type\":\"" + typeValue + "\",\"method\":\"" + methodValue + "\"}";
        var record = new McpCallRecord(
            CorrelationId: "pii-7",
            Method: "tools/call",
            Tool: "sql_execute_query",
            RawRequestJson: "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\"sql_execute_query\",\"arguments\":" + argsJson + "}}",
            ArgumentsJson: argsJson,
            ResponseJson: "{\"jsonrpc\":\"2.0\",\"id\":7,\"result\":{}}",
            DurationMs: 1,
            Success: true);

        writer.Record(record);

        string dayDir = GetDayDir();
        string requestContent = File.ReadAllText(Directory.GetFiles(dayDir, "*-request.json").Single());
        string jsonlContent = File.ReadAllText(Directory.GetFiles(dayDir, "*-call.jsonl").Single());

        foreach (string sensitive in new[] { idValue, typeValue, methodValue })
        {
            Assert.DoesNotContain(sensitive, requestContent, StringComparison.Ordinal);
            Assert.DoesNotContain(sensitive, jsonlContent, StringComparison.Ordinal);
        }
        // The envelope-root keys on the request itself remain readable.
        Assert.Contains("\"jsonrpc\": \"2.0\"", requestContent, StringComparison.Ordinal);
        Assert.Contains("\"method\": \"tools/call\"", requestContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_ShouldRedactTypeProperty_InContentArrayNotOnResult()
    {
        // TD-003 fix (audit-hardening EPIC-06 step-006): a "content" array is only ever a
        // real MCP content block when found directly on the envelope's "result" object. An
        // LLM-authored "arguments.content" array with the same shape must not get the
        // content-block exemption for its "type" property.
        var writer = CreateWriter(enabled: true, anonymizerEnabled: true);
        const string sensitiveValue = "SensitiveArgTypeValue";
        string argsJson = "{\"content\":[{\"type\":\"" + sensitiveValue + "\"}]}";
        var record = new McpCallRecord(
            CorrelationId: "pii-10",
            Method: "tools/call",
            Tool: "sql_execute_query",
            RawRequestJson: "{\"jsonrpc\":\"2.0\",\"id\":10,\"method\":\"tools/call\",\"params\":{\"name\":\"sql_execute_query\",\"arguments\":" + argsJson + "}}",
            ArgumentsJson: argsJson,
            ResponseJson: "{\"jsonrpc\":\"2.0\",\"id\":10,\"result\":{}}",
            DurationMs: 1,
            Success: true);

        writer.Record(record);

        string dayDir = GetDayDir();
        string jsonlContent = File.ReadAllText(Directory.GetFiles(dayDir, "*-call.jsonl").Single());

        Assert.DoesNotContain(sensitiveValue, jsonlContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_ShouldKeepContentBlockTypeDiscriminator_Readable_ButRedactNestedTypeElsewhere()
    {
        // Regression: "type" as the content-block discriminator in result.content[0].type stays
        // readable; a "type" nested inside the already-serialized text blob is redacted as part
        // of that string leaf (known, accepted over-redaction — not this fix's scope).
        var writer = CreateWriter(enabled: true, anonymizerEnabled: true);
        const string sensitiveValue = "NestedSecretTypeValue";
        string responseJson = "{\"jsonrpc\":\"2.0\",\"id\":9,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"type\\\":\\\"" + sensitiveValue + "\\\"}\"}],\"isError\":false}}";
        var record = new McpCallRecord("pii-9", "tools/call", "sql_get_schema", "{}", "{}", responseJson, 1, true);

        writer.Record(record);

        string dayDir = GetDayDir();
        string responseContent = File.ReadAllText(Directory.GetFiles(dayDir, "*-response.json").Single());

        Assert.Contains("\"type\": \"text\"", responseContent, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveValue, responseContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_ShouldRedactTypeProperty_InNestedContentArray_NotDirectlyOnResult()
    {
        // TD-003 fix, additional depth check: a "content" array nested one level below
        // "result" (e.g. result.someWrapper.content) is not the direct MCP content block
        // either — only result.content[] itself is exempt.
        var writer = CreateWriter(enabled: true, anonymizerEnabled: true);
        const string sensitiveValue = "SensitiveNestedWrapperTypeValue";
        string responseJson = "{\"jsonrpc\":\"2.0\",\"id\":11,\"result\":{\"someWrapper\":{\"content\":[{\"type\":\"" + sensitiveValue + "\"}]}}}";
        var record = new McpCallRecord("pii-11", "tools/call", "sql_get_schema", "{}", "{}", responseJson, 1, true);

        writer.Record(record);

        string dayDir = GetDayDir();
        string responseContent = File.ReadAllText(Directory.GetFiles(dayDir, "*-response.json").Single());

        Assert.DoesNotContain(sensitiveValue, responseContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_ShouldNotThrow_WhenArgumentsJsonIsInvalid()
    {
        var writer = CreateWriter(enabled: true, anonymizerEnabled: true);
        var record = new McpCallRecord(
            CorrelationId: "pii-6",
            Method: "tools/call",
            Tool: "sql_get_schema",
            RawRequestJson: "{not valid json",
            ArgumentsJson: "{not valid json",
            ResponseJson: "{\"jsonrpc\":\"2.0\",\"id\":6,\"result\":{}}",
            DurationMs: 1,
            Success: true);

        var ex = Record.Exception(() => writer.Record(record));
        Assert.Null(ex);

        string dayDir = GetDayDir();
        // Fail-safe: the invalid JSON is written through unchanged rather than dropped.
        string requestContent = File.ReadAllText(Directory.GetFiles(dayDir, "*-request.json").Single());
        Assert.Equal("{not valid json", requestContent);
    }
}
