using System.Diagnostics;
using System.Text.Json;
using SqlToAi.Configuration;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Configuration;

public sealed class UserConfigurationTests
{
    private const string MinimalSettings = """{"SqlToAi":{"Servers":{"primary":{"Server":"localhost"}}}}""";

    public static TheoryData<string, string> MissingFileCases => new(
        from entry in new[] { "default", "server", "query", "export", "exploration" }
        from setup in new[] { "empty", "environment", "environment-json", "installation-template" }
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
        if (setup == "environment") environment["SqlToAi__Servers__primary__Server"] = "environment-only";
        if (setup == "environment-json") temp.CreateFile("user-root/appsettings.StartupTest.json", "{}");
        string templatePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        Assert.True(File.Exists(templatePath)); // Build output contains the executable-adjacent template.
        byte[] templateBytes = await File.ReadAllBytesAsync(templatePath, TestContext.Current.CancellationToken);

        var result = await RunAsync(temp, root, entry, environment);

        AssertFailure(result, root);
        Assert.Contains("missing", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(root, "appsettings.json")));
        Assert.False(Directory.Exists(temp.GetPath("exports")));
        Assert.False(Directory.Exists(temp.GetPath("temp/exploration")));
        Assert.Equal(templateBytes, await File.ReadAllBytesAsync(templatePath, TestContext.Current.CancellationToken));
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

    [Theory]
    [InlineData("AppLog", 0)]
    [InlineData("AppLog", -1)]
    [InlineData("ErrorLog", 0)]
    [InlineData("ErrorLog", -1)]
    public async Task InvalidEnabledFileRetention_FailsWithActionableConfigurationDiagnostic(string sink, int count)
    {
        using var temp = TestTempDirectory.Create();
        string file = temp.CreateFile("user-root/appsettings.json",
            $"{{ \"SqlToAi\": {{ \"Servers\": {{ \"primary\": {{ \"Server\": \"localhost\" }} }}, \"Logging\": {{ \"{sink}\": {{ \"RetainedFileCount\": {count} }} }} }} }}");
        string root = Path.GetDirectoryName(file)!;

        var result = await RunAsync(temp, root, "query-help");

        AssertFailure(result, root);
        Assert.Contains($"SqlToAi.Logging.{sink}.RetainedFileCount", result.Error, StringComparison.Ordinal);
        Assert.Contains("greater than zero", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(count.ToString(System.Globalization.CultureInfo.InvariantCulture), result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("At least one file must be retained", result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(temp.GetPath("logs")));
    }

    [Theory]
    [InlineData("MaxInputCount", 0)]
    [InlineData("MaxInputCount", -1)]
    [InlineData("MaxResponseBytes", 0)]
    [InlineData("MaxResponseBytes", -1)]
    public async Task InvalidSchemaBatchLimit_FailsAtStartupWithActionableDiagnostic(string field, int value)
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("user-root/appsettings.json",
            $"{{ \"SqlToAi\": {{ \"Servers\": {{ \"primary\": {{ \"Server\": \"localhost\" }} }}, \"SchemaBatch\": {{ \"{field}\": {value} }} }} }}");
        string root = temp.GetPath("user-root");
        var result = await RunAsync(temp, root, "query-help");
        AssertFailure(result, root);
        Assert.Contains($"SqlToAi.SchemaBatch.{field}", result.Error, StringComparison.Ordinal);
        Assert.Contains("greater than zero", result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(temp.GetPath("logs")));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 65536)]
    public void PositiveSchemaBatchLimits_LoadWithoutRewritingSettings(int maxCount, int maxBytes)
    {
        using var temp = TestTempDirectory.Create();
        string text = $"{{ \"SqlToAi\": {{ \"Servers\": {{ \"primary\": {{ \"Server\": \"localhost\" }} }}, \"SchemaBatch\": {{ \"MaxInputCount\": {maxCount}, \"MaxResponseBytes\": {maxBytes} }} }} }}";
        string path = temp.CreateFile("user-root/appsettings.json", text);
        using var configuration = UserConfiguration.Load(temp.GetPath("user-root"));
        Assert.Equal(maxCount, configuration.Options.SchemaBatch.MaxInputCount);
        Assert.Equal(maxBytes, configuration.Options.SchemaBatch.MaxResponseBytes);
        Assert.Equal(text, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("AppLog", 1)]
    [InlineData("AppLog", int.MaxValue)]
    [InlineData("ErrorLog", 1)]
    [InlineData("ErrorLog", int.MaxValue)]
    public async Task PositiveFileRetention_IsAcceptedAtStartup(string sink, int count)
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("user-root/appsettings.json",
            $"{{ \"SqlToAi\": {{ \"Servers\": {{ \"primary\": {{ \"Server\": \"localhost\" }} }}, \"Logging\": {{ \"{sink}\": {{ \"RetainedFileCount\": {count} }} }} }} }}");

        var result = await RunAsync(temp, temp.GetPath("user-root"), "query-help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("sql_list_databases", result.Output, StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("AppLog", 0)]
    [InlineData("AppLog", -1)]
    [InlineData("ErrorLog", 0)]
    [InlineData("ErrorLog", -1)]
    public async Task InvalidRetention_IsIgnoredWhenFileSinkIsDisabled(string sink, int count)
    {
        using var temp = TestTempDirectory.Create();
        temp.CreateFile("user-root/appsettings.json",
            $"{{ \"SqlToAi\": {{ \"Servers\": {{ \"primary\": {{ \"Server\": \"localhost\" }} }}, \"Logging\": {{ \"{sink}\": {{ \"Enabled\": false, \"RetainedFileCount\": {count} }} }} }} }}");

        var result = await RunAsync(temp, temp.GetPath("user-root"), "query-help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("sql_list_databases", result.Output, StringComparison.Ordinal);
        Assert.Empty(result.Error);
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
        const string json = """{ "SqlToAi": { "Servers": { "primary": { "Server": "manual" } } }, "UnknownUserKey": [1, 2, 3] }""";
        string file = temp.CreateFile("user-root/appsettings.json", json);
        string root = Path.GetDirectoryName(file)!;
        byte[] before = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
        {
            using var startup = UserConfiguration.Load(root);
            Assert.Equal("manual", startup.Options.Servers["primary"].Server);
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
              "Servers": { "test": { "Server": "base-server" } }
            } }
            """);
        temp.CreateFile("user-root/appsettings.StartupTest.json", """{"StartupProbe":"environment-file","SqlToAi":{"Servers":{"test":{"Server":"environment-file-server"}}}}""");
        byte[] before = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);
        Dictionary<string, string?> environment = new()
        {
            ["SqlToAi__Servers__test__Server"] = "runtime-server",
            ["SqlToAi__Servers__test__Password"] = "runtime-test-secret"
        };
        var result = await RunAsync(temp, Path.GetDirectoryName(file)!, "inspect", environment);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var output = JsonDocument.Parse(result.Output);
        Assert.Equal("runtime-server", output.RootElement.GetProperty("Server").GetString());
        Assert.Equal("runtime-server", output.RootElement.GetProperty("BoundServer").GetString());
        Assert.Equal("runtime-test-secret", output.RootElement.GetProperty("Password").GetString());
        Assert.Equal("environment-file", output.RootElement.GetProperty("Probe").GetString());
        Assert.True(output.RootElement.GetProperty("AppLogEnabled").GetBoolean());
        Assert.True(output.RootElement.GetProperty("ErrorLogEnabled").GetBoolean());
        Assert.Equal(before, await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken));
        Assert.Equal(2, Directory.EnumerateFiles(Path.GetDirectoryName(file)!).Count());
    }

    [Fact]
    public async Task ExistingFile_ConcurrentCliStartsPreserveBytesAndUseOnlyIsolatedLogs()
    {
        using var temp = TestTempDirectory.Create();
        string file = temp.CreateFile("user-root/appsettings.json", """{ "SqlToAi": { "Servers": { "primary": { "Server": "localhost" } } }, "UnknownUserKey": "preserve me" }""");
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

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("MetadataProvider")]
    [InlineData("AnonymizationRules")]
    public async Task ObsoleteSection_AtRootOrUnderSqlToAi_FailsAtStartup(string sectionName)
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.GetPath("user-root");

        // Test under SqlToAi
        temp.CreateFile("user-root/appsettings.json",
            $$"""{ "SqlToAi": { "Servers": { "primary": { "Server": "localhost" } }, "{{sectionName}}": {} } }""");
        var result = await RunAsync(temp, root, "query-help");
        AssertFailure(result, root);
        Assert.Contains("Obsolete configuration section", result.Error, StringComparison.Ordinal);

        // Test at root
        temp.CreateFile("user-root/appsettings.json",
            $$"""{ "SqlToAi": { "Servers": { "primary": { "Server": "localhost" } } }, "{{sectionName}}": {} }""");
        var resultRoot = await RunAsync(temp, root, "query-help");
        AssertFailure(resultRoot, root);
        Assert.Contains("Obsolete configuration section", resultRoot.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SqlToAi__SqlServer__Server")]
    [InlineData("SqlToAi__MetadataProvider__TableMetadataQuery")]
    [InlineData("SqlToAi__AnonymizationRules__Enabled")]
    public async Task ObsoleteEnvironmentVariable_FailsAtStartup(string envKey)
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.GetPath("user-root");
        temp.CreateFile("user-root/appsettings.json", MinimalSettings);

        Dictionary<string, string?> environment = new()
        {
            [envKey] = "obsolete-val"
        };

        var result = await RunAsync(temp, root, "query-help", environment);
        AssertFailure(result, root);
        Assert.Contains("Obsolete configuration environment variable", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateServerAlias_ExactOrCaseInsensitive_FailsAtStartup()
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.GetPath("user-root");
        string json = """
        {
          "SqlToAi": {
            "Servers": {
              "ServerA": { "Server": "host1" },
              "servera": { "Server": "host2" }
            }
          }
        }
        """;
        temp.CreateFile("user-root/appsettings.json", json);

        var result = await RunAsync(temp, root, "query-help");
        AssertFailure(result, root);
        Assert.Contains("Duplicate server alias", result.Error, StringComparison.Ordinal);
    }

    public static TheoryData<string, string, string> HiddenDuplicateAliasCases => new(
        from source in new[] { "base", "environment" }
        from wrapper in new[] { "root", "servers", "both" }
        from secondAlias in new[] { "a", "A" }
        select (source, wrapper, secondAlias));

    [Theory]
    [MemberData(nameof(HiddenDuplicateAliasCases))]
    public async Task DuplicateAliases_InEveryServersOccurrence_FailBeforeSourceMerge(
        string source, string wrapper, string secondAlias)
    {
        const string firstServer = """ "first":{"Server":"host","Password":"credential-marker"} """;
        string duplicates = """ "a":{"Server":"other"},"$ALIAS":{"IntegratedSecurity":true} """
            .Replace("$ALIAS", secondAlias, StringComparison.Ordinal);
        string template = wrapper switch
        {
            "root" => """{"SqlToAi":{"Servers":{ $FIRST }},"sqltoai":{"SERVERS":{ $DUPLICATES }}}""",
            "servers" => """{"SqlToAi":{"Servers":{ $FIRST },"servers":{ $DUPLICATES }}}""",
            _ => """{"SqlToAi":{"Servers":{ $FIRST }},"sqltoai":{"Servers":{"safe":{"Server":"safe"}},"servers":{ $DUPLICATES }}}"""
        };
        string json = template.Replace("$FIRST", firstServer, StringComparison.Ordinal)
            .Replace("$DUPLICATES", duplicates, StringComparison.Ordinal);

        await AssertRejectedSourceUnchangedAsync(source, json, "Duplicate server alias");
    }

    public static TheoryData<string, string, string> FlattenedObsoleteCases => new(
        from source in new[] { "base", "environment" }
        from section in new[] { "SqlServer", "MetadataProvider", "AnonymizationRules" }
        from shape in new[] { "root", "qualified", "partial", "section-object" }
        select (source, section, shape));

    [Theory]
    [MemberData(nameof(FlattenedObsoleteCases))]
    public async Task FlattenedObsoletePaths_FailBeforeSourceMerge(string source, string section, string shape)
    {
        string template = shape switch
        {
            "root" => """{"SqlToAi":{"Servers":{"primary":{"Server":"host"}}},"$SECTION:credential-marker":"credential-marker"}""",
            "qualified" => """{"SqlToAi":{"Servers":{"primary":{"Server":"host"}}},"sQlToAi:$SECTION:credential-marker":"credential-marker"}""",
            "partial" => """{"SqlToAi":{"Servers":{"primary":{"Server":"host"}},"$SECTION:credential-marker":"credential-marker"}}""",
            _ => """{"SqlToAi":{"Servers":{"primary":{"Server":"host"}}},"SqlToAi:$SECTION":{"credential-marker":"credential-marker"}}"""
        };
        string json = template.Replace("$SECTION", section, StringComparison.Ordinal);
        string diagnostic = shape == "root" ? $"section '{section}'" : $"section 'SqlToAi.{section}'";

        await AssertRejectedSourceUnchangedAsync(source, json, diagnostic);
    }

    [Theory]
    [InlineData("base", "a")]
    [InlineData("base", "A")]
    [InlineData("environment", "a")]
    [InlineData("environment", "A")]
    public async Task FlattenedServersObject_RejectsExactAndCaseDuplicateAliases(string source, string alias)
    {
        string json = """
            {"sQlToAi:SeRvErS":{"a":{"Server":"host","Password":"credential-marker"},
              "$ALIAS":{"IntegratedSecurity":true}}}
            """.Replace("$ALIAS", alias, StringComparison.Ordinal);

        await AssertRejectedSourceUnchangedAsync(source, json, "Duplicate server alias");
    }

    [Theory]
    [InlineData("base")]
    [InlineData("environment")]
    public async Task FlattenedAliasFields_RejectCaseVariantAliases(string source)
    {
        const string json = """
            {"SqlToAi:Servers":{"a:Server":"host","A:Password":"credential-marker"}}
            """;

        await AssertRejectedSourceUnchangedAsync(source, json, "Duplicate server alias");
    }

    [Fact]
    public async Task FlattenedFieldsAndUnknownObjects_PreserveSupportedConfigurationPaths()
    {
        using var temp = TestTempDirectory.Create();
        const string json = """
            {"SqlToAi:Servers":{"primary:Server":"host","primary:IntegratedSecurity":true},
             "SqlToAi:SqlServerExtra:Password":"credential-marker",
             "Unknown":{"SqlToAi:SqlServer:Password":"credential-marker",
               "SqlToAi:Servers":{"a":{"Server":"ignored"},"A":{"IntegratedSecurity":true}}}}
            """;
        string file = temp.CreateFile("user-root/appsettings.json", json);

        var result = await RunAsync(temp, temp.GetPath("user-root"), "query");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal("[\"primary\"]", result.Output.Trim());
        Assert.Equal(json, await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("base", "SqlServer")]
    [InlineData("base", "MetadataProvider")]
    [InlineData("base", "AnonymizationRules")]
    [InlineData("environment", "SqlServer")]
    [InlineData("environment", "MetadataProvider")]
    [InlineData("environment", "AnonymizationRules")]
    public async Task ObsoleteSections_InLaterSqlToAiOccurrence_FailBeforeSourceMerge(string source, string section)
    {
        string json = """
            {"SqlToAi":{"Servers":{"primary":{"Server":"host"}}},
             "sqltoai":{"$SECTION":{"Password":"credential-marker"}}}
            """.Replace("$SECTION", section, StringComparison.Ordinal);

        await AssertRejectedSourceUnchangedAsync(source, json, $"SqlToAi.{section}");
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("MetadataProvider")]
    [InlineData("AnonymizationRules")]
    public async Task ObsoleteBaseSection_IsRejectedEvenWhenLaterSourcesOverrideIt(string section)
    {
        using var temp = TestTempDirectory.Create();
        string json = """{"SqlToAi":{"Servers":{"primary":{"Server":"host"}},"$SECTION":{"Password":"credential-marker"}}}"""
            .Replace("$SECTION", section, StringComparison.Ordinal);
        string file = temp.CreateFile("user-root/appsettings.json", json);
        string environmentJson = """{"SqlToAi":{"$SECTION":{"Password":"overridden-marker"}}}"""
            .Replace("$SECTION", section, StringComparison.Ordinal);
        string environmentFile = temp.CreateFile("user-root/appsettings.StartupTest.json", environmentJson);
        var result = await RunAsync(temp, temp.GetPath("user-root"), "query-help");

        AssertFailure(result, temp.GetPath("user-root"));
        Assert.Contains("Obsolete", result.Error, StringComparison.Ordinal);
        Assert.Contains($"detected in '{file}'", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("marker", result.Error, StringComparison.Ordinal);
        Assert.Equal(json, await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
        Assert.Equal(environmentJson, await File.ReadAllTextAsync(environmentFile, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFiles(temp.DirectoryPath, "*.log", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CaseVariantAliases_AcrossSourcesRemainValidOverrides()
    {
        using var temp = TestTempDirectory.Create();
        const string json = """{"SqlToAi":{"Servers":{"TEST":{"Server":"base-host"}}}}""";
        const string environmentJson = """{"sqltoai":{"servers":{"Test":{"Server":"environment-host"}}}}""";
        string file = temp.CreateFile("user-root/appsettings.json", json);
        string environmentFile = temp.CreateFile("user-root/appsettings.StartupTest.json", environmentJson);
        var environment = new Dictionary<string, string?> { ["SQLTOAI__SERVERS__test__Server"] = "process-host" };

        var inspect = await RunAsync(temp, temp.GetPath("user-root"), "inspect", environment);
        Assert.Equal(0, inspect.ExitCode);
        Assert.Empty(inspect.Error);
        using var output = JsonDocument.Parse(inspect.Output);
        Assert.Equal("process-host", output.RootElement.GetProperty("Server").GetString());
        Assert.Equal("process-host", output.RootElement.GetProperty("BoundServer").GetString());
        var list = await RunAsync(temp, temp.GetPath("user-root"), "query", environment);
        Assert.Equal(0, list.ExitCode);
        Assert.Empty(list.Error);
        Assert.Equal("[\"test\"]", list.Output.Trim());
        Assert.Equal(json, await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
        Assert.Equal(environmentJson, await File.ReadAllTextAsync(environmentFile, TestContext.Current.CancellationToken));
    }

    private static async Task AssertRejectedSourceUnchangedAsync(string source, string json, string diagnostic)
    {
        using var temp = TestTempDirectory.Create();
        string baseJson = source == "base" ? json : MinimalSettings;
        string baseFile = temp.CreateFile("user-root/appsettings.json", baseJson);
        string? environmentFile = source == "environment"
            ? temp.CreateFile("user-root/appsettings.StartupTest.json", json)
            : null;
        string root = temp.GetPath("user-root");
        var result = await RunAsync(temp, root, "query-help");

        AssertFailure(result, root);
        Assert.Contains(diagnostic, result.Error, StringComparison.Ordinal);
        Assert.Contains(environmentFile ?? baseFile, result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("credential-marker", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", result.Error, StringComparison.Ordinal);
        Assert.Equal(baseJson, await File.ReadAllTextAsync(baseFile, TestContext.Current.CancellationToken));
        if (environmentFile is not null)
            Assert.Equal(json, await File.ReadAllTextAsync(environmentFile, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFiles(temp.DirectoryPath, "*.log", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(temp.DirectoryPath, "*.bak", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task MissingServers_FailsAtStartup()
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.GetPath("user-root");
        temp.CreateFile("user-root/appsettings.json", """{ "SqlToAi": { "Servers": {} } }""");

        var result = await RunAsync(temp, root, "query-help");
        AssertFailure(result, root);
        Assert.Contains("At least one named server must be configured", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("123invalid")]
    [InlineData("bad/alias")]
    [InlineData("-dashstart")]
    public async Task InvalidServerAlias_FailsAtStartup(string alias)
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.GetPath("user-root");
        string json = $$"""{ "SqlToAi": { "Servers": { "{{alias}}": { "Server": "host" } } } }""";
        temp.CreateFile("user-root/appsettings.json", json);

        var result = await RunAsync(temp, root, "query-help");
        AssertFailure(result, root);
        Assert.Contains("is invalid", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyServerAddress_FailsAtStartup()
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.GetPath("user-root");
        string json = """{ "SqlToAi": { "Servers": { "primary": { "Server": "   " } } } }""";
        temp.CreateFile("user-root/appsettings.json", json);

        var result = await RunAsync(temp, root, "query-help");
        AssertFailure(result, root);
        Assert.Contains("empty or missing 'Server' address", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnqualifiedDatabaseGrant_FailsAtStartup()
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.GetPath("user-root");
        string json = """
        {
          "SqlToAi": {
            "Servers": { "primary": { "Server": "localhost" } },
            "Databases": {
              "ReadWrite": [ "DemoDB" ]
            }
          }
        }
        """;
        temp.CreateFile("user-root/appsettings.json", json);

        var result = await RunAsync(temp, root, "query-help");
        AssertFailure(result, root);
        Assert.Contains("Every database grant must be qualified in 'alias/database' form", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DatabaseGrant_ReferencingUnknownServer_FailsAtStartup()
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.GetPath("user-root");
        string json = """
        {
          "SqlToAi": {
            "Servers": { "primary": { "Server": "localhost" } },
            "Databases": {
              "ReadWrite": [ "unknown/DemoDB" ]
            }
          }
        }
        """;
        temp.CreateFile("user-root/appsettings.json", json);

        var result = await RunAsync(temp, root, "query-help");
        AssertFailure(result, root);
        Assert.Contains("references unknown server alias", result.Error, StringComparison.Ordinal);
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

    internal static async Task<ProcessResult> RunAsync(TestTempDirectory temp, string root, string entry,
        Dictionary<string, string?>? environment = null, string? harnessDirectory = null)
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
        start.ArgumentList.Add(Path.Combine(harnessDirectory ?? AppContext.BaseDirectory, "SqlToAi.Tests.runtimeconfig.json"));
        start.ArgumentList.Add("--depsfile");
        start.ArgumentList.Add(Path.Combine(harnessDirectory ?? AppContext.BaseDirectory, "SqlToAi.Tests.deps.json"));
        start.ArgumentList.Add(harnessDirectory is null ? typeof(StartupHarness.StartupHarness).Assembly.Location : Path.Combine(harnessDirectory, "SqlToAi.StartupHarness.dll"));
        start.ArgumentList.Add(root);
        start.ArgumentList.Add(entry is "exploration" or "list" or "usage" ? "exploration" : entry);
        string[] arguments = entry switch
        {
            "server" => ["server"],
            "query" => ["query", "sql_list_servers"],
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

    internal sealed record ProcessResult(int ExitCode, string Output, string Error);
}
