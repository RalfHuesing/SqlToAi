namespace SqlToAi.Exploration.Scenarios;

internal static class ScenarioRegistry
{
    internal static IReadOnlyDictionary<string, IExplorationScenario> All { get; } =
        typeof(ScenarioRegistry).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(IExplorationScenario).IsAssignableFrom(type))
            .ToDictionary(type => type.Name,
                type => (IExplorationScenario)Activator.CreateInstance(type)!, StringComparer.OrdinalIgnoreCase);
}
