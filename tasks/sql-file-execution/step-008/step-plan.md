---
status: done (pending audit)
type: step-plan
task: sql-file-execution
step: 008
corrects: null
title: "Verify SQL file execution against live SQL Server"
epic: EPIC-05
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: GPT-5
created_by_model_knowledge_cutoff: not provided by runtime
created_at: 2026-08-29
related_to:
  - step-007
  - step-006
  - step-005
  - step-003
---

# Step 008: Verify `sql_execute_file` against live SQL Server

## Bezug

- **Task:** `sql-file-execution`
- **Epic:** `EPIC-05` from `roadmap.md` — close the missing live SQL
  verification slice for the already implemented file-execution path.
- **Concept reference:** `konzept.md`, the Definition of Done and the
  sections “Dateipfad-Auflösung & Sicherheits-Checks”, “Multi-Batch &
  `GO`-Unterstützung”, “Sicherheits-Guardrails”, “Ausführung”, and
  “Strukturiertes Markdown-Ausgabeformat”.

## Aktueller Projektzustand (JIT-Kontext)

Step-007 is approved and already connects `sql_execute_file` through the
canonical MCP constants, SDK registration, dispatcher, registry, CLI, and
the existing DI graph. Its review also confirms that `README.md` and
`docs/architecture-spec.md` describe the current five-argument contract,
defaults, access behavior, and report format. No public contract or
production implementation change is required for this step.

The existing unit coverage is deliberately not duplicated here:

- `SqlScriptFileReaderTests` covers local path handling, extension and size
  checks, and UTF-8/UTF-16/ANSI decoding.
- `SqlScriptBatchSplitterTests` covers the `GO` grammar, comments, nested
  block comments, invalid repeat counts, and repeat metadata.
- `ScriptExecutionServiceTests` covers preflight, sequential batches,
  atomic/provider-autocommit selection, rollback, read-only modes,
  anonymization flags, repeats, cancellation, and transaction integrity
  with database fakes.
- `ScriptExecutionReportFactoryTests`,
  `ScriptExecutionReportRendererTests`, and the Step-007 dispatcher/MCP/CLI
  tests cover report aggregation, diagnostics, transport, and argument
  forwarding without a live database.

The missing boundary is under `tests/SqlToAi.Tests/Integration`: the shared
`SqlServerFixture` loads the real `DemoDB` configuration and exposes the
existing `SqlConnectionFactory`, safety components, options, and
`QueryExecutionService`, but no script-specific integration test currently
exists. `QueryExecutionService` already implements `IQueryBatchExecutor`, so
the new test class can compose the real `ScriptExecutionService` directly
from those fixture members. The fixture must not gain another public service
property; its current semantic metrics already exceed the configured public
member and context-footprint thresholds.

The existing integration suite uses `SqlServerCollectionFixture`,
`TestConstants.DatabaseName`, `TestContext.Current.CancellationToken`, and
the `FakeAccessLevelProvider` pattern in
`QueryExecutionServiceIntegrationTests`. The fictional setup script already
provides `dbo.FakeProjects` for controlled write/rollback checks and
`dbo.FakeContacts` for protected result-set checks. Temporary `.sql` files
should follow the lifecycle pattern already used by
`SqlScriptFileReaderTests`; write-test markers must be unique and removed in
`finally` cleanup.

The current semantic quality baseline is clean: AiNetLinter reports zero
violations and `safeguard` reports 10/10. The required drift audit found only
the pre-existing exact constructor cluster recorded as non-auto-fixable
`TD-001`; no new duplicate or refactoring-drift finding is part of this plan.

## Intention

Add one focused live integration boundary for `sql_execute_file`, exercising
the existing reader, splitter, execution service, report factory, and
renderer together against the configured SQL Server. The tests must prove
real batch results and database-state behavior for both write transaction
modes and protected read modes, including a rendered batch diagnostic.

This is the next and only planned Step-008 slice. It does not add parallel
unit coverage, a second fixture, a new MCP path, or documentation changes
that Step-007 already completed.

## Konkrete Änderungen

### Datei 1: `tests/SqlToAi.Tests/Integration/ScriptExecutionServiceIntegrationTests.cs` (new file)

- **What:** Add a sealed xUnit v3 integration test class using the existing
  `SqlServerCollectionFixture`. Create short temporary `.sql` files, read
  them through `SqlScriptFileReader.Read` with the fixture's configured
  `QueryExecutionOptions`, and build the real `ScriptExecutionService` from
  the fixture's connection factory, safety validator, options, and
  `QueryExecutionService` as `IQueryBatchExecutor`.
- **Why:** This verifies the end-to-end service boundary against live SQL
  Server while reusing the approved production pipeline and existing
  integration infrastructure.
- **What:** Add a multi-batch success test using real `GO` separators and a
  repeat form such as `GO 2`. Assert successful ordered batch reports,
  repeat execution count, returned data, `ReadWriteAtomic` mode, and
  non-negative report metrics.
- **Why:** This closes the live execution and report-result gap without
  re-testing the splitter grammar in a second unit-test suite.
- **What:** Add an atomic-failure test with a uniquely marked insert in
  batch 1, an intentionally invalid SQL statement in batch 2, and a
  harmless batch 3. Assert the first write is rolled back, the report marks
  batch 2 failed and batch 3 not executed, and
  `ScriptExecutionReportRenderer.Render` contains the failed batch number,
  source-line context, SQL snippet, and `SQL-AI-0102`.
- **Why:** This verifies real transaction rollback, stop-after-failure
  ordering, and actionable diagnostics in one non-redundant scenario.
- **What:** Add a provider-autocommit failure test with the same isolated
  marker pattern and `UseTransaction = false`. Assert the first batch is
  committed before the later failure, the report uses
  `ReadWriteProviderAutocommit`, and the marker is deleted in `finally`.
- **Why:** This proves the documented non-atomic ReadWrite behavior against
  the actual provider rather than only against the existing fake connection.
- **What:** Add a ReadOnly mutation-rejection test using the existing
  custom access-level-provider pattern. Assert preflight returns
  `WriteOperationBlockedCode`, the transaction mode remains `NotStarted`,
  and no marker row is created.
- **Why:** This confirms the script preflight applies the real read-only
  guard before any live execution is opened.
- **What:** Add one `[Theory]` covering `ReadOnly` and
  `ReadOnlyAnonymized` for a real `dbo.FakeContacts` SELECT. Assert the
  respective rollback mode, successful result set, and, for the anonymized
  case, `WasAnonymized` plus populated anonymized-column metadata.
- **Why:** This verifies both protected modes and the PII result contract
  through the script batch path without duplicating the existing
  query-service anonymization tests.
- **What:** Keep all database writes isolated by a unique marker and clean
  them up with a direct fixture connection in `finally`, including when an
  assertion or execution fails. Do not change `SqlServerFixture`,
  `QueryExecutionServiceIntegrationTests`, production code, or the public
  documentation in this step.
- **Why:** The tests must be repeatable against the shared `DemoDB` and must
  not enlarge an already broad fixture or create parallel test ownership.

## Tests

- [ ] Run the focused live coverage during implementation with
  `dotnet test tests/SqlToAi.Tests/SqlToAi.Tests.csproj --no-restore --filter "FullyQualifiedName~ScriptExecutionServiceIntegrationTests"`.
- [ ] Run `dotnet build SqlToAi.slnx` with zero warnings and zero errors.
- [ ] After all changes are complete, run the full command
  `dotnet test SqlToAi.slnx` exactly once; it must be green before the code
  commit. The existing integration prerequisite is the configured live
  SQL Server and `DemoDB`; do not silently convert these tests into unit
  tests or skip a database failure.
- [ ] Verify the existing `README.md` file-execution section and
  `docs/architecture-spec.md` tool/configuration/error sections remain
  synchronized with the Step-007 contract; no documentation edit is
  expected unless a factual mismatch is found.
- [ ] Run the configured lint test
  `dotnet test SqlToAi.slnx --filter FullyQualifiedName~AiNetLinterTests.RunLinterShouldBeClean`;
  an unavailable local `AiNetLinter.exe` may only produce the repository's
  explicit skip and must be recorded, while the semantic MCP gates must
  still report zero violations and a passing safeguard score.
- [ ] After the implementation, run the project MCP `get_violations` and
  `safeguard` checks for the absolute project root. The expected result is
  zero violations and a safeguard score of at least 8/10.
- [ ] Repeat the epic-cadence drift audit with
  `find_duplicates(scopeDir="src", minTokens=20)`, classify exact and near
  clusters, and leave the pre-existing non-auto-fixable `TD-001` in the
  tech-debt log without creating a new Epic for it.
- [ ] The critic must not repeat the complete test command when the coder has
  supplied a green Step-008 full-suite result; the critic reviews the result
  independently and runs only targeted integration or diagnostic tests when
  a concrete residual risk justifies them.

## Definition of Done

- [ ] The new integration class is the only code/test addition required for
  this step and reuses the existing fixture, service pipeline, and test
  conventions.
- [ ] A live multi-batch script with `GO` and a repeat executes in order and
  returns the expected batch data and report metadata.
- [ ] Live ReadWrite atomic failure rolls back earlier writes and renders
  precise failed-batch diagnostics; live provider autocommit preserves the
  earlier committed batch.
- [ ] Live ReadOnly mutation rejection and both protected read modes behave
  as specified, including anonymized result metadata.
- [ ] `dotnet build SqlToAi.slnx` is green with zero warnings.
- [ ] `dotnet test SqlToAi.slnx` is green exactly once after all changes and
  before the code commit.
- [ ] The critic accepts the coder's green full-suite evidence without a
  second full-suite run and documents any independently executed targeted
  test together with its concrete risk rationale.
- [ ] The configured AiNetLinter test and semantic MCP quality gates are
  clean or have only the explicitly documented executable-unavailable skip.
- [ ] README and architecture documentation remain synchronized; no
  redundant documentation change is introduced.
- [ ] The drift audit has been classified, with `TD-001` unchanged and no
  unreviewed new exact/near production duplicate.
- [ ] The coder writes `step-008/step-result.md`, performs the code commit
  with the required German imperative Conventional Commit subject and
  `[sql-file-execution]` suffix, and changes this plan's status only after
  implementation to `done (pending audit)`.

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4` — xUnit v3 coverage is required
  for functional changes, and README/architecture documentation must stay
  synchronized with feature behavior.
- `.agents/rules/SqlToAiRichtlinien.mdc#5` — zero-warning, baseline-free
  quality and AiNetLinter MCP usage are mandatory; duplicate code remains
  technical debt unless an architectural decision authorizes a change.
- `.agents/rules/AiNetLinter.mdc#test-coverage` — test classes or other
  sentinel-recognized coverage must accompany complex production types.
- `.agents/rules/AiNetLinter.mdc#agent-resilience` — integration test code
  must use asynchronous APIs and must not introduce blocking task access.
- `.agents/rules/AiNetLinter-McpWorkflow.mdc#Werkzeugwahl` — use
  `get_violations`, `safeguard`, and the required `find_duplicates` audit for
  semantic quality verification; the MCP result is authoritative for its
  scope.

## Bekannte Ausnahmen

- Live integration tests require the SQL Server and `DemoDB` configuration
  already used by the current integration suite. An unavailable or failing
  prerequisite is a concrete environment blocker to report, not a reason to
  weaken the assertions.
- `AiNetLinterTests.RunLinterShouldBeClean` explicitly skips when no
  configured `AiNetLinter.exe` exists. Preserve that project rule, record the
  skip, and rely additionally on the semantic MCP `get_violations` and
  `safeguard` results.
- `TD-001` is a pre-existing exact constructor duplicate with
  `auto_fixable: nein`; it remains outside this test-only step and must not
  become a new Epic or an opportunistic refactoring.
- The current fixture's semantic metrics exceed public-member and context
  footprint thresholds, but no actionable lint violation is reported for
  the test scope. Do not worsen that condition by adding another public
  fixture dependency.

## Code-Skizze (optional)

```csharp
// Compose the real script path without expanding the shared fixture API.
var scriptService = new ScriptExecutionService(
    _fixture.ConnectionFactory,
    safetyValidator,
    _fixture.QueryExecutionService,
    Options.Create(_fixture.Options),
    NullLogger<ScriptExecutionService>.Instance);
```

## Notes

- The public tool and documentation are already synchronized by approved
  Step-007. This step is intentionally an integration-verification slice,
  not a second MCP/CLI implementation or a documentation rewrite.
- Use `TestConstants.DatabaseName`, the existing collection fixture, and
  `TestContext.Current.CancellationToken`; do not introduce another test
  database, fixture, or production abstraction.
- A later planner invocation may reassess EPIC-05 after this step's result
  and review. No Step-009 or other future step is planned here.
