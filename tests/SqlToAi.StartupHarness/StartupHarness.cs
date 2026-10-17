using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using SqlToAi.Cli;
using SqlToAi.Database;
using SqlToAi.Domain;
using System.Reflection;
using SqlToAi.Configuration;

namespace SqlToAi.StartupHarness;

// Only test code accepts a root argument; production entry points always resolve the Windows user root.
public static class StartupHarness
{
    private static Task<int> Main(string[] args) => args[1] switch
    {
        "exploration" => Exploration.ExplorationRunner.RunAsync(args[2..], args[0]),
        "inspect" => InspectAsync(args[0]),
        "log-probe" => WriteLogsAsync(args[0]),
        "cwd-probe" => ProbeCwdAsync(args[0]),
        _ => SqlToAi.Program.RunAsync(args[2..], args[0])
    };

    private static async Task<int> InspectAsync(string root)
    {
        using var startup = UserConfiguration.Load(root);
        await using var services = SqlToAi.Program.BuildServiceProvider(startup.Configuration, startup.Options, startup.RootDirectory);
        var boundOptions = services.GetRequiredService<IOptions<SqlToAiOptions>>().Value;
        var directServer = startup.Options.Servers.TryGetValue("test", out var directOpt) ? directOpt : startup.Options.Servers.Values.FirstOrDefault() ?? new();
        var boundServer = boundOptions.Servers.TryGetValue("test", out var boundOpt) ? boundOpt : boundOptions.Servers.Values.FirstOrDefault() ?? new();
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            startup.RootDirectory,
            directServer.Server,
            directServer.Password,
            BoundServer = boundServer.Server,
            AppLogEnabled = boundOptions.Logging.AppLog.Enabled,
            ErrorLogEnabled = boundOptions.Logging.ErrorLog.Enabled,
            DirectMetadata = directServer.MetadataProvider?.TableMetadataQuery,
            BoundMetadata = boundServer.MetadataProvider?.TableMetadataQuery,
            DirectLogRoot = startup.Options.Logging.GetAbsoluteRoot(),
            BoundLogRoot = boundOptions.Logging.GetAbsoluteRoot(),
            Probe = startup.Configuration["StartupProbe"]
        }));
        return 0;
    }

    private static async Task<int> WriteLogsAsync(string root)
    {
        using var startup = UserConfiguration.Load(root);
        await using var logger = SqlToAi.Program.BuildLogger(startup.Options.Logging);
        logger.Information("Application path probe");
        logger.Warning("Error path probe");
        return 0;
    }

    private static async Task<int> ProbeCwdAsync(string root)
    {
        using var startup = UserConfiguration.Load(root);
        var script = SqlScriptFileReader.Read("input.sql", startup.Options.QueryExecution);
        if (script.IsFailure) return 1;
        var schema = DispatchProxy.Create<ISchemaService, EmptySchemaProxy>();
        var export = new SchemaExportService(schema, Options.Create(startup.Options), NullLogger<SchemaExportService>.Instance);
        var result = await export.ExportAsync("test/CwdProbe", "export");
        Console.WriteLine(script.Value.Text);
        return result.IsSuccess ? 0 : 1;
    }

    public class EmptySchemaProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == "GetExportObjectsAsync" && args?[0] is DatabaseTargetContext
                ? Task.FromResult(Result<IReadOnlyList<SchemaObject>>.Success([]))
                : throw new InvalidOperationException("Unexpected schema call.");
    }
}
