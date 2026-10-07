using SqlToAi.Mcp;

namespace SqlToAi.Exploration.Scenarios;

internal sealed class ExploreSqlTools : IExplorationScenario
{
    public async Task RunAsync(ExplorationContext context)
    {
        const string database = "DemoDB";
        const string objectName = "dbo.FakeProjects";
        await context.CallAsync(McpConstants.ToolListDatabases, new { });
        await context.CallAsync(McpConstants.ToolSearchDatabases, new { search_term = "Demo" });
        await context.CallAsync(McpConstants.ToolSearchObjects, new { database, search_term = "Fake", max_results = 20 });
        await context.CallAsync(McpConstants.ToolGetSchema, new { database, object_name = objectName });
        await context.CallAsync(McpConstants.ToolGetSchemaForeignKeys, new { database, object_name = objectName });
        await context.CallAsync(McpConstants.ToolGetSchemaIndexes, new { database, object_name = objectName });
        await context.CallAsync(McpConstants.ToolGetSchemaConstraints, new { database, object_name = objectName });
        await context.CallAsync(McpConstants.ToolGetObjectReferences, new { database, object_name = objectName });
        await context.CallAsync(McpConstants.ToolGetRoutineParameters, new { database, object_name = "dbo.spFakeSysTan" });
        await context.CallAsync(McpConstants.ToolGetTriggerDefinition,
            new { database, object_name = objectName, trigger_name = "ExplorationMissingTrigger" });
        await context.CallAsync(McpConstants.ToolValidateQuery, new { database, query = "SELECT @Value AS Value", parameters = new { Value = 42 } });
        await context.CallAsync(McpConstants.ToolExecuteQuery, new { database, query = "SELECT @Value AS Value", parameters = new { Value = 42 }, requested_row_limit = 5 });
        await context.CallAsync(McpConstants.ToolExecuteQuery, new { database, query = "SELECT TOP (3) name, type_desc FROM sys.objects ORDER BY name", requested_row_limit = 3 });
        var scriptPath = Path.Combine(context.OutputDirectory, "read-only.sql");
        await File.WriteAllTextAsync(scriptPath, "SELECT 1 AS FirstValue;\nGO\nSELECT 2 AS SecondValue;", context.CancellationToken);
        await context.CallAsync(McpConstants.ToolExecuteFile, new { database, file_path = scriptPath, use_transaction = true, requested_row_limit = 5 });
        await context.CallAsync(McpConstants.ToolCompareQueries, new { database, query_a = "SELECT 1 AS Value", query_b = "SELECT 2 AS Value" });
        await context.CallAsync(McpConstants.ToolMeasurePerformance, new { database, query = "SELECT 1 AS Value", warmup_runs = 0, execution_runs = 1, include_plan_analysis = false });
        await context.CallAsync(McpConstants.ToolBenchmarkOptimization, new { database, query_a = "SELECT 1 AS Value", query_b = "SELECT 1 AS Value", warmup_runs = 0, execution_runs = 1 });
        await context.CallAsync(McpConstants.ToolSuggestIndexes, new { database, top = 3 });
        await context.CallAsync(McpConstants.ToolExecuteQuery, new { database });
        await context.CallAsync("exploration_unknown_tool", new { });
        await context.CallAsync(McpConstants.ToolExecuteQuery, new { database, query = "SELECT FROM" });
        // A successful call after errors makes continuation visible without an assertion.
        await context.CallAsync(McpConstants.ToolExecuteQuery, new { database, query = "SELECT 7 AS AfterErrors" });
    }
}
