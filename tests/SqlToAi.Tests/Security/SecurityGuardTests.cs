#nullable enable

using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
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
}
