---
status: open
type: step-plan
task: sql-optimization-tools
step: step-001
title: "Typisierte SQL-Parameter in Execute- und Validate-Tools nachrüsten"
epic: EPIC-01
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: gemini-3.6-flash
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-03T10:12:00+02:00
related_to: []
---

# Step 001: Typisierte SQL-Parameter in Execute- und Validate-Tools nachrüsten

## Bezug

- **Task:** `sql-optimization-tools`
- **Epic:** `EPIC-01` aus `roadmap.md` — Typisierte SQL-Parameter in `sql_execute_query` & `sql_validate_query`
- **Konzept-Referenz:** `konzept.md` §Muss-Haben/Refactoring & §Zielplattformen ("Parameter Mapping Engine: System.Text.Json Parser mit automatischer Typerkennung sowie Fallback für explizite Typvorgaben")

## Aktueller Projektzustand (JIT-Kontext)

- `QueryExecutionService.cs` und `QueryValidationService.cs` führen Queries ohne SQL-Parameter aus. Parameter in MCP-Calls werden aktuell nicht entgegengenommen.
- `ToolRegistry.cs` definiert schemas für `sql_execute_query` und `sql_validate_query` ohne ein `parameters`-Feld.
- `ToolDispatcher.cs` extrahiert nur `query`, `database` und `requestedRowLimit`.
- Dapper (`SqlMapper`) bzw. `DbCommand` werden bereits für Datenzugriffe genutzt. `McpConstants.cs` speichert die MCP-Argument-Strings.

## Intention

Ein zentrales, typsicheres Parameter-Binding für MCP-Calls erstellen. Es ermöglicht sowohl automatische Typerkennung (int, double, bool, ISO-8601 DateTime/Guid, string) als auch explizite Typvorgaben via JSON (`{"value": "...", "dbType": "AnsiString"}`). Dieses Binder-Modul soll von `sql_execute_query`, `sql_validate_query` sowie später von den neuen Performance- und Compare-Tools genutzt werden.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Database/SqlParameterBinder.cs` (NEU)
- **Was:** Implementierung der statischen Helferklasse/Engine `SqlParameterBinder`, die ein JSON-Element oder ein `Dictionary<string, object?>` in `DynamicParameters` / `DbParameter` umwandelt.
- **Warum:** Zentrale und wiederverwendbare Logik für Parameter-Binding inkl. Auto-Detection und explizitem DB-Typ-Override.

### Datei 2: `src/SqlToAi/Mcp/McpConstants.cs`
- **Was:** Ergänzen der Konstanten `ArgParameters = "parameters"`.
- **Warum:** Vermeidung von Magic Strings bei MCP-Argument-Schlüsseln.

### Datei 3: `src/SqlToAi/Database/IQueryExecutionService.cs` & `QueryExecutionService.cs`
- **Was:** Überladung/Erweiterung von `ExecuteQueryAsync` um den optionalen Parameter `IDictionary<string, object?>? parameters` bzw. `JsonElement? parameters`.
- **Warum:** Einbindung der Parameter beim Datenbankaufruf über `SqlParameterBinder`.

### Datei 4: `src/SqlToAi/Database/IQueryValidationService.cs` & `QueryValidationService.cs`
- **Was:** Überladung/Erweiterung von `ValidateQueryAsync` um optionales Parameter-Binding bei der Syntax- und Objektreferenzprüfung (`PARSEONLY`).
- **Warum:** Parametrisierte Abfragen mit `@param` Syntax-Prüfung ermöglichen.

### Datei 5: `src/SqlToAi/Mcp/ToolRegistry.cs`
- **Was:** Aktualisieren der InputSchemas von `BuildExecuteQuery()` und `BuildValidateQuery()` um das optionale `parameters`-Objekt mit Beschreibung.
- **Warum:** MCP Client / Agent Schema-Awareness.

### Datei 6: `src/SqlToAi/Mcp/ToolDispatcher.cs`
- **Was:** Extrahieren des optionalen `parameters`-Arguments im Dispatcher und Weiterleitung an `_queryExecutionService` und `_queryValidationService`.
- **Warum:** Durchreichen des MCP-Payloads an die Domain-Services.

### Datei 7: `tests/SqlToAi.Tests/SqlParameterBinderTests.cs` (NEU) & Erweiterung bestehender Execution/Validation Tests
- **Was:** Unit-Tests für `SqlParameterBinder` (Primitives, Dates, Guids, Nulls, Explicit DbTypes, Error-Cases) sowie Integrationstests für parametrisierte Execution & Validation.
- **Warum:** Sicherstellen der Korrektheit und 100%ige Abdeckung.

## Tests

- [ ] `SqlParameterBinderTests.BindParameters_AutoDetectsTypes`
- [ ] `SqlParameterBinderTests.BindParameters_HandlesExplicitDbTypeOverride`
- [ ] `QueryExecutionServiceTests.ExecuteQueryAsync_WithParameters_BindsSuccessfully`
- [ ] `QueryValidationServiceTests.ValidateQueryAsync_WithParameters_ValidatesSuccessfully`

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command aus Tech-Stack-Notiz (`dotnet build SqlToAi.slnx`) grün
- [ ] Test-Command aus Tech-Stack-Notiz (`dotnet test SqlToAi.slnx`) grün
- [ ] Commit auf aktuellem Branch (Conventional Commit)
- [ ] `step-001/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#2` — Read-Only Guard & Safety-Check
- `.agents/rules/AiNetLinter.mdc` — C# 14 / .NET 10 Coding-Styles & Sealed Classes

## Bekannte Ausnahmen

- Keine

## Code-Skizze (optional)

```csharp
public static class SqlParameterBinder
{
    public static DynamicParameters BuildDynamicParameters(JsonElement element) { ... }
}
```

## Notes

- `SqlParameterBinder` muss fehlertolerant und sicher gegen Injection arbeiten.
- Vorhandene Tests dürfen nicht brechen (Rückwärtskompatibilität ohne `parameters`).
