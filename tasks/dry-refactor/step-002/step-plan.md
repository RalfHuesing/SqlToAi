---
status: done (pending audit)
type: step-plan
task: dry-refactor
step: step-002
corrects: null
title: "Linter-Errors & Core C#-Fixes"
epic: EPIC-02
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: Gemini 3.7 Flash (High)
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-15T18:22:00+02:00
related_to: [step-001]
---

# Step 002: Linter-Errors & Core C#-Fixes

## Bezug

- **Task:** `dry-refactor`
- **Epic:** `EPIC-02` aus `roadmap.md`
- **Konzept-Referenz:** [Konzept.md](tasks/dry-refactor/Konzept.md) Abschnitt „Scope > Muss-Haben"

## Aktueller Projektzustand (JIT-Kontext)

`McpJsonContext` und `FakeDbConnection` sind nicht `sealed` (Fehler gem. `EnforceSealedClasses`). `PerformanceMeasurementService` hat Methoden `ExecuteWarmupRunsAsync` und `ExecuteMeasuredRunsAsync` mit jeweils 8 Parametern (Warnung gem. `MaxMethodParameterCount`).

## Intention

Beheben der beiden `sealed`-Fehler und Bündelung der Parameter in `PerformanceMeasurementService` via Parameter-Record `MeasurementExecutionContext`.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Mcp/McpJsonContext.cs`
- **Was:** `internal sealed partial class McpJsonContext : JsonSerializerContext;`
- **Warum:** Linter-Regel `EnforceSealedClasses`.

### Datei 2: `tests/SqlToAi.Tests/TestSupport/FakeDbConnection.cs` und abhängige Test-Klassen
- **Was:** `FakeDbConnection` als `internal sealed class` deklarieren; `MockMetadataConnection` und `MockConnection` auf statische Factories bzw. versiegelte Konstruktion umstellen.
- **Warum:** Linter-Regel `EnforceSealedClasses`.

### Datei 3: `src/SqlToAi/Database/PerformanceMeasurementService.cs`
- **Was:** Einführung des `MeasurementExecutionContext`-Records und Reduktion der Parameterzahl in `ExecuteWarmupRunsAsync` und `ExecuteMeasuredRunsAsync` auf <= 4.
- **Warum:** Linter-Regel `MaxMethodParameterCount`.

## Tests

- [ ] `dotnet build` läuft ohne Fehler/Warnungen.
- [ ] `dotnet test` läuft vollständig grün durch.

## Definition of Done

- [ ] Alle Änderungen umgesetzt
- [ ] Build & Test grün
- [ ] Commit erfolgt
- [ ] `step-result.md` geschrieben
