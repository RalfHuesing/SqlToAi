#nullable enable

using SqlToAi.Domain;

namespace SqlToAi.Tests.Domain;

// @covers SqlToAi.Domain.GlobMatcher
public sealed class GlobMatcherTests
{
    [Theory]
    [InlineData("Demo_App", "Demo_*", true)] // '*' matches the trailing segment
    [InlineData("OtherApp", "Demo_*", false)] // text does not start with "Demo_"
    [InlineData("Demo_", "Demo_*", true)] // '*' matches zero or more characters
    [InlineData("Demo", "Demo_*", false)] // literal text without trailing '_' does not satisfy "Demo_*"
    public void IsMatch_ShouldHandleStarWildcard(string text, string pattern, bool expected)
    {
        bool actual = GlobMatcher.IsMatch(text, pattern);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Demo_A", "Demo_?", true)] // '?' matches exactly one character
    [InlineData("Demo_AB", "Demo_?", false)] // '?' matches one character, two do not fit
    [InlineData("Demo_", "Demo_?", false)] // '?' requires exactly one character; empty does not satisfy
    public void IsMatch_ShouldHandleQuestionMarkWildcard(string text, string pattern, bool expected)
    {
        bool actual = GlobMatcher.IsMatch(text, pattern);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("MyServer.1", "MyServer.1", true)] // literal '.' must be matched verbatim
    [InlineData("MyServerX1", "MyServer.1", false)] // '.' is escaped, not a regex metacharacter
    [InlineData("MyServer.1", "MyServer.1*", true)] // '*' after a literal metacharacter
    [InlineData("MyServer.", "MyServer?", true)] // '?' substitutes the literal '.'
    public void IsMatch_ShouldEscapeRegexMetacharacters(string text, string pattern, bool expected)
    {
        bool actual = GlobMatcher.IsMatch(text, pattern);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("demo_app", "DEMO_*", true)] // case-insensitive: lowercase text vs uppercase pattern
    [InlineData("demo_a", "DEMO_?", true)]
    [InlineData("DEMO_App", "demo_*", true)]
    public void IsMatch_ShouldBeCaseInsensitive(string text, string pattern, bool expected)
    {
        bool actual = GlobMatcher.IsMatch(text, pattern);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsMatch_ShouldReturnFalse_OnEmptyPattern()
    {
        bool actual = GlobMatcher.IsMatch("Demo_App", "");
        Assert.False(actual);
    }

    [Fact]
    public void IsMatch_ShouldReturnFalse_OnEmptyText()
    {
        // Empty text never matches a non-empty pattern (anchors ^...$ require at least one char).
        bool actual = GlobMatcher.IsMatch(string.Empty, "Demo_*");
        Assert.False(actual);
    }

    [Fact]
    public void IsMatch_ShouldReturnFalse_OnBothEmpty()
    {
        // Empty pattern short-circuits to false before the regex is built.
        bool actual = GlobMatcher.IsMatch(string.Empty, string.Empty);
        Assert.False(actual);
    }
}
