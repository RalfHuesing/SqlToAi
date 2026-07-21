#nullable enable

using System.Text.RegularExpressions;

namespace SqlToAi.Anonymization;

/// <summary>
/// Matches text against glob-style patterns (<c>*</c> and <c>?</c> wildcards), as used by
/// <see cref="Configuration.AnonymizerOptions.ExcludedColumns"/>. Shared between
/// <see cref="Anonymizer"/> (per-cell masking decision) and
/// <see cref="AnonymizationPolicyResolver"/> (proactive schema annotation), so both agree on
/// the same column-name exclusion rules.
/// </summary>
internal static class GlobPatternMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>Checks whether <paramref name="text"/> matches the glob-style <paramref name="pattern"/>.</summary>
    public static bool IsMatch(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        string regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        try
        {
            return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase, RegexTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
