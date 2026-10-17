using Microsoft.Extensions.Configuration;
using SqlToAi.Configuration;

namespace SqlToAi.Tests.Integration;

public sealed class SqlServerFixtureConfigurationTests
{
    [Fact]
    public void GeneralFixture_UsesCheckedInTrustOverlay_WithoutChangingDemoDefaults()
    {
        const string variable = "SQLTOAI_INTEGRATION_SqlToAi__Servers__test__TrustServerCertificate";
        string? original = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, null);

        try
        {
            var configuration = SqlServerFixture.BuildConfiguration(AppContext.BaseDirectory);
            var fixtureOptions = configuration.GetSection("SqlToAi").Get<SqlToAiOptions>()!;
            Assert.True(fixtureOptions.Servers["test"].TrustServerCertificate);

            using var demoStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "appsettings.integration.json"));
            var demo = new ConfigurationBuilder().AddJsonStream(demoStream).Build()
                .GetSection("SqlToAi").Get<SqlToAiOptions>()!;
            Assert.False(demo.Servers["test"].TrustServerCertificate);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
        }
    }

    [Fact]
    public void ExplicitFalseEnvironmentValue_OverridesGeneralFixtureTrustOverlay()
    {
        const string variable = "SQLTOAI_INTEGRATION_SqlToAi__Servers__test__TrustServerCertificate";
        string? original = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, "false");

        try
        {
            var configuration = SqlServerFixture.BuildConfiguration(AppContext.BaseDirectory);
            var options = configuration.GetSection("SqlToAi").Get<SqlToAiOptions>()!;

            Assert.False(options.Servers["test"].TrustServerCertificate);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
        }
    }
}
