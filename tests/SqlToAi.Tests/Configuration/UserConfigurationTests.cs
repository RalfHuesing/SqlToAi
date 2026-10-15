using System.Diagnostics;
using System.Text.Json;
using SqlToAi.Configuration;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Configuration;

public sealed class UserConfigurationTests
{
    public static TheoryData<string, string> MissingFileCases => new(
        from entry in new[] { "default", "server", "query", "export", "exploration" }
        from setup in new[] { "empty", "environment", "environment-json", "legacy" }
        select (entry, setup));

    [Fact]
    public void ResolveRoot_UsesWindowsLocalApplicationDataChild()
    {
        using var temp = TestTempDirectory.Create();
        Assert.Equal(temp.GetPath("SqlToAi"), UserConfiguration.ResolveRoot(temp.DirectoryPath));
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlToAi"),
            UserConfiguration.ResolveRoot());
    }

    [Theory]
    [MemberData(nameof(MissingFileCases))]
    public async Task MissingBaseFile_FailsEveryEntryPointWithoutOutputOrWrites(string entry, string setup)
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.GetPath("user-root");
        Dictionary<string, string?> environment = [];
        if (setup == "environment") environment["SqlToAi__SqlServer__Server"] = "environment-only";
        if (setup == "environment-json") temp.CreateFile("user-root/appsettings.StartupTest.json", "{}");
        string legacyPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        Assert.True(File.Exists(legacyPath)); // Build output contains the executable-adjacent legacy template.
        byte[] legacyBytes = await File.ReadAllBytesAsync(legacyPath, TestContext.Current.CancellationToken);

        var result = await RunAsync(temp, root, entry, environment);

        AssertFailure(result, root);
        Assert.Contains("missing", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(root, "appsettings.json")));
        Assert.False(Directory.Exists(temp.GetPath("exports")));
        Assert.False(Directory.Exists(temp.GetPath("temp/exploration")));
        Assert.Equal(legacyBytes, await File.ReadAllBytesAsync(legacyPath, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFiles(temp.DirectoryPath, "*.bak", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(temp.DirectoryPath, "*.log", SearchOption.AllDirectories));
        if (setup != "environment-json") Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("exploration")]
    public async Task InvalidJson_FailsBeforeTransportOrArtifacts(string entry)
    {
        using var temp = TestTempDirectory.Create();
        string file = temp.CreateFile("user-root/appsettings.json", "{ invalid-json");
        var result = await RunAsync(temp, Path.GetDirectoryName(file)!, entry);
        AssertFailure(result, Path.GetDirectoryName(file)!);
        Assert.Contains("invalid", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("{ invalid-json", await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(temp.GetPath("temp/exploration")));
    }

    [Fact]
    public async Task UnreadableFile_FailsWithUsefulDirectDiagnostic()
    {
        using var temp = TestTempDirectory.Create();
        string file = temp.CreateFile("user-root/appsettings.json", "{}");
        await using var exclusiveLock = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = await RunAsync(temp, Path.GetDirectoryName(file)!, "server");
        AssertFailure(result, Path.GetDirectoryName(file)!);
        Assert.Contains("could not be read", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExistingFile_PreservesUnknownKeysDuringRepeatedAndConcurrentLoads()
    {
        using var temp = TestTempDirectory.Create();
        const string json = """{ "SqlToAi": { "SqlServer": { "Server": "manual" } }, "UnknownUserKey": [1, 2, 3] }""";
        string file = temp.CreateFile("user-root/appsettings.json", json);
        string root = Path.GetDirectoryName(file)!;
        byte[] before = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
        {
            using var startup = UserConfiguration.Load(root);
            Assert.Equal("manual", startup.Options.SqlServer.Server);
            Assert.Equal("3", startup.Configuration["UnknownUserKey:2"]);
        }, TestContext.Current.CancellationToken)));
        using (var startup = UserConfiguration.Load(root)) Assert.Equal(root, startup.RootDirectory);
        Assert.Equal(before, await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken));
        Assert.Single(Directory.EnumerateFiles(root));
    }

    [Fact]
    public async Task ExistingFile_UsesEnvironmentPrecedenceThroughServiceBindingWithoutPersistingSecrets()
    {
        using var temp = TestTempDirectory.Create();
        string file = temp.CreateFile("user-root/appsettings.json", """
            { "StartupProbe": "base", "SqlToAi": {
              "SqlServer": { "Server": "base-server" },
              "Observability": { "Enabled": false }
            } }
            """);
        temp.CreateFile("user-root/appsettings.StartupTest.json", """{"StartupProbe":"environment-file","SqlToAi":{"SqlServer":{"Server":"environment-file-server"}}}""");
        byte[] before = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);
        Dictionary<string, string?> environment = new()
        {
            ["SqlToAi__SqlServer__Server"] = "runtime-server",
            ["SqlToAi__SqlServer__Password"] = "runtime-test-secret"
        };
        var result = await RunAsync(temp, Path.GetDirectoryName(file)!, "inspect", environment);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var output = JsonDocument.Parse(result.Output);
        Assert.Equal("runtime-server", output.RootElement.GetProperty("Server").GetString());
        Assert.Equal("runtime-server", output.RootElement.GetProperty("BoundServer").GetString());
        Assert.Equal("runtime-test-secret", output.RootElement.GetProperty("Password").GetString());
        Assert.Equal("environment-file", output.RootElement.GetProperty("Probe").GetString());
        Assert.Equal(before, await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken));
        Assert.Equal(2, Directory.EnumerateFiles(Path.GetDirectoryName(file)!).Count());
    }

    [Fact]
    public async Task ExistingFile_ConcurrentCliStartsPreserveBytesAndUseOnlyIsolatedLogs()
    {
        using var temp = TestTempDirectory.Create();
        string file = temp.CreateFile("user-root/appsettings.json", """{ "UnknownUserKey": "preserve me" }""");
        byte[] before = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);
        string root = Path.GetDirectoryName(file)!;
        var results = await Task.WhenAll(RunAsync(temp, root, "query-help"), RunAsync(temp, root, "query-help"));
        foreach (var result in results)
        {
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("sql_list_databases", result.Output, StringComparison.Ordinal);
            Assert.Empty(result.Error);
        }
        Assert.Equal(before, await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken));
        Assert.Single(Directory.EnumerateFiles(root));
        Assert.Empty(Directory.EnumerateFiles(temp.DirectoryPath, "*.bak", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ExplorationListAndUsage_DoNotRequireSettings()
    {
        using var temp = TestTempDirectory.Create();
        var list = await RunAsync(temp, temp.GetPath("missing"), "list");
        Assert.Equal(0, list.ExitCode);
        Assert.Contains("ExploreSqlTools", list.Output, StringComparison.Ordinal);
        Assert.Empty(list.Error);
        var usage = await RunAsync(temp, temp.GetPath("missing"), "usage");
        Assert.Equal(2, usage.ExitCode);
        Assert.Empty(usage.Output);
        Assert.Contains("Usage:", usage.Error, StringComparison.Ordinal);
    }

    private static void AssertFailure(ProcessResult result, string root)
    {
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(Path.Combine(root, "appsettings.json"), result.Error, StringComparison.Ordinal);
        Assert.Contains("manually", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("template", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restart", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ProcessResult> RunAsync(TestTempDirectory temp, string root, string entry,
        Dictionary<string, string?>? environment = null)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = temp.DirectoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("--runtimeconfig");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "SqlToAi.Tests.runtimeconfig.json"));
        start.ArgumentList.Add("--depsfile");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "SqlToAi.Tests.deps.json"));
        start.ArgumentList.Add(typeof(StartupHarness.StartupHarness).Assembly.Location);
        start.ArgumentList.Add(root);
        start.ArgumentList.Add(entry is "exploration" or "list" or "usage" ? "exploration" : entry);
        string[] arguments = entry switch
        {
            "server" => ["server"],
            "query" => ["query", "sql_list_databases"],
            "query-help" => ["query", "--help"],
            "export" => ["export-schema", "--database", "StartupTest", "--output", temp.GetPath("exports")],
            "exploration" => [temp.DirectoryPath, "ExploreSqlTools", "1"],
            "list" => ["--list"],
            _ => []
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        // Never change global environment state or allow inherited product overrides to redirect logging.
        foreach (string key in start.Environment.Keys.Where(key => key.StartsWith("SqlToAi__", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        start.Environment["DOTNET_ENVIRONMENT"] = "StartupTest";
        start.Environment["LOCALAPPDATA"] = temp.GetPath("local-app-data");
        start.Environment["SqlToAi__Logging__Directory"] = temp.GetPath("logs");
        start.Environment["SqlToAi__Observability__LogDirectory"] = temp.GetPath("observability");
        start.Environment["SqlToAi__Observability__Enabled"] = "false";
        if (environment is not null)
            foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;

        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
