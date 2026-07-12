#nullable enable

using SqlToAi.Configuration;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace SqlToAi.Tests.Configuration;

// @covers SqlToAi.Configuration.SqlToAiOptions
// @covers SqlToAi.Configuration.SqlDatabaseOptions
// @covers SqlToAi.Configuration.DatabasesOptions
// @covers SqlToAi.Configuration.AnonymizerOptions
// @covers SqlToAi.Configuration.AnonymizerRule
// @covers SqlToAi.Configuration.MetadataProviderOptions
public sealed class SqlToAiOptionsTests
{
    private static readonly Type TargetType = typeof(SqlToAiOptions);

    [Fact]
    public void SqlToAiOptions_ShouldHaveDefaultValues()
    {
        // Act
        var options = new SqlToAiOptions();

        // Assert
        Assert.NotNull(options.SqlDatabase);
        Assert.NotNull(options.Databases);
        Assert.NotNull(options.Anonymizer);
        Assert.NotNull(options.MetadataProvider);

        Assert.True(options.SqlDatabase.EnforceSafetyCheck);
        Assert.True(options.Anonymizer.Enabled);
        Assert.True(options.MetadataProvider.Enabled);
        Assert.Equal("ScramblePattern", options.Anonymizer.DefaultMode);
    }

    [Fact]
    public void SqlToAiOptions_ShouldBindFromConfiguration()
    {
        // Arrange
        var appSettingsJson = """
        {
          "SqlDatabase": {
            "Server": "my-server",
            "DefaultDatabase": "MyDb"
          },
          "Databases": {
            "Default": "DemoDb",
            "Allowed": ["Demo_*"],
            "CacheTtlSeconds": 100
          },
          "Anonymizer": {
            "Enabled": true,
            "DefaultMode": "Hash",
            "ExcludedColumns": ["*Code", "Status"]
          }
        }
        """;

        var builder = new ConfigurationBuilder();
        builder.AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(appSettingsJson)));
        var configuration = builder.Build();

        // Act
        var options = new SqlToAiOptions();
        configuration.Bind(options);

        // Assert
        Assert.Equal("my-server", options.SqlDatabase.Server);
        Assert.Equal("MyDb", options.SqlDatabase.DefaultDatabase);

        Assert.Equal("DemoDb", options.Databases.Default);
        var allowed = Assert.Single(options.Databases.Allowed);
        Assert.Equal("Demo_*", allowed);
        Assert.Equal(100, options.Databases.CacheTtlSeconds);

        Assert.True(options.Anonymizer.Enabled);
        Assert.Equal("Hash", options.Anonymizer.DefaultMode);
        Assert.Equal(2, options.Anonymizer.ExcludedColumns.Count);
        Assert.Contains("*Code", options.Anonymizer.ExcludedColumns);
        Assert.Contains("Status", options.Anonymizer.ExcludedColumns);
    }
}
