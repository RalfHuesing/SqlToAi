---
status: done
type: step-result
task: sql-file-execution
step: 007
epic: EPIC-04
step_type: single
coded_by: coder
coded_by_model: GPT-5
coded_by_model_knowledge_cutoff: not provided by runtime
coded_at: 2026-08-29
code_commit_hash: 59a3b31
status_after: done (pending audit)
blocker_category: n/a
---

# Result Step 007: Expose sql_execute_file through MCP and CLI wiring

## Summary

The `sql_execute_file` tool is now exposed through the canonical MCP constants,
SDK registration, dispatcher, registry, and generated CLI command. The
dispatcher reuses the existing local script reader, script execution service,
and Markdown report renderer, preserving catalogued intake failures and full
execution diagnostics. Focused dispatcher, registry, SDK observability, and CLI
tests cover the new contract, including optional argument forwarding and the
`use_transaction` default.

## Changed Files

- `src/SqlToAi/Mcp/McpConstants.cs` — added canonical file-tool and argument names.
- `src/SqlToAi/Mcp/ToolRegistry.cs` — added the 17th SQL tool and its typed schema.
- `src/SqlToAi/Mcp/SqlMcpToolRegistrations.cs` — registered the typed SDK file-tool adapter.
- `src/SqlToAi/Mcp/ToolDispatcher.cs` — wired local intake, script execution, and report rendering.
- `src/SqlToAi/Cli/ToolCommandFactory.cs` — added nullable Boolean option parsing.
- `tests/SqlToAi.Tests/Mcp/ToolDispatcherTestFakes.cs` — added the request-capturing script-service fake.
- `tests/SqlToAi.Tests/Mcp/ToolDispatcherTests.cs` — covered forwarding, defaults, rendered failures, and file errors.
- `tests/SqlToAi.Tests/Mcp/ToolRegistryTests.cs` — covered the 17-tool registry and exact file-tool schema.
- `tests/SqlToAi.Tests/Mcp/McpModelsTests.cs` — updated the canonical constants completeness contract.
- `tests/SqlToAi.Tests/Mcp/McpObservabilityIntegrationTests.cs` — covered SDK collection counts, listing, and protocol argument forwarding.
- `tests/SqlToAi.Tests/Cli/ToolCommandFactoryTests.cs` — covered typed file-tool CLI options and JSON parameter pass-through.
- `README.md` — documented the file-tool contract, access behavior, and CLI invocation.
- `docs/architecture-spec.md` — documented the 17-tool MCP surface and file-tool contract.

## Commit

- **Code-Commit-Hash:** `59a3b31`
- **Message:**
  ```
  feat(mcp): Exponiere SQL-Dateiausführung [sql-file-execution]

  Refs: tasks/sql-file-execution/step-007
  ```
- **Branch:** `main`
- **Push:** nein (lokal)
- **Doku-Commit:** separater zweiter Commit folgt.

## Build-/Test-Output

- `dotnet test tests/SqlToAi.Tests/SqlToAi.Tests.csproj --no-restore --filter "FullyQualifiedName~ToolDispatcherTests|FullyQualifiedName~ToolRegistryTests|FullyQualifiedName~McpModelsTests|FullyQualifiedName~McpObservabilityIntegrationTests|FullyQualifiedName~ToolCommandFactoryTests"` → grün (55 Tests, 0 Fehler)
- `dotnet build SqlToAi.slnx` → grün (0 Warnungen, 0 Fehler)
- `dotnet test SqlToAi.slnx` → grün (605 Tests, 0 Fehler, 0 übersprungen; genau einmal nach allen Änderungen vor dem Code-Commit)

## Deviations from Plan

The existing `IScriptExecutionService` and its report/request contracts are
internal. Adding that dependency to the previously public concrete
`ToolDispatcher` constructor caused an inconsistent-accessibility compiler
error. The concrete implementation was therefore made internal while the
existing public `IToolDispatcher` contract and DI registration remain
unchanged; this keeps the internal report boundary private without exposing
additional public models. No other plan deviation occurred.

## Observations

`Program.cs` already registers `IScriptExecutionService` and
`IToolDispatcher`, so no composition-root change was needed. The existing
single-query content-block order and text remain unchanged. The initial
targeted compile failure from the visibility mismatch was resolved before the
green targeted test run; no infrastructure failure occurred.

## Known Uncertainties

No functional uncertainties are known. The critic should specifically verify
that the internal visibility adjustment of the concrete `ToolDispatcher` is
acceptable for the intended public surface, whose stable dependency remains
the public `IToolDispatcher` interface.
