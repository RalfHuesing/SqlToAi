---
status: done
type: step-plan
task: sql-file-execution
step: 006
corrects: null
title: "Structured script execution report and batch diagnostics"
epic: EPIC-03
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: gpt-5
created_by_model_knowledge_cutoff: not provided by runtime
created_at: 2026-08-29
related_to: [step-005]
---

# Step 006: Structured script execution report and batch diagnostics

## Bezug

- **Task:** `sql-file-execution`
- **Epic:** `EPIC-03` from `roadmap.md` — the internal execution core now
  exists, but its per-batch results are not yet exposed as a complete script
  report with aggregate metrics and actionable failure context.
- **Konzept-Referenz:** `konzept.md` “Strukturiertes Markdown-Ausgabeformat”,
  “Ausführung”, and the error-reporting criteria in “Definition of Done”.

## Aktueller Projektzustand (JIT-Kontext)

`step-005` is approved and completes EPIC-02. `ScriptExecutionService` currently
returns `Result<IReadOnlyList<ScriptBatchExecutionResult>>`; the internal result
keeps each original `SqlBatch` and its `QueryExecutionResult` repetitions. Each
`QueryExecutionResult` already contains JSON-lines data, row count, elapsed
milliseconds, CPU milliseconds, logical reads, and anonymization metadata.

The execution service still discards the successful batch prefix when a later
batch fails and exposes neither the failed batch's source metadata nor the
selected transaction mode. There is also no total metric aggregation or
script-level Markdown renderer. The only production consumer of
`IScriptExecutionService` is the DI registration in `Program.cs`; no MCP, CLI,
or registry consumer constrains this internal boundary yet. The existing
`ToolDispatcher` single-query formatting must remain unchanged.

The current database-scope AiNetLinter baseline is clean (10/10 safeguard,
zero violations). The drift audit found only the pre-existing non-auto-fixable
`TD-001` constructor clone, which is unrelated and remains outside this step.

## Intention

Introduce the internal structured report boundary that the future MCP adapter
can render or map to a tool result. The report must represent both complete and
failed executions, retain ordered batch/source context and successful results,
aggregate the existing per-execution metrics without adding a second metrics
parser, and render the concept's Markdown success and diagnostic sections.

Expected execution failures become report data at this internal report-producing
boundary so a failed batch can be reported together with the already completed
prefix; the lower-level validator and batch executor continue to use the
existing `Result<T>` contracts. Cancellation remains exceptional and must still
be rethrown.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Database/ScriptExecutionReport.cs` (new)

- **Was:** Define internal sealed report contracts and semantic enums for the
  overall execution status, per-batch status, and effective transaction mode.
  `ScriptExecutionReport` must carry the resolved script path and encoding,
  database name, status, mode, total elapsed milliseconds, aggregated CPU time
  and logical reads, ordered batch reports, and an optional catalogued error.
  Each `ScriptBatchReport` must carry its one-based batch number, the original
  `SqlBatch` (therefore source start/end lines and repeat count), status,
  completed `QueryExecutionResult` values, and an optional catalogued error.
- **Warum:** The later MCP surface needs one stable internal contract that
  preserves source and result detail for both success and failure without
  modifying the existing `QueryExecutionResult` or inventing a parallel batch
  serializer. The mode enum must distinguish ReadWrite atomic,
  ReadWrite provider-autocommit, ReadOnly rollback, anonymized ReadOnly
  rollback, and not-started/preflight cases.

### Datei 2: `src/SqlToAi/Database/ScriptExecutionReportFactory.cs` (new)

- **Was:** Add a small internal factory/aggregator used by the execution
  service to construct reports from ordered batch outcomes. Sum
  `ElapsedMs`, `CpuTimeMs`, and `LogicalReads` from every retained successful
  repetition for the script-level metrics; preserve each repetition's row/data
  and anonymization fields in its batch report. Keep catalog error codes and
  messages unchanged, and allow a report to contain a failed batch followed by
  `NotExecuted` batches. Do not parse SQL Server statistics again or introduce
  configurable magic limits.
- **Warum:** This centralizes aggregation and report invariants, keeps the
  coordinator's control flow within the linter budgets, and ensures metrics are
  neither averaged incorrectly nor lost on an autocommit partial failure.

### Datei 3: `src/SqlToAi/Database/ScriptExecutionReportRenderer.cs` (new)

- **Was:** Implement an internal static Markdown renderer over
  `ScriptExecutionReport`. Render a header containing script path, encoding,
  database, overall status, effective transaction mode, `elapsed_ms`,
  `cpu_time_ms`, and `logical_reads`. Render every ordered batch with its
  number, `StartLine`–`EndLine`, status, repeat execution details, row counts,
  existing JSON-lines data, and anonymization/token metadata when present. For
  a failure, render the first actionable error with its catalog code/message,
  failed batch number and source line range when available, plus the failing
  batch text as a SQL code block; show later batches as not executed. Preserve
  the existing result text and handle empty data without inventing a result
  set.
- **Warum:** This directly implements the structured Markdown contract while
  keeping presentation out of the execution and single-query paths. Use
  Markdown escaping/fencing that cannot merge metadata with SQL or result
  content, and do not log or expose additional raw data beyond the report
  contract.

### Datei 4: `src/SqlToAi/Database/IScriptExecutionService.cs` (existing lines 7–23)

- **Was:** Replace the list-only return type of `ExecuteAsync` with the new
  `ScriptExecutionReport` boundary and remove or supersede the obsolete
  `ScriptBatchExecutionResult` contract. Keep `ScriptExecutionRequest`
  unchanged, including the `UseTransaction = true` default and validated
  `SqlScriptFile` input.
- **Warum:** A list-only `Result<T>` cannot carry partial batch outcomes on
  failure. The report is the requested diagnostic value at this internal
  boundary; the lower-level `Result<T>` values remain intact for safety and
  batch execution, and a future MCP adapter can translate report status to its
  protocol result without changing this step's public surface.

### Datei 5: `src/SqlToAi/Database/ScriptExecutionService.cs` (existing lines 40–254)

- **Was:** Adapt the coordinator to build the report while preserving all
  approved EPIC-02 behavior: split and preflight all batches before opening a
  connection; derive the effective transaction mode from the validated access
  level and `UseTransaction`; retain explicit transaction integrity checks;
  preserve ReadOnly/ReadOnlyAnonymized rollback and anonymization; and preserve
  ReadWrite provider-autocommit semantics. Record one-based batch outcomes in
  order. On a preflight rejection, identify the rejected batch and mark all
  execution batches as not executed. On a runtime failure, retain successful
  earlier repetitions/batches, attach the catalog error to the failed batch,
  mark the remaining batches as not executed, and keep the existing rollback or
  partial-commit semantics. Map connection/commit failures to a report-level
  error without fabricating a batch number. Keep the existing logging and
  cancellation behavior.
- **Warum:** The report must describe what actually happened, including the
  difference between an atomic failure and an autocommit failure, without
  weakening any guardrail or changing the existing single-query pipeline.
  Dependency registration in `Program.cs` remains valid because the service
  type and lifetime do not change.

### Datei 6: `tests/SqlToAi.Tests/Database/ScriptExecutionReportFactoryTests.cs` (new)

- **Was:** Add focused xUnit v3 tests for aggregate sums across multiple batches
  and repeat executions, preservation of script metadata and source ranges,
  ordered success/failure/not-executed statuses, and retention of the original
  catalog error and anonymization/result fields.
- **Warum:** The pure aggregation contract can be verified without database
  infrastructure and protects the metric semantics used by the renderer.

### Datei 7: `tests/SqlToAi.Tests/Database/ScriptExecutionReportRendererTests.cs` (new)

- **Was:** Add focused xUnit v3 tests for a successful multi-batch report,
  asserting the metadata header, effective mode, aggregate metrics, source
  ranges, repeat details, JSON-lines result block, and anonymization note. Add
  failure coverage asserting the failed batch number, source lines, SQL
  snippet, catalog error code/message, and not-executed later batches. Cover a
  report-level error without a failed batch and content containing Markdown
  fence/inline-code delimiters.
- **Warum:** These tests pin the externally consumable Markdown shape and the
  diagnostic fields required by `konzept.md` before MCP wiring is introduced.

### Datei 8: `tests/SqlToAi.Tests/Database/ScriptExecutionServiceTests.cs` (existing)

- **Was:** Update existing assertions to the report boundary while retaining
  all approved transaction, safety, anonymization, repeat, integrity, and
  cancellation cases. Add explicit tests for a three-batch runtime failure
  retaining the successful prefix and marking the failed/later batches;
  ReadWrite autocommit failure showing the autocommit mode and committed
  prefix; atomic failure showing the atomic mode and rollback behavior; and
  preflight rejection identifying the rejected batch without opening a
  connection. Add a service-level success case proving that metrics from
  multiple repetitions reach the aggregate report.
- **Warum:** This verifies that the new contract is populated from the real
  execution coordinator rather than only from isolated report fixtures, while
  ensuring EPIC-02 semantics do not regress.

## Tests

- [ ] `ScriptExecutionReportFactoryTests.BuildReport_SumsMetricsAcrossBatchRepetitionsAndPreservesDetails()` verifies metric sums and retained execution data.
- [ ] `ScriptExecutionReportFactoryTests.BuildFailureReport_OrdersFailedAndNotExecutedBatches()` verifies batch numbering, source metadata, and catalog error preservation.
- [ ] `ScriptExecutionReportRendererTests.RenderSuccessReport_ContainsMetadataMetricsBatchesAndJsonLines()` verifies the success Markdown contract.
- [ ] `ScriptExecutionReportRendererTests.RenderFailureReport_ContainsBatchDiagnosticsAndNotExecutedMarkers()` verifies batch number, line range, SQL snippet, error code/message, and later-batch status.
- [ ] `ScriptExecutionReportRendererTests.RenderReportLevelFailure_UsesErrorWithoutInventingBatchContext()` covers connection/commit-style failures without a batch.
- [ ] `ScriptExecutionServiceTests.ExecuteAsync_RuntimeFailureRetainsPrefixAndMarksRemainingBatchesNotExecuted()` covers real coordinator outcome tracking.
- [ ] `ScriptExecutionServiceTests.ExecuteAsync_AutocommitFailureReportShowsModeAndCommittedPrefix()` covers the Step-005 partial-commit semantics.
- [ ] Existing `ScriptExecutionServiceTests` for ReadOnly rollback/anonymization, explicit transaction integrity, repeats, empty scripts, and cancellation remain green after the contract adaptation.

## Definition of Done

- [ ] `ScriptExecutionReport` and `ScriptBatchReport` represent the script metadata, effective mode, overall status, aggregate metrics, ordered batch statuses, result sets, and catalogued diagnostics required by `konzept.md`.
- [ ] Successful results retain all existing per-repetition JSON/data, row, metrics, and anonymization information; script CPU and logical reads are sums of retained executions.
- [ ] Preflight and runtime failures expose the failed batch number, original source line range, original batch text, and unchanged catalog error code/message when a batch exists; later work is explicitly marked not executed.
- [ ] Existing EPIC-02 transaction selection, rollback/commit, integrity protection, ReadOnly guardrails, parameter binding, row limits, and cancellation behavior are unchanged.
- [ ] `dotnet build SqlToAi.slnx` is green with zero warnings and errors.
- [ ] After all implementation changes, the coder runs `dotnet test SqlToAi.slnx` exactly once before the code commit; it is green and the command/result are recorded in `step-result.md`.
- [ ] AiNetLinter MCP checks after the edit report no relevant violations and the changed symbols remain within configured complexity/line/footprint budgets.
- [ ] The coder writes `step-result.md`, updates the task CodeMap for the new report boundary, and commits the implementation with a German imperative Conventional Commit subject carrying `[sql-file-execution]`.
- [ ] `step-plan.md` is changed to `done (pending audit)` only after the implementation and result artifact are complete.
- [ ] The critic does not repeat `dotnet test SqlToAi.slnx` when the coder has supplied the required green pre-commit evidence; the critic performs only targeted checks for concrete report/diagnostic risks.

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#2. Architektur- & Guardrail-Konzepte (Constraints)` — preserve default-deny access, read-only rollback/anonymization, transaction integrity, and the standardized error catalog.
- `.agents/rules/SqlToAiRichtlinien.mdc#3. Windows-Umgebung & Tool-Regeln` — use the repository's PowerShell-compatible build and test commands.
- `.agents/rules/SqlToAiRichtlinien.mdc#4. Updates, Dokumentation & Sprachen (Updates, Documentation & Languages)` — write source/task artifacts in English, use xUnit v3, keep user-facing documentation scoped to the later public contract, and avoid absolute Markdown links.
- `.agents/rules/SqlToAiRichtlinien.mdc#5. Qualitätsdrift-Prävention & Tech Debt (AiNetLinter)` — maintain zero warnings, Result-based lower-level boundaries, linter conformity, and no new duplicate serializer/metrics implementation.
- `.agents/rules/AiNetLinter.mdc#Kurz-Stil` — keep new concrete types sealed, nullable-enabled, asynchronous where applicable, and methods short and flat.
- `.agents/rules/AiNetLinter.mdc#Grenzwerte (Produktion)` — respect method/file/complexity/parameter/public-member and AI-context budgets; use focused helper types if the coordinator grows.
- `.agents/rules/AiNetLinter.mdc#agent-resilience` — preserve cancellation propagation and visible error handling; do not introduce blocking access or silent catches.
- `.agents/rules/AiNetLinter.mdc#test-coverage` — add a test sentinel and focused xUnit coverage for each new complex report type.
- `.agents/rules/AiNetLinter-McpWorkflow.mdc#Verbindliche Priorität` and `#Werkzeugwahl` — use AiNetLinter MCP first for changed C# symbols, impact/dependencies, metrics, violations, and targeted post-edit validation.

## Bekannte Ausnahmen

- No README or `docs/architecture-spec.md` change is included: this step creates an internal report boundary and renderer but does not expose `sql_execute_file` through MCP, the canonical registry, or the CLI. Public contract documentation remains outside this internal step.
- `TD-001` is deliberately untouched because it is non-auto-fixable, predates this task's report work, and requires an architectural decision; it must not become a new Epic or batch item.
- The critic must not rerun the full test command after a green coder gate; targeted report-rendering or failure-context checks are sufficient unless a concrete risk invalidates the evidence.

## Notes

- Reuse `SqlScriptBatchSplitter`, `SqlBatch`, `SqlScriptFile`, `QueryExecutionResult`, `SqlToAiError`, `IQuerySafetyValidator`, `IQueryBatchExecutor`, `TransactionIntegrityGuard`, and the existing test doubles. Do not add a second serializer, parameter binder, row-limit helper, statistics parser, transaction helper, or MCP output model.
- The report-producing service boundary may return a report with `Status = Failed` for expected execution failures so partial diagnostics remain available; do not change the global `Result<T>` implementation or make low-level safety/executor failures throw.
- `ScriptTransactionMode` must be resolved from the validated `QuerySafetyCheckResult` plus `UseTransaction`; the request flag alone must never bypass protected-mode rollback.
- Use the original `SqlBatch.Text`, `StartLine`, `EndLine`, and `RepeatCount`; do not expand or rewrite batches merely for rendering. Aggregate only successful `QueryExecutionResult` values retained in the report.
- Keep `ToolDispatcher`'s existing single-query content block order and text unchanged. No `sql_execute_file` constant, registration, registry entry, CLI command, DI dependency expansion, or public MCP model belongs in this step.
