---
status: done
type: step-result
task: dry-refactor
step: step-004
epic: EPIC-04
step_type: single
coded_by: coder
coded_by_model: Gemini 3.7 Flash (High)
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-15T18:30:00+02:00
code_commit_hash: f65b765
status_after: done
blocker_category: n/a
---

# Result Step 004: Architektur: Facade & Dispatcher-Entlastung

## Zusammenfassung

`DatabaseAnalysisServices` wurde als Dependency-Bundle für die 4 Analyse-/Optimierungsdienste (`IPerformanceMeasurementService`, `IQueryComparisonService`, `IOptimizationBenchmarkService`, `IIndexSuggestionService`) eingeführt. `ToolDispatcher` hat nun nur noch 4 non-framework Konstruktor-Abhängigkeiten (Limit: 5). `Program.cs` und `BuildDispatcher` in `ToolDispatcherTests.cs` wurden angepasst.

## Geänderte Dateien

- `src/SqlToAi/Database/DatabaseAnalysisServices.cs` (neu) — Service-Bundle-Record.
- `src/SqlToAi/Mcp/ToolDispatcher.cs` — Konstruktor auf `DatabaseAnalysisServices` umgestellt.
- `src/SqlToAi/Program.cs` — `DatabaseAnalysisServices` in DI registriert.
- `tests/SqlToAi.Tests/Mcp/ToolDispatcherTests.cs` — `BuildDispatcher` auf 4 Parameter reduziert.

## Commit

- **Code-Commit-Hash:** `f65b765`
- **Message:** `refactor(mcp): Fuehre DatabaseAnalysisServices zur Entlastung des ToolDispatcher-Konstruktors ein`
- **Branch:** `main`

## Build-/Test-Output

```
dotnet build → grün (0 Warnungen, 0 Fehler)
dotnet test  → grün (486 Tests, 0 Fehler)
AiNetLinter get_violations → MaxConstructorDependencies behoben
```

## Abweichungen vom Plan

Keine.

## Beobachtungen

Keine.

## Bekannte Unschärfen

Keine.
