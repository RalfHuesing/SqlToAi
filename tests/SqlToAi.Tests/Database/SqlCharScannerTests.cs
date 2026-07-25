#nullable enable

using SqlToAi.Database;

namespace SqlToAi.Tests.Database;

// @covers SqlToAi.Database.SqlCharScanner
public sealed class SqlCharScannerTests
{
    [Fact]
    public void Scan_ShouldHandleEmptyInput()
    {
        var events = SqlCharScanner.Scan(string.Empty).ToArray();

        Assert.Empty(events);
    }

    [Theory]
    // -- foo\nbar: "--" opens a line comment, '\n' ends it, "bar" follows in Normal. The
    // second '-' of '--' is consumed internally, so length-10 input produces 9 events.
    [InlineData("-- foo\nbar",
        new byte[] { 1, 1, 1, 1, 1, 0, 0, 0, 0 },
        new int[]  { 0, 2, 3, 4, 5, 6, 7, 8, 9 })]
    // /* bar */baz: "/*" opens, "*/" closes (single '*' event transitions to Normal; the
    // matching '/' is consumed and never emitted). Length 11, but only 10 events.
    [InlineData("/* bar */baz",
        new byte[] { 2, 2, 2, 2, 2, 2, 0, 0, 0, 0 },
        new int[]  { 0, 2, 3, 4, 5, 6, 7, 9, 10, 11 })]
    // 'lit': single literal. Opening '\'' is SingleQuote, closing '\'' transitions to Normal.
    [InlineData("'lit'",
        new byte[] { 3, 3, 3, 3, 0 },
        new int[]  { 0, 1, 2, 3, 4 })]
    // [id]: bracket identifier.
    [InlineData("[id]",
        new byte[] { 4, 4, 4, 0 },
        new int[]  { 0, 1, 2, 3 })]
    // SELECT 1: every character is in Normal.
    [InlineData("SELECT 1",
        new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 },
        new int[]  { 0, 1, 2, 3, 4, 5, 6, 7 })]
    public void Scan_ShouldClassifyCommentAndLiteralStates(
        string sql,
        byte[] expectedStateOrdinals,
        int[] expectedIndices)
    {
        Assert.Equal(expectedStateOrdinals.Length, expectedIndices.Length);

        var actual = SqlCharScanner.Scan(sql).ToArray();

        Assert.Equal(expectedIndices.Length, actual.Length);
        for (int i = 0; i < expectedStateOrdinals.Length; i++)
        {
            Assert.Equal((SqlCharState)expectedStateOrdinals[i], actual[i].State);
            Assert.Equal(expectedIndices[i], actual[i].Index);
        }
    }

    [Fact]
    public void Scan_ShouldHandleEscapedQuotesInsideLiterals()
    {
        // "'''" — open quote at 0, escaped quote at 1 (consumes the second '\'' internally),
        // and no closing quote. The third '\'' at index 2 is never processed because the
        // escape has already consumed the loop counter — same as the original
        // SqlMultiStatementDetector / ReadOnlyGuard behavior.
        var threeQuotes = SqlCharScanner.Scan("'''").ToArray();

        Assert.Equal(2, threeQuotes.Length);
        Assert.Equal(SqlCharState.SingleQuote, threeQuotes[0].State);
        Assert.Equal(0, threeQuotes[0].Index);
        Assert.Equal('\'', threeQuotes[0].Character);
        Assert.Equal(SqlCharState.SingleQuote, threeQuotes[1].State);
        Assert.Equal(1, threeQuotes[1].Index);
        Assert.Equal('\'', threeQuotes[1].Character);

        // "''''" — open at 0, escaped at 1 (consumes index 1), then literal closes at 3
        // (the state was still SingleQuote from the escape, so the '\'' at index 3 transitions
        // back to Normal). The character at index 2 is consumed by the escape, so the sequence
        // is 0, 1, 3 — matches the original SqlMultiStatementDetector / ReadOnlyGuard behavior.
        var fourQuotes = SqlCharScanner.Scan("''''").ToArray();

        Assert.Equal(3, fourQuotes.Length);
        Assert.Equal(SqlCharState.SingleQuote, fourQuotes[0].State);
        Assert.Equal(0, fourQuotes[0].Index);
        Assert.Equal(SqlCharState.SingleQuote, fourQuotes[1].State);
        Assert.Equal(1, fourQuotes[1].Index);
        Assert.Equal(SqlCharState.Normal, fourQuotes[2].State);
        Assert.Equal(3, fourQuotes[2].Index);
    }

    [Fact]
    public void Scan_ShouldHandleNestedBlockCommentAndBracketEnd()
    {
        // A '/*' inside an already-open block comment is NOT a re-opener — it just stays in
        // BlockComment. The first '*/' closes the entire comment.
        const string nestedComment = "/* nested /* still comment */ end";
        var commentEvents = SqlCharScanner.Scan(nestedComment).ToArray();

        // Locate the closing '*/' by its position in the input string, not by event array index
        // (the '/' of the closing pair is consumed internally and never appears as an event).
        int closingStarIndex = nestedComment.IndexOf("*/", StringComparison.Ordinal);
        Assert.Equal(27, closingStarIndex);

        // Every event whose Index is strictly less than the closing '*' must be BlockComment.
        // The closing '*' itself transitions to Normal.
        foreach (var ev in commentEvents.Where(e => e.Index < closingStarIndex))
        {
            Assert.Equal(SqlCharState.BlockComment, ev.State);
        }

        // The event at Index == closingStarIndex is the closing '*' (state = Normal after transition).
        var closingStar = Assert.Single(commentEvents, e => e.Index == closingStarIndex);
        Assert.Equal(SqlCharState.Normal, closingStar.State);
        Assert.Equal('*', closingStar.Character);

        // Every event with Index > closingStarIndex + 1 (i.e. past the consumed '/') is Normal.
        foreach (var ev in commentEvents.Where(e => e.Index > closingStarIndex + 1))
        {
            Assert.Equal(SqlCharState.Normal, ev.State);
        }

        // ']' inside a bracket identifier is just a character, NOT a closer. The closer is the
        // first ']' after '['. A second ']' outside the bracket is just a plain character in Normal.
        const string bracket = "[bracket-with-]-inside]";
        var bracketEvents = SqlCharScanner.Scan(bracket).ToArray();

        // The '[' at index 0 opens the bracket.
        var openBracket = Assert.Single(bracketEvents, e => e.Index == 0);
        Assert.Equal(SqlCharState.Bracket, openBracket.State);
        Assert.Equal('[', openBracket.Character);

        // The first ']' (at index 14) closes the bracket → state becomes Normal.
        var closingBracket = Assert.Single(bracketEvents, e => e.Index == 14);
        Assert.Equal(SqlCharState.Normal, closingBracket.State);
        Assert.Equal(']', closingBracket.Character);

        // Everything between the '[' and the first ']' is Bracket.
        foreach (var ev in bracketEvents.Where(e => e.Index > 0 && e.Index < 14))
        {
            Assert.Equal(SqlCharState.Bracket, ev.State);
        }

        // Everything after the first ']' is Normal — the second ']' at index 22 is just a
        // regular character, not a bracket opener.
        foreach (var ev in bracketEvents.Where(e => e.Index > 14))
        {
            Assert.Equal(SqlCharState.Normal, ev.State);
        }
    }

    [Fact]
    public void Scan_ShouldReportNextCharAndOriginalChar()
    {
        // 'Next' is the character at Index+1, or '\0' at end of input.
        var events = SqlCharScanner.Scan("ab").ToArray();

        Assert.Equal(2, events.Length);
        Assert.Equal('a', events[0].Character);
        Assert.Equal('b', events[0].Next);
        Assert.Equal('b', events[1].Character);
        Assert.Equal('\0', events[1].Next);
    }
}
