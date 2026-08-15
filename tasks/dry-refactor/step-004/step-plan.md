---
status: done (pending audit)
type: step-plan
task: dry-refactor
step: step-004
corrects: null
title: "Architektur: Facade & Dispatcher-Entlastung"
epic: EPIC-04
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: Gemini 3.7 Flash (High)
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-15T18:28:00+02:00
related_to: [step-003]
---

# Step 004: Architektur: Facade & Dispatcher-Entlastung

## Bezug

- **Task:** `dry-refactor`
- **Epic:** `EPIC-04` aus `roadmap.md`
- **Konzept-Referenz:** [Konzept.md](tasks/dry-refactor/Konzept.md) Abschnitt „Scope > Muss-Haben"

## Aktueller Projektzustand (JIT-Kontext)

`ToolDispatcher` injiziert im Konstruktor 7 Services (`ISchemaService`, `IQueryExecutionService`, `IQueryValidationService`, `IQueryComparisonService`, `IPerformanceMeasurementService`, `IOptimizationBenchmarkService`, `IIndexSuggestionService`), was das Linter-Limit von maximal 5 Konstruktor-Abhängigkeiten überschreitet (`MaxConstructorDependencies`). In `ToolDispatcherTests.cs` hat `BuildDispatcher` 7 Parameter (`MaxMethodParameterCount`).

## Intention

Einführung von `DatabaseAnalysisServices` zur Bündelung der 4 Analyse-/Optimierungsdienste. Reduktion der Konstruktor-Abhängigkeiten im `ToolDispatcher` auf 4 non-framework Services und Anpassung der DI-Registrierung in `Program.cs` sowie des Test-Builders.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Database/DatabaseAnalysisServices.cs` (neu)
- **Was:** Neuer Record `DatabaseAnalysisServices(IPerformanceMeasurementService PerformanceMeasurement, IQueryComparisonService QueryComparison, IOptimizationBenchmarkService Benchmark, IIndexSuggestionService IndexSuggestion)`.
- **Warum:** Bündelung fachlich zusammengehörender Analyse-Services nach dem etablierten Pattern (`AnonymizationDependencies`).

### Datei 2: `src/SqlToAi/Mcp/ToolDispatcher.cs`
- **Was:** Konstruktor auf 4 non-framework Parameter (`ISchemaService`, `IQueryExecutionService`, `IQueryValidationService`, `DatabaseAnalysisServices`) reduzieren.
- **Warum:** Linter-Regel `MaxConstructorDependencies <= 5`.

### Datei 3: `src/SqlToAi/Program.cs`
- **Was:** Registrierung von `DatabaseAnalysisServices` im DI-Container.
- **Warum:** Saubere DI-Auflösung zur Laufzeit.

### Datei 4: `tests/SqlToAi.Tests/Mcp/ToolDispatcherTests.cs`
- **Was:** Anpassung von `BuildDispatcher` zur Nutzung von `DatabaseAnalysisServices` und Reduktion der Parameterzahl auf <= 4.
- **Warum:** Linter-Regel `MaxMethodParameterCount <= 6`.

## Tests

- [ ] `dotnet build` läuft ohne Fehler/Warnungen.
- [ ] `dotnet test` läuft vollständig grün durch.
- [ ] AiNetLinter meldet `MaxConstructorDependencies` für `ToolDispatcher` als behoben.

## Definition of Done

- [ ] Alle Änderungen umgesetzt
- [ ] Build & Test grün
- [ ] Commit erfolgt
- [ ] `step-result.md` geschrieben
