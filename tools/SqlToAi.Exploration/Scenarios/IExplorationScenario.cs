namespace SqlToAi.Exploration.Scenarios;

internal interface IExplorationScenario
{
    Task RunAsync(ExplorationContext context);
}
