#nullable enable

namespace SqlToAi.Database;

/// <summary>
/// Detects multiple SQL statements by scanning for semicolons outside string literals
/// (<c>'...'</c>), bracket identifiers (<c>[...]</c>), and comments (<c>--</c>, <c>/* */</c>).
/// Extracted from <see cref="QueryExecutionService"/> so that file stays within the project's
/// line-count budget; this scanner has no dependency on the rest of that class.
/// </summary>
internal static class SqlMultiStatementDetector
{
    private enum SqlParserState
    {
        Normal,
        LineComment,
        BlockComment,
        SingleQuote,
        Bracket
    }

    public static bool ContainsMultipleStatements(string query)
    {
        var state = SqlParserState.Normal;
        ReadOnlySpan<char> span = query.AsSpan();

        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            char next = i + 1 < span.Length ? span[i + 1] : '\0';

            state = Transition(state, c, next, ref i);

            if (state == SqlParserState.Normal && c == ';')
            {
                // Allow trailing semicolon at end (after trimming whitespace)
                string remaining = query[(i + 1)..].TrimEnd();
                if (remaining.Length > 0)
                {
                    return true; // text after semicolon → second statement
                }
            }
        }

        return false;
    }

    private static SqlParserState Transition(SqlParserState state, char c, char next, ref int i)
    {
        switch (state)
        {
            case SqlParserState.LineComment:
                if (c == '\n') return SqlParserState.Normal;
                return SqlParserState.LineComment;

            case SqlParserState.BlockComment:
                if (c == '*' && next == '/')
                {
                    i++; // skip '/'
                    return SqlParserState.Normal;
                }
                return SqlParserState.BlockComment;

            case SqlParserState.SingleQuote:
                if (c == '\'' && next == '\'')
                {
                    i++; // escaped quote
                    return SqlParserState.SingleQuote;
                }
                if (c == '\'') return SqlParserState.Normal;
                return SqlParserState.SingleQuote;

            case SqlParserState.Bracket:
                if (c == ']') return SqlParserState.Normal;
                return SqlParserState.Bracket;

            default:
                return TransitionFromNormal(c, next, ref i);
        }
    }

    private static SqlParserState TransitionFromNormal(char c, char next, ref int i)
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
        if (c == '\'') return SqlParserState.SingleQuote;
        if (c == '[') return SqlParserState.Bracket;
        return SqlParserState.Normal;
    }
}
