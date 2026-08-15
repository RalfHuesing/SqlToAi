#nullable enable

using SqlToAi.Configuration;

namespace SqlToAi.Tests.TestSupport;

internal static class AnonymizationTestHelper
{
    public static SqlToAiOptions BuildTokenizationOptions(bool enabled = true)
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.Tokenization.Enabled = enabled;
        return options;
    }
}
