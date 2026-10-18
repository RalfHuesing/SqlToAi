using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Configuration;

[SupportedOSPlatform("windows")]
public sealed class UserPathTests
{
    private const string MinimalSettings = """{"SqlToAi":{"Servers":{"test":{"Server":"test-host"}}}}""";

    private static Dictionary<string, string?> DefaultPaths => new()
    {
        ["SqlToAi__Logging__Directory"] = null
    };

    [Fact]
    public async Task LoadedAndDirectOptions_UseOneRootWithoutReloadingResolvedSql()
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.CreateSubdirectory("user");
        const string sql = "SELECT 1 -- source.sql";
        temp.CreateFile("user/metadata.sql", sql);
        temp.CreateFile("user/appsettings.json", """{"SqlToAi":{"Servers":{"primary":{"Server":"test-host","MetadataProvider":{"TableMetadataQuery":"metadata.sql"}}}}}""");
        using var startup = UserConfiguration.Load(root);
        // Re-register the already resolved instance after removing its source. The direct
        // options must retain SQL; DI binds a separate instance and reads the source once.
        await using (var services = Program.BuildServiceProvider(startup.Configuration, startup.Options, root))
        {
            var bound = services.GetRequiredService<IOptions<SqlToAiOptions>>().Value;
            File.Delete(temp.GetPath("user/metadata.sql"));
            ConfigurationResolver.Resolve(startup.Options, root);
            ConfigurationResolver.Resolve(bound, root);
            Assert.Equal(sql, startup.Options.Servers["primary"].MetadataProvider.TableMetadataQuery);
            Assert.Equal(sql, bound.Servers["primary"].MetadataProvider.TableMetadataQuery);
            Assert.Equal(temp.GetPath("user/log"), bound.Logging.GetAbsoluteRoot());
            Assert.Equal(bound.Logging.Directory, startup.Options.Logging.Directory);
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SqlToAi:Servers:primary:Server"] = "test-host"
        }).Build();
        var direct = configuration.GetSection("SqlToAi").Get<SqlToAiOptions>()!;
        await using var directServices = Program.BuildServiceProvider(configuration, direct, root);
        Assert.Equal(temp.GetPath("user/log"), direct.Logging.GetAbsoluteRoot());
        Assert.Equal(direct.Logging.Directory, directServices.GetRequiredService<IOptions<SqlToAiOptions>>().Value.Logging.Directory);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("server")]
    [InlineData("query")]
    [InlineData("export")]
    [InlineData("exploration")]
    public async Task EveryEntryPoint_LoadsMetadataFromUserRootAtArbitraryCwd(string entry)
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.CreateSubdirectory("user");
        temp.CreateFile("metadata.sql", "wrong CWD contents");
        temp.CreateFile("user/metadata.sql", "SELECT 1 -- source.sql");
        temp.CreateFile("user/appsettings.json", """{"SqlToAi":{"Servers":{"primary":{"Server":"test-host","MetadataProvider":{"TableMetadataQuery":"metadata.sql"}}}}}""");
        var result = await UserConfigurationTests.RunAsync(temp, root, entry, DefaultPaths);
        Assert.DoesNotContain("configuration", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not found", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Startup failed", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(entry == "export" ? 1 : 0, result.ExitCode);
        Assert.False(Directory.Exists(temp.GetPath("log")));
        Assert.True(Directory.Exists(temp.GetPath("user/log/app")) || entry == "exploration");
    }

    [Fact]
    public async Task EnvironmentPaths_AreExpandedBeforeRootingAndAbsolutePathsRemainAbsolute()
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.CreateSubdirectory("user");
        string external = temp.CreateFile("external/query.sql", "SELECT 'external'");
        temp.CreateFile("user/appsettings.json", MinimalSettings);
        var environment = DefaultPaths;
        environment["SQLTOAI_TEST_METADATA"] = external;
        environment["SQLTOAI_TEST_LOGS"] = temp.GetPath("absolute-logs");
        environment["SqlToAi__Servers__test__MetadataProvider__TableMetadataQuery"] = "%SQLTOAI_TEST_METADATA%";
        environment["SqlToAi__Logging__Directory"] = "%SQLTOAI_TEST_LOGS%";
        var result = await UserConfigurationTests.RunAsync(temp, root, "inspect", environment);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var document = JsonDocument.Parse(result.Output);
        foreach (string property in new[] { "DirectLogRoot", "BoundLogRoot" })
            Assert.Equal(temp.GetPath("absolute-logs"), document.RootElement.GetProperty(property).GetString());
        Assert.Equal("SELECT 'external'", document.RootElement.GetProperty("BoundMetadata").GetString());
    }

    [Theory]
    [InlineData("omitted")]
    [InlineData("relative")]
    [InlineData("absolute")]
    public async Task ActualApplicationAndErrorLogs_UseDefaultOrExplicitDestination(string mode)
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.CreateSubdirectory("user");
        temp.CreateFile("user/appsettings.json", MinimalSettings);
        var environment = DefaultPaths;
        if (mode == "absolute") environment["SqlToAi__Logging__Directory"] = temp.GetPath("external-logs");
        if (mode == "relative") environment["SqlToAi__Logging__Directory"] = "custom-logs";
        var result = await UserConfigurationTests.RunAsync(temp, root, "log-probe", environment);
        Assert.Equal(0, result.ExitCode);
        string logs = mode switch
        {
            "absolute" => temp.GetPath("external-logs"),
            "relative" => temp.GetPath("user/custom-logs"),
            _ => temp.GetPath("user/log")
        };
        string application = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(Path.Combine(logs, "app"))), TestContext.Current.CancellationToken);
        string error = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(Path.Combine(logs, "error"))), TestContext.Current.CancellationToken);
        Assert.Contains("Application path probe", application, StringComparison.Ordinal);
        Assert.Contains("Error path probe", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Application path probe", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(temp.GetPath("log")));
    }

    [Fact]
    public async Task SqlInputAndSchemaOutput_KeepCwdSemantics()
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.CreateSubdirectory("user");
        temp.CreateFile("user/appsettings.json", MinimalSettings);
        temp.CreateFile("user/input.sql", "wrong root");
        temp.CreateFile("input.sql", "SELECT 'CWD'");
        var result = await UserConfigurationTests.RunAsync(temp, root, "cwd-probe", DefaultPaths);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("SELECT 'CWD'", result.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(temp.GetPath("export/README.md")));
        Assert.False(Directory.Exists(temp.GetPath("user/export")));
    }

    [Fact]
    public async Task MissingMetadataFile_ReportsResolvedPathWithoutCreatingOutputs()
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.CreateSubdirectory("user");
        temp.CreateFile("user/appsettings.json", """{"SqlToAi":{"Servers":{"primary":{"Server":"test-host","MetadataProvider":{"TableMetadataQuery":"missing.sql"}}}}}""");
        var result = await UserConfigurationTests.RunAsync(temp, root, "server", DefaultPaths);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(temp.GetPath("user/missing.sql"), result.Error, StringComparison.Ordinal);
        Assert.Contains("configured SQL file", result.Error, StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(root));
        Assert.Empty(Directory.GetDirectories(root));
    }

    [Fact]
    public async Task ActualLogs_ExpandEnvironmentPathsAtRuntimeOnly()
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.CreateSubdirectory("user");
        string file = temp.CreateFile("user/appsettings.json", """{"SqlToAi":{"Servers":{"test":{"Server":"test-host"}},"Logging":{"Directory":"%SQLTOAI_TEST_RELATIVE_LOGS%"}}}""");
        var environment = DefaultPaths;
        environment["SQLTOAI_TEST_RELATIVE_LOGS"] = "expanded-logs";
        var result = await UserConfigurationTests.RunAsync(temp, root, "log-probe", environment);
        Assert.Equal(0, result.ExitCode);
        Assert.NotEmpty(Directory.GetFiles(temp.GetPath("user/expanded-logs"), "*.log", SearchOption.AllDirectories));
        Assert.Contains("%SQLTOAI_TEST_RELATIVE_LOGS%", await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnwritableUserRoot_FailsDirectlyEvenWithAllFileLoggingDisabled()
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.CreateSubdirectory("user");
        temp.CreateFile("user/appsettings.json", """{"SqlToAi":{"Servers":{"test":{"Server":"test-host"}},"Logging":{"AppLog":{"Enabled":false},"ErrorLog":{"Enabled":false}}}}""");
        using (DenyWrites(root))
        {
            var result = await UserConfigurationTests.RunAsync(temp, root, "server", DefaultPaths);
            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains(root, result.Error, StringComparison.Ordinal);
            Assert.Contains("not writable", result.Error, StringComparison.Ordinal);
            Assert.Contains("permissions", result.Error, StringComparison.Ordinal);
        }
        Assert.Single(Directory.GetFiles(root));
    }

    [Theory]
    [InlineData("app")]
    [InlineData("error")]
    public async Task FileInsteadOfLogDirectory_FailsWithPathOnStderr(string sink)
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.CreateSubdirectory("user");
        temp.CreateFile("user/appsettings.json", MinimalSettings);
        string blocked = temp.CreateFile($"user/log/{sink}", "preserve");
        var result = await UserConfigurationTests.RunAsync(temp, root, "server", DefaultPaths);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(blocked, result.Error, StringComparison.Ordinal);
        Assert.Contains("not writable", result.Error, StringComparison.Ordinal);
        Assert.Equal("preserve", await File.ReadAllTextAsync(blocked, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LockedSerilogFile_ReportsLazySinkFailureOnStderrAndKeepsSequenceFallback()
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.CreateSubdirectory("user");
        temp.CreateFile("user/appsettings.json", MinimalSettings);
        string file = temp.CreateFile("user/log/app/app-" + DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + ".log", "preserve");
        await using var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = await UserConfigurationTests.RunAsync(temp, root, "log-probe", DefaultPaths);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Logging failure", result.Error, StringComparison.Ordinal);
        Assert.Contains(file, result.Error, StringComparison.Ordinal);
        Assert.Equal(2, Directory.GetFiles(temp.GetPath("user/log/app")).Length);
    }

    [Fact]
    public async Task ReadOnlyInstallation_UsesWritableUserRootAndLeavesInstallationFilesUntouched()
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.CreateSubdirectory("user");
        string install = temp.CreateSubdirectory("installation");
        foreach (string file in Directory.EnumerateFiles(AppContext.BaseDirectory))
            File.Copy(file, Path.Combine(install, Path.GetFileName(file)));
        string installSettings = temp.CreateFile("installation/appsettings.json", "deliberately invalid template settings");
        temp.CreateFile("user/appsettings.json", MinimalSettings);
        using (DenyWrites(install))
        {
            var result = await UserConfigurationTests.RunAsync(temp, root, "query-help", DefaultPaths, install);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("sql_list_databases", result.Output, StringComparison.Ordinal);
            Assert.Empty(result.Error);
            Assert.False(Directory.Exists(Path.Combine(install, "log")));
            var logResult = await UserConfigurationTests.RunAsync(temp, root, "log-probe", DefaultPaths, install);
            Assert.Equal(0, logResult.ExitCode);
        }
        Assert.Equal("deliberately invalid template settings", await File.ReadAllTextAsync(installSettings, TestContext.Current.CancellationToken));
        Assert.NotEmpty(Directory.GetFiles(temp.GetPath("user/log"), "*.log", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task PackageReplacement_PreservesUserSettingsAndExistingLogsAtUnrelatedCwd()
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.CreateSubdirectory("user");
        string install = temp.CreateSubdirectory("installation");
        string settings = temp.CreateFile("user/appsettings.json",
            """{ "SqlToAi": { "Servers": { "test": { "Server": "manual-user-server" } } }, "UnknownUserKey": [1, 2] }""");
        string existingLog = temp.CreateFile("user/log/app/existing.log", "existing diagnostic bytes\r\n");
        string installLog = temp.CreateFile("installation/log/install.log", "installation log bytes\r\n");
        byte[] settingsBefore = await File.ReadAllBytesAsync(settings, TestContext.Current.CancellationToken);
        byte[] logBefore = await File.ReadAllBytesAsync(existingLog, TestContext.Current.CancellationToken);
        for (int replacement = 0; replacement < 2; replacement++)
        {
            foreach (string file in Directory.EnumerateFiles(AppContext.BaseDirectory))
                File.Copy(file, Path.Combine(install, Path.GetFileName(file)), overwrite: true);
            string installSettings = temp.CreateFile("installation/appsettings.json", "invalid installation template");
            var result = await UserConfigurationTests.RunAsync(temp, root, "query-help", DefaultPaths, install);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("sql_list_databases", result.Output, StringComparison.Ordinal);
            Assert.Empty(result.Error);
            Assert.Equal(settingsBefore, await File.ReadAllBytesAsync(settings, TestContext.Current.CancellationToken));
            Assert.Equal(logBefore, await File.ReadAllBytesAsync(existingLog, TestContext.Current.CancellationToken));
            Assert.Equal("invalid installation template", await File.ReadAllTextAsync(installSettings, TestContext.Current.CancellationToken));
            Assert.Equal("installation log bytes\r\n", await File.ReadAllTextAsync(installLog, TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.bak", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*backup*", SearchOption.AllDirectories));
        }
    }

    private static IDisposable DenyWrites(string path) => new WriteDenial(path);

    private sealed class WriteDenial : IDisposable
    {
        private readonly DirectoryInfo _directory;
        private readonly DirectorySecurity _original;

        internal WriteDenial(string path)
        {
            _directory = new DirectoryInfo(path);
            _original = _directory.GetAccessControl();
            var restricted = _directory.GetAccessControl();
            using var identity = WindowsIdentity.GetCurrent();
            restricted.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.Write,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Deny));
            _directory.SetAccessControl(restricted);
        }

        public void Dispose() => _directory.SetAccessControl(_original);
    }
}
