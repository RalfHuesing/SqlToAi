---
status: open
type: step-plan
task: sql-file-execution
step: 005
corrects: null
title: "Add ReadWrite autocommit execution mode"
epic: EPIC-02
estimated_risk: high
step_type: single
items: []
created_by: planer
created_by_model: GPT-5
created_by_model_knowledge_cutoff: not provided by runtime
created_at: 2026-08-29T09:16:50+02:00
related_to:
  - tasks/sql-file-execution/step-004/step-result.md
  - tasks/sql-file-execution/step-004/step-review.md
---

# Step 005: Add ReadWrite autocommit execution mode

## Context

- **Task:** `sql-file-execution`
- **Epic:** `EPIC-02` from `roadmap.md` — the atomic execution core exists;
  the missing transaction-selection branch must now support the requested
  `ReadWrite` autocommit behavior without weakening protected modes.
- **Concept reference:** `tasks/sql-file-execution/konzept.md`, sections
  “Neues MCP-Tool” (`use_transaction`), “Sicherheits-Guardrails”, and
  “Ausführung”.

## Current Project State (JIT Context)

Step-004 is approved and provides the only script coordinator,
`ScriptExecutionService`, plus the caller-owned `IQueryBatchExecutor` seam.
`ExecuteAtomicallyAsync` currently always opens one `ReadCommitted`
transaction, executes all batches through the existing query serializer, and
commits or rolls back once at the end. `ScriptExecutionRequest` has no
transaction-mode field yet.

The reusable execution seam currently requires a non-null
`DbTransaction`, and `DatabaseCommandExecutor.ExecuteSetOptionAsync` passes
that transaction to the statistics and `ROWCOUNT` commands. The existing
`QueryExecutionService` remains the single implementation of parameter
binding, row limits, result serialization, anonymization, and per-execution
metrics. Its normal single-query path always supplies an explicit transaction
and must keep that behavior.

`QuerySafetyValidator.ValidateBatchSafetyAsync` already preflights every
distinct batch before connection creation. It resolves the access level and
delegates mutation detection to `ReadOnlyGuard`; `ReadOnly` and
`ReadOnlyAnonymized` therefore cannot use an unguarded autocommit path. The
current script coordinator uses `TransactionIntegrityGuard` only for
non-write access. For an explicit `ReadWrite` transaction, a batch that
changes `@@TRANCOUNT` could otherwise break the promised all-or-nothing
boundary, so that existing guard must cover every explicit transaction mode.

`QueryExecutionResult` already retains `ElapsedMs`, `CpuTimeMs`, and
`LogicalReads` for every batch repetition, and `ScriptBatchExecutionResult`
preserves the batch and repetition metadata. This step therefore needs no
second metrics type or aggregation/report renderer; the later structured
report step can aggregate these values without losing detail.

The existing CodeMap already covers all affected production and test areas;
no new module or parallel execution helper is needed. The non-auto-fixable
constructor duplication in `tech-debt.md` is unrelated and remains out of
scope.

## Intention

Broaden the internal script execution entrypoint from an atomic-only
operation to a transaction-selecting operation with `use_transaction`
defaulting to `true`. `ReadWrite` with `false` must reuse the existing
connection, batch, parameter, row-limit, result, anonymization, and metrics
pipeline without creating an explicit transaction, so each command is
committed by provider autocommit semantics. All non-`ReadWrite` modes must
continue to use the rollback transaction regardless of the requested flag.

Keep the existing atomic behavior intact for `true`, and reject transaction
state changes in every explicit transaction mode before any commit is
performed. Do not expose the MCP tool or render Markdown in this step.

## Concrete Changes

### File 1: `src/SqlToAi/Database/IScriptExecutionService.cs` (lines 7-22)

- **What:** Rename the internal entrypoint to a mode-neutral
  `ExecuteAsync` and add `bool UseTransaction = true` to
  `ScriptExecutionRequest`, preserving the existing optional row-limit and
  parameter arguments. Update the contract and record documentation if
  present so the default and the protected-mode rule are explicit.
- **Why:** The current method name promises atomic execution even when the
  request selects autocommit. The request-level default keeps existing
  callers atomic while providing the future MCP dispatcher a direct mapping
  for `use_transaction`.

### File 2: `src/SqlToAi/Database/IQueryBatchExecutor.cs` (lines 8-21)

- **What:** Change only the caller-owned `QueryBatchExecutionArgs.Transaction`
  property to nullable. Keep the same connection, query, row-limit,
  anonymization, and parameter fields and the same executor method.
- **Why:** One established seam must serve both explicit transaction and
  provider-autocommit execution. The seam still performs no transaction
  lifecycle work; existing explicit callers continue to pass a non-null
  transaction.

### File 3: `src/SqlToAi/Database/DatabaseCommandExecutor.cs` (lines 12-20)

- **What:** Accept a nullable transaction in
  `ExecuteSetOptionAsync` and assign it unchanged to the command. Keep the
  helper as the sole implementation for `SET STATISTICS` and `SET ROWCOUNT`
  setup/reset commands.
- **Why:** The existing serializer must also work on an open connection with
  `Transaction == null`; changing this shared helper avoids a second
  autocommit-specific command path.

### File 4: `src/SqlToAi/Database/QueryExecutionService.cs` (lines 119-251)

- **What:** Update the existing batch adapter and serializer plumbing to
  accept the nullable transaction argument while leaving the public
  `IQueryExecutionService` lifecycle unchanged. Keep parameter binding,
  row-limit setup/reset, anonymization, serialization, and
  `PerformanceMetricsCalculator.ParseRunMessages` exactly on this path.
- **Why:** The script coordinator must reuse the approved query execution
  implementation. The ordinary single-query route remains explicitly
  transactional, so this change must not add autocommit behavior to
  `sql_execute_query`.

### File 5: `src/SqlToAi/Database/ScriptExecutionService.cs` (lines 40-224)

- **What:** Route the renamed entrypoint to the existing preflight and then
  select the execution mode as follows: `ReadWrite` plus
  `UseTransaction == false` uses one opened connection and no explicit
  transaction; every other successful access-level result uses the existing
  explicit transaction path. Keep all distinct-batch preflight before either
  connection path.
- **What:** In the autocommit path, build the same batch-execution arguments
  with `Transaction == null`, execute batches and `RepeatCount` repetitions
  sequentially through `IQueryBatchExecutor`, and perform no begin,
  commit, rollback, or transaction-integrity probe. A returned failure or
  exception stops later batches; previously executed commands remain
  committed by provider autocommit semantics and are not falsely reported as
  rolled back.
- **What:** In the explicit path, preserve one connection and one
  `ReadCommitted` transaction, row-limit resolution, parameters,
  `ReadOnlyAnonymized` selection, failure/cancellation rollback, and the
  single final commit for successful `ReadWrite`. Run the existing
  `TransactionIntegrityGuard` baseline/after-execution check for every
  explicit access level, including `ReadWrite`; route a changed count through
  `RejectViolationAsync` and never commit that transaction.
- **What:** Keep `ReadOnly` and `ReadOnlyAnonymized` rollback-only behavior
  even when `UseTransaction` is false, including their existing
  anonymization flag. Keep `None` and `SchemaOnly` rejected by the existing
  validator and do not introduce a new error code.
- **Why:** This closes the missing `use_transaction=false` contract while
  preserving the default-deny/read-only boundaries and the approved shared
  execution pipeline. It also prevents an explicit RW script from silently
  escaping its atomic boundary through transaction-control statements.
- **Boundary:** Do not add public MCP arguments, registration, CLI wiring,
  Markdown output, batch diagnostic snippets, or aggregate metric DTOs in
  this execution-core step.

### File 6: `tests/SqlToAi.Tests/Database/ScriptExecutionServiceTests.cs` (existing tests)

- **What:** Update callers and test names for the mode-neutral entrypoint,
  retain the existing default/explicit atomic tests, and add focused xUnit
  coverage for: successful `ReadWrite` autocommit with multiple batches
  (including an `ALTER DATABASE`-shaped batch) and null transactions;
  autocommit failure after an earlier successful batch stopping subsequent
  work without rollback; `use_transaction=false` being forced back to a
  rollback transaction for both `ReadOnly` and `ReadOnlyAnonymized`; and an
  explicit `ReadWrite` transaction-count change being rejected with no
  commit.
- **Why:** These cases pin the requested transaction-mode semantics,
  ReadWrite DDL exception, partial-commit behavior, protected-mode override,
  and the newly closed explicit integrity gap using the existing recording
  safety/executor doubles and `MockQueryConnectionFactory`.

### File 7: `tests/SqlToAi.Tests/Database/QueryExecutionServiceBatchTests.cs` (existing tests)

- **What:** Add a focused test that invokes the existing internal batch seam
  with `Transaction == null` and verifies the real serializer still binds
  parameters, applies/resets the row limit, returns a result, and records a
  null transaction on the reader command. Keep the existing non-null seam
  test as regression coverage for explicit transactions.
- **Why:** The coordinator tests alone would only prove that a fake executor
  received null; this test proves the shared production serializer and its
  setup commands accept the new optional transaction without duplicating
  query execution logic.

## Tests

- [ ] Update and run focused execution tests after all changes:
  `dotnet test tests/SqlToAi.Tests --filter "FullyQualifiedName~ScriptExecutionServiceTests|FullyQualifiedName~QueryExecutionServiceBatchTests"`.
- [ ] Run the existing single-query regression tests after all changes:
  `dotnet test tests/SqlToAi.Tests --filter "FullyQualifiedName~QueryExecutionServiceTests|FullyQualifiedName~QueryExecutionServiceTransactionTests|FullyQualifiedName~QueryExecutionServiceAnonymizationTests"`.
- [ ] Verify with AiNetLinter MCP before and after implementation: inspect
  feature context/metrics for `ScriptExecutionService`,
  `QueryExecutionService`, and `QuerySafetyValidator`; inspect references
  for the changed request and nullable seam; check scoped violations for
  `src/SqlToAi/Database`; and confirm no duplicate execution, transaction,
  safety, parameter, result, or metrics helper is introduced.
- [ ] Run `dotnet build SqlToAi.slnx` after all production and test changes;
  it must be green with zero warnings and zero errors.
- [ ] After all changes, the coder runs the complete test command exactly
  once before the code commit: `dotnet test SqlToAi.slnx`. Record the green
  result in `step-005/step-result.md`.
- [ ] If the coder provides that green full-suite evidence, the critic does
  not repeat `dotnet test SqlToAi.slnx`; the critic runs a focused command
  only if a concrete residual risk requires it.

## Definition of Done

- [ ] The internal script request defaults to transactional execution and
  carries the `use_transaction` choice without changing the future public
  response contract.
- [ ] `ReadWrite` with `UseTransaction == false` opens no explicit
  transaction, executes all batches/repetitions sequentially through the
  existing batch executor, and preserves provider autocommit semantics on
  success and failure.
- [ ] `ReadWrite` with the default/true flag remains one-transaction,
  all-or-nothing execution with one final commit; explicit transaction-count
  changes are rejected and never committed.
- [ ] `ReadOnly` and `ReadOnlyAnonymized` always use the rollback transaction
  and preserve their mutation guard and anonymization selection regardless
  of the requested flag; `None` and `SchemaOnly` remain rejected.
- [ ] The nullable transaction seam is accepted by the shared serializer,
  while the existing single-query path remains explicitly transactional and
  continues to use existing parameter, row-limit, result, anonymization,
  and per-execution metric logic.
- [ ] Focused xUnit tests cover mode selection, ReadWrite DDL-shaped
  autocommit, failure short-circuiting, protected-mode forcing, explicit RW
  integrity, and the real nullable batch seam.
- [ ] No new aggregate metrics implementation, public MCP/CLI surface,
  Markdown renderer, or error code is added in this step; existing
  `QueryExecutionResult` metrics remain available for the later report.
- [ ] `dotnet build SqlToAi.slnx` is green with zero warnings and errors.
- [ ] `dotnet test SqlToAi.slnx` was run exactly once by the coder after all
  changes and before the code commit, with the green result recorded in
  `step-005/step-result.md`; the critic follows the no-repeat rule.
- [ ] The coder updates `codemap.md`, writes `step-005/step-result.md`, sets
  this plan to the workflow's completed status, and creates the required
  German imperative Conventional Commit with the `[sql-file-execution]`
  suffix.

## Rules References

- `.agents/rules/AiNetLinter-McpWorkflow.mdc#Verbindliche Priorität` — C#
  symbols, callers, dependencies, metrics, violations, and duplicate checks
  require the matching AiNetLinter MCP query before supplemental text work.
- `.agents/rules/AiNetLinter-McpWorkflow.mdc#Werkzeugwahl` — use feature
  context, symbol bodies, references/impact, dependency, metrics, violations,
  and targeted duplicate checks for this execution change.
- `.agents/rules/AiNetLinter.mdc#Kurz-Stil` — keep nullable enablement,
  sealed concrete types, short flat methods, grouped inputs, and visible
  exception handling.
- `.agents/rules/AiNetLinter.mdc#Grenzwerte (Produktion)` — keep the
  coordinator, shared serializer, constructors, and dependencies within
  configured line, complexity, parameter, and footprint budgets.
- `.agents/rules/AiNetLinter.mdc#agent-resilience` — do not swallow
  cancellation, use asynchronous database operations, and keep rollback
  failures visible through the established logging/error paths.
- `.agents/rules/AiNetLinter.mdc#test-coverage` — changed production types
  require focused xUnit coverage or a coverage sentinel.
- `.agents/rules/SqlToAiRichtlinien.mdc#2. Architektur- & Guardrail-Konzepte (Constraints)` — preserve
  default-deny access levels, read-only rollback/anonymization, transaction
  integrity, least privilege, and the standardized error catalog.
- `.agents/rules/SqlToAiRichtlinien.mdc#3. Windows-Umgebung & Tool-Regeln` —
  use the repository's PowerShell-compatible build and test commands.
- `.agents/rules/SqlToAiRichtlinien.mdc#4. Updates, Dokumentation & Sprachen (Updates, Documentation & Languages)` —
  keep source artifacts in English, use xUnit v3, and defer public
  documentation until the MCP/tool contract is exposed in its planned epic.
- `.agents/rules/SqlToAiRichtlinien.mdc#5. Qualitätsdrift-Prävention & Tech Debt (AiNetLinter)` —
  maintain zero warnings, Result-based boundaries, linter conformity, and no
  unapproved duplicate execution or metrics implementation.

## Known Exceptions

- The intentional partial-commit behavior of `ReadWrite` autocommit is not
  an exception to the safety model: once a command has autocommitted, a later
  failure cannot roll it back. The later report/diagnostic step must expose
  the failing batch and the selected mode.
- No README or architecture-spec update is included because this step does
  not expose the MCP tool or its public argument contract; the synchronized
  public documentation belongs with that exposure and report work.

## Notes

- Reuse `SqlScriptBatchSplitter`, `SqlBatch`, `IQueryBatchExecutor`,
  `QueryExecutionService`, `DatabaseCommandExecutor`,
  `TransactionIntegrityGuard`, and the existing recording fakes. Do not add
  a second batch serializer, parameter binder, row-limit helper, metrics
  parser, or transaction helper.
- `use_transaction=false` is a `ReadWrite` capability, not a way to bypass
  the read-only rollback boundary. The effective mode must be derived from
  the validated `QuerySafetyCheckResult`, not from the request flag alone.
- Keep `ScriptBatchExecutionResult` and its per-execution
  `QueryExecutionResult` list unchanged in this step so source-line,
  repeat-count, result, anonymization, and raw metric data remain available
  to the later report boundary.
- `TD-001` remains untouched: it is non-auto-fixable, unrelated to this
  execution-mode change, and must not become a new epic or batch item.
