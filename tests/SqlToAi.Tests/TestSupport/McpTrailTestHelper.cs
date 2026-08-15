#nullable enable

using Microsoft.Extensions.Logging.Abstractions;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Mcp;

namespace SqlToAi.Tests.TestSupport;

internal static class McpTrailTestHelper
{
    public static McpTrailWriter CreateWriter(string logRoot, bool enabled, bool anonymizerEnabled = false)
    {
        var options = new SqlToAiOptions
        {
            Logging = new LoggingOptions
            {
                Directory = logRoot,
                McpTrail = new McpTrailOptions { Enabled = enabled, Directory = "mcp", RetainedDays = 14 }
            }
        };
        return new McpTrailWriter(
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<McpTrailWriter>.Instance,
            CreateAnonymizer(anonymizerEnabled));
    }

    public static Anonymizer CreateAnonymizer(bool anonymizerEnabled)
    {
        var options = new SqlToAiOptions
        {
            Anonymizer = new AnonymizerOptions { Enabled = anonymizerEnabled, DefaultMode = "ScramblePattern" },
        };
        return new Anonymizer(Microsoft.Extensions.Options.Options.Create(options), new TokenVault());
    }
}
