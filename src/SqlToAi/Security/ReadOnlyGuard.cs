#nullable enable

using System.Text;
using System.Text.RegularExpressions;

namespace SqlToAi.Security;

/// <summary>
/// Validates SQL queries to ensure they are strictly read-only and contain no mutating commands.
/// </summary>
public sealed class ReadOnlyGuard : IReadOnlyGuard
{
    private static readonly Regex MutatingKeywordsRegex = new(
        @"\b(insert|update|delete|drop|alter|truncate|create|merge|grant|revoke|reconfigure|checkpoint|backup|restore|dbcc|exec|execute|into)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));

    /// <summary>
    /// Checks if a query is safe for read-only execution by stripping comments and string
    /// literals, then verifying the remainder against mutating keywords.
    /// </summary>
    /// <param name="query">The SQL query string to evaluate.</param>
    /// <returns>True if the query is safe (read-only); otherwise, false.</returns>
    public bool IsQuerySafe(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        try
        {
            string cleanQuery = StripCommentsAndStringLiterals(query);
            return !MutatingKeywordsRegex.IsMatch(cleanQuery);
        }
        catch (RegexMatchTimeoutException)
        {
            // Fail secure: If regex times out, assume unsafe
            return false;
        }
    }

    /// <summary>
    /// Blanks out SQL comments (<c>--</c>, <c>/* */</c>) and single-quoted string literal
    /// contents in a single pass, so a value like <c>WHERE Status = 'DELETE'</c> is not
    /// mistaken for the mutating keyword DELETE. Quotes are replaced with a single space
    /// (not removed) so tokens on either side never merge into an unrelated word.
    /// </summary>
    private static string StripCommentsAndStringLiterals(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        bool inSingleQuote = false;
        bool inLineComment = false;
        bool inBlockComment = false;

        ReadOnlySpan<char> span = sql.AsSpan();
        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            char next = i + 1 < span.Length ? span[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n') { inLineComment = false; sb.Append(c); }
                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && next == '/') { inBlockComment = false; i++; }
                continue;
            }

            if (inSingleQuote)
            {
                if (c == '\'' && next == '\'') { i++; continue; } // escaped '' inside literal
                if (c == '\'') { inSingleQuote = false; sb.Append(' '); }
                continue;
            }

            if (c == '-' && next == '-') { inLineComment = true; i++; continue; }
            if (c == '/' && next == '*') { inBlockComment = true; i++; continue; }
            if (c == '\'') { inSingleQuote = true; sb.Append(' '); continue; }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
