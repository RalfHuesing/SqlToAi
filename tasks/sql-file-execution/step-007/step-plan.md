---
status: done
type: step-plan
task: sql-file-execution
step: 007
corrects: null
title: "Expose sql_execute_file through MCP and CLI wiring"
epic: EPIC-04
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: gpt-5
created_by_model_knowledge_cutoff: not provided by runtime
created_at: 2026-08-29T10:30:33+02:00
related_to: [step-006]
---

# Step 007: Expose `sql_execute_file` through MCP and CLI wiring

## Bezug

- **Task:** `sql-file-execution`
- **Epic:** `EPIC-04` from `roadmap.md` — wire the already implemented script
  execution/report boundary into the public MCP and CLI surfaces.
- **Concept reference:** `konzept.md`, sections “Neues MCP-Tool”, “Wo im
  Projekt”, and the `sql_execute_file` Definition of Done.

## Aktueller Projektzustand (JIT-Kontext)

Step-006 is approved and completes EPIC-03. The internal execution boundary is
now `IScriptExecutionService.ExecuteAsync(ScriptExecutionRequest, ...)`, which
returns `ScriptExecutionReport`; `ScriptExecutionReportRenderer.Render` is the
existing Markdown output contract. `ScriptExecutionService` expects a
validated `SqlScriptFile`, so the public adapter must reuse the existing
`SqlScriptFileReader.Read(filePath, QueryExecutionOptions)` and propagate its
`Result<SqlScriptFile>` errors instead of creating another intake or report
contract.

The MCP layer currently exposes 16 tools. `McpConstants` owns all tool and
argument names. `SqlMcpToolRegistrations` builds the SDK collection used by the
real protocol `tools/list` surface and forwards typed lambda arguments through
one existing `ExecuteAsync` converter. `ToolDispatcher` owns a handler map for
the same 16 tools and already receives `IOptions<SqlToAiOptions>`; it currently
stores only `DatabasesOptions`, so the new handler must additionally retain the
existing `QueryExecutionOptions` and inject `IScriptExecutionService`.

`ToolRegistry` builds the canonical metadata list used by `Program` to create
the CLI command tree. `ToolListResult` has no production callers, so no new
custom MCP model or parallel `tools/list` path is needed. The registry and SDK
registration must nevertheless stay aligned. `Program.cs` already registers
`IScriptExecutionService` as a singleton, `ToolRegistry` as a singleton, and
`ToolDispatcher` through DI; no additional service registration is expected.

`ToolCommandFactory.AddOption` currently maps integers to nullable integer
options and all other schema types to string options. The new `parameters`
value can remain a JSON string because `SqlParameterBinder` explicitly accepts
JSON strings, but `use_transaction` must be mapped to a nullable boolean so
CLI `false` is not silently treated as the omitted/default value.

The current MCP and CLI scopes have zero AiNetLinter violations. Current type
metrics are within budget: `ToolDispatcher` 280 LOC / 763 AI-context lines,
`SqlMcpToolRegistrations` 311 LOC / 713 AI-context lines, `ToolRegistry` 268
LOC / 329 AI-context lines, and `ToolCommandFactory` 66 LOC / 105 AI-context
lines. The only tech-debt index entry, `TD-001`, concerns unrelated duplicate
constructors in database performance services, is non-auto-fixable, and is
outside this step.

## Intention

Expose one complete `sql_execute_file` call path through the existing public
surfaces: SDK registration, dispatcher routing, canonical registry metadata,
and the registry-driven CLI. The adapter must read local files with the
approved reader, invoke the approved execution service, and return the
existing rendered report without duplicating serialization, Markdown, intake,
or execution logic. Keep the scope limited to this tool's wiring; broader
verification and repository-wide documentation closure remain outside this
step.

## Konkrete Änderungen

### File 1: `src/SqlToAi/Mcp/McpConstants.cs` (tool and argument constants)

- **What:** Add `ToolExecuteFile = "sql_execute_file"`, `ArgFilePath =
  "file_path"`, and `ArgUseTransaction = "use_transaction"` in the existing
  tool-name and argument-key sections.
- **Why:** All MCP, registry, dispatcher, and CLI mappings must use the same
  canonical identifiers; no new string literals should be introduced in the
  individual surfaces.

### File 2: `src/SqlToAi/Mcp/ToolRegistry.cs` (canonical metadata)

- **What:** Add `BuildExecuteFile()` to the existing `BuildTools()` list and
  define the tool schema with the following properties: required string
  `file_path` and `database`; optional boolean `use_transaction` with the
  documented default `true`; optional integer `requested_row_limit`; and
  optional object `parameters`. Describe that the tool accepts local `.sql`
  files, supports multi-batch execution, and returns the structured Markdown
  report including transaction mode and diagnostics.
- **Why:** The registry drives the CLI command tree and is the canonical
  metadata representation for tool names, argument types, required arguments,
  and descriptions. Reuse `StringParam` and the existing schema records; do
  not add a second metadata or public output model.

### File 3: `src/SqlToAi/Mcp/SqlMcpToolRegistrations.cs` (SDK registration)

- **What:** Update the collection summary/count from 16 to 17 and add one
  `sql_execute_file` registration in the query-execution group. Use the
  established `McpServerTool.Create` pattern with descriptions on typed
  parameters for `file_path`, `database`, nullable `use_transaction`, nullable
  `requested_row_limit`, optional `parameters`, and the existing cancellation
  token. Populate the argument dictionary exclusively through
  `McpConstants`, omit nullable optional values when absent, and reuse the
  existing private `ExecuteAsync` adapter.
- **Why:** The SDK registration is the actual transport-facing source for the
  protocol `tools/list` collection and must expose exactly the same names and
  argument contract as `ToolRegistry`. Keeping the new lambda in a dedicated
  registration method preserves the existing method-size and registration
  conventions.

### File 4: `src/SqlToAi/Mcp/ToolDispatcher.cs` (file-tool adapter)

- **What:** Inject `IScriptExecutionService`, retain
  `options.Value.QueryExecution`, and add a handler for `ToolExecuteFile`.
  Implement the handler as a focused private async method rather than growing
  the constructor's initializer further. It must:

  1. read required `file_path` and `database` through the existing argument
     helpers and map `ArgumentException` to `InvalidParametersCode`;
  2. call `SqlScriptFileReader.Read` with the stored query-execution options and
     propagate any reader `SqlToAiError` through the existing MCP failure shape;
  3. create `ScriptExecutionRequest` with the validated file, optional row
     limit, optional parameter object, and `use_transaction` defaulting to
     `true` when omitted;
  4. await `IScriptExecutionService.ExecuteAsync` with the caller's
     cancellation token;
  5. render the returned report with
     `ScriptExecutionReportRenderer.Render(report)` and return one text content
     block; and
  6. set MCP `IsError` from `report.Status == ScriptExecutionStatus.Failed` so
     failed execution reports remain fully visible, including batch context,
     while file-intake failures remain catalogued `ToolCallResult.Failure`
     responses.

- **Why:** This connects the approved intake, execution, and report contracts
  without changing any transaction, safety, anonymization, metric, or
  single-query behavior. A report failure must not be reduced to a short error
  string, because the renderer carries the required batch number, source range,
  SQL snippet, and catalog error.

### File 5: `src/SqlToAi/Cli/ToolCommandFactory.cs` (minimal typed CLI mapping)

- **What:** Extend `AddOption` with the existing schema-type dispatch pattern
  for `boolean`, using `Option<bool?>` and returning its nullable parsed value.
  Preserve the current string handling for `object` so JSON parameter text is
  passed through to the already compatible `SqlParameterBinder`; preserve
  existing integer and other-type behavior outside this tool.
- **Why:** The registry automatically creates the CLI subcommand. The new
  `use_transaction=false` value must reach the dispatcher as a Boolean `false`
  rather than as a string that the current `GetBool` helper would ignore.
  This is the smallest change that makes the new tool's optional CLI contract
  faithful without introducing a separate CLI argument model or serializer.

### File 6: `tests/SqlToAi.Tests/Mcp/ToolDispatcherTestFakes.cs` (dispatcher seam)

- **What:** Extend `ToolDispatcherTestHelper.BuildDispatcher` with an optional
  `FakeScriptExecutionService`, update construction for the new dispatcher
  dependency, and add a fake that captures `ScriptExecutionRequest` and returns
  a supplied `ScriptExecutionReport`.
- **Why:** The dispatcher tests need to assert the public-to-internal mapping
  without opening a database or duplicating a service implementation.

### File 7: `tests/SqlToAi.Tests/Mcp/ToolDispatcherTests.cs` (routing and adapter behavior)

- **What:** Add focused xUnit v3 tests using temporary valid `.sql` files and
  the fake service:

  - a successful call forwards resolved file content, database, row limit,
    parameters, and `use_transaction=false`, then returns the rendered report;
  - an omitted `use_transaction` is forwarded as the existing default `true`;
  - a failed report returns `IsError=true` while retaining the Markdown header,
    failed batch, source range, and catalog error text; and
  - a missing file returns `FileNotFoundCode` without invoking the execution
    service.

- **Why:** These tests pin the adapter's argument/default/error semantics and
  prove that the Step-006 renderer is reused at the MCP boundary.

### File 8: `tests/SqlToAi.Tests/Mcp/ToolRegistryTests.cs` and `tests/SqlToAi.Tests/Mcp/McpModelsTests.cs`

- **What:** Update the expected tool count from 16 to 17 and include
  `ToolExecuteFile` in the canonical-name assertions. Add schema assertions for
  required `file_path`/`database`, boolean `use_transaction`, integer row
  limit, and object parameters; update the constants completeness test and its
  specification-count comment.
- **Why:** Registry metadata and centralized constants are independently
  guarded contracts and must not drift from the new public tool.

### File 9: `tests/SqlToAi.Tests/Mcp/McpObservabilityIntegrationTests.cs` (SDK collection)

- **What:** Update the enabled-tool count from 17 to 18 (17 SQL tools plus
  the feedback tool) and the disabled-observability count from 16 to 17. Assert
  that `ToolExecuteFile` is present in the SDK `ListToolsAsync` result. Add a
  focused protocol call assertion that the registration forwards the required
  and optional `sql_execute_file` arguments to the test dispatcher, using the
  existing stream-host fixture and fake dispatcher capture.
- **Why:** This verifies the actual ModelContextProtocol registration path,
  rather than only testing the dispatcher and registry in isolation.

### File 10: `tests/SqlToAi.Tests/Cli/ToolCommandFactoryTests.cs` (CLI contract)

- **What:** Add a test that invokes the registry-generated
  `sql_execute_file` subcommand with `file_path`, `database`,
  `--use_transaction false`, `--requested_row_limit`, and JSON parameter text;
  assert that the callback receives the canonical tool name, Boolean `false`,
  integer row limit, and the unchanged JSON parameter string. Keep the
  existing required-option and command-generation tests green.
- **Why:** This proves the shared registry produces a usable CLI command and
  specifically protects the only new type conversion required by the tool.

### File 11: `README.md` and `docs/architecture-spec.md` (scoped public contract sync)

- **What:** Synchronize only the newly exposed tool contract: update the
  documented tool count, add `sql_execute_file` with its five arguments and
  `use_transaction=true` default, describe local-file-only intake, protected
  access-level behavior, and the rendered Markdown report, and add one CLI
  invocation example. Keep the existing query/tool descriptions and file-size
  configuration/error-catalog text intact.
- **Why:** Public feature changes must keep the user-facing documentation
  aligned with the SDK schema, registry, and CLI. This is a narrow contract
  synchronization, not the full verification/documentation closure of EPIC-05.

### No expected change: `src/SqlToAi/Program.cs`

- **What:** Verify the existing singleton registration
  `IScriptExecutionService -> ScriptExecutionService`, `ToolRegistry`, and
  `IToolDispatcher -> ToolDispatcher`; do not add a duplicate registration or
  new composition-root abstraction.
- **Why:** The dependency graph already contains the execution service from
  Step-004/005. The new constructor dependency is resolved by the existing DI
  registration, so a source change would add no behavior.

## Tests

- [ ] `ToolDispatcherTests.ExecuteFile_ShouldForwardFileRequestAndRenderReport()`
  verifies local intake, all optional arguments, and reuse of the existing
  renderer.
- [ ] `ToolDispatcherTests.ExecuteFile_ShouldDefaultToTransaction()` verifies
  the omitted `use_transaction` default.
- [ ] `ToolDispatcherTests.ExecuteFile_ShouldReturnRenderedFailureReport()`
  verifies MCP error status without losing batch diagnostics.
- [ ] `ToolDispatcherTests.ExecuteFile_ShouldReturnFileErrorWithoutExecuting()`
  verifies reader error propagation and no service call.
- [ ] `ToolRegistryTests` and `McpModelsTests` verify the 17-tool count, name,
  required arguments, and exact schema types.
- [ ] `McpObservabilityIntegrationTests.ListTools_ShouldReturn_AllSqlToolsAndFeedbackTool()`
  verifies the 17 SQL-tool SDK collection plus observability feedback, and the
  new protocol-call test verifies argument forwarding.
- [ ] `ToolCommandFactoryTests.ExecuteFile_ShouldParseTypedOptions()` verifies
  Boolean false, integer row limit, and pass-through JSON parameters.
- [ ] Existing MCP dispatcher, registry, CLI, observability, and all Step-006
  report tests remain green.
- [ ] After all implementation and test changes, the coder runs
  `dotnet test SqlToAi.slnx` exactly once before the code commit and records
  the green result in `step-result.md`. The critic does not repeat this full
  command when that evidence is green; targeted checks are sufficient unless a
  concrete risk invalidates the evidence.

## Definition of Done

- [ ] `sql_execute_file` is present in `McpConstants`, the SDK tool collection,
  the canonical registry, and the registry-generated CLI command with matching
  names and argument types.
- [ ] The dispatcher reads local scripts through `SqlScriptFileReader`, calls
  `IScriptExecutionService`, forwards the documented defaults and optional
  values, and renders with `ScriptExecutionReportRenderer` without duplicating
  report or serialization logic.
- [ ] Failed script reports retain their full Markdown diagnostics and set MCP
  `IsError`; intake failures retain their catalog error codes.
- [ ] Existing DI registration is reused, existing single-query behavior is
  unchanged, and no custom MCP model or parallel execution/renderer helper is
  introduced.
- [ ] Scoped README and architecture-spec documentation are synchronized with
  the new public contract.
- [ ] `dotnet build SqlToAi.slnx` is green with zero warnings and errors.
- [ ] `dotnet test SqlToAi.slnx` is green and was executed exactly once by the
  coder after all changes and before the code commit.
- [ ] AiNetLinter MCP reports no relevant violations and the changed symbols
  remain within configured budgets.
- [ ] The coder updates the task CodeMap if the implementation introduces a
  new relevant area, writes `step-result.md`, commits with a German imperative
  Conventional Commit subject carrying `[sql-file-execution]`, and changes
  this plan to `done (pending audit)`.
- [ ] The critic independently reviews the public schema, argument/default
  mapping, report/error semantics, and targeted tests without repeating the
  full test command after a green coder gate.

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#2. Architektur- & Guardrail-Konzepte
  (Constraints)` — preserve default-deny access, protected-mode rollback,
  anonymization, and the standardized error catalog while exposing the new
  tool.
- `.agents/rules/SqlToAiRichtlinien.mdc#3. Windows-Umgebung & Tool-Regeln` —
  use PowerShell-compatible build/test commands and keep the stdio MCP model.
- `.agents/rules/SqlToAiRichtlinien.mdc#4. Updates, Dokumentation & Sprachen
  (Updates, Documentation & Languages)` — use xUnit v3, synchronize the two
  public documentation files, write repository artifacts in English, and use
  repository-relative Markdown links.
- `.agents/rules/SqlToAiRichtlinien.mdc#5. Qualitätsdrift-Prävention & Tech
  Debt (AiNetLinter)` — reuse `Result` at the intake boundary, keep zero
  warnings, avoid duplicate contracts/renderers, and use AiNetLinter MCP for
  semantic checks.
- `.agents/rules/AiNetLinter.mdc#Kurz-Stil` — keep concrete classes sealed,
  nullable-enabled, asynchronous where applicable, and avoid silent catches or
  blocking access.
- `.agents/rules/AiNetLinter.mdc#Grenzwerte (Produktion)` — keep the new
  handler/registration helpers within file, method, complexity, parameter,
  dependency, and AI-context budgets.
- `.agents/rules/AiNetLinter.mdc#agent-resilience` — preserve cancellation and
  visible catalogued failures; do not catch cancellation as a normal error.
- `.agents/rules/AiNetLinter.mdc#test-coverage` — maintain test sentinels and
  focused xUnit coverage for the new dispatcher path.
- `.agents/rules/AiNetLinter-McpWorkflow.mdc#Verbindliche Priorität` and
  `#Werkzeugwahl` — use AiNetLinter MCP first for C# symbols, references,
  dependencies, metrics, and violations, then run targeted post-edit checks.

## Bekannte Ausnahmen

- `Program.cs` is intentionally not modified because the existing
  `IScriptExecutionService` singleton registration already satisfies the new
  constructor dependency; the coder should verify resolution through build and
  the existing composition root.
- `ToolListResult` and the custom `McpModels` transport list are not wired into
  production `tools/list`; the SDK collection is the actual protocol source,
  while `ToolRegistry` remains the shared CLI metadata source. This step keeps
  both existing surfaces aligned without introducing a dead parallel path.
- `TD-001` remains untouched because it is unrelated, non-auto-fixable, and
  requires an architectural decision.
- No separate file-validation, file-performance, public report, or parameter
  serializer contract may be introduced; the step reuses the contracts from
  Steps 003–006.

## Code-Skizze (optional)

```csharp
// Conceptual flow only; use the existing reader, service, renderer, and Result/error helpers.
Result<SqlScriptFile> file = SqlScriptFileReader.Read(filePath, _queryExecutionOptions);
ScriptExecutionReport report = await _scriptExecutionService.ExecuteAsync(
    new ScriptExecutionRequest(file.Value, database, rowLimit, parameters, useTransaction), ct);
return new ToolCallResult
{
    Content = [new ToolContent { Type = "text", Text = ScriptExecutionReportRenderer.Render(report) }],
    IsError = report.Status == ScriptExecutionStatus.Failed
};
```

## Notes

- The SDK lambda should use nullable optional arguments so omitted values do
  not enter the argument dictionary; the dispatcher owns the effective
  `use_transaction=true` default and protected-mode enforcement remains inside
  `ScriptExecutionService`.
- Keep `ToolDispatcher`'s existing `sql_execute_query` content-block order and
  text unchanged. Do not route the new report through the generic `CallAsync`
  helper because its `Result<T>` contract cannot carry this report boundary.
- Keep the new registration separate from `RegisterQueryExecutionTools` if
  needed to avoid pushing that method beyond the AiNetLinter method budget.
- Do not add an explicit transaction, safety validator, anonymizer, metric
  parser, JSON-lines serializer, or Markdown renderer in the MCP layer. Those
  responsibilities already belong to the Step-004–006 services and contracts.
- Before finalizing the implementation, the coder should run the required
  AiNetLinter semantic checks on changed symbols and the scoped MCP/CLI
  violations, then record the evidence in `step-result.md`.
