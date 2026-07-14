#nullable enable

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
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

        var sqlToAiOptions = configuration.GetSection("SqlToAi").Get<SqlToAiOptions>() ?? new SqlToAiOptions();
        ConfigurationResolver.Resolve(sqlToAiOptions);

        // ---------------------------------------------------------------------------
        // Serilog (file-based, rolling) — used by both Microsoft.Extensions.Logging
        // and direct Serilog.Log.Logger consumers. Stdout stays clean for the MCP
        // stdio protocol; Serilog writes to stderr at Information+ and to file at the
        // configured level.
        // ---------------------------------------------------------------------------
        Log.Logger = BuildLogger(sqlToAiOptions.Logging);

        try
        {
            // ---------------------------------------------------------------------------
            // Dependency Injection
            // ---------------------------------------------------------------------------
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

            // Database
            services.AddSingleton<IDatabaseConnectionFactory, SqlConnectionFactory>();
            services.AddSingleton<IMetadataProvider, MetadataProvider>();
            services.AddSingleton<ISchemaService, SchemaService>();
            services.AddSingleton<IQueryExecutionService, QueryExecutionService>();
            services.AddSingleton<IQueryValidationService, QueryValidationService>();

            // Anonymization
            services.AddSingleton<IAnonymizer, Anonymizer>();
            services.AddSingleton<IAnonymizerExclusionProvider, AnonymizerExclusionProvider>();

            // MCP
            services.AddSingleton<ToolRegistry>();
            services.AddSingleton<IToolDispatcher, ToolDispatcher>();
            services.AddSingleton<IMcpTrailWriter, McpTrailWriter>();
            services.AddSingleton<IMcpHost, McpHost>();
            services.AddSingleton<LogRetentionService>(sp =>
                new LogRetentionService(
                    sp.GetRequiredService<SqlToAiOptions>().Logging,
                    sp.GetRequiredService<ILogger<LogRetentionService>>()));

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();

            // ---------------------------------------------------------------------------
            // Run retention sweep on startup (best-effort)
            // ---------------------------------------------------------------------------
            serviceProvider.GetRequiredService<LogRetentionService>().Run();

            // ---------------------------------------------------------------------------
            // Run MCP host
            // ---------------------------------------------------------------------------
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var host = serviceProvider.GetRequiredService<IMcpHost>();

            // Ensure stdout uses UTF-8 without BOM so the MCP JSON stream stays clean.
            Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            // Wrap stdin in a BOM-stripping StreamReader. Some clients (e.g. PowerShell)
            // emit a UTF-8 BOM on the first write. detectEncodingFromByteOrderMarks:true
            // transparently skips those bytes so the first JSON-RPC message is never lost.
            using var stdinReader = new StreamReader(
                Console.OpenStandardInput(),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                detectEncodingFromByteOrderMarks: true);

            await host.RunAsync(stdinReader, Console.Out, cts.Token);

            return 0;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
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
                restrictedToMinimumLevel: LogEventLevel.Warning);

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
