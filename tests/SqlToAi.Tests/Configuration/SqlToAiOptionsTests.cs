using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.IO;

namespace SqlToAi.Tests.Configuration;

public sealed class SqlToAiOptionsTests
{
    private static readonly Type TargetType = typeof(SqlToAiOptions);

    [Fact]
    public async Task SqlToAiOptions_ShouldHaveDefaultValues()
    {
        // Act
        var options = new SqlToAiOptions();

        // Assert
        Assert.NotNull(options.SqlServer);
        Assert.NotNull(options.Databases);
        Assert.NotNull(options.Anonymizer);
        Assert.NotNull(options.MetadataProvider);
        Assert.NotNull(options.Observability);

        Assert.Empty(options.Databases.ReadWrite);
        Assert.Empty(options.Databases.ReadOnly);
        Assert.Empty(options.Databases.ReadOnlyAnonymized);
        Assert.Empty(options.Databases.SchemaOnly);
        Assert.Equal(string.Empty, options.SqlServer.Server);
        Assert.Null(options.SqlServer.UserId);
        Assert.Null(options.SqlServer.Password);
        Assert.False(options.SqlServer.IntegratedSecurity);
        Assert.False(options.MetadataProvider.IntegratedSecurity);
        Assert.True(options.Anonymizer.Enabled);
        Assert.True(options.MetadataProvider.Enabled);
        Assert.Equal("ScramblePattern", options.Anonymizer.DefaultMode);
        Assert.Equal(10_485_760L, options.QueryExecution.MaxScriptFileSizeBytes);

        Assert.True(options.Observability.Enabled);
        Assert.False(options.Observability.EnableToolCallLogging);
        Assert.True(options.Observability.EnableFeedbackTool);
        Assert.Null(options.Observability.LogDirectory);
        Assert.False(options.Observability.EnableResponseLogging);
        Assert.Equal(0, options.Observability.MaxResponseLength);
        Assert.True(options.Logging.AppLog.Enabled);
        Assert.True(options.Logging.ErrorLog.Enabled);
        Assert.False(options.Logging.McpTrail.Enabled);

        var accessLevels = new AccessLevelProvider(Options.Create(options));
        Assert.Equal(AccessLevel.None,
            await accessLevels.GetAccessLevelAsync("DemoDB", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShippedTemplate_UsesSafeBoundDefaults_AndDemoExampleIsExplicit()
    {
        var templateOptions = LoadOptions("appsettings.json");
        var demoOptions = LoadOptions("appsettings.integration.json");
        var defaults = new SqlToAiOptions();

        Assert.Equal(defaults.Databases.ReadWrite, templateOptions.Databases.ReadWrite);
        Assert.Equal(defaults.Databases.ReadOnly, templateOptions.Databases.ReadOnly);
        Assert.Equal(defaults.Databases.ReadOnlyAnonymized, templateOptions.Databases.ReadOnlyAnonymized);
        Assert.Equal(defaults.Databases.SchemaOnly, templateOptions.Databases.SchemaOnly);
        Assert.Equal(defaults.SqlServer.Server, templateOptions.SqlServer.Server);
        Assert.Equal(defaults.SqlServer.UserId, templateOptions.SqlServer.UserId);
        Assert.Equal(defaults.SqlServer.Password, templateOptions.SqlServer.Password);
        Assert.Equal(defaults.SqlServer.IntegratedSecurity, templateOptions.SqlServer.IntegratedSecurity);
        Assert.Equal(defaults.Observability.EnableToolCallLogging, templateOptions.Observability.EnableToolCallLogging);
        Assert.Equal(defaults.Observability.EnableResponseLogging, templateOptions.Observability.EnableResponseLogging);
        Assert.Equal(defaults.Logging.AppLog.Enabled, templateOptions.Logging.AppLog.Enabled);
        Assert.Equal(defaults.Logging.ErrorLog.Enabled, templateOptions.Logging.ErrorLog.Enabled);

        var safeAccess = new AccessLevelProvider(Options.Create(templateOptions));
        Assert.Equal(AccessLevel.None,
            await safeAccess.GetAccessLevelAsync("DemoDB", TestContext.Current.CancellationToken));
        Assert.Equal("Agent", demoOptions.SqlServer.UserId);
        Assert.Equal("Agent!", demoOptions.SqlServer.Password);
        Assert.Contains("DemoDB", demoOptions.Databases.ReadWrite);
        Assert.Equal(AccessLevel.ReadWrite,
            await new AccessLevelProvider(Options.Create(demoOptions))
                .GetAccessLevelAsync("DemoDB", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void OmittedSettings_WhenBoundThroughConfigurationKeepSafeDefaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var options = new SqlToAiOptions();
        configuration.Bind(options);

        Assert.Empty(options.Databases.ReadWrite);
        Assert.False(options.Observability.EnableToolCallLogging);
        Assert.False(options.Observability.EnableResponseLogging);
        Assert.True(options.Logging.AppLog.Enabled);
        Assert.True(options.Logging.ErrorLog.Enabled);
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
            "ReadWrite": ["DemoDB"],
            "ReadOnly": ["ArchiveDB"],
            "CacheTtlSeconds": 100
          },
          "Anonymizer": {
            "Enabled": true,
            "DefaultMode": "Hash"
          },
          "QueryExecution": {
            "MaxScriptFileSizeBytes": 2048
          },
          "Observability": {
            "Enabled": true,
            "EnableToolCallLogging": false,
            "EnableFeedbackTool": true,
            "LogDirectory": "custom_log",
            "MaxResponseLength": 500
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

        var rw = Assert.Single(options.Databases.ReadWrite);
        Assert.Equal("DemoDB", rw);
        var ro = Assert.Single(options.Databases.ReadOnly);
        Assert.Equal("ArchiveDB", ro);
        Assert.Equal(100, options.Databases.CacheTtlSeconds);

        Assert.True(options.Anonymizer.Enabled);
        Assert.Equal("Hash", options.Anonymizer.DefaultMode);
        Assert.Equal(2048L, options.QueryExecution.MaxScriptFileSizeBytes);

        Assert.True(options.Observability.Enabled);
        Assert.False(options.Observability.EnableToolCallLogging);
        Assert.True(options.Observability.EnableFeedbackTool);
        Assert.Equal("custom_log", options.Observability.LogDirectory);
        Assert.Equal(500, options.Observability.MaxResponseLength);
    }

    private static SqlToAiOptions LoadOptions(string fileName)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, fileName));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        return configuration.GetSection("SqlToAi").Get<SqlToAiOptions>() ?? new SqlToAiOptions();
    }

    [Fact]
    public void ConfigurationResolver_ShouldLeaveInlineSqlUntouched()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.MetadataProvider.TableMetadataQuery = "SELECT * FROM tables";
        options.MetadataProvider.ColumnMetadataQuery = "SELECT * FROM columns";

        // Act
        ConfigurationResolver.Resolve(options);

        // Assert
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
            options.MetadataProvider.TableMetadataQuery = relativeFileName;
            options.MetadataProvider.ColumnMetadataQuery = absoluteFileName;

            // Act
            ConfigurationResolver.Resolve(options);

            // Assert
            Assert.Equal(relativeSql, options.MetadataProvider.TableMetadataQuery);
            Assert.Equal(absoluteSql, options.MetadataProvider.ColumnMetadataQuery);
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
        options.MetadataProvider.TableMetadataQuery = "non-existent-script.sql";

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
            options.Databases.ReadWrite = new List<string> { "%TEST_ENV_VAR_DB%_RW" };

            // Act
            ConfigurationResolver.Resolve(options);

            // Assert
            Assert.Equal("EnvServerName\\MSSQLSERVER", options.SqlServer.Server);
            var rw = Assert.Single(options.Databases.ReadWrite);
            Assert.Equal("EnvDbName_RW", rw);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_ENV_VAR_SERVER", null);
            Environment.SetEnvironmentVariable("TEST_ENV_VAR_DB", null);
        }
    }

    [Fact]
    public void ConfigurationResolver_ShouldFallbackToMachineNameWhenComputerNameEnvVarIsMissing()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.SqlServer.Server = "%COMPUTERNAME%\\MSSQLSERVER2022";

        string? originalComputerName = Environment.GetEnvironmentVariable("COMPUTERNAME");
        Environment.SetEnvironmentVariable("COMPUTERNAME", null);

        try
        {
            // Act
            ConfigurationResolver.Resolve(options);

            // Assert
            Assert.Equal($"{Environment.MachineName}\\MSSQLSERVER2022", options.SqlServer.Server);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COMPUTERNAME", originalComputerName);
        }
    }
}
