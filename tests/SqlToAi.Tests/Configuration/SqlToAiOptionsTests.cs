using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.IO;
using SqlToAi.Tests.TestSupport;

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
        Assert.NotNull(options.Servers);
        Assert.Empty(options.Servers);
        Assert.NotNull(options.Databases);
        Assert.NotNull(options.Anonymizer);

        Assert.Empty(options.Databases.ReadWrite);
        Assert.Empty(options.Databases.ReadOnly);
        Assert.Empty(options.Databases.ReadOnlyAnonymized);
        Assert.Empty(options.Databases.SchemaOnly);
        Assert.True(options.Anonymizer.Enabled);
        Assert.Equal("ScramblePattern", options.Anonymizer.DefaultMode);
        Assert.Equal(10_485_760L, options.QueryExecution.MaxScriptFileSizeBytes);
        Assert.Equal(10, options.SchemaBatch.MaxInputCount);
        Assert.Equal(65_536, options.SchemaBatch.MaxResponseBytes);
        Assert.True(options.Logging.AppLog.Enabled);
        Assert.True(options.Logging.ErrorLog.Enabled);
        Assert.False(options.Logging.McpTrail.Enabled);

        var accessLevels = new AccessLevelProvider(Options.Create(options));
        Assert.Equal(AccessLevel.None,
            await accessLevels.GetTargetAccessLevelAsync(new DatabaseTargetContext("test", "DemoDB"), TestContext.Current.CancellationToken));
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
        Assert.True(templateOptions.Servers.ContainsKey("primary"));
        var primary = templateOptions.Servers["primary"];
        Assert.Equal(string.Empty, primary.Server);
        Assert.Null(primary.UserId);
        Assert.Null(primary.Password);
        Assert.False(primary.IntegratedSecurity);
        Assert.False(primary.TrustServerCertificate);
        Assert.False(primary.MetadataProvider.TrustServerCertificate);
        Assert.False(primary.AnonymizationRules.TrustServerCertificate);

        Assert.True(demoOptions.Servers.ContainsKey("test"));
        var demoServer = demoOptions.Servers["test"];
        Assert.False(demoServer.TrustServerCertificate);
        Assert.Equal(defaults.Logging.AppLog.Enabled, templateOptions.Logging.AppLog.Enabled);
        Assert.Equal(defaults.Logging.ErrorLog.Enabled, templateOptions.Logging.ErrorLog.Enabled);
        Assert.Equal(defaults.SchemaBatch.MaxInputCount, templateOptions.SchemaBatch.MaxInputCount);
        Assert.Equal(defaults.SchemaBatch.MaxResponseBytes, templateOptions.SchemaBatch.MaxResponseBytes);

        var safeAccess = new AccessLevelProvider(Options.Create(templateOptions));
        Assert.Equal(AccessLevel.None,
            await safeAccess.GetTargetAccessLevelAsync(new DatabaseTargetContext("primary", "DemoDB"), TestContext.Current.CancellationToken));
        Assert.Equal("Agent", demoServer.UserId);
        Assert.Equal("Agent!", demoServer.Password);
        Assert.Contains("test/DemoDB", demoOptions.Databases.ReadWrite);
        Assert.Equal(AccessLevel.ReadWrite,
            await new AccessLevelProvider(Options.Create(demoOptions))
                .GetTargetAccessLevelAsync(new DatabaseTargetContext("test", "DemoDB"), TestContext.Current.CancellationToken));
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
        Assert.True(options.Logging.AppLog.Enabled);
        Assert.True(options.Logging.ErrorLog.Enabled);
    }

    [Fact]
    public void SqlToAiOptions_ShouldBindFromConfiguration()
    {
        // Arrange
        var appSettingsJson = """
        {
          "Servers": {
            "my-server": {
              "Server": "localhost",
              "IntegratedSecurity": true,
              "TrustServerCertificate": true,
              "MetadataProvider": {
                "TrustServerCertificate": false
              },
              "AnonymizationRules": {
                "TrustServerCertificate": true
              }
            }
          },
          "Databases": {
            "ReadWrite": ["my-server/DemoDB"],
            "ReadOnly": ["my-server/ArchiveDB"],
            "CacheTtlSeconds": 100
          },
          "Anonymizer": {
            "Enabled": true,
            "DefaultMode": "Hash"
          },
          "QueryExecution": {
            "MaxScriptFileSizeBytes": 2048
          },
          "SchemaBatch": {
            "MaxInputCount": 3,
            "MaxResponseBytes": 4096
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
        Assert.True(options.Servers.ContainsKey("my-server"));
        var server = options.Servers["my-server"];
        Assert.Equal("localhost", server.Server);
        Assert.True(server.IntegratedSecurity);
        Assert.True(server.TrustServerCertificate);
        Assert.False(server.MetadataProvider.TrustServerCertificate);
        Assert.True(server.AnonymizationRules.TrustServerCertificate);

        var rw = Assert.Single(options.Databases.ReadWrite);
        Assert.Equal("my-server/DemoDB", rw);
        var ro = Assert.Single(options.Databases.ReadOnly);
        Assert.Equal("my-server/ArchiveDB", ro);
        Assert.Equal(100, options.Databases.CacheTtlSeconds);

        Assert.True(options.Anonymizer.Enabled);
        Assert.Equal("Hash", options.Anonymizer.DefaultMode);
        Assert.Equal(2048L, options.QueryExecution.MaxScriptFileSizeBytes);
        Assert.Equal(3, options.SchemaBatch.MaxInputCount);
        Assert.Equal(4096, options.SchemaBatch.MaxResponseBytes);
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
        var server = new NamedServerOptions
        {
            Server = "localhost",
            MetadataProvider = new MetadataProviderOptions
            {
                TableMetadataQuery = "SELECT * FROM tables",
                ColumnMetadataQuery = "SELECT * FROM columns"
            }
        };
        options.Servers["primary"] = server;

        // Act
        ConfigurationResolver.Resolve(options);

        // Assert
        Assert.Equal("SELECT * FROM tables", server.MetadataProvider.TableMetadataQuery);
        Assert.Equal("SELECT * FROM columns", server.MetadataProvider.ColumnMetadataQuery);
    }

    [Fact]
    public void ConfigurationResolver_ShouldResolveRelativeAndAbsolutePaths()
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.CreateSubdirectory("user-root");
        temp.CreateFile("user-root/relative.sql", "SELECT 'Relative';");
        string absolutePath = temp.CreateFile("external/absolute.sql", "SELECT 'Absolute';");
        var options = new SqlToAiOptions();
        var server = new NamedServerOptions
        {
            Server = "localhost",
            MetadataProvider = new MetadataProviderOptions
            {
                TableMetadataQuery = "relative.sql",
                ColumnMetadataQuery = absolutePath
            }
        };
        options.Servers["primary"] = server;

        ConfigurationResolver.Resolve(options, root);
        Assert.Equal("SELECT 'Relative';", server.MetadataProvider.TableMetadataQuery);
        Assert.Equal("SELECT 'Absolute';", server.MetadataProvider.ColumnMetadataQuery);
    }

    [Fact]
    public void ConfigurationResolver_ShouldThrowFileNotFoundException_WhenFileDoesNotExist()
    {
        using var temp = TestTempDirectory.Create();
        // Arrange
        var options = new SqlToAiOptions();
        options.Servers["primary"] = new NamedServerOptions
        {
            Server = "localhost",
            MetadataProvider = new MetadataProviderOptions
            {
                TableMetadataQuery = "non-existent-script.sql"
            }
        };

        // Act & Assert
        var exception = Assert.Throws<FileNotFoundException>(() => ConfigurationResolver.Resolve(options, temp.DirectoryPath));
        Assert.Equal(temp.GetPath("non-existent-script.sql"), exception.FileName);
    }

    [Fact]
    public void ConfigurationResolver_ShouldExpandEnvironmentVariables()
    {
        // Arrange
        var options = new SqlToAiOptions();
        string? originalServer = Environment.GetEnvironmentVariable("TEST_ENV_VAR_SERVER");
        string? originalDatabase = Environment.GetEnvironmentVariable("TEST_ENV_VAR_DB");

        try
        {
            Environment.SetEnvironmentVariable("TEST_ENV_VAR_SERVER", "EnvServerName");
            Environment.SetEnvironmentVariable("TEST_ENV_VAR_DB", "EnvDbName");
            var server = new NamedServerOptions
            {
                Server = "%TEST_ENV_VAR_SERVER%\\MSSQLSERVER"
            };
            options.Servers["primary"] = server;
            options.Databases.ReadWrite = new List<string> { "primary/%TEST_ENV_VAR_DB%_RW" };

            // Act
            ConfigurationResolver.Resolve(options);

            // Assert
            Assert.Equal("EnvServerName\\MSSQLSERVER", server.Server);
            var rw = Assert.Single(options.Databases.ReadWrite);
            Assert.Equal("primary/EnvDbName_RW", rw);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_ENV_VAR_SERVER", originalServer);
            Environment.SetEnvironmentVariable("TEST_ENV_VAR_DB", originalDatabase);
        }
    }

    [Fact]
    public void ConfigurationResolver_ShouldFallbackToMachineNameWhenComputerNameEnvVarIsMissing()
    {
        // Arrange
        var options = new SqlToAiOptions();
        var server = new NamedServerOptions
        {
            Server = "%COMPUTERNAME%\\MSSQLSERVER2022"
        };
        options.Servers["primary"] = server;

        string? originalComputerName = Environment.GetEnvironmentVariable("COMPUTERNAME");
        Environment.SetEnvironmentVariable("COMPUTERNAME", null);

        try
        {
            // Act
            ConfigurationResolver.Resolve(options);

            // Assert
            Assert.Equal($"{Environment.MachineName}\\MSSQLSERVER2022", server.Server);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COMPUTERNAME", originalComputerName);
        }
    }
}
