# Tool exploration

[SqlToAi.Exploration](../tools/SqlToAi.Exploration/) is a console runner for agent experiments. It calls the real `IToolDispatcher` directly with the production configuration and DI factories. There is no MCP transport, server loop, handshake, or client process. The scenario and artifact conventions follow `AiNetCodeNavigator.Exploration`; SqlToAi needs no polling or protocol paging machinery.

Scenarios collect observations without assertions, approved snapshots, or a required green result. They run only when explicitly invoked, outside the test suite and CI. Focused xUnit tests verify the runner's infrastructure separately.

## Run

From the repository root in PowerShell 7:

```powershell
pwsh -File scripts/explore.ps1 -List
pwsh -File scripts/explore.ps1 -Scenario ExploreSqlTools
pwsh -File scripts/explore.ps1 -Scenario ExploreSqlTools -TimeoutSeconds 180
```

The wrapper propagates the console exit code. Direct invocation is also available:

```powershell
dotnet run --project tools/SqlToAi.Exploration -- --list
dotnet run --project tools/SqlToAi.Exploration -- . ExploreSqlTools 300
```

Exit `0` means the scenario finished technically, even when calls returned `IsError=true` or a dispatcher call threw an exception. The runner records those observations and continues. Exit `1` means a scenario/runtime/artifact-writing failure or timeout; exit `2` means invalid runner arguments. Timeout must be between 1 and 3600 seconds and uses cooperative cancellation. A successful exit is not an assessment of output quality or database availability; inspect the artifacts.

## Configuration and baseline

The runner reads the required `src/SqlToAi/appsettings.json`, the optional `appsettings.<DOTNET_ENVIRONMENT or Production>.json` in that directory, and the same environment-variable provider and `ConfigurationResolver` as the executable. It reuses the actual application service registrations and security policies. It does not migrate or rewrite source configuration, configure the executable's Serilog sinks, or run log retention. Existing configuration value/file expansion semantics remain unchanged; run from the repository root. Credentials are never copied into the run manifest.

`ExploreSqlTools` is the permanent baseline. It exercises all 17 SQL tools against `DemoDB`: database/object discovery, `dbo.FakeProjects` schema and details, `dbo.spFakeSysTan` parameters, query validation, parameterized execution, a catalog query, a two-batch SQL file, differing-query comparison, performance, a benchmark, and index suggestions. It also records a missing trigger, a missing argument, an unknown tool, and invalid SELECT syntax, followed by another valid query to show continuation. It never executes the procedure, creates a trigger/index, or changes business records or schema. Query output uses constants and catalog metadata.

The baseline retains the configured access level: the template gives `DemoDB` `ReadWrite`, so its SELECT-only script can report `ReadWriteAtomic`. There is no exploration-level mutation guard or automatic access override. Review new scenarios' inputs accordingly. The normal application access checks and guards apply; index suggestions are only read, never executed. A different DemoDB schema or permissions can produce useful error observations rather than a failed scenario.

## Artifacts

Each run creates a retained, git-ignored directory under `temp/exploration/<ScenarioName>/<UTC-timestamp>/` and prints its absolute path. Retained observation artifacts use ordinary directories so agents can inspect them after the process exits; they are not managed test workspaces.

- `run.json`: scenario, source configuration directory, resolved DemoDB access level and timeout; no configuration/credentials dump.
- `01/`, `02/`, ...: one directory per call. `request.json` contains its tool name and typed arguments; `response.json` preserves the complete result, including every content block and `isError`; `response.txt` joins all content text in order, including notices and metrics.
- A call's `error.txt` captures a thrown dispatcher exception instead of a response. Later calls still run.
- Root `error.txt` records a technical run failure or timeout when the artifact directory is writable. If setup or writing that file fails, stderr reports the technical failure and exit remains `1`.
- Baseline `read-only.sql` contains the exact SELECT-only script passed to `sql_execute_file`.

Inputs, results, metadata, and exception text can contain sensitive data. These local files preserve observed content, not a redacted report. Review them before sharing. Delete retained runs when no longer needed.

For new experiments, follow [scenario authoring](../tools/SqlToAi.Exploration/Scenarios/README.md). The future schema-export review still invokes the actual export CLI and inspects its generated files; direct dispatcher calls supply supplementary schema evidence.
