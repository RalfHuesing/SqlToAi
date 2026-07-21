#nullable enable

using SqlToAi.Anonymization;

namespace SqlToAi.Tests.Anonymization;

// @covers SqlToAi.Anonymization.LikePatternMatcher
public sealed class LikePatternMatcherTests
{
    private static readonly Type TargetType = typeof(LikePatternMatcher);

    [Theory]
    [InlineData("FakeConsultants", "%", true)]
    [InlineData("FakeConsultants", "FakeConsultants", true)]
    [InlineData("FakeConsultants", "fakeconsultants", true)] // case-insensitive
    [InlineData("FakeConsultants", "Fake%", true)]
    [InlineData("FakeConsultants", "%Consultants", true)]
    [InlineData("FakeGroupContoso", "Fake%Contoso", true)]
    [InlineData("FakeGroupContoso", "Fake_GroupContoso", false)] // single-char wildcard, wrong length
    [InlineData("FakeXGroupContoso", "Fake_GroupContoso", true)]
    [InlineData("FakeConsultants", "OtherTable", false)]
    [InlineData("FakeConsultants", "", false)]
    public void IsMatch_ShouldEvaluateLikeWildcardsCaseInsensitively(string text, string pattern, bool expected)
    {
        Assert.Equal(expected, LikePatternMatcher.IsMatch(text, pattern));
    }

    [Theory]
    [InlineData("%", 0)]
    [InlineData("", 0)]
    [InlineData("Fake%", 1)]
    [InlineData("%Fake%", 1)]
    [InlineData("Fake_Group", 1)]
    [InlineData("FakeConsultants", 2)]
    public void SpecificityScore_ShouldRankExactOverPartialOverWildcard(string pattern, int expected)
    {
        Assert.Equal(expected, LikePatternMatcher.SpecificityScore(pattern));
    }
}
