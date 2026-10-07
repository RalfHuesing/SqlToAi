using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlToAi.Configuration;
using SqlToAi.Exploration;
using SqlToAi.Exploration.Scenarios;
using SqlToAi.Mcp;
using SqlToAi.Security;

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
    var repositoryRoot = Path.GetFullPath(args[0]);
    outputDirectory = Path.Combine(repositoryRoot, "temp", "exploration", scenario.GetType().Name,
        DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture));
    Directory.CreateDirectory(outputDirectory);
    Console.WriteLine($"Artifacts: {outputDirectory}");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
    var configurationDirectory = Path.Combine(repositoryRoot, "src", "SqlToAi");
    if (!File.Exists(Path.Combine(configurationDirectory, "appsettings.json")))
        throw new FileNotFoundException("Repository appsettings.json is required.");
    // Read source configuration without migrating or modifying it.
    var configuration = SqlToAi.Program.BuildConfiguration(configurationDirectory);
    var options = configuration.GetSection("SqlToAi").Get<SqlToAiOptions>() ?? new SqlToAiOptions();
    ConfigurationResolver.Resolve(options);
    await using var services = SqlToAi.Program.BuildServiceProvider(configuration, options);
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
    Console.Error.WriteLine($"Technical execution failed ({exception.GetType().Name}).");
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
