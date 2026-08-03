---
status: open
type: step-plan
task: sql-optimization-tools
step: step-002
title: "Ergebnissatz- & Äquivalenzvergleich (sql_compare_queries) implementieren"
epic: EPIC-02
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: gemini-3.6-flash
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-03T10:18:00+02:00
related_to: [step-001]
---

# Step 002: Ergebnissatz- & Äquivalenzvergleich (sql_compare_queries) implementieren

## Bezug

- **Task:** `sql-optimization-tools`
- **Epic:** `EPIC-02` aus `roadmap.md` — DB-seitiger Ergebnissatz- & Äquivalenzvergleich (`sql_compare_queries`)
- **Konzept-Referenz:** `konzept.md` §Muss-Haben/Tool 1 (`sql_compare_queries`: Schema-Check, Count-Check, DB-seitiger Set-Differenz-Vergleich via `EXCEPT`, Diff-Feedback, Parameter-Support)

## Aktueller Projektzustand (JIT-Kontext)

- `SqlParameterBinder.cs` (in step-001 implementiert) bindet typisierte Parameter an `DbCommand` Objekte.
- `QueryExecutionService.cs` prüft Whitelist, AccessLevel, Read-Only Guard und Multi-Statement Detector und unterstützt PII-Anonymisierung.
- `McpConstants.cs`, `ToolRegistry.cs`, `ToolDispatcher.cs` sind bereit für die Registrierung des neuen MCP-Tools `sql_compare_queries`.

## Intention

Implementierung des Domain-Service `IQueryComparisonService` / `QueryComparisonService` sowie des MCP-Tools `sql_compare_queries`.
Das Tool vergleicht zwei SQL-Abfragen auf der Ziel-DB:
1. **Schema-Check:** Spaltenanzahl, Spaltennamen, Datentypen via `CommandBehavior.SchemaOnly`.
2. **Count-Check:** Exakter Zeilenanzahl-Vergleich.
3. **Inhalts-Check (DB-seitig):** Ausführen von `EXCEPT`-Set-Differenzen in beide Richtungen (A EXCEPT B, B EXCEPT A) im Datenbank-Kontext ohne Übertragung großer Datenmengen.
4. **Diff-Feedback:** Kompaktes JSON-Ergebnis mit Äquivalenz-Status (`is_equal`), Schema-Mismatches, Count-Mismatches und Beispielzeilen für Diffs.
5. **Parameter-Support:** Unterstützung von `parameters_a`, `parameters_b` und gemeinsamen `parameters`.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Domain/QueryComparisonResult.cs` (NEU)
- **Was:** Record/Klasse zur Kapselung des Vergleichsergebnisses (IsEqual, SchemaMatch, CountMatch, RowCountA, RowCountB, SchemaDifferences, RowsInANotInB, RowsInBNotInA).
- **Warum:** Strukturierte Rückgabe des Äquivalenzvergleichs.

### Datei 2: `src/SqlToAi/Database/IQueryComparisonService.cs` & `QueryComparisonService.cs` (NEU)
- **Was:** Service zur Durchführung von Schema-Check, Count-Check und EXCEPT-Set-Differenzvergleich inkl. Parameter-Binding & Security Guards.
- **Warum:** Kernlogik für DB-seitigen Äquivalenzvergleich.

### Datei 3: `src/SqlToAi/Mcp/McpConstants.cs`
- **Was:** Ergänzen von `ToolCompareQueries = "sql_compare_queries"`, `ArgQueryA`, `ArgQueryB`, `ArgParametersA`, `ArgParametersB`, `ArgMaxDiffRows`.
- **Warum:** Vermeidung von Magic Strings im MCP-Layer.

### Datei 4: `src/SqlToAi/Mcp/ToolRegistry.cs`
- **Was:** Erweitern von `BuildTools()` um `BuildCompareQueries()`.
- **Warum:** MCP Schema Registration.

### Datei 5: `src/SqlToAi/Mcp/ToolDispatcher.cs`
- **Was:** Hinzufügen des Handlers für `sql_compare_queries` und Weiterleitung an `IQueryComparisonService`.
- **Warum:** MCP Routing.

### Datei 6: `src/SqlToAi/Program.cs` / Dependency Injection Registration
- **Was:** Registrieren von `IQueryComparisonService` / `QueryComparisonService` in DI.
- **Warum:** Verfeuerung im `ToolDispatcher`.

### Datei 7: `tests/SqlToAi.Tests/Database/QueryComparisonServiceTests.cs` (NEU) & MCP Dispatcher Tests
- **Was:** Unit-Tests für Äquivalenzvergleich (Equal Queries, Schema Mismatch, Row Count Mismatch, Data Mismatch, Parameterized Comparisons).
- **Warum:** 100%ige Testabdeckung aller Pfade.

## Tests

- [ ] `QueryComparisonServiceTests.CompareQueriesAsync_EqualQueries_ReturnsIsEqualTrue`
- [ ] `QueryComparisonServiceTests.CompareQueriesAsync_SchemaMismatch_ReturnsIsEqualFalseWithSchemaDiff`
- [ ] `QueryComparisonServiceTests.CompareQueriesAsync_RowCountMismatch_ReturnsIsEqualFalseWithCountDiff`
- [ ] `QueryComparisonServiceTests.CompareQueriesAsync_DataMismatch_ReturnsExceptDiffs`
- [ ] `QueryComparisonServiceTests.CompareQueriesAsync_WithParameters_ExecutesSuccessfully`

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command aus Tech-Stack-Notiz (`dotnet build SqlToAi.slnx`) grün
- [ ] Test-Command aus Tech-Stack-Notiz (`dotnet test SqlToAi.slnx`) grün
- [ ] Commit auf aktuellem Branch (Conventional Commit)
- [ ] `step-002/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#2` — Read-Only Guard & Safety-Check
- `.agents/rules/AiNetLinter.mdc` — C# 14 / .NET 10 Coding-Styles & Sealed Classes

## Bekannte Ausnahmen

- Keine

## Code-Skizze (optional)

```csharp
public sealed class QueryComparisonService : IQueryComparisonService { ... }
```
