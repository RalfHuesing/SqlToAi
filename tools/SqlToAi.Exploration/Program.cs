using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SqlToAi.Configuration;
using SqlToAi.Exploration.Scenarios;
using SqlToAi.Mcp;
using SqlToAi.Security;

namespace SqlToAi.Exploration;

internal static class ExplorationRunner
{
    private static Task<int> Main(string[] args) => RunAsync(args);

    internal static async Task<int> RunAsync(string[] args, string? userRoot = null)
    {
        if (args is ["--list"])
        {
            foreach (var name in ScenarioRegistry.All.Keys.Order(StringComparer.Ordinal)) Console.WriteLine(name);
            return 0;
        }

        if (args.Length != 3 || !ScenarioRegistry.All.TryGetValue(args[1], out var scenario)
            || !int.TryParse(args[2], CultureInfo.InvariantCulture, out var seconds) || seconds <= 0 || seconds > 3600)
        {
            Console.Error.WriteLine("Usage: exploration <repository-root> <scenario> <timeout-seconds (1..3600)>, or --list.");
            return 2;
        }

        string? outputDirectory = null;
        try
        {
            using var startup = UserConfiguration.LoadOrReport(userRoot);
            if (startup is null) return 1;
            var repositoryRoot = Path.GetFullPath(args[0]);
            outputDirectory = Path.Combine(repositoryRoot, "temp", "exploration", scenario.GetType().Name,
                DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(outputDirectory);
            Console.WriteLine($"Artifacts: {outputDirectory}");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            var configurationDirectory = startup.RootDirectory;
            await using var services = SqlToAi.Program.BuildServiceProvider(startup.Configuration, startup.Options, startup.RootDirectory);
            var context = new ExplorationContext(services.GetRequiredService<IToolDispatcher>(), repositoryRoot, outputDirectory, timeout.Token);
            var access = services.GetRequiredService<IAccessLevelProvider>();
            await context.WriteJsonAsync(Path.Combine(outputDirectory, "run.json"), new
            {
                scenario = scenario.GetType().Name,
                configurationDirectory,
                demoDbAccessLevel = (await access.GetAccessLevelAsync("DemoDB", timeout.Token)).ToString(),
                timeoutSeconds = seconds,
                configurationOverrides = "None; production configuration and environment providers apply."
            });
            await scenario.RunAsync(context).ConfigureAwait(false);
            Console.WriteLine("Technical execution completed. Inspect all artifacts; tool errors are observations.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception is ConfigurationStartupException or IOException or UnauthorizedAccessException
                ? $"Technical execution failed: {exception.Message}"
                : $"Technical execution failed ({exception.GetType().Name}).");
            if (outputDirectory is not null)
            {
                try
                {
                    await File.WriteAllTextAsync(Path.Combine(outputDirectory, "error.txt"), exception.ToString(), CancellationToken.None).ConfigureAwait(false);
                    Console.Error.WriteLine("Inspect error.txt in the artifact directory.");
                }
                catch (Exception artifactException)
                {
                    Console.Error.WriteLine($"Could not record error.txt ({artifactException.GetType().Name}).");
                }
            }
            return 1;
        }
    }
}
