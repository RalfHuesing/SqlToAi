#nullable enable

using System.Collections.Generic;

namespace SqlToAi.Database;

/// <summary>
/// State of the SQL character scanner at a given position.
/// </summary>
public enum SqlCharState
{
    /// <summary>Plain SQL code (outside any comment, string literal, or bracket identifier).</summary>
    Normal,

    /// <summary>Inside a line comment (<c>-- ...</c>); terminates at the next line break.</summary>
    LineComment,

    /// <summary>Inside a block comment (<c>/* ... */</c>); terminates at the next <c>*/</c>.</summary>
    BlockComment,

    /// <summary>Inside a single-quoted string literal (<c>'...'</c>); an embedded <c>''</c> is an escaped quote.</summary>
    SingleQuote,

    /// <summary>Inside a bracketed identifier (<c>[...]</c>); terminates at the next <c>]</c>.</summary>
    Bracket,
}

/// <summary>
/// One step of the SQL character scanner: the character that was just processed, the character
/// immediately following it (or <c>'\0'</c> at end of input), the state the scanner is in
/// <em>after</em> processing <see cref="Character"/>, and the zero-based position of
/// <see cref="Character"/> in the input. Multi-character transitions (<c>--</c>, <c>/*</c>,
/// <c>''</c>, <c>*/</c>) emit one event for the first character of the pair; the second
/// character is consumed internally and never surfaced as its own event.
/// </summary>
/// <param name="State">The scanner state after processing <paramref name="Character"/>.</param>
/// <param name="Character">The character at position <paramref name="Index"/>.</param>
/// <param name="Next">The character at position <c>Index + 1</c>, or <c>'\0'</c> at end of input.</param>
/// <param name="Index">The zero-based position of <paramref name="Character"/> in the input.</param>
public readonly record struct SqlCharEvent(SqlCharState State, char Character, char Next, int Index);

/// <summary>
/// Shared primitive that walks a SQL string character-by-character and reports a
/// <see cref="SqlCharEvent"/> for string literal extraction in <see cref="SqlLiteralScanner"/>.
/// </summary>
internal static class SqlCharScanner
{
    /// <summary>
    /// Scans the input character-by-character. For each character the consumer should act on,
    /// yields a <see cref="SqlCharEvent"/> with the state <em>after</em> processing the character.
    /// Multi-character transitions (e.g. <c>--</c>, <c>/*</c>, <c>*/</c>, <c>''</c>) emit exactly
    /// one event, for the first character of the pair; the second character is consumed internally.
    /// </summary>
    /// <param name="sql">The SQL text to scan. Must not be modified during iteration.</param>
    public static IEnumerable<SqlCharEvent> Scan(string sql)
    {
        var state = SqlCharState.Normal;
        int i = 0;
        while (i < sql.Length)
        {
            int currentIndex = i;
            char c = sql[i];
            char next = i + 1 < sql.Length ? sql[i + 1] : '\0';
            state = Transition(state, c, next, ref i);
            yield return new SqlCharEvent(state, c, next, currentIndex);
            i++;
        }
    }

    private static SqlCharState Transition(SqlCharState state, char c, char next, ref int i)
    {
        switch (state)
        {
            case SqlCharState.LineComment:
                if (c == '\n') return SqlCharState.Normal;
                return SqlCharState.LineComment;

            case SqlCharState.BlockComment:
                if (c == '*' && next == '/')
                {
                    i++; // skip '/'
                    return SqlCharState.Normal;
                }
                return SqlCharState.BlockComment;

            case SqlCharState.SingleQuote:
                if (c == '\'' && next == '\'')
                {
                    i++; // escaped quote, stays inside the literal
                    return SqlCharState.SingleQuote;
                }
                return c == '\'' ? SqlCharState.Normal : SqlCharState.SingleQuote;

            case SqlCharState.Bracket:
                return c == ']' ? SqlCharState.Normal : SqlCharState.Bracket;

            default:
                return TransitionFromNormal(c, next, ref i);
        }
    }

    private static SqlCharState TransitionFromNormal(char c, char next, ref int i)
    {
        if (c == '-' && next == '-')
        {
            i++;
            return SqlCharState.LineComment;
        }
        if (c == '/' && next == '*')
        {
            i++;
            return SqlCharState.BlockComment;
        }
        if (c == '\'') return SqlCharState.SingleQuote;
        if (c == '[') return SqlCharState.Bracket;
        return SqlCharState.Normal;
    }
}
