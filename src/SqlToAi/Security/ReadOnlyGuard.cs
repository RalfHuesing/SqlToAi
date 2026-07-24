#nullable enable

using System.Text;
using System.Text.RegularExpressions;

namespace SqlToAi.Security;

/// <summary>
/// Validates SQL queries to ensure they are strictly read-only and contain no mutating commands.
/// </summary>
public sealed class ReadOnlyGuard : IReadOnlyGuard
{
    // sp_executesql is listed explicitly (not just "exec"/"execute"): it is one contiguous
    // token (the underscore counts as a word character, so "exec" never appears inside it as
    // its own bounded match), and T-SQL allows invoking it as a batch's sole statement with no
    // EXEC/EXECUTE prefix at all. There is no safe way to inspect what the dynamic SQL string
    // argument actually contains without recursively re-parsing it, so any use of
    // sp_executesql/sys.sp_executesql is treated as inherently mutating-adjacent and rejected
    // outright — a deliberate blanket block, not a partial allow-list.
    private static readonly Regex MutatingKeywordsRegex = new(
        @"\b(insert|update|delete|drop|alter|truncate|create|merge|grant|revoke|reconfigure|checkpoint|backup|restore|dbcc|exec|execute|sp_executesql|into)\b",
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
    private enum SqlParserState
    {
        Normal,
        LineComment,
        BlockComment,
        SingleQuote
    }

    private static string StripCommentsAndStringLiterals(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        var state = SqlParserState.Normal;
        ReadOnlySpan<char> span = sql.AsSpan();

        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            char next = i + 1 < span.Length ? span[i + 1] : '\0';

            state = ProcessChar(state, c, next, sb, ref i);
        }

        return sb.ToString();
    }

    private static SqlParserState ProcessChar(
        SqlParserState state,
        char c,
        char next,
        StringBuilder sb,
        ref int i)
    {
        switch (state)
        {
            case SqlParserState.LineComment:
                if (c == '\n')
                {
                    sb.Append(c);
                    return SqlParserState.Normal;
                }
                return SqlParserState.LineComment;

            case SqlParserState.BlockComment:
                if (c == '*' && next == '/')
                {
                    i++;
                    return SqlParserState.Normal;
                }
                return SqlParserState.BlockComment;

            case SqlParserState.SingleQuote:
                if (c == '\'' && next == '\'')
                {
                    i++; // escaped '' inside literal
                    return SqlParserState.SingleQuote;
                }
                if (c == '\'')
                {
                    sb.Append(' ');
                    return SqlParserState.Normal;
                }
                return SqlParserState.SingleQuote;

            default:
                return TransitionFromNormalState(c, next, sb, ref i);
        }
    }

    private static SqlParserState TransitionFromNormalState(char c, char next, StringBuilder sb, ref int i)
    {
        if (c == '-' && next == '-')
        {
            i++;
            return SqlParserState.LineComment;
        }
        if (c == '/' && next == '*')
        {
            i++;
            return SqlParserState.BlockComment;
        }
        if (c == '\'')
        {
            sb.Append(' ');
            return SqlParserState.SingleQuote;
        }
        sb.Append(c);
        return SqlParserState.Normal;
    }
}
