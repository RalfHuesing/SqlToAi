#nullable enable

using System.Text;
using System.Text.RegularExpressions;

namespace SqlToAi.Anonymization;

/// <summary>
/// Matches text against SQL <c>LIKE</c>-style patterns (<c>%</c> and <c>_</c> wildcards) and
/// scores how specific a pattern is, so the most specific of several matching
/// <see cref="AnonymizationRule"/> entries can be picked deterministically.
/// </summary>
internal static class LikePatternMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>Checks whether <paramref name="text"/> matches the SQL <c>LIKE</c>-style <paramref name="pattern"/>.</summary>
    public static bool IsMatch(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        string regexPattern = "^" + ToRegexPattern(pattern) + "$";

        try
        {
            return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase, RegexTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Scores how specific a pattern is: <c>2</c> for an exact literal (no wildcard), <c>1</c> for
    /// a partial wildcard (e.g. <c>Cust%Group</c>), <c>0</c> for a pure wildcard (<c>%</c>) that
    /// matches almost anything. Used to rank competing rules — the highest total score wins.
    /// </summary>
    public static int SpecificityScore(string pattern)
    {
        if (string.IsNullOrEmpty(pattern) || pattern == "%")
        {
            return 0;
        }
        return pattern.Contains('%') || pattern.Contains('_') ? 1 : 2;
    }

    private static string ToRegexPattern(string pattern)
    {
        var sb = new StringBuilder(pattern.Length * 2);
        foreach (char c in pattern)
        {
            switch (c)
            {
                case '%':
                    sb.Append(".*");
                    break;
                case '_':
                    sb.Append('.');
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        return sb.ToString();
    }
}
