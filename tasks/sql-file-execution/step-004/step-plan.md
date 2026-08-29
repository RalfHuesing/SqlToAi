---
status: done (pending audit)
type: step-plan
task: sql-file-execution
step: 004
corrects: null
title: "Atomic guarded execution of script batches"
epic: EPIC-02
estimated_risk: high
step_type: single
items: []
created_by: planer
created_by_model: GPT-5
created_by_model_knowledge_cutoff: not provided by runtime
created_at: 2026-08-29T08:40:58+02:00
related_to:
  - tasks/sql-file-execution/step-003/step-result.md
  - tasks/sql-file-execution/step-003/step-review.md
---

# Step 004: Atomic guarded execution of script batches

## Context

- **Task:** `sql-file-execution`
- **Epic:** `EPIC-02` from `roadmap.md` — the first executable slice is an
  atomic, guarded core for already-read and `GO`-split script batches.
- **Concept reference:** `tasks/sql-file-execution/konzept.md`, sections
  “Sicherheits-Guardrails” and “Ausführung”.

## Current Project State (JIT Context)

`SqlScriptFileReader` and `SqlScriptBatchSplitter` are complete after
`step-003`; the splitter returns `SqlBatch` values with inclusive source-line
ranges and an unexpanded `RepeatCount`. They are internal components and can
be consumed directly by the execution layer without another file or parser
abstraction.

The existing single-query path is centered on
`src/SqlToAi/Database/QueryExecutionService.cs:91-117` for the shared safety
pipeline, `:120-181` for connection/transaction ownership, and `:198-255`
for command execution, row limiting, parameter binding, result serialization,
and statistics parsing. Its anonymization and schema-origin helpers remain in
`src/SqlToAi/Database/QueryExecutionService.Anonymization.cs:17-227`.
`IQueryExecutionService` is intentionally a single-query public contract and
must not be called once per batch: doing so would create separate connections
and transactions and would make an atomic script impossible.

`QuerySafetyValidator` currently performs the six-stage pipeline at
`src/SqlToAi/Database/QuerySafetyValidator.cs:65-117` and always rejects
multiple statements at stage 6. `ReadOnlyGuard` already inspects the complete
AST and rejects mutating statements, while
`TransactionIntegrityGuard` provides the existing `@@TRANCOUNT` check and
defensive rollback. `SqlParameterBinder`, `QueryExecutionResult`, and
`PerformanceMetricsCalculator.ParseRunMessages` are already reusable and
must remain the only implementations of those concerns.

The test project has `InternalsVisibleTo` through
`src/SqlToAi/SqlToAi.csproj:33-35`; `FakeDbConnection` and
`FakeDbTransaction` already expose caller-owned transaction state. The
current semantic baseline is clean: the database scope has safeguard score
10/10, zero violations, and the duplicate audit reports only the pre-existing
constructor cluster recorded as `TD-001`.

## Intention

Introduce one internal execution seam that runs a validated batch on a
caller-owned explicit transaction, and one service that uses that seam to
execute a complete pre-read script atomically. Every distinct batch is
validated before the connection is opened; batches and `GO n` repetitions are
then executed sequentially on the same transaction, with ReadWrite commit,
read-only rollback, anonymization, and transaction-integrity protection.

This step deliberately stops at the execution result boundary. It does not
add the public MCP/CLI surface, Markdown rendering, diagnostic enrichment,
autocommit mode, or a second implementation of query serialization.

## Concrete Changes

### File 1: `src/SqlToAi/Database/IQueryBatchExecutor.cs` (new, planned lines 1-55)

- **What:** Add an internal `IQueryBatchExecutor` and an internal sealed
  `QueryBatchExecutionArgs` record. The record carries the target
  `DbConnection`, a non-null caller-owned `DbTransaction`, database name,
  batch SQL, effective row limit, anonymization flag, and parameter object.
  The interface exposes one `ExecuteBatchAsync` method taking that record and
  a cancellation token.
- **Why:** The script coordinator needs the existing command/result path on a
  shared transaction. A small internal contract prevents it from calling the
  public single-query service or copying its parameter, row-limit,
  serialization, anonymization, and metric logic. The explicit transaction
  requirement keeps this step limited to the atomic path.
- **Contract:** The executor performs no safety validation and never begins,
  commits, or rolls back the supplied transaction. Execution exceptions remain
  available to the owning coordinator for rollback and catalog mapping; the
  ordinary result is the existing `Result<QueryExecutionResult>` shape.

### File 2: `src/SqlToAi/Database/QueryExecutionService.cs` (existing lines 36, 120-181, 189-255)

- **What:** Make `QueryExecutionService` implement the internal
  `IQueryBatchExecutor` through an explicit interface implementation (so no
  new public member is added). Replace the private `ExecutionArgs` record with
  the shared `QueryBatchExecutionArgs` and add the adapter that delegates to
  the existing `ExecuteAndSerializeAsync` path. Change
  `ExecuteQueryInTransactionAsync` to construct the shared request and call
  the adapter; keep its existing connection ownership,
  `TransactionIntegrityGuard` probes, commit/rollback rules, cancellation,
  logging, and exception-to-error mapping.
- **What:** Change only the execution-argument type of
  `ExecuteAndSerializeAsync`; retain its existing calls to
  `DatabaseCommandExecutor`, `SqlParameterBinder`,
  `PerformanceMetricsCalculator.ParseRunMessages`, and the partial-class
  anonymization helpers. No new serializer, binder, metrics parser, or
  anonymizer is permitted.
- **Why:** This exposes the already-approved implementation to the script
  coordinator without changing the public `IQueryExecutionService` contract
  or the behavior of `sql_execute_query`.

### File 3: `src/SqlToAi/Database/QuerySafetyValidator.cs` (existing lines 24-43 and 65-117)

- **What:** Extend `IQuerySafetyValidator` with an explicit
  `ValidateBatchSafetyAsync` operation and implement it through one shared
  private pipeline. The existing `ValidateQuerySafetyAsync` keeps its current
  strict single-statement behavior. The batch operation runs the same input,
  whitelist, access-level, read-only, and error-catalog checks, but skips only
  the final multiple-statement rejection because one `SqlBatch` may contain
  several intended T-SQL statements.
- **What:** Keep `ReadOnlyGuard` before the relaxed statement-count boundary:
  a multi-statement read-only batch succeeds only when every statement is
  read-only, while a mutating or procedure-execution batch still returns
  `WriteOperationBlocked`. `SchemaOnly` and `None` remain rejected for this
  execution service.
- **Why:** This is the narrow script-specific safety boundary described by the
  concept. It prevents the coordinator from bypassing the existing guardrail
  pipeline or weakening the single-query path.

### File 4: `src/SqlToAi/Database/IScriptExecutionService.cs` (new, planned lines 1-75)

- **What:** Add an internal `IScriptExecutionService` with
  `ExecuteAtomicallyAsync(ScriptExecutionRequest, CancellationToken)`.
  Define internal sealed request/result records in the same file: the request
  contains the validated `SqlScriptFile`, target database, optional requested
  row limit, and optional parameters; each
  `ScriptBatchExecutionResult` contains the original `SqlBatch` and the
  `QueryExecutionResult` list produced for its `RepeatCount` executions.
- **Why:** The contract preserves source-line and repeat metadata for the
  later diagnostic/report boundary while keeping this step's return value
  internal and structured. Per-execution metrics remain in the existing
  `QueryExecutionResult`; no report DTO is introduced here.

### File 5: `src/SqlToAi/Database/ScriptExecutionService.cs` (new, planned lines 1-260)

- **What:** Implement `IScriptExecutionService` as a sealed service using the
  existing connection factory, `IQuerySafetyValidator`,
  `IQueryBatchExecutor`, query-execution options, and an error logger. Split
  `request.ScriptFile.Text` with `SqlScriptBatchSplitter`; return the existing
  `InvalidParameters` catalog result for an empty script.
- **What:** Preflight every distinct `SqlBatch` with
  `ValidateBatchSafetyAsync` before creating or opening a database connection.
  Return the first catalogued failure unchanged. Use the successful first
  safety outcome to select `ReadWrite` versus rollback-only behavior and set
  anonymization only for `ReadOnlyAnonymized`. Resolve the requested row limit
  with the same configured default/max semantics as the single-query path.
- **What:** For the atomic path, open exactly one connection and one
  `ReadCommitted` transaction. Execute each batch in source order and invoke
  the batch executor exactly `RepeatCount` times without expanding or
  mutating the `SqlBatch` value. Store each execution result together with its
  original batch metadata.
- **What:** For `ReadWrite`, commit once only after all repetitions succeed.
  For `ReadOnly` and `ReadOnlyAnonymized`, always roll back after successful
  execution. On a returned batch failure or exception, roll back and return
  the existing error mapping; cancellation is rethrown after the established
  rollback attempt. For non-write access, reuse
  `TransactionIntegrityGuard.GetTranCountAsync` around the batch executions
  and use its defensive rejection path when the ambient transaction changes.
- **Why:** This is one coherent atomic orchestration boundary. It provides
  sequential multi-batch execution and guardrails while leaving the
  transaction-free/autocommit choice and public response formatting outside
  the step.

### File 6: `src/SqlToAi/Program.cs` (existing lines 169-178)

- **What:** Register the concrete `QueryExecutionService` once and alias the
  same singleton to `IQueryExecutionService` and `IQueryBatchExecutor`; add
  the internal `IScriptExecutionService` registration. Do not register a
  second query service instance.
- **Why:** The internal execution seam must be resolvable by the atomic
  coordinator while preserving the current singleton lifetime. No MCP tool,
  registry, CLI command, or public argument wiring is added in this step.

### File 7: `tests/SqlToAi.Tests/Database/QueryExecutionServiceMockDb.cs` (existing lines 62-129 and 209-300)

- **What:** Add the new batch-validation method to the existing
  `FakeQuerySafetyValidator` and its delegate without duplicating the six
  pipeline stages. Extend `MockQueryConnectionFactory` only as needed to
  retain the ordered reader command texts and their transaction references so
  shared-connection execution can be asserted.
- **Why:** The established test doubles already model the relevant ADO.NET
  ownership and should be extended rather than replaced by a parallel mock
  stack.

### File 8: `tests/SqlToAi.Tests/Database/QuerySafetyValidatorTests.cs` (existing tests, after the stage-6 cases)

- **What:** Add focused tests for the new batch operation: a ReadWrite
  multi-statement batch succeeds, a ReadOnly multi-statement read batch
  succeeds, and a ReadOnly multi-statement batch containing a mutating
  statement remains `WriteOperationBlocked`. Keep the existing strict
  `ValidateQuerySafetyAsync` multi-statement tests unchanged.
- **Why:** These cases pin the one intentional difference between the public
  single-query path and the script-batch safety boundary.

### File 9: `tests/SqlToAi.Tests/Database/QueryExecutionServiceBatchTests.cs` (new, planned lines 1-180)

- **What:** Add an internal-service test class with the existing
  `QueryExecutionService` construction pattern. Verify that two batch calls
  use the same supplied connection/transaction, bind parameters, preserve
  the row-limit/statistics setup, and leave commit/rollback counts at zero.
  Reuse the existing query-service anonymization/result tests as regression
  coverage for the delegated serializer rather than copying their PII setup.
- **Why:** These tests establish the ownership contract of the new seam and
  prove that the existing implementation, not a new test-only serializer, is
  used for each batch.

### File 10: `tests/SqlToAi.Tests/Database/ScriptExecutionServiceTests.cs` (new, planned lines 1-300)

- **What:** Add focused unit tests with small recording safety and batch
  executor doubles for: empty-script rejection; all-batch preflight before
  opening a connection; sequential two-batch ReadWrite success with one
  commit; ReadOnly and ReadOnlyAnonymized success with one rollback and the
  correct anonymization flag; `RepeatCount` execution and metadata
  preservation; second-batch failure stopping later work and rolling back;
  and a transaction-integrity change returning `QueryError` after defensive
  rollback.
- **Why:** The coordinator's safety ordering, transaction ownership,
  repeat semantics, rollback behavior, and access-level mapping are not
  covered by the existing single-query tests and need direct assertions.

## Tests

- [ ] Run the focused safety and execution tests after all code/test edits:
  `dotnet test tests/SqlToAi.Tests --filter "FullyQualifiedName~QuerySafetyValidatorTests|FullyQualifiedName~QueryExecutionServiceBatchTests|FullyQualifiedName~ScriptExecutionServiceTests"`.
- [ ] Run the existing query execution regression tests:
  `dotnet test tests/SqlToAi.Tests --filter "FullyQualifiedName~QueryExecutionServiceTests|FullyQualifiedName~QueryExecutionServiceTransactionTests|FullyQualifiedName~QueryExecutionServiceAnonymizationTests"`.
- [ ] Use AiNetLinter MCP before and after the edit: query feature context and
  metrics for `QueryExecutionService`, `QuerySafetyValidator`, and the new
  script service; query impact for the changed symbols; and query scoped
  violations for `src/SqlToAi/Database`. Confirm the new types stay within
  file/method/constructor/footprint budgets and that no duplicate command,
  safety, transaction, parameter, result, or metrics implementation was
  introduced.
- [ ] Run `dotnet build SqlToAi.slnx` after all code and test changes; it must
  be green with zero warnings and zero errors.
- [ ] The coder runs the complete test command exactly once after all changes
  and before the code commit: `dotnet test SqlToAi.slnx`. Record the green
  result in `step-004/step-result.md`.
- [ ] When the coder supplies a green full-suite result, the critic does not
  repeat `dotnet test SqlToAi.slnx`; the critic may run only a focused command
  when a concrete residual risk requires it.

## Definition of Done

- [ ] `IQueryBatchExecutor` and its request record provide one reusable,
  caller-owned explicit-transaction execution seam; the executor does not
  begin, commit, or roll back transactions.
- [ ] `QueryExecutionService` delegates through that seam without changing
  the public `IQueryExecutionService` contract or existing single-query
  safety, transaction, result, anonymization, parameter, row-limit, and
  metrics behavior.
- [ ] `ValidateBatchSafetyAsync` reuses the existing safety stages, relaxes
  only the final multi-statement restriction, and retains ReadOnly,
  ReadOnlyAnonymized, SchemaOnly, None, and ReadWrite guard semantics.
- [ ] `ScriptExecutionService.ExecuteAtomicallyAsync` preflights all batches
  before opening the database, executes them sequentially on one explicit
  transaction, honors `RepeatCount`, preserves source metadata, commits only
  successful ReadWrite work, and forces rollback for read-only modes.
- [ ] ReadOnly and ReadOnlyAnonymized integrity changes are rejected through
  `TransactionIntegrityGuard`; returned failures and exceptions stop later
  batches and do not commit.
- [ ] The focused tests cover safety-boundary behavior, transaction
  ownership, parameters, row limits, anonymization selection, repeats,
  failure short-circuiting, and integrity protection; existing single-query
  tests remain green.
- [ ] `dotnet build SqlToAi.slnx` is green with zero warnings and errors.
- [ ] `dotnet test SqlToAi.slnx` was run exactly once by the coder after all
  changes and before the code commit, with the green result recorded in
  `step-004/step-result.md`; the critic follows the no-repeat rule above.
- [ ] AiNetLinter MCP checks show no relevant violations, and the duplicate
  audit has no new execution/safety/transaction/parameter/result/metrics
  cluster.
- [ ] No MCP registration, canonical registry, CLI command, Markdown report,
  diagnostic renderer, autocommit mode, new error code, or unrelated
  documentation is added. The coder updates `codemap.md`, writes
  `step-004/step-result.md`, sets this plan to the workflow's completed
  status, and creates one German imperative Conventional Commit with the
  `[sql-file-execution]` suffix.

## Rules References

- `.agents/rules/AiNetLinter-McpWorkflow.mdc#Verbindliche Priorität` — C#
  symbols, callers, dependencies, impact, metrics, and violations must be
  answered with the matching AiNetLinter MCP query before supplemental text
  inspection.
- `.agents/rules/AiNetLinter-McpWorkflow.mdc#Werkzeugwahl` — use feature
  context/symbol bodies, dependency/reference/impact queries, violations, and
  duplicate checks for the execution and safety changes.
- `.agents/rules/AiNetLinter.mdc#Kurz-Stil` — retain nullable enablement,
  sealed concrete types, short flat methods, input records for grouped data,
  and visible exception handling.
- `.agents/rules/AiNetLinter.mdc#Grenzwerte (Produktion)` — keep new files,
  methods, constructors, dependencies, and type footprints within configured
  budgets.
- `.agents/rules/AiNetLinter.mdc#agent-resilience` — cancellation must not be
  swallowed, blocking task access is forbidden, and catches must log plus
  return a visible error or rethrow.
- `.agents/rules/AiNetLinter.mdc#test-coverage` — the new production types
  require focused xUnit coverage or an explicit coverage sentinel.
- `.agents/rules/SqlToAiRichtlinien.mdc#2. Architektur- & Guardrail-Konzepte (Constraints)` —
  preserve default-deny access levels, read-only rollback, anonymization,
  least privilege, and the standardized error catalog.
- `.agents/rules/SqlToAiRichtlinien.mdc#3. Windows-Umgebung & Tool-Regeln` —
  use the repository's Windows-compatible build and test commands.
- `.agents/rules/SqlToAiRichtlinien.mdc#4. Updates, Dokumentation & Sprachen (Updates, Documentation & Languages)` —
  functional changes require xUnit v3 tests, English source artifacts, and
  synchronized user documentation when a public feature or option changes.
- `.agents/rules/SqlToAiRichtlinien.mdc#5. Qualitätsdrift-Prävention & Tech Debt (AiNetLinter)` —
  maintain zero warnings, Result-based service boundaries, linter
  conformity, and no unapproved duplication.

## Known Exceptions

- None accepted. The new tests are in-memory/fake-database tests; the
  project's configured integration tests remain part of the required complete
  suite.

## Code-Skizze (optional)

```csharp
internal sealed record QueryBatchExecutionArgs(
    DbConnection Connection,
    DbTransaction Transaction,
    string DatabaseName,
    string Query,
    int RowLimit,
    bool Anonymize,
    object? Parameters);

internal interface IQueryBatchExecutor
{
    Task<Result<QueryExecutionResult>> ExecuteBatchAsync(
        QueryBatchExecutionArgs args,
        CancellationToken cancellationToken = default);
}
```

## Notes

- Do not call `IQueryExecutionService` once per batch. The new coordinator
  must use the internal seam implemented by the existing
  `QueryExecutionService` so all batches share one connection and one
  transaction.
- `SqlScriptFileReader` and `SqlScriptBatchSplitter` are inputs to this
  execution core; do not duplicate file validation, encoding detection, or
  `GO` scanning. `RepeatCount` is interpreted by the coordinator but the
  original `SqlBatch` is never expanded or rewritten.
- `TransactionIntegrityGuard.RejectViolationAsync` returns a generic query
  result by design. The script coordinator must reuse its logging and
  defensive rollback, then propagate the contained catalog error through the
  script result type.
- The atomic-only boundary is intentional for this step. Do not add a
  `use_transaction` switch, nullable transaction shortcut, autocommit branch,
  or a second transaction helper here.
- Do not convert per-batch execution results into Markdown or add batch error
  snippets/diagnostic formatting. Those are public response concerns outside
  this execution-core step.
- `TD-001` is not auto-fixable and does not overlap this step; leave it in
  `tech-debt.md` and do not create a new epic or refactor either constructor.
