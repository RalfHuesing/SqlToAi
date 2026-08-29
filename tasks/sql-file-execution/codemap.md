---
task: sql-file-execution
type: codemap
maintained_by: planer, coder, kritiker
last_updated: 2026-08-29
---

# CodeMap: sql-file-execution

Task-scoped pointer map of the project areas relevant to the SQL file
execution task. It is maintained serially as the task progresses and is not a
complete map of the repository.

## Map

- **`src/SqlToAi/Database`** — database execution services, connection/command helpers, safety validation, transaction integrity, parameter binding, and performance metrics used by the batch execution engine.
- **`src/SqlToAi/Database/SqlBatch.cs` and `src/SqlToAi/Database/SqlScriptBatchSplitter.cs`** — internal batch metadata and script-splitting foundation for later file-execution steps. (last: step-002)
- **`src/SqlToAi/Security`** — access-level resolution and the read-only guard that define database authorization and mutation protection.
- **`src/SqlToAi/Anonymization`** — anonymizer, token vault, token resolver, and rule/policy components needed for protected script result sets.
- **`src/SqlToAi/Configuration`** — `SqlToAiOptions` and nested execution options where the script-size limit and related defaults belong.
- **`src/SqlToAi/Domain`** — result/value objects and the standardized `SqlToAiError` catalog used by service and report contracts.
- **`src/SqlToAi/Mcp`** — tool constants, dispatcher, MCP registration, canonical tool registry, protocol models, and output conversion for public exposure.
- **`src/SqlToAi/Cli`** — registry-driven direct query command construction, relevant because the new MCP tool is also a CLI-verifiable tool.
- **`src/SqlToAi/appsettings.json`** — embedded and copied factory configuration template for the script file-size default.
- **`src/SqlToAi/Program.cs`** — application composition root and DI registrations for new execution services.
- **`tests/SqlToAi.Tests/Database`** — unit tests and database fakes covering execution, safety, transactions, parameters, and result handling.
- **`tests/SqlToAi.Tests/Configuration`** — existing option-binding and temporary-file tests relevant to the script-size configuration contract.
- **`tests/SqlToAi.Tests/Domain`** — centralized `SqlToAiError` catalog assertions that will cover the script-file error codes.
- **`tests/SqlToAi.Tests/Mcp`** — dispatcher, tool registration, registry, and MCP output tests for routing and contract exposure.
- **`tests/SqlToAi.Tests/Integration`** — live SQL Server fixture and integration coverage for transaction, guardrail, anonymization, and result behavior.
- **`tests/SqlToAi.Tests/AiNetLinter`** — linter clean-check test and the project rules JSON used by the semantic quality gate.
- **`README.md`** — user-facing setup, CLI, configuration, tool, and deployment documentation that must list the new capability.
- **`docs/architecture-spec.md`** — authoritative MCP tool, security, output, and error-catalog specification to synchronize with the implementation.
- **`scripts/deploy.ps1` and `.github/workflows/release.yml`** — local deployment and tag-based release validation/publish configuration.

## Planning Notes

- The existing single-query path centers on `QueryExecutionService`,
  `QuerySafetyValidator`, `ReadOnlyGuard`, `TransactionIntegrityGuard`,
  `SqlParameterBinder`, and `PerformanceMetricsCalculator`; future steps
  should inspect and reuse these anchors before introducing parallel helpers.
- The existing MCP surface has two synchronized registries: runtime
  registration in `SqlMcpToolRegistrations` and canonical metadata in
  `ToolRegistry`; both are required for a complete tool exposure.
- `QueryExecutionOptions` is nested in `SqlToAiOptions.cs`; the conceptual
  path `Configuration/QueryExecutionOptions.cs` does not currently exist.
- The current AiNetLinter baseline is clean: safeguard score 10/10 and zero
  violations across 160 indexed C# files.
