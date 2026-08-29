---
status: done
type: step-result
task: sql-file-execution
step: 006
epic: EPIC-03
step_type: single
coded_by: coder
coded_by_model: GPT-5
coded_by_model_knowledge_cutoff: not provided by runtime
coded_at: 2026-08-29
code_commit_hash: 5213fdd
status_after: done (pending audit)
blocker_category: n/a
---

# Result Step 006: Structured script execution report and batch diagnostics

## Summary

The internal script execution boundary now returns a structured
`ScriptExecutionReport` with script metadata, effective transaction mode,
aggregate metrics, ordered batch reports, and catalogued diagnostics. Expected
preflight and execution failures retain completed prefixes, identify the failed
batch where applicable, and mark later batches as not executed. Connection and
commit-style failures remain report-level diagnostics without fabricated batch
context. Cancellation continues to rethrow, while the existing ReadOnly
rollback/anonymization, ReadWrite atomic, and ReadWrite provider-autocommit
semantics remain unchanged.

The new Markdown renderer preserves existing JSON-lines data and
anonymization/token metadata and uses safe dynamic fences for SQL and result
content. The factory sums only retained `QueryExecutionResult` metrics and does
not introduce another statistics parser or serializer.

## Changed Files

- `src/SqlToAi/Database/ScriptExecutionReport.cs` — added internal report contracts and status/transaction-mode enums.
- `src/SqlToAi/Database/ScriptExecutionReportFactory.cs` — added ordered batch construction and aggregate metric logic.
- `src/SqlToAi/Database/ScriptExecutionReportRenderer.cs` — added structured Markdown rendering and failure diagnostics.
- `src/SqlToAi/Database/IScriptExecutionService.cs` — changed the internal execution boundary to `Task<ScriptExecutionReport>` and removed the obsolete list-only batch result.
- `src/SqlToAi/Database/ScriptExecutionService.cs` — adapted orchestration to retain report outcomes while preserving transaction, guardrail, anonymization, and cancellation behavior.
- `tests/SqlToAi.Tests/Database/ScriptExecutionReportFactoryTests.cs` — added factory aggregation and failure-order tests.
- `tests/SqlToAi.Tests/Database/ScriptExecutionReportRendererTests.cs` — added success, failure, report-level failure, JSON, anonymization, and fence tests.
- `tests/SqlToAi.Tests/Database/ScriptExecutionServiceTests.cs` — updated the report boundary assertions and added service-level aggregate metric coverage.

## Commit

- **Code commit:** `5213fdd`
- **Message:** `feat(database): Ergänze Script-Reports [sql-file-execution]`
- **Branch:** `main`
- **Push:** no

## Build and Test Output

- `dotnet test tests/SqlToAi.Tests/SqlToAi.Tests.csproj --no-restore --filter "FullyQualifiedName~ScriptExecutionServiceTests|FullyQualifiedName~ScriptExecutionReportFactoryTests|FullyQualifiedName~ScriptExecutionReportRendererTests"` → green (22 tests, 0 failures, 0 skipped).
- `dotnet build SqlToAi.slnx` → green (0 warnings, 0 errors).
- `dotnet test SqlToAi.slnx` → green (597 tests, 0 failures, 0 skipped; exactly once as the pre-commit gate).

## Deviations from Plan

None. No MCP, registry, CLI, public tool registration, ToolDispatcher, new
error code, public output model, or unrelated documentation change was made.

## Observations

- The report boundary intentionally carries expected operational failures as
  `ScriptExecutionReport.Status = Failed` with an optional catalogued error;
  the lower-level validator and batch executor continue to use their existing
  `Result<T>` contracts.
- The first post-edit AiNetLinter check identified a 63-line coordinator
  method. Report construction branches were extracted, reducing
  `ExecuteAsync` to 43 code lines; the final database-scope checks reported
  zero violations and a 10/10 safeguard score.
- Git reported only the repository's existing LF-to-CRLF normalization notices
  while staging; the staged diff passed `git diff --cached --check`.

## Known Uncertainties

No functional uncertainties are known. Public MCP/CLI exposure and synchronized
user-facing documentation remain intentionally deferred to the later planned
steps.
