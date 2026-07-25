#nullable enable

using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Tests.Security;

// @covers SqlToAi.Security.SecurityGuard
public sealed class SecurityGuardTests
{
    private static readonly Type TargetType = typeof(SecurityGuard);

    [Fact]
    public void IsDatabaseAllowed_ShouldReturnFalse_ForEmptyOrNullDatabase()
    {
        // Arrange
        var options = new SqlToAiOptions();
        var guard = new SecurityGuard(Options.Create(options));

        // Act & Assert
        Assert.False(guard.IsDatabaseAllowed(""));
        Assert.False(guard.IsDatabaseAllowed("   "));
    }

    [Fact]
    public void IsDatabaseAllowed_ShouldReturnTrue_WhenMatchesAllowedPattern()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = new List<string> { "Demo_*", "Reporting" };
        var guard = new SecurityGuard(Options.Create(options));

        // Act & Assert
        Assert.True(guard.IsDatabaseAllowed("Demo_App"));
        Assert.True(guard.IsDatabaseAllowed("Reporting"));
        Assert.False(guard.IsDatabaseAllowed("Demo"));
        Assert.False(guard.IsDatabaseAllowed("Master"));
    }

    [Fact]
    public void IsDatabaseAllowed_ShouldReturnFalse_WhenMatchesBlockedOrExcludedPattern()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = new List<string> { "*" };
        options.Databases.Blocked = new List<string> { "master", "msdb", "tempdb" };
        options.SqlServer.ExcludedDatabases = new List<string> { "HR_Payroll" };
        var guard = new SecurityGuard(Options.Create(options));

        // Act & Assert
        Assert.True(guard.IsDatabaseAllowed("Demo_App"));
        Assert.False(guard.IsDatabaseAllowed("master"));
        Assert.False(guard.IsDatabaseAllowed("msdb"));
        Assert.False(guard.IsDatabaseAllowed("HR_Payroll"));
    }

    [Theory]
    [InlineData("Demo_A", "Demo_?", true)] // single-char wildcard matches exactly one character
    [InlineData("Demo_App", "Demo_??", false)] // '?' matches one character, so 'Demo_??' is 7 chars vs 8-char text
    [InlineData("MyServer.1", "MyServer.1", true)] // exact match including regex metacharacters
    [InlineData("MyServer.", "MyServer?", true)] // '?' substitutes the '.' (one char)
    [InlineData("MyServer.1", "MyServer.1*", true)] // '*' after a metacharacter
    [InlineData("MyServerX1", "MyServer.1", false)] // '.' must be escaped as literal
    [InlineData("demo_a", "DEMO_?", true)] // case-insensitive matching
    [InlineData("Demo_App", "Demo_App?", false)] // '?' requires at least one trailing character
    [InlineData("Demo_App", "Demo_App*", true)] // '*' matches zero or more trailing characters
    public void MatchesPattern_ShouldEvaluateGlobWildcardsCaseInsensitively(string text, string pattern, bool expected)
    {
        Assert.Equal(expected, GlobMatcher.IsMatch(text, pattern));
    }

    [Theory]
    [InlineData("", "Demo_*")] // empty text never matches a non-empty pattern
    [InlineData("Demo_App", "")] // empty pattern is rejected by the guard
    [InlineData("", "")] // both empty -> no match
    public void MatchesPattern_ShouldReturnFalse_OnTimeoutOrEmptyInput(string text, string pattern)
    {
        Assert.False(GlobMatcher.IsMatch(text, pattern));
    }
}
