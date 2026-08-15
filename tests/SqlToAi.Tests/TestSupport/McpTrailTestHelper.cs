#nullable enable

using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Mcp;

namespace SqlToAi.Tests.TestSupport;

internal static class McpTrailTestHelper
{
    /// <summary>
    /// Prefix for all isolated log roots created by <see cref="CreateIsolatedLogRoot"/>. Kept as a
    /// constant so test scaffolding under <c>%TEMP%</c> is recognisable in the file system during
    /// debugging (step-003 / DRY-T1).
    /// </summary>
    private const string LogRootPrefix = "SqlToAiMcpTrail";

    public static McpTrailWriter CreateWriter(string logRoot, McpTrailTestWriterConfig config)
    {
        var options = new SqlToAiOptions
        {
            Logging = new LoggingOptions
            {
                Directory = logRoot,
                McpTrail = new McpTrailOptions { Enabled = config.TrailEnabled, Directory = "mcp", RetainedDays = 14 }
            }
        };
        return new McpTrailWriter(
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<McpTrailWriter>.Instance,
            CreateAnonymizer(config.AnonymizerEnabled));
    }

    public static Anonymizer CreateAnonymizer(bool anonymizerEnabled)
    {
        var options = new SqlToAiOptions
        {
            Anonymizer = new AnonymizerOptions { Enabled = anonymizerEnabled, DefaultMode = "ScramblePattern" },
        };
        return new Anonymizer(Microsoft.Extensions.Options.Options.Create(options), new TokenVault());
    }

    /// <summary>
    /// Creates a fresh, unique log root directory under <c>%TEMP%</c> for a test class. The
    /// <paramref name="suffix"/> is appended to the prefix and the path is suffixed with a random
    /// GUID to guarantee isolation between tests (step-003 / DRY-T1 — replaces the duplicated
    /// <c>Path.Combine(Path.GetTempPath(), "SqlToAiMcpTrail" + … + "_" + Guid.NewGuid().ToString("N"))</c>
    /// pattern that used to live in both <c>McpTrailWriterTests</c> and
    /// <c>McpTrailWriterRedactionTests</c>).
    /// </summary>
    public static string CreateIsolatedLogRoot(string suffix) =>
        Path.Combine(Path.GetTempPath(), LogRootPrefix + suffix + "_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Returns the per-day directory that <see cref="McpTrailWriter"/> writes its JSONL/JSON/MD
    /// companion files into. Replaces the private <c>GetDayDir()</c> method that used to be
    /// duplicated in both <c>McpTrailWriterTests</c> and <c>McpTrailWriterRedactionTests</c>
    /// (step-003 / DRY-T1).
    /// </summary>
    public static string GetDayDir(string logRoot) =>
        Path.Combine(logRoot, "mcp", DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
}

/// <summary>
/// Parameter object for <see cref="McpTrailTestHelper.CreateWriter"/> — bundles the two booleans
/// (trail on/off, anonymizer on/off) into a named record so call sites read
/// <c>new McpTrailTestWriterConfig(TrailEnabled: true)</c> instead of <c>true, false</c>
/// (step-003 / DRY-T1, satisfies AiNetLinter <c>MaxBoolParameterCount = 1</c>).
/// </summary>
internal sealed record McpTrailTestWriterConfig(bool TrailEnabled, bool AnonymizerEnabled = false);
