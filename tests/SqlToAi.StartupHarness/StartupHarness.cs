using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;

namespace SqlToAi.StartupHarness;

// Only test code accepts a root argument; production entry points always resolve the Windows user root.
public static class StartupHarness
{
    private static Task<int> Main(string[] args) => args[1] switch
    {
        "exploration" => Exploration.ExplorationRunner.RunAsync(args[2..], args[0]),
        "inspect" => InspectAsync(args[0]),
        _ => SqlToAi.Program.RunAsync(args[2..], args[0])
    };

    private static async Task<int> InspectAsync(string root)
    {
        using var startup = UserConfiguration.Load(root);
        await using var services = SqlToAi.Program.BuildServiceProvider(startup.Configuration, startup.Options);
        var boundOptions = services.GetRequiredService<IOptions<SqlToAiOptions>>().Value;
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            startup.RootDirectory,
            startup.Options.SqlServer.Server,
            startup.Options.SqlServer.Password,
            BoundServer = boundOptions.SqlServer.Server,
            Probe = startup.Configuration["StartupProbe"]
        }));
        return 0;
    }
}
