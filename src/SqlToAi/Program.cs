#nullable enable

using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RalfHuesing.Mcp.Observability;
using Serilog;
using Serilog.Events;
using SqlToAi.Anonymization;
using SqlToAi.Cli;
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
        string appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        MigrationResult migrationResult = AppSettingsMigrator.Migrate(appSettingsPath);

        IConfiguration configuration = BuildConfiguration();

        var sqlToAiOptions = configuration.GetSection("SqlToAi").Get<SqlToAiOptions>() ?? new SqlToAiOptions();
        ConfigurationResolver.Resolve(sqlToAiOptions);

        // ---------------------------------------------------------------------------
        // Serilog (file-based, rolling) — used by both Microsoft.Extensions.Logging
        // and direct Serilog.Log.Logger consumers. Stdout stays clean for the MCP
        // stdio protocol (and for `query` output), Serilog writes to stderr at
        // Information+ and to file at the configured level.
        // ---------------------------------------------------------------------------
        Log.Logger = BuildLogger(sqlToAiOptions.Logging);

        foreach (string entry in migrationResult.LogEntries)
        {
            Log.Information("[ConfigMigration] {Message}", entry);
        }

        try
        {
            await using ServiceProvider serviceProvider = BuildServiceProvider(configuration, sqlToAiOptions);

            // ---------------------------------------------------------------------------
            // Run retention sweep on startup (best-effort)
            // ---------------------------------------------------------------------------
            serviceProvider.GetRequiredService<LogRetentionService>().Run();

            RootCommand rootCommand = BuildRootCommand(serviceProvider);
            ParseResult parseResult = rootCommand.Parse(args);
            return await parseResult.InvokeAsync();
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Command tree: `server` (default, MCP stdio loop) and `query <tool>` (CLI escape hatch)
    // -------------------------------------------------------------------------

    private static RootCommand BuildRootCommand(ServiceProvider serviceProvider)
    {
        var rootCommand = new RootCommand("SqlToAi — MCP server for SQL Server, with a CLI escape hatch for manual tool verification.");

        Func<ParseResult, CancellationToken, Task<int>> runServer = (_, cancellationToken) => RunServerAsync(serviceProvider, cancellationToken);

        var serverCommand = new Command("server", "Runs the MCP stdio server. This is also the default when no subcommand is given.");
        serverCommand.SetAction(runServer);
        rootCommand.Add(serverCommand);
        rootCommand.SetAction(runServer);

        Command queryCommand = ToolCommandFactory.BuildQueryCommand(
            new ToolRegistry().GetAll(),
            (toolName, arguments, cancellationToken) => ExecuteToolAsync(serviceProvider, toolName, arguments, cancellationToken));
        rootCommand.Add(queryCommand);

        return rootCommand;
    }

    private static async Task<int> RunServerAsync(ServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var serverOptions = serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var transport = new StdioServerTransport(serverOptions);
        await using var server = McpServer.Create(transport, serverOptions, serviceProvider: serviceProvider);
        await server.RunAsync(cancellationToken);
        return 0;
    }

    private static async Task<int> ExecuteToolAsync(
        ServiceProvider serviceProvider,
        string toolName,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var dispatcher = serviceProvider.GetRequiredService<IToolDispatcher>();
        var callParams = new ToolCallParams { Name = toolName, Arguments = arguments };

        ToolCallResult result = await dispatcher.DispatchAsync(callParams, cancellationToken);
        return PrintToolResult(result);
    }

    /// <summary>
    /// Prints a tool result for CLI consumption. On error, everything goes to stderr with exit code 1.
    /// On success, all but the last content block (e.g. the anonymization notice on `sql_execute_query`)
    /// go to stderr, and the last block (the actual data) goes to stdout — so output stays pipeable.
    /// </summary>
    private static int PrintToolResult(ToolCallResult result)
    {
        if (result.IsError)
        {
            foreach (ToolContent content in result.Content)
            {
                Console.Error.WriteLine(content.Text);
            }
            return 1;
        }

        for (int i = 0; i < result.Content.Count; i++)
        {
            TextWriter writer = i == result.Content.Count - 1 ? Console.Out : Console.Error;
            writer.WriteLine(result.Content[i].Text);
        }
        return 0;
    }

    // -------------------------------------------------------------------------
    // Configuration & DI
    // -------------------------------------------------------------------------

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

    private static ServiceProvider BuildServiceProvider(IConfiguration configuration, SqlToAiOptions sqlToAiOptions)
    {
        var services = new ServiceCollection();

        services.AddSingleton(sqlToAiOptions);
        services.AddOptions();
        services.Configure<SqlToAiOptions>(configuration.GetSection("SqlToAi"));
        services.PostConfigure<SqlToAiOptions>(options => ConfigurationResolver.Resolve(options));

        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSerilog(dispose: false);
        });

        // Security
        services.AddSingleton<ISecurityGuard, SecurityGuard>();
        services.AddSingleton<IAccessLevelProvider, AccessLevelProvider>();
        services.AddSingleton<IReadOnlyGuard, ReadOnlyGuard>();
        services.AddSingleton<IQuerySafetyValidator, QuerySafetyValidator>();

        // Database
        services.AddSingleton<IDatabaseConnectionFactory, SqlConnectionFactory>();
        services.AddSingleton<IMetadataProvider, MetadataProvider>();
        services.AddSingleton<ISchemaService, SchemaService>();
        services.AddSingleton<QueryExecutionService>();
        services.AddSingleton<IQueryExecutionService>(sp => sp.GetRequiredService<QueryExecutionService>());
        services.AddSingleton<IQueryBatchExecutor>(sp => sp.GetRequiredService<QueryExecutionService>());
        services.AddSingleton<IScriptExecutionService, ScriptExecutionService>();
        services.AddSingleton<IQueryValidationService, QueryValidationService>();
        services.AddSingleton<QueryExecutionDependencies>(sp => new QueryExecutionDependencies(
            sp.GetRequiredService<IDatabaseConnectionFactory>(),
            sp.GetRequiredService<IQuerySafetyValidator>(),
            sp.GetRequiredService<IOptions<SqlToAiOptions>>()));
        services.AddSingleton<IQueryComparisonService>(sp => new QueryComparisonService(
            sp.GetRequiredService<QueryExecutionDependencies>(),
            sp.GetRequiredService<ILogger<QueryComparisonService>>()));
        services.AddSingleton<IPerformanceMeasurementService>(sp => new PerformanceMeasurementService(
            sp.GetRequiredService<QueryExecutionDependencies>(),
            sp.GetRequiredService<ILogger<PerformanceMeasurementService>>()));
        services.AddSingleton<IOptimizationBenchmarkService, OptimizationBenchmarkService>();
        services.AddSingleton<IIndexSuggestionService, IndexSuggestionService>();
        services.AddSingleton<DatabaseAnalysisServices>(sp => new DatabaseAnalysisServices(
            sp.GetRequiredService<IPerformanceMeasurementService>(),
            sp.GetRequiredService<IQueryComparisonService>(),
            sp.GetRequiredService<IOptimizationBenchmarkService>(),
            sp.GetRequiredService<IIndexSuggestionService>()));

        // Anonymization
        services.AddSingleton<ITokenVault, TokenVault>();
        services.AddSingleton<IAnonymizer, Anonymizer>();
        services.AddSingleton<IAnonymizationRuleProvider, AnonymizationRuleProvider>();
        services.AddSingleton<IAnonymizationPolicyResolver, AnonymizationPolicyResolver>();
        services.AddSingleton<IQueryTokenResolver, QueryTokenResolver>();
        services.AddSingleton<AnonymizationDependencies>(sp => new AnonymizationDependencies(
            sp.GetRequiredService<IAnonymizer>(),
            sp.GetRequiredService<IAnonymizationRuleProvider>(),
            sp.GetRequiredService<IQueryTokenResolver>()));

        // MCP & Observability
        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<IToolDispatcher, ToolDispatcher>();
        services.AddSingleton<LogRetentionService>(sp =>
            new LogRetentionService(
                sp.GetRequiredService<SqlToAiOptions>().Logging,
                sp.GetRequiredService<ILogger<LogRetentionService>>()));

        services.AddMcpServer(serverOptions =>
        {
            serverOptions.ServerInfo = new Implementation
            {
                Name = McpConstants.ServerName,
                Version = typeof(Program).Assembly.GetName().Version?.ToString() ?? McpConstants.ServerVersion
            };
            serverOptions.ServerInstructions = McpConstants.ServerInstructions;
        })
        .WithObservability(sqlToAiOptions.Observability);

        services.AddSingleton<IConfigureOptions<McpServerOptions>>(sp =>
            new ConfigureNamedOptions<McpServerOptions>(Options.DefaultName, options =>
            {
                var dispatcher = sp.GetRequiredService<IToolDispatcher>();
                options.ToolCollection = SqlMcpToolRegistrations.BuildToolCollection(dispatcher);
            }));

        return services.BuildServiceProvider();
    }

    // -------------------------------------------------------------------------
    // Serilog setup
    // -------------------------------------------------------------------------

    private static Serilog.Core.Logger BuildLogger(LoggingOptions options)
    {
        var config = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Console(
                formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                restrictedToMinimumLevel: LogEventLevel.Warning,
                standardErrorFromLevel: LogEventLevel.Warning);

        if (options.AppLog.Enabled)
        {
            string appPath = Path.Combine(options.GetAbsoluteRoot(), "app", "app-.log");
            config = config.WriteTo.File(
                appPath,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                rollingInterval: ParseRollingInterval(options.AppLog.RollingInterval),
                rollOnFileSizeLimit: false,
                retainedFileCountLimit: options.AppLog.RetainedFileCount,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                restrictedToMinimumLevel: ParseLogLevel(options.AppLog.Level));
        }

        if (options.ErrorLog.Enabled)
        {
            string errorPath = Path.Combine(options.GetAbsoluteRoot(), "error", "error-.log");
            config = config.WriteTo.File(
                errorPath,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                rollingInterval: ParseRollingInterval(options.ErrorLog.RollingInterval),
                rollOnFileSizeLimit: false,
                retainedFileCountLimit: options.ErrorLog.RetainedFileCount,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                restrictedToMinimumLevel: ParseLogLevel(options.ErrorLog.Level));
        }

        return config.CreateLogger();
    }

    private static RollingInterval ParseRollingInterval(string s) => s?.ToLowerInvariant() switch
    {
        "minute" => RollingInterval.Minute,
        "hour"   => RollingInterval.Hour,
        "day"    => RollingInterval.Day,
        "month"  => RollingInterval.Month,
        "year"   => RollingInterval.Year,
        _        => RollingInterval.Day
    };

    private static LogEventLevel ParseLogLevel(string s) => s?.ToLowerInvariant() switch
    {
        "verbose"     => LogEventLevel.Verbose,
        "debug"       => LogEventLevel.Debug,
        "information" => LogEventLevel.Information,
        "info"        => LogEventLevel.Information,
        "warning"     => LogEventLevel.Warning,
        "warn"        => LogEventLevel.Warning,
        "error"       => LogEventLevel.Error,
        "fatal"       => LogEventLevel.Fatal,
        _             => LogEventLevel.Information
    };
}
