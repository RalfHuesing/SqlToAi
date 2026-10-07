# Exploration scenarios

`ExploreSqlTools` is the sole permanent baseline. Task-specific scenarios are temporary: remove their `.cs` files when the investigation ends. Scenarios have no assertions or snapshots and are never scheduled by tests or CI. The infrastructure tests do not turn scenario observations into pass/fail expectations.

Add an `internal sealed` class with a public parameterless constructor implementing `IExplorationScenario` in `SqlToAi.Exploration.Scenarios`. Discovery uses its class name, case-insensitively; no registry edit is required.

```csharp
namespace SqlToAi.Exploration.Scenarios;

internal sealed class ExploreIssue : IExplorationScenario
{
    public async Task RunAsync(ExplorationContext context)
    {
        await context.CallAsync("sql_execute_query", new
        {
            database = "DemoDB",
            query = "SELECT @Value AS Value",
            parameters = new { Value = 42 },
            requested_row_limit = 5
        });
    }
}
```

Use `context.OutputDirectory` for retained observations, `context.RepositoryRoot` to locate repository files, and `context.CancellationToken` for asynchronous work. `CallAsync` returns the raw result (including tool failures), or null after recording a dispatcher exception; cancellation and artifact-write failures stop the run. Calls are sequential. Keep tool names and inputs in `request.json`; numbered directories avoid using arbitrary tool names as file paths. See the [runner documentation](../../../docs/exploration.md).

## Schema Markdown export review

The `export-schema` CLI is implemented; the [schema-export roadmap](../../../tasks/schema-markdown-export/roadmap.md) tracks its remaining audit and reviews against the [concept](../../../tasks/schema-markdown-export/Konzept.md). After the general audit, the first reviewer must use a temporary task-specific scenario for `CallAsync` schema observations. They must still run the actual `export-schema` CLI and inspect the actual exported files; do not substitute direct tool calls for an export.

Before inspection, state concrete expected content and navigation, then assess whether the observed output is what an agent would expect for the object and offline use. Inspect requests, every response content block and error state, joined text, and recorded exceptions; runner exit zero is not a quality verdict. Keep expectations/findings in the roadmap, not assertions or approved snapshots in scenarios. Existing MCP output must remain unchanged; compare export differences against the task's explicit presentation contract rather than changing tool output to match the export.

Keep the two sequential independent reviews: one compares a real DemoDB export to existing schema operations; the next creates a fresh export and reads only its exported directory during offline content review. Reuse `TestTempDirectory` from the test support as required by the task, using a fresh child such as `GetPath("dump")` for CLI output. Keep the owner alive through inspection and dispose it afterward. The owner marker makes the managed root nonempty. Do not put a required-empty export target directly in a retained runner directory, introduce another cleanup mechanism, or run cleanup before the reviewer has inspected the files. Any temporary scenario using that helper should reuse its source/support instead of creating a production dependency on the test suite.

Record review evidence and findings in the existing roadmap item, fix findings through implementation, and remove temporary scenario files after review. This runner preparation neither implements the export command nor completes any roadmap item.
