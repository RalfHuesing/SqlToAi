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

- **`src/SqlToAi/Database`** — database execution services, the nullable caller-owned batch-execution seam, transaction-selecting script coordinator, connection/command helpers, safety validation, transaction integrity, parameter binding, performance metrics, and local script intake used by the batch execution engine.
- **`src/SqlToAi/Database/IQueryBatchExecutor.cs`** — internal caller-owned nullable transaction seam for reusing the existing query execution pipeline.
- **`src/SqlToAi/Database/IScriptExecutionService.cs`** — internal mode-neutral script request and structured report-producing execution boundary.
- **`src/SqlToAi/Database/ScriptExecutionReport.cs`** — internal script and batch report records plus status and transaction-mode enums.
- **`src/SqlToAi/Database/ScriptExecutionReportFactory.cs`** — internal report factory that preserves ordered batch outcomes and aggregates retained execution metrics.
- **`src/SqlToAi/Database/ScriptExecutionReportRenderer.cs`** — internal Markdown renderer for script metadata, batch results, anonymization metadata, and failure diagnostics.
- **`src/SqlToAi/Database/DatabaseCommandExecutor.cs`** — shared SET STATISTICS and SET ROWCOUNT command helper used with explicit or provider-autocommit execution.
- **`src/SqlToAi/Database/QueryExecutionService.cs`** — single-query serializer and batch adapter that forwards optional caller-owned transactions through the established execution pipeline.
- **`src/SqlToAi/Database/ScriptExecutionService.cs`** — internal script-batch coordinator selecting transaction modes and producing ordered success, failure, and not-executed reports.
- **`src/SqlToAi/Database/SqlScriptFile.cs` and `src/SqlToAi/Database/SqlScriptFileReader.cs`** — internal immutable file value and validated local SQL script reader for later batch execution (last: step-003).
- **`src/SqlToAi/Database/SqlBatch.cs` and `src/SqlToAi/Database/SqlScriptBatchSplitter.cs`** — internal batch metadata and script-splitting foundation for later file-execution steps. (last: step-002)
- **`src/SqlToAi/Security`** — access-level resolution and the read-only guard that define database authorization and mutation protection.
- **`src/SqlToAi/Anonymization`** — anonymizer, token vault, token resolver, and rule/policy components needed for protected script result sets.
- **`src/SqlToAi/Configuration`** — `SqlToAiOptions` and nested execution options containing the script-size limit and related defaults (last: step-003).
- **`src/SqlToAi/Domain`** — result/value objects and the standardized `SqlToAiError` catalog containing the file-intake codes (last: step-003).
- **`src/SqlToAi/Mcp`** — canonical tool constants, registry metadata, SDK registrations, dispatcher routing, protocol models, and output conversion for public SQL-tool exposure (last: step-007).
- **`src/SqlToAi/Cli`** — registry-driven direct query command construction with typed option mapping for CLI-verifiable tools (last: step-007).
- **`src/SqlToAi/appsettings.json`** — embedded and copied factory configuration template containing the script file-size default (last: step-003).
- **`src/SqlToAi/Program.cs`** — application composition root and DI registrations for new execution services.
- **`tests/SqlToAi.Tests/Database`** — unit tests and database fakes covering execution, safety, transactions, parameters, result handling, and script-file intake (last: step-003).
- **`tests/SqlToAi.Tests/Database/QueryExecutionServiceBatchTests.cs`** — focused coverage for the caller-owned explicit and nullable batch execution seam (last: step-005).
- **`tests/SqlToAi.Tests/Database/ScriptExecutionServiceTests.cs`** — focused coverage for script preflight, transaction modes, transaction ownership, repeats, failures, cancellation, integrity protection, and report outcomes (last: step-006).
- **`tests/SqlToAi.Tests/Database/ScriptExecutionReportFactoryTests.cs`** — focused coverage for report metadata, metric aggregation, retained execution details, and ordered failure outcomes (last: step-006).
- **`tests/SqlToAi.Tests/Database/ScriptExecutionReportRendererTests.cs`** — focused coverage for successful Markdown output, anonymization metadata, failure diagnostics, and safe content fencing (last: step-006).
- **`tests/SqlToAi.Tests/Configuration`** — option-binding and temporary-file tests covering the script-size configuration contract (last: step-003).
- **`tests/SqlToAi.Tests/Domain`** — centralized `SqlToAiError` catalog assertions covering the script-file error codes (last: step-003).
- **`tests/SqlToAi.Tests/Database/SqlScriptFileReaderTests.cs`** — focused local path, size, and encoding contract tests (last: step-003).
- **`tests/SqlToAi.Tests/Mcp`** — dispatcher, tool registration, registry, observability, and MCP output tests for script-tool routing and contract exposure (last: step-007).
- **`tests/SqlToAi.Tests/Cli`** — registry-generated CLI command tests for tool argument parsing and callback mapping (last: step-007).
- **`tests/SqlToAi.Tests/Integration`** — live SQL Server fixture and integration coverage for transaction, guardrail, anonymization, and result behavior.
- **`tests/SqlToAi.Tests/Integration/ScriptExecutionServiceIntegrationTests.cs`** — live SQL Server verification for local script intake, batch reports, transaction modes, read-only rejection, and anonymized result metadata.
- **`tests/SqlToAi.Tests/AiNetLinter`** — linter clean-check test and the project rules JSON used by the semantic quality gate.
- **`README.md`** — user-facing setup, CLI, configuration, tool, and deployment documentation synchronized with local script execution (last: step-007).
- **`docs/architecture-spec.md`** — authoritative MCP tool, security, output, configuration, and error-catalog specification synchronized with local script execution (last: step-007).
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
