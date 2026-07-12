#nullable enable

using SqlToAi.Configuration;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace SqlToAi.Tests.Configuration;

// @covers SqlToAi.Configuration.SqlToAiOptions
// @covers SqlToAi.Configuration.SqlServerOptions
// @covers SqlToAi.Configuration.DatabasesOptions
// @covers SqlToAi.Configuration.AnonymizerOptions
// @covers SqlToAi.Configuration.MetadataProviderOptions
// @covers SqlToAi.Configuration.ConfigurationResolver
public sealed class SqlToAiOptionsTests
{
    private static readonly Type TargetType = typeof(SqlToAiOptions);

    [Fact]
    public void SqlToAiOptions_ShouldHaveDefaultValues()
    {
        // Act
        var options = new SqlToAiOptions();

        // Assert
        Assert.NotNull(options.SqlServer);
        Assert.NotNull(options.Databases);
        Assert.NotNull(options.Anonymizer);
        Assert.NotNull(options.MetadataProvider);

        Assert.True(options.SqlServer.EnforceSafetyCheck);
        Assert.False(options.SqlServer.IntegratedSecurity);
        Assert.False(options.MetadataProvider.IntegratedSecurity);
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
          "SqlServer": {
            "Server": "my-server",
            "IntegratedSecurity": true
          },
          "Databases": {
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
        Assert.Equal("my-server", options.SqlServer.Server);
        Assert.True(options.SqlServer.IntegratedSecurity);

        var allowed = Assert.Single(options.Databases.Allowed);
        Assert.Equal("Demo_*", allowed);
        Assert.Equal(100, options.Databases.CacheTtlSeconds);

        Assert.True(options.Anonymizer.Enabled);
        Assert.Equal("Hash", options.Anonymizer.DefaultMode);
        Assert.Equal(2, options.Anonymizer.ExcludedColumns.Count);
        Assert.Contains("*Code", options.Anonymizer.ExcludedColumns);
        Assert.Contains("Status", options.Anonymizer.ExcludedColumns);
    }

    [Fact]
    public void ConfigurationResolver_ShouldLeaveInlineSqlUntouched()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.AccessCheckSql = "SELECT CASE WHEN DB_NAME() = 'OLDemoReweAbfD910' THEN 'ReadWrite' ELSE 'None' END";
        options.SqlServer.SafetyCheckSql = "SELECT 1";
        options.MetadataProvider.TableMetadataQuery = "SELECT * FROM tables";
        options.MetadataProvider.ColumnMetadataQuery = "SELECT * FROM columns";

        // Act
        ConfigurationResolver.Resolve(options);

        // Assert
        Assert.Equal("SELECT CASE WHEN DB_NAME() = 'OLDemoReweAbfD910' THEN 'ReadWrite' ELSE 'None' END", options.Databases.AccessCheckSql);
        Assert.Equal("SELECT 1", options.SqlServer.SafetyCheckSql);
        Assert.Equal("SELECT * FROM tables", options.MetadataProvider.TableMetadataQuery);
        Assert.Equal("SELECT * FROM columns", options.MetadataProvider.ColumnMetadataQuery);
    }

    [Fact]
    public void ConfigurationResolver_ShouldResolveRelativeAndAbsolutePaths()
    {
        // Arrange
        string relativeFileName = "test-relative-query.sql";
        string absoluteFileName = Path.Combine(Path.GetTempPath(), "test-absolute-query.sql");

        string relativeSql = "SELECT 'Relative';";
        string absoluteSql = "SELECT 'Absolute';";

        string relativeFullPath = Path.Combine(AppContext.BaseDirectory, relativeFileName);
        File.WriteAllText(relativeFullPath, relativeSql);
        File.WriteAllText(absoluteFileName, absoluteSql);

        try
        {
            var options = new SqlToAiOptions();
            options.Databases.AccessCheckSql = relativeFileName;
            options.MetadataProvider.TableMetadataQuery = absoluteFileName;

            // Act
            ConfigurationResolver.Resolve(options);

            // Assert
            Assert.Equal(relativeSql, options.Databases.AccessCheckSql);
            Assert.Equal(absoluteSql, options.MetadataProvider.TableMetadataQuery);
        }
        finally
        {
            if (File.Exists(relativeFullPath)) File.Delete(relativeFullPath);
            if (File.Exists(absoluteFileName)) File.Delete(absoluteFileName);
        }
    }

    [Fact]
    public void ConfigurationResolver_ShouldThrowFileNotFoundException_WhenFileDoesNotExist()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.AccessCheckSql = "non-existent-script.sql";

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => ConfigurationResolver.Resolve(options));
    }

    [Fact]
    public void ConfigurationResolver_ShouldExpandEnvironmentVariables()
    {
        // Arrange
        var options = new SqlToAiOptions();
        Environment.SetEnvironmentVariable("TEST_ENV_VAR_SERVER", "EnvServerName");
        Environment.SetEnvironmentVariable("TEST_ENV_VAR_DB", "EnvDbName");

        try
        {
            options.SqlServer.Server = "%TEST_ENV_VAR_SERVER%\\MSSQLSERVER";
            options.Databases.Allowed = new List<string> { "%TEST_ENV_VAR_DB%_Allowed" };

            // Act
            ConfigurationResolver.Resolve(options);

            // Assert
            Assert.Equal("EnvServerName\\MSSQLSERVER", options.SqlServer.Server);
            var allowed = Assert.Single(options.Databases.Allowed);
            Assert.Equal("EnvDbName_Allowed", allowed);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_ENV_VAR_SERVER", null);
            Environment.SetEnvironmentVariable("TEST_ENV_VAR_DB", null);
        }
    }
}
