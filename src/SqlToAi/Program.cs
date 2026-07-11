#nullable enable

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Mcp;
using SqlToAi.Metadata;
using SqlToAi.Security;

namespace SqlToAi;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // ---------------------------------------------------------------------------
        // Configuration
        // ---------------------------------------------------------------------------
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // ---------------------------------------------------------------------------
        // Dependency Injection
        // ---------------------------------------------------------------------------
        var services = new ServiceCollection();

        services.Configure<SqlToAiOptions>(configuration.GetSection("SqlToAi"));

        services.AddLogging(logging =>
        {
            logging.AddConsole(opts => opts.LogToStandardErrorThreshold = LogLevel.Trace);
            logging.SetMinimumLevel(LogLevel.Warning);
        });

        // Security
        services.AddSingleton<ISecurityGuard, SecurityGuard>();
        services.AddSingleton<IAccessLevelProvider, AccessLevelProvider>();
        services.AddSingleton<IReadOnlyGuard, ReadOnlyGuard>();

        // Database
        services.AddSingleton<IDatabaseConnectionFactory, SqlConnectionFactory>();
        services.AddSingleton<IMetadataProvider, MetadataProvider>();
        services.AddSingleton<ISchemaService, SchemaService>();
        services.AddSingleton<IQueryExecutionService, QueryExecutionService>();
        services.AddSingleton<IQueryValidationService, QueryValidationService>();

        // Anonymization
        services.AddSingleton<IAnonymizer, Anonymizer>();

        // MCP
        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<IToolDispatcher, ToolDispatcher>();
        services.AddSingleton<IMcpHost, McpHost>();

        await using ServiceProvider serviceProvider = services.BuildServiceProvider();

        // ---------------------------------------------------------------------------
        // Run
        // ---------------------------------------------------------------------------
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var host = serviceProvider.GetRequiredService<IMcpHost>();
        await host.RunAsync(cts.Token);

        return 0;
    }
}
