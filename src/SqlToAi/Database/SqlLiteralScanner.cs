#nullable enable

namespace SqlToAi.Database;

/// <summary>
/// Locates the content spans of top-level SQL string literals (<c>'...'</c>) in a query, skipping
/// content inside line/block comments and bracket identifiers (<c>[...]</c>). Used by
/// <see cref="QueryTokenResolver"/> to make sure anonymization-token substitution only ever touches
/// actual literal content — never comments, identifiers, or SQL keywords.
/// <para>
/// The underlying state machine lives in <see cref="SqlCharScanner"/>; this class only owns the
/// range-tracking business logic on top of it.
/// </para>
/// </summary>
internal static class SqlLiteralScanner
{
    /// <summary>Returns the (start, length) of each literal's content, excluding the surrounding quotes.</summary>
    public static IReadOnlyList<(int Start, int Length)> GetLiteralContentRanges(string sql)
    {
        var ranges = new List<(int Start, int Length)>();
        var previous = SqlCharState.Normal;
        int literalStart = -1;

        foreach (var ev in SqlCharScanner.Scan(sql))
        {
            if (previous != SqlCharState.SingleQuote && ev.State == SqlCharState.SingleQuote)
            {
                // Entering a literal: content starts immediately after the opening quote.
                literalStart = ev.Index + 1;
            }
            else if (previous == SqlCharState.SingleQuote && ev.State != SqlCharState.SingleQuote && literalStart >= 0)
            {
                // Leaving a literal: content ends at the closing quote (exclusive).
                ranges.Add((literalStart, ev.Index - literalStart));
                literalStart = -1;
            }

            previous = ev.State;
        }

        return ranges;
    }
}
