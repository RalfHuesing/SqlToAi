using System.IO.Pipelines;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RalfHuesing.Mcp.Observability;
using SqlToAi.Configuration;
using SqlToAi.Mcp;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Configuration;

[SupportedOSPlatform("windows")]
public sealed class UserPathTests
{
    private static Dictionary<string, string?> DefaultPaths => new()
    {
        ["SqlToAi__Logging__Directory"] = null,
        ["SqlToAi__Observability__LogDirectory"] = null,
        ["SqlToAi__Observability__Enabled"] = null
    };

    [Fact]
    public async Task LoadedAndDirectOptions_UseOneRootWithoutReloadingResolvedSql()
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.CreateSubdirectory("user");
        const string sql = "SELECT 1 -- source.sql";
        temp.CreateFile("user/metadata.sql", sql);
        temp.CreateFile("user/appsettings.json", """{"SqlToAi":{"MetadataProvider":{"TableMetadataQuery":"metadata.sql"}}}""");
        using var startup = UserConfiguration.Load(root);
        // Re-register the already resolved instance after removing its source. The direct
        // options must retain SQL; DI binds a separate instance and reads the source once.
        await using (var services = Program.BuildServiceProvider(startup.Configuration, startup.Options, root))
        {
            var bound = services.GetRequiredService<IOptions<SqlToAiOptions>>().Value;
            File.Delete(temp.GetPath("user/metadata.sql"));
            ConfigurationResolver.Resolve(startup.Options, root);
            ConfigurationResolver.Resolve(bound, root);
            Assert.Equal(sql, startup.Options.MetadataProvider.TableMetadataQuery);
            Assert.Equal(sql, bound.MetadataProvider.TableMetadataQuery);
            Assert.Equal(temp.GetPath("user/log"), bound.Logging.GetAbsoluteRoot());
            Assert.Equal(bound.Logging.Directory, startup.Options.Logging.Directory);
            Assert.Equal(temp.GetPath("user/log/observability"), bound.Observability.LogDirectory);
            Assert.Equal(bound.Observability.LogDirectory, startup.Options.Observability.LogDirectory);
        }
        var direct = new SqlToAiOptions();
        direct.Observability.Enabled = false;
        var configuration = new ConfigurationBuilder().Build();
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
        temp.CreateFile("user/appsettings.json", """{"SqlToAi":{"MetadataProvider":{"TableMetadataQuery":"metadata.sql"}}}""");
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
        temp.CreateFile("user/appsettings.json", "{}");
        var environment = DefaultPaths;
        environment["SQLTOAI_TEST_METADATA"] = external;
        environment["SQLTOAI_TEST_LOGS"] = temp.GetPath("absolute-logs");
        environment["SQLTOAI_TEST_OBSERVABILITY"] = "relative-observability";
        environment["SqlToAi__MetadataProvider__TableMetadataQuery"] = "%SQLTOAI_TEST_METADATA%";
        environment["SqlToAi__Logging__Directory"] = "%SQLTOAI_TEST_LOGS%";
        environment["SqlToAi__Observability__LogDirectory"] = "%SQLTOAI_TEST_OBSERVABILITY%";
        var result = await UserConfigurationTests.RunAsync(temp, root, "inspect", environment);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var document = JsonDocument.Parse(result.Output);
        foreach (string property in new[] { "DirectLogRoot", "BoundLogRoot" })
            Assert.Equal(temp.GetPath("absolute-logs"), document.RootElement.GetProperty(property).GetString());
        foreach (string property in new[] { "DirectObservabilityRoot", "BoundObservabilityRoot" })
            Assert.Equal(temp.GetPath("user/relative-observability"), document.RootElement.GetProperty(property).GetString());
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
        temp.CreateFile("user/appsettings.json", "{}");
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

    [Theory]
    [InlineData("omitted")]
    [InlineData("relative")]
    [InlineData("absolute")]
    [InlineData("null")]
    public async Task ActualMcpToolAndFeedbackRecords_UseResolvedProductionDestination(string mode)
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.CreateSubdirectory("user");
        var values = new Dictionary<string, string?>();
        if (mode != "omitted") values["SqlToAi:Observability:EnableToolCallLogging"] = "true";
        if (mode == "relative") values["SqlToAi:Observability:LogDirectory"] = "custom-observability";
        if (mode == "absolute") values["SqlToAi:Observability:LogDirectory"] = temp.GetPath("external-observability");
        if (mode == "null") values["SqlToAi:Observability:LogDirectory"] = null;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = configuration.GetSection("SqlToAi").Get<SqlToAiOptions>() ?? new();
        await using var services = Program.BuildServiceProvider(configuration, options, root);
        var serverOptions = services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var outgoing = new Pipe();
        var incoming = new Pipe();
        await using var serverTransport = new StreamServerTransport(incoming.Reader.AsStream(), outgoing.Writer.AsStream(), "path-test");
        await using var server = McpServer.Create(serverTransport, serverOptions, serviceProvider: services);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var running = server.RunAsync(deadline.Token);
        await using (var client = await McpClient.CreateAsync(
            new StreamClientTransport(incoming.Writer.AsStream(), outgoing.Reader.AsStream()), cancellationToken: deadline.Token))
        {
            var result = await client.CallToolAsync("report_observability_feedback", new Dictionary<string, object?>
            {
                ["feedbackType"] = "comment", ["title"] = "Path test", ["description"] = "Temporary data only"
            }, cancellationToken: deadline.Token);
            Assert.False(result.IsError ?? false);
            var observation = services.GetRequiredService<IMcpObservabilityService>();
            await observation.FlushAsync(deadline.Token);
            string expectedRoot = mode switch
            {
                "relative" => temp.GetPath("user/custom-observability"),
                "absolute" => temp.GetPath("external-observability"),
                _ => temp.GetPath("user/log/observability")
            };
            string log = Assert.Single(Directory.GetFiles(expectedRoot, "*.jsonl", SearchOption.AllDirectories));
            Assert.Equal(log, observation.CurrentLogFilePath);
            await using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            string contents = await reader.ReadToEndAsync(deadline.Token);
            Assert.Contains("\"recordType\":\"feedback\"", contents, StringComparison.Ordinal);
            Assert.Equal(mode != "omitted", contents.Contains("\"recordType\":\"tool_call\"", StringComparison.Ordinal));
            Assert.False(Directory.Exists(temp.GetPath("log")));
        }
        await deadline.CancelAsync();
        try { await running; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task SqlInputAndSchemaOutput_KeepCwdSemantics()
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.CreateSubdirectory("user");
        temp.CreateFile("user/appsettings.json", "{}");
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
        temp.CreateFile("user/appsettings.json", """{"SqlToAi":{"MetadataProvider":{"TableMetadataQuery":"missing.sql"}}}""");
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
        string file = temp.CreateFile("user/appsettings.json", """{"SqlToAi":{"Logging":{"Directory":"%SQLTOAI_TEST_RELATIVE_LOGS%"}}}""");
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
        temp.CreateFile("user/appsettings.json", """{"SqlToAi":{"Logging":{"AppLog":{"Enabled":false},"ErrorLog":{"Enabled":false}},"Observability":{"Enabled":false}}}""");
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
    [InlineData("observability")]
    public async Task FileInsteadOfLogDirectory_FailsWithPathOnStderr(string sink)
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.CreateSubdirectory("user");
        temp.CreateFile("user/appsettings.json", "{}");
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
        temp.CreateFile("user/appsettings.json", "{}");
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
    public async Task ReadOnlyInstallation_UsesWritableUserRootAndLeavesLegacyFilesUntouched()
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.CreateSubdirectory("user");
        string install = temp.CreateSubdirectory("installation");
        foreach (string file in Directory.EnumerateFiles(AppContext.BaseDirectory))
            File.Copy(file, Path.Combine(install, Path.GetFileName(file)));
        string legacy = temp.CreateFile("installation/appsettings.json", "deliberately invalid legacy settings");
        temp.CreateFile("user/appsettings.json", "{}");
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
        Assert.Equal("deliberately invalid legacy settings", await File.ReadAllTextAsync(legacy, TestContext.Current.CancellationToken));
        Assert.NotEmpty(Directory.GetFiles(temp.GetPath("user/log"), "*.log", SearchOption.AllDirectories));
    }

    [Fact]
    public void Retention_UsesResolvedUserLogRootAndPreservesLegacyDirectories()
    {
        using var temp = TestTempDirectory.Create();
        string oldDay = DateTime.UtcNow.AddDays(-100).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        string current = temp.CreateSubdirectory($"user/custom/mcp/{oldDay}");
        string legacy = temp.CreateFile($"installation/log/mcp/{oldDay}/old.jsonl", "legacy");
        var options = new SqlToAiOptions();
        options.Logging.Directory = "custom";
        options.Logging.McpTrail.Enabled = true;
        ConfigurationResolver.Resolve(options, temp.GetPath("user"));
        new LogRetentionService(options.Logging, NullLogger<LogRetentionService>.Instance).Run();
        Assert.False(Directory.Exists(current));
        Assert.Equal("legacy", File.ReadAllText(legacy));
        Assert.Equal(temp.GetPath("user/custom/mcp"), options.Logging.McpTrail.Directory);
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
