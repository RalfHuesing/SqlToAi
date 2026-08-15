#nullable enable
using System.Text.RegularExpressions;
using SqlToAi.Security;

namespace SqlToAi.Domain;

/// <summary>
/// Matches text against simple glob-style patterns (<c>*</c> and <c>?</c>).
/// Case-insensitive, single-pass Regex with a 200 ms timeout; on
/// <see cref="RegexMatchTimeoutException"/> returns <c>false</c> (fail-closed).
/// Lives in <c>SqlToAi.Domain</c> because the matcher is a generic string
/// utility, used today by <c>SecurityGuard</c> (database whitelist) and
/// previously by <c>Anonymizer</c> (column exclusion) — no anonymization- or
/// security-specific semantics.
/// </summary>
internal static class GlobMatcher
{
    private static readonly TimeSpan RegexTimeout = SecurityConstants.DefaultRegexTimeout;

    /// <summary>
    /// Returns <c>true</c> when <paramref name="text"/> matches the glob
    /// <paramref name="pattern"/>. Empty pattern always returns <c>false</c>;
    /// <see cref="RegexMatchTimeoutException"/> is caught and converted to
    /// <c>false</c> (fail-closed).
    /// </summary>
    /// <param name="text">The text to test. May be null or empty.</param>
    /// <param name="pattern">The glob pattern. <c>*</c> matches any sequence
    /// of characters, <c>?</c> matches exactly one character. All other
    /// characters are matched literally (regex metacharacters are escaped).</param>
    /// <returns><c>true</c> if the text matches the pattern; otherwise <c>false</c>.</returns>
    public static bool IsMatch(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        // Convert wildcard glob pattern (* and ?) to Regex equivalent
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
