---
task: sql-file-execution
type: tech-debt-log
maintained_by: kritiker
last_updated: 2026-08-29T07:31:22+02:00
---

# Tech-Debt-Log: sql-file-execution

Append-only. Each entry is an architecture, anti-pattern, duplication, or consistency observation made during a step review and deliberately not fixed because it is outside that step's scope.

## Index

| ID | Bereich / Datei | Priorität | Auto-Fixable | Kurzfassung |
|---|---|---|---|---|
| TD-001 | `src/SqlToAi/Database/PerformanceMeasurementService.cs`, `src/SqlToAi/Database/QueryComparisonService.cs` | mittel | nein | Exact duplicate dependency-initialization constructors require an architectural consolidation decision. |

## Einträge

### TD-001 — Duplicated constructor dependency initialization [Priorität: mittel] [Auto-Fixable: nein]

- **Gefunden in:** step-001 (Kritiker-Review vom 2026-08-29)
- **Ort:** `src/SqlToAi/Database/PerformanceMeasurementService.cs:33-43`; `src/SqlToAi/Database/QueryComparisonService.cs:33-43`
- **Befund:** The two public constructors assign the same four dependency categories to corresponding fields and read `options.Value.QueryExecution` in the same shape. The drift audit classified the methods as one `exact` cluster with score 1.00 and 22 tokens per member.
- **Warum nicht sofort gefixt:** The duplicate predates Step-001 and is outside the splitter foundation. Consolidating constructor initialization across two independently scoped services requires architectural judgment and is not a mechanical change for this step.
- **Vorschlag:** In a future database-service refactoring, evaluate a shared options/dependency initialization abstraction or another design that removes the exact clone without obscuring the services' distinct responsibilities.
- **Auto-Fixable:** nein — the consolidation requires architecture decisions and could affect construction/DI behavior.
- **Status:** offen
