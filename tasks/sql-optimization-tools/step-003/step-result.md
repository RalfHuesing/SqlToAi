---
status: done
type: step-result
task: sql-optimization-tools
step: step-003
epic: EPIC-03
step_type: single
coded_by: coder
coded_by_model: gemini-3.6-flash
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-03T10:24:00+02:00
code_commit_hash: 27a6b0a
status_after: done
blocker_category: n/a
---

# Result Step 003: Performance- & Plan-Analyse Engine (sql_measure_performance) implementieren

## Zusammenfassung

Implementierung des neuen MCP-Tools `sql_measure_performance` zur Messung von CPU-Zeit, Elapsed Time und IO-Metriken (Logical Reads, Physical Reads, Read-Ahead Reads) via T-SQL `STATISTICS IO, TIME` sowie zur Extraktion von Plan-Warnungen (`Missing Indexes`, `CONVERT_IMPLICIT`, `Table Scans`) aus dem XML-Ausführungsplan (`SET STATISTICS XML ON`). Bei fehlendem `SHOWPLAN`-Recht degradiert der Dienst sauber auf reine IO/TIME-Messung (`HasShowplanPermission = false`).

## Geänderte Dateien

- `src/SqlToAi/Domain/PerformanceMeasurementResult.cs` & `QueryPerformanceArgs.cs` (neu) — Domain-Records für Metriken, Plan-Warnungen und Messparameter.
- `src/SqlToAi/Database/IPerformanceMeasurementService.cs` & `PerformanceMeasurementService.cs` (neu) — Engine für STATISTICS IO/TIME-Messung, Warmup/Averaging, Graceful Degradation & XML Plan Parsing.
- `src/SqlToAi/Database/QueryComparisonService.cs` — Parameter-Refactoring von `ExecuteExceptDiffsAsync` zur Einhaltung der AiNetLinter-Grenzen.
- `src/SqlToAi/Mcp/McpConstants.cs` — Konstanten `ToolMeasurePerformance`, `ArgWarmupRuns`, `ArgExecutionRuns`, `ArgIncludePlanAnalysis` hinzugefügt.
- `src/SqlToAi/Mcp/ToolRegistry.cs` — Registrierung von `sql_measure_performance` (Tool #14).
- `src/SqlToAi/Mcp/ToolDispatcher.cs` — Dispatcher Handler & `GetBool` Helper.
- `src/SqlToAi/Mcp/McpJsonContext.cs` — Native AOT Serialisierungsunterstützung für Performance-Domain-Types.
- `src/SqlToAi/Program.cs` — DI-Registrierung für `IPerformanceMeasurementService`.
- `tests/SqlToAi.Tests/Database/PerformanceMeasurementServiceTests.cs` (neu) — Unit-Tests für Guards, XML-Parsing und Error-Handling.
- `tests/SqlToAi.Tests/Mcp/ToolDispatcherTests.cs`, `ToolRegistryTests.cs`, `McpHostTests.cs` — Assertions auf 14 Tools aktualisiert.

## Commit

- **Code-Commit-Hash:** `27a6b0a`
- **Message:**
  ```
  feat(database): Performance- & Plan-Analyse Engine (sql_measure_performance) implementieren [sql-optimization-tools]

  Refs: tasks/sql-optimization-tools/step-003
  ```
- **Branch:** main
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit.

## Build-/Test-Status

```
dotnet build SqlToAi.slnx -> grün
dotnet test SqlToAi.slnx  -> grün (455 Tests, 0 Fehler)
```

## Abweichungen vom Plan

Keine.

## Beobachtungen

- XML-Parsing wurde modular in `ExtractMissingIndexWarnings` und `ExtractOperatorWarnings` zerlegt, wodurch die zyklomatische Komplexität unter das Linter-Limit (12) sinkt.

## Bekannte Unschärfen

Keine.

## Falls Status `blocked`

n/a
