---
status: active  # active | done
task: sql-performance
derived_from: konzept.md
created_at: 2026-08-03T10:06:00Z
last_updated: 2026-08-03T18:00:00Z
created_by_model: Claude Sonnet 4.6
created_by_model_knowledge_cutoff: 2025-04
---

# Roadmap: sql-performance

Grober Anker, kein Detailplan — Detail-Steps entstehen erst JIT im
Step-Modus des Planers. Diese Datei wird laufend angepasst.

## Tech-Stack-Notiz

- **Build-Command:** `dotnet build`
- **Test-Command:** `dotnet test`
- **Lint-Command:** Läuft automatisch via `dotnet test` (AiNetLinterTests)
- **Code-Style-Kurzfassung:** C# 14 / .NET 10; `sealed` für konkrete Klassen; `#nullable enable` am Dateianfang; Methoden ≤60 Zeilen (compound suppression bei CC≤3/CogC≤5: 150); ab 5 Parametern ein Input-`record`; kein `dynamic`, kein leeres `catch`; Namespace = Verzeichnispfad; `EnforceNullableEnable`; `EnforceSealedClasses`
- **Commit-Konventionen:** Conventional Commits, Deutsch Imperativ, Subject ≤72 Zeichen inkl. Suffix `[sql-performance]`; Body mit `Refs: tasks/sql-performance/step-NNN`; kein Push

## Regel-Index

- `.agents/rules/SqlToAiRichtlinien.mdc` — Architektur- & Workflow-Richtlinien: Safety-by-Design, Dapper/SqlClient, Doku-Synchronisation (mcp-specification.md + README.md Pflicht), xUnit v3 Tests, Zero-Warning, AppSettings-Pflicht für neue Config-Werte
- `.agents/rules/AiNetLinter.mdc` — C#-Codequalität: Grenzwerte (MaxLineCount 500, MaxMethodLineCount 60, MaxCyclomaticComplexity 12, MaxCognitiveComplexity 15), `sealed`, `#nullable enable`, Namespace-Directory-Mapping, keine async void, keine blockierenden Task-Zugriffe

## Epics

- [x] EPIC-01: PerformanceMetrics min/avg/max — `PerformanceMetrics` um nullable `min_*/max_*`-Felder erweitern; `ProcessCapturedOutput` per Run parsen statt summieren; `McpJsonContext.cs` anpassen; Tests ergänzen (Bezug: konzept.md §Muss-Haben 1) — erledigt in `step-001` + `step-001/fix-01` (Fix: Min/Max-Gültigkeit strukturell über Regex-Match statt Wert-Schwellenwert, siehe `step-001/step-review.md` Finding 1)
- [x] EPIC-02: STATISTICS IO/TIME in sql_execute_query — `QueryExecutionService` mit `InfoMessage`-Handler und `SET STATISTICS IO/TIME ON` erweitern; `QueryExecutionResult` um `LogicalReads`/`CpuTimeMs` ergänzen; `ToolDispatcher` Execution-Info-Text erweitern; Tests ergänzen (Bezug: konzept.md §Muss-Haben 2) — erledigt in `step-002` (Verdict: approved, keine Findings)
- [x] EPIC-03: ToolRegistry Descriptions Rewrite — `BuildMeasurePerformance`, `BuildBenchmarkOptimization`, `BuildExecuteQuery` mit agentenlesbaren Descriptions rewriten; alle JSON-Felder, Semantik von min/avg/max, Verdict-Werte, Execution-Info-Block explizit nennen (Bezug: konzept.md §Muss-Haben 3) — erledigt in `step-003` (Verdict: approved, keine Findings; MINOR-Beobachtung zu `ArgExecutionRuns`-Text „per metric" vs. präziser „elapsed/cpu" notiert, kein Blocker)
- [ ] EPIC-04: Dokumentation — `mcp-specification.md` §14 + §15 aktualisieren (Betrifft: docs/mcp-specification.md §14 sql_measure_performance, §15 sql_benchmark_optimization, §12 sql_execute_query)
