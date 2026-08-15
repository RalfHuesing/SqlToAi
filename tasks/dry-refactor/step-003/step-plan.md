---
status: done (pending audit)
type: step-plan
task: dry-refactor
step: step-003
corrects: null
title: "DRY-Konsolidierung (Produktionscode)"
epic: EPIC-03
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: Gemini 3.7 Flash (High)
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-15T18:25:00+02:00
related_to: [step-002]
---

# Step 003: DRY-Konsolidierung (Produktionscode)

## Bezug

- **Task:** `dry-refactor`
- **Epic:** `EPIC-03` aus `roadmap.md`
- **Konzept-Referenz:** [Konzept.md](tasks/dry-refactor/Konzept.md) Abschnitt „Scope > Muss-Haben"

## Aktueller Projektzustand (JIT-Kontext)

Zwischen `QueryDeconstructor` und `SqlMultiStatementDetector` existieren 6 exakt duplizierte Methoden für Statement-Segmentierung und Kommentar-Skipping. Zwischen `PerformanceMeasurementService` und `QueryExecutionService` existiert eine exakte Duplikation von `ExecuteSetOptionAsync`.

## Intention

Beseitigung aller Duplikate im Produktionscode durch Integration der gemeinsamen Segmentierungs-/Scanning-Methoden in `SqlCharScanner` und Auslagerung von `ExecuteSetOptionAsync` in `DatabaseCommandExecutor`.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Database/SqlCharScanner.cs`
- **Was:** Ergänzung der gemeinsamen Methoden `GetSemicolonIndices`, `SplitIntoSegments`, `GetLastNonEmptySegmentIndex`, `StripLeadingCommentsAndWhitespace`.
- **Warum:** Zentralisierung des SQL-Scannings und Segmentierens gem. DRY.

### Datei 2: `src/SqlToAi/Database/QueryDeconstructor.cs`
- **Was:** Entfernen der lokalen Duplikate und Delegation an `SqlCharScanner`.
- **Warum:** Beseitigung von `DuplicateCode`-Warnungen.

### Datei 3: `src/SqlToAi/Database/SqlMultiStatementDetector.cs`
- **Was:** Entfernen der lokalen Duplikate und Delegation an `SqlCharScanner`.
- **Warum:** Beseitigung von `DuplicateCode`-Warnungen.

### Datei 4: `src/SqlToAi/Database/DatabaseCommandExecutor.cs` (neu)
- **Was:** Neue interne Helper-Klasse mit `ExecuteSetOptionAsync`.
- **Warum:** Beseitigung der Duplikation zwischen `PerformanceMeasurementService` und `QueryExecutionService`.

### Datei 5: `src/SqlToAi/Database/PerformanceMeasurementService.cs` und `QueryExecutionService.cs`
- **Was:** Aufruf von `DatabaseCommandExecutor.ExecuteSetOptionAsync` statt privater Duplikat-Methoden.
- **Warum:** Beseitigung von `DuplicateCode`-Warnungen.

## Tests

- [ ] `dotnet build` läuft ohne Fehler/Warnungen.
- [ ] `dotnet test` läuft vollständig grün durch.
- [ ] AiNetLinter `find_duplicates` meldet 0 Duplikate im Produktionscode für diese Methoden.

## Definition of Done

- [ ] Alle Änderungen umgesetzt
- [ ] Build & Test grün
- [ ] Commit erfolgt
- [ ] `step-result.md` geschrieben
