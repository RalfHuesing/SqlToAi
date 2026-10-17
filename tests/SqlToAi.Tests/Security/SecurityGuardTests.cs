using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Security;
using Xunit;

namespace SqlToAi.Tests.Security;

public sealed class SecurityGuardTests
{
    [Fact]
    public void IsTargetAllowed_ShouldReturnTrue_WhenTargetIsInConfiguredLevelList()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Servers["primary"] = new NamedServerOptions { Server = "localhost" };
        options.Databases.ReadWrite = ["primary/DemoDB"];
        options.Databases.ReadOnly = ["primary/ReportingDB"];
        var guard = new SecurityGuard(Options.Create(options));

        // Act & Assert
        Assert.True(guard.IsTargetAllowed(new DatabaseTargetContext("primary", "DemoDB")));
        Assert.True(guard.IsTargetAllowed(new DatabaseTargetContext("primary", "ReportingDB")));
        Assert.False(guard.IsTargetAllowed(new DatabaseTargetContext("primary", "UnknownDB")));
        Assert.False(guard.IsTargetAllowed(new DatabaseTargetContext("other", "DemoDB")));
    }

    [Fact]
    public void IsTargetAllowed_ShouldReturnFalse_WhenTargetIsGloballyExcluded()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Servers["primary"] = new NamedServerOptions
        {
            Server = "localhost",
            ExcludedDatabases = ["HR_Payroll", "master*"]
        };
        options.Databases.ReadWrite = ["primary/DemoDB", "primary/HR_Payroll"];
        var guard = new SecurityGuard(Options.Create(options));

        // Act & Assert
        Assert.True(guard.IsTargetAllowed(new DatabaseTargetContext("primary", "DemoDB")));
        Assert.False(guard.IsTargetAllowed(new DatabaseTargetContext("primary", "HR_Payroll")));
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
