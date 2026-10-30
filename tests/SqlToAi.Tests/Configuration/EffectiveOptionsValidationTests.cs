using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Configuration;

public sealed class EffectiveOptionsValidationTests
{
    public static TheoryData<string, string> InvalidSettings => new()
    {
        { "missing-server", "SqlToAi.Servers" },
        { "empty-address", "Server" },
        { "invalid-alias", "Server alias" },
        { "unqualified-grant", "SqlToAi.Databases.ReadWrite" },
        { "unknown-grant", "SqlToAi.Databases.ReadOnly" },
        { "unknown-anonymized-write-grant", "SqlToAi.Databases.ReadWriteAnonymized" },
        { "schema-count", "SqlToAi.SchemaBatch.MaxInputCount" },
        { "schema-bytes", "SqlToAi.SchemaBatch.MaxResponseBytes" },
        { "app-retention", "SqlToAi.Logging.AppLog.RetainedFileCount" },
        { "error-retention", "SqlToAi.Logging.ErrorLog.RetainedFileCount" },
        { "export-default-limit", "SqlToAi.QueryExecution.Export.DefaultRowLimit" },
        { "export-max-limit", "SqlToAi.QueryExecution.Export.MaxRowLimit" },
        { "explain-xml-bytes", "SqlToAi.ExplainQuery" },
        { "explain-operators", "SqlToAi.ExplainQuery" },
        { "explain-depth", "SqlToAi.ExplainQuery" },
        { "explain-response-bytes", "SqlToAi.ExplainQuery" }
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public void DirectOptions_RejectInvalidEffectiveSettings(string invalidSetting, string settingPath)
    {
        using var temp = TestTempDirectory.Create();
        var invalidConfiguration = CreateConfiguration(invalidSetting);
        var options = invalidConfiguration.GetSection("SqlToAi").Get<SqlToAiOptions>() ?? new();

        var exception = Assert.Throws<ConfigurationStartupException>(() =>
        {
            using var services = Program.BuildServiceProvider(CreateConfiguration(), options, temp.DirectoryPath);
        });

        AssertDiagnostic(exception, temp.DirectoryPath, settingPath);
    }

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public void BoundOptions_RejectInvalidEffectiveSettings(string invalidSetting, string settingPath)
    {
        using var temp = TestTempDirectory.Create();
        using var services = Program.BuildServiceProvider(CreateConfiguration(invalidSetting), ValidOptions(), temp.DirectoryPath);

        var exception = Assert.Throws<ConfigurationStartupException>(() =>
            services.GetRequiredService<IOptions<SqlToAiOptions>>().Value);

        AssertDiagnostic(exception, temp.DirectoryPath, settingPath);
    }

    [Fact]
    public void DirectOptions_RejectCaseDuplicateAliases()
    {
        using var temp = TestTempDirectory.Create();
        var options = ValidOptions();
        options.Servers.Add("PRIMARY", new NamedServerOptions { Server = "second-host" });

        var exception = Assert.Throws<ConfigurationStartupException>(() =>
        {
            using var services = Program.BuildServiceProvider(CreateConfiguration(), options, temp.DirectoryPath);
        });

        AssertDiagnostic(exception, temp.DirectoryPath, "Duplicate server alias");
    }

    [Fact]
    public void RegisteredPostConfiguration_RejectsCaseDuplicateAliases()
    {
        using var temp = TestTempDirectory.Create();
        using var services = Program.BuildServiceProvider(CreateConfiguration(), ValidOptions(), temp.DirectoryPath);
        var options = ValidOptions();
        options.Servers.Add("PRIMARY", new NamedServerOptions { Server = "second-host" });

        // Configuration merges aliases case-insensitively. Exercise the registered options
        // boundary directly for a duplicate dictionary supplied by another options source.
        var postConfiguration = Assert.Single(services.GetServices<IPostConfigureOptions<SqlToAiOptions>>());
        var exception = Assert.Throws<ConfigurationStartupException>(() =>
            postConfiguration.PostConfigure(Options.DefaultName, options));

        AssertDiagnostic(exception, temp.DirectoryPath, "Duplicate server alias");
    }

    [Fact]
    public void DirectAndBoundOptions_ValidateExpandedGrantsAndKeepResolvedProviderSql()
    {
        using var temp = TestTempDirectory.Create();
        const string sql = "SELECT 1 -- retained.sql";
        string sqlPath = temp.CreateFile("metadata.sql", sql);
        var configuration = CreateConfiguration();
        configuration["SqlToAi:Databases:ReadWrite:0"] = "PRIMARY/%COMPUTERNAME%";
        configuration["SqlToAi:Servers:primary:MetadataProvider:TableMetadataQuery"] = "metadata.sql";
        var direct = configuration.GetSection("SqlToAi").Get<SqlToAiOptions>()!;
        using var services = Program.BuildServiceProvider(configuration, direct, temp.DirectoryPath);
        var bound = services.GetRequiredService<IOptions<SqlToAiOptions>>().Value;
        File.Delete(sqlPath);

        foreach (var options in new[] { direct, bound })
        {
            ConfigurationResolver.Resolve(options, temp.DirectoryPath);
            ConfigurationResolver.Validate(options);
            string expectedDatabase = Environment.GetEnvironmentVariable("COMPUTERNAME") ?? Environment.MachineName;
            Assert.Equal($"PRIMARY/{expectedDatabase}", Assert.Single(options.Databases.ReadWrite));
            Assert.Equal(sql, options.Servers["primary"].MetadataProvider.TableMetadataQuery);
            Assert.Equal(temp.DirectoryPath, options.ResolvedRootDirectory);
        }
        Assert.Same(bound, services.GetRequiredService<IOptions<SqlToAiOptions>>().Value);
    }

    private static SqlToAiOptions ValidOptions() => CreateConfiguration().GetSection("SqlToAi").Get<SqlToAiOptions>()!;

    private static IConfigurationRoot CreateConfiguration(string? invalidSetting = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["SqlToAi:Servers:primary:Server"] = "test-host",
            ["SqlToAi:Servers:primary:Password"] = "credential-marker"
        };
        switch (invalidSetting)
        {
            case "missing-server": settings.Clear(); break;
            case "empty-address": settings["SqlToAi:Servers:primary:Server"] = " "; break;
            case "invalid-alias": settings["SqlToAi:Servers:bad.alias:Server"] = "host"; break;
            case "unqualified-grant": settings["SqlToAi:Databases:ReadWrite:0"] = "AppDb"; break;
            case "unknown-grant": settings["SqlToAi:Databases:ReadOnly:0"] = "unknown/AppDb"; break;
            case "unknown-anonymized-write-grant": settings["SqlToAi:Databases:ReadWriteAnonymized:0"] = "unknown/AppDb"; break;
            case "schema-count": settings["SqlToAi:SchemaBatch:MaxInputCount"] = "0"; break;
            case "schema-bytes": settings["SqlToAi:SchemaBatch:MaxResponseBytes"] = "-1"; break;
            case "app-retention": settings["SqlToAi:Logging:AppLog:RetainedFileCount"] = "0"; break;
            case "error-retention": settings["SqlToAi:Logging:ErrorLog:RetainedFileCount"] = "-1"; break;
            case "export-default-limit": settings["SqlToAi:QueryExecution:Export:DefaultRowLimit"] = "0"; break;
            case "export-max-limit": settings["SqlToAi:QueryExecution:Export:MaxRowLimit"] = "-1"; break;
            case "explain-xml-bytes": settings["SqlToAi:ExplainQuery:MaxShowplanXmlBytes"] = "0"; break;
            case "explain-operators": settings["SqlToAi:ExplainQuery:MaxOperators"] = "-1"; break;
            case "explain-depth": settings["SqlToAi:ExplainQuery:MaxDepth"] = "0"; break;
            case "explain-response-bytes": settings["SqlToAi:ExplainQuery:MaxResponseBytes"] = "-5"; break;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static void AssertDiagnostic(ConfigurationStartupException exception, string root, string settingPath)
    {
        Assert.Contains(settingPath, exception.Message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(root, "appsettings.json"), exception.Message, StringComparison.Ordinal);
        Assert.Contains("manually", exception.Message, StringComparison.Ordinal);
        Assert.Contains("restart", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("credential-marker", exception.ToString(), StringComparison.Ordinal);
    }
}
