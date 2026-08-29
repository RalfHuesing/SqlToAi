---
status: active
task: sql-file-execution
derived_from: konzept.md
created_at: 2026-08-29
last_updated: 2026-08-29
created_by_model: gpt-5 (Codex)
created_by_model_knowledge_cutoff: not provided by runtime
---

# Roadmap: sql-file-execution

Coarse roadmap for introducing `sql_execute_file`. Detailed steps are created
just in time by the Step-mode planner; this document records only epic-level
scope and the project anchors needed by later agents.

## Tech-Stack Note

- **Runtime:** .NET 10, C# 14, nullable reference types, implicit usings.
- **Solution/build:** `SqlToAi.slnx` with one executable project and one xUnit
  v3 test project; Dapper, `Microsoft.Data.SqlClient`, ScriptDom, MCP SDK 2.2.0,
  `System.Text.Json`, and Microsoft.Extensions DI/options/logging.
- **Build command:** `dotnet build SqlToAi.slnx` (release validation:
  `dotnet build SqlToAi.slnx -c Release`).
- **Test command:** `dotnet test SqlToAi.slnx` (the deployment script uses
  `dotnet test -c Debug`; integration tests may require the configured SQL
  Server).
- **Lint command:** `dotnet test SqlToAi.slnx --filter FullyQualifiedName~AiNetLinterTests.RunLinterShouldBeClean`; the test skips when the configured `AiNetLinter.exe` is unavailable. The semantic quality gate is AiNetLinter MCP `safeguard`/`get_violations`.
- **Drift-audit command:** before closing an epic or the task, use the project
  MCP `find_duplicates(scopeDir="src", minTokens=20)` and manually classify
  `exact` and `near` clusters; investigate refactoring drift only for an
  identified helper.
- **Release/CI:** `scripts/deploy.ps1` runs tests and publishes a self-contained
  Windows single-file executable; the tag-triggered GitHub workflow publishes
  `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64` archives. The release
  workflow itself does not run the test suite.
- **Code-style summary:** keep concrete classes `sealed`, enable nullable at
  file start, keep methods short and flat, stay within the configured file,
  method, parameter, complexity, and dependency limits, use `Result` at
  service boundaries, avoid dynamic/blocking access/silent catches, and keep
  configuration values in options plus `appsettings.json` rather than in code.
  Safety, least privilege, rollback integrity, anonymization, and zero warnings
  are mandatory constraints.
- **Documentation/language:** source and repository documentation are written
  in English; agent communication is German; repository-relative links are
  required in Markdown.
- **Commit convention:** German imperative Conventional Commits; Drift-Loop
  task commits additionally carry the `[sql-file-execution]` subject suffix.
  This roadmap task does not create commits.

## Rule Index

- `.agents/rules/AiNetLinter-McpWorkflow.mdc` — requires AiNetLinter MCP-first analysis for C# symbols, dependencies, references, violations, and duplicate checks.
- `.agents/rules/AiNetLinter.mdc` — defines generated C# style, architecture, resilience, metric, naming, and duplicate-code rules including test overrides.
- `.agents/rules/SqlToAiRichtlinien.mdc` — defines SqlToAi security/architecture constraints, Windows commands, testing, documentation/configuration, language, commit, and quality requirements.

## Initial Drift Audit

`find_duplicates(scopeDir="src", minTokens=20)` found one `exact` cluster:
the `PerformanceMeasurementService` and `QueryComparisonService` constructors
share an identical dependency-initialization shape. It is outside this task's
scope and requires architectural judgment, so it is a technical-debt
candidate for the Critic's log rather than a new epic or an implementation
change here.

## Epics

- [ ] **EPIC-01: Local script intake and batch foundation** — Establish the
  local `.sql` file contract, path/extension/size/encoding validation, stable
  error representation, and a robust `GO`-aware batch model retaining source
  line ranges and optional repeat counts. Based on `konzept.md` Scope,
  “Dateipfad-Auflösung & Sicherheits-Checks”, and “Multi-Batch & GO-Unterstützung”.
- [ ] **EPIC-02: Guarded multi-batch execution engine** — Implement the
  sequential batch orchestration with parameter binding, row limits,
  ReadOnly/ReadOnlyAnonymized guardrails, ReadWrite transaction selection,
  rollback/commit behavior, transaction-integrity protection, anonymization,
  and aggregate/per-batch execution metrics. Preserve the single-query safety
  pipeline while defining the script-specific boundary that permits isolated
  script batches to contain their intended statements. Based on `konzept.md`
  “Sicherheits-Guardrails” and “Ausführung”.
- [ ] **EPIC-03: Structured report and diagnostic contract** — Define and
  render the Markdown result for script metadata, overall status and metrics,
  per-batch status/result sets, and actionable failures with batch number,
  source line, SQL snippet, and catalog error code. Based on `konzept.md`
  “Strukturiertes Markdown-Ausgabeformat” and its error-reporting criteria.
- [ ] **EPIC-04: MCP, registry, CLI, and dependency wiring** — Expose
  `sql_execute_file` consistently through the MCP tool registration and
  dispatcher, the canonical tool registry used by `tools/list` and the CLI,
  and the application/test dependency graph with the documented arguments and
  defaults. Based on `konzept.md` “Neues MCP-Tool” and “Wo im Projekt”.
- [ ] **EPIC-05: Verification, documentation, and quality closure** — Add
  focused xUnit v3 unit/integration coverage for file validation, encoding,
  `GO` variants, multi-batch success/failure, transaction modes, read-only
  rejection, anonymized result sets, and diagnostics; synchronize `README.md`,
  `docs/architecture-spec.md`, configuration examples, and error catalog;
  finish with build/test, AiNetLinter, and drift-audit gates. Based on the
  complete Definition of Done in `konzept.md`.

## Scope Guardrails

- Local filesystem paths only; no remote or network URLs and no interactive
  input prompt.
- No separate file-validation or file-performance MCP tools.
- Existing single-query behavior remains intact; script execution is the
  dedicated multi-batch surface.
