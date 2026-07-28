#nullable enable

using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Security;
using Xunit;

namespace SqlToAi.Tests.Security;

// @covers SqlToAi.Security.SecurityGuard
public sealed class SecurityGuardTests
{
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
    public void IsDatabaseAllowed_ShouldReturnTrue_WhenDatabaseIsInConfiguredLevelList()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = ["DemoDB"];
        options.Databases.ReadOnly = ["ReportingDB"];
        var guard = new SecurityGuard(Options.Create(options));

        // Act & Assert
        Assert.True(guard.IsDatabaseAllowed("DemoDB"));
        Assert.True(guard.IsDatabaseAllowed("ReportingDB"));
        Assert.False(guard.IsDatabaseAllowed("UnknownDB"));
    }

    [Fact]
    public void IsDatabaseAllowed_ShouldReturnFalse_WhenDatabaseIsGloballyExcluded()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = ["DemoDB", "HR_Payroll"];
        options.SqlServer.ExcludedDatabases = ["HR_Payroll", "master*"];
        var guard = new SecurityGuard(Options.Create(options));

        // Act & Assert
        Assert.True(guard.IsDatabaseAllowed("DemoDB"));
        Assert.False(guard.IsDatabaseAllowed("HR_Payroll"));
    }

    [Theory]
    [InlineData("Demo_A", "Demo_?", true)]
    [InlineData("Demo_App", "Demo_??", false)]
    [InlineData("MyServer.1", "MyServer.1", true)]
    [InlineData("MyServer.", "MyServer?", true)]
    [InlineData("MyServer.1", "MyServer.1*", true)]
    [InlineData("MyServerX1", "MyServer.1", false)]
    [InlineData("demo_a", "DEMO_?", true)]
    [InlineData("Demo_App", "Demo_App?", false)]
    [InlineData("Demo_App", "Demo_App*", true)]
    public void MatchesPattern_ShouldEvaluateGlobWildcardsCaseInsensitively(string text, string pattern, bool expected)
    {
        Assert.Equal(expected, GlobMatcher.IsMatch(text, pattern));
    }

    [Theory]
    [InlineData("", "Demo_*")]
    [InlineData("Demo_App", "")]
    [InlineData("", "")]
    public void MatchesPattern_ShouldReturnFalse_OnTimeoutOrEmptyInput(string text, string pattern)
    {
        Assert.False(GlobMatcher.IsMatch(text, pattern));
    }
}
