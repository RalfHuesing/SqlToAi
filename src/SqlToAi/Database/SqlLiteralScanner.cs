#nullable enable

namespace SqlToAi.Database;

/// <summary>
/// Locates the content spans of top-level SQL string literals (<c>'...'</c>) in a query, skipping
/// content inside line/block comments and bracket identifiers (<c>[...]</c>). Used by
/// <see cref="QueryTokenResolver"/> to make sure anonymization-token substitution only ever touches
/// actual literal content — never comments, identifiers, or SQL keywords.
/// <para>
/// This is a standalone, independently tested utility rather than a shared refactor of
/// <see cref="QueryExecutionService"/>'s multi-statement guard, to keep that already-tested,
/// security-critical detector untouched.
/// </para>
/// </summary>
internal static class SqlLiteralScanner
{
    private enum State
    {
        Normal,
        LineComment,
        BlockComment,
        SingleQuote,
        Bracket
    }

    /// <summary>Returns the (start, length) of each literal's content, excluding the surrounding quotes.</summary>
    public static IReadOnlyList<(int Start, int Length)> GetLiteralContentRanges(string sql)
    {
        var ranges = new List<(int Start, int Length)>();
        var state = State.Normal;
        int literalStart = -1;
        ReadOnlySpan<char> span = sql.AsSpan();

        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            char next = i + 1 < span.Length ? span[i + 1] : '\0';
            State previous = state;
            state = Transition(state, c, next, ref i);

            if (previous != State.SingleQuote && state == State.SingleQuote)
            {
                literalStart = i + 1;
            }
            else if (previous == State.SingleQuote && state != State.SingleQuote && literalStart >= 0)
            {
                ranges.Add((literalStart, i - literalStart));
                literalStart = -1;
            }
        }

        return ranges;
    }

    private static State Transition(State state, char c, char next, ref int i)
    {
        switch (state)
        {
            case State.LineComment:
                return c == '\n' ? State.Normal : State.LineComment;

            case State.BlockComment:
                if (c == '*' && next == '/')
                {
                    i++; // skip '/'
                    return State.Normal;
                }
                return State.BlockComment;

            case State.SingleQuote:
                if (c == '\'' && next == '\'')
                {
                    i++; // escaped quote, stays inside the literal
                    return State.SingleQuote;
                }
                return c == '\'' ? State.Normal : State.SingleQuote;

            case State.Bracket:
                return c == ']' ? State.Normal : State.Bracket;

            default:
                return TransitionFromNormal(c, next, ref i);
        }
    }

    private static State TransitionFromNormal(char c, char next, ref int i)
    {
        if (c == '-' && next == '-')
        {
            i++;
            return State.LineComment;
        }
        if (c == '/' && next == '*')
        {
            i++;
            return State.BlockComment;
        }
        if (c == '\'') return State.SingleQuote;
        if (c == '[') return State.Bracket;
        return State.Normal;
    }
}
