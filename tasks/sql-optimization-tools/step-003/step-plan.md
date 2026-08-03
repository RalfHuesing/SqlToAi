---
status: done (pending audit)
type: step-plan
task: sql-optimization-tools
step: step-003
title: "Performance- & Plan-Analyse Engine (sql_measure_performance) implementieren"
epic: EPIC-03
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: gemini-3.6-flash
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-03T10:22:00+02:00
related_to: [step-001, step-002]
---

# Step 003: Performance- & Plan-Analyse Engine (sql_measure_performance) implementieren

## Bezug

- **Task:** `sql-optimization-tools`
- **Epic:** `EPIC-03` aus `roadmap.md` — Performance- & Plan-Analyse Engine (`sql_measure_performance`)
- **Konzept-Referenz:** `konzept.md` §Muss-Haben/Tool 2 (Server-Metriken: CPU-Zeit, Elapsed Time, Logical/Physical Reads via STATISTICS IO/TIME, Execution Plan Warning Parsing, Graceful Degradation bei fehlendem SHOWPLAN-Recht, Warmup & Averaging, Parameter-Support)

## Aktueller Projektzustand (JIT-Kontext)

- `SqlParameterBinder.cs` steht bereit für typsicheres Parameter-Binding.
- `QueryExecutionService.cs` und `QueryComparisonService.cs` demonstrieren Whitelisting, AccessLevel und Read-Only-Guard Integration.
- `McpConstants.cs`, `ToolRegistry.cs` und `ToolDispatcher.cs` können um `sql_measure_performance` erweitert werden.

## Intention

Implementierung der Performance- & Ausführungsplan-Engine (`IPerformanceMeasurementService` & `PerformanceMeasurementService`) sowie des MCP-Tools `sql_measure_performance`.
Das Tool erfasst präzise Server-Metriken:
1. **Server-Metriken:** CPU-Zeit, Elapsed Time, Logical Reads, Physical Reads, Read-Ahead Reads via T-SQL `STATISTICS IO, TIME`.
2. **Actual Execution Plan XML Parsing:** Extraktion von Warnungen (`Missing Indexes`, `CONVERT_IMPLICIT`, kostspielige `Table Scans`).
3. **Graceful Degradation:** Fängt fehlende `SHOWPLAN`-Rechte (SqlException 262) ab und degradiert sauber auf reine IO/TIME-Messung.
4. **Warmup & Averaging:** Ermöglicht Warmup-Durchläufe und gemittelte Metriken über mehrere Iterationen.
5. **Parameter-Support:** Parameter-Binding via `SqlParameterBinder`.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Domain/PerformanceMeasurementResult.cs` & `QueryPerformanceArgs.cs` (NEU)
- **Was:** Records für Metriken (`PerformanceMetrics`), Warnungen (`PerformancePlanWarning`), Argumente (`QueryPerformanceArgs`) und Gesamtergebnis (`PerformanceMeasurementResult`).
- **Warum:** Strukturierte Rückgabe der Performancemessung und AOT-konforme JSON-Serialisierung.

### Datei 2: `src/SqlToAi/Database/IPerformanceMeasurementService.cs` & `PerformanceMeasurementService.cs` (NEU)
- **Was:** Service zur Durchführung von `STATISTICS IO, TIME, XML`-Analysen mit Warmup, Averaging, Graceful Degradation und XML-Plan-Parsing.
- **Warum:** Empirische Leistungsmessung auf SQL Server.

### Datei 3: `src/SqlToAi/Mcp/McpConstants.cs`
- **Was:** Ergänzen von `ToolMeasurePerformance = "sql_measure_performance"`, `ArgWarmupRuns`, `ArgExecutionRuns`, `ArgIncludePlanAnalysis`.
- **Warum:** Vermeidung von Magic Strings im MCP-Layer.

### Datei 4: `src/SqlToAi/Mcp/ToolRegistry.cs` & `ToolDispatcher.cs` & `McpJsonContext.cs`
- **Was:** Registrieren von `sql_measure_performance` (Tool #14), Handhabung im Dispatcher, AOT-Serialisierung in `McpJsonContext`.
- **Warum:** MCP Routing und Registrierung.

### Datei 5: `src/SqlToAi/Program.cs`
- **Was:** DI-Registrierung für `IPerformanceMeasurementService`.
- **Warum:** DI-Verfügbarkeit.

### Datei 6: `tests/SqlToAi.Tests/Database/PerformanceMeasurementServiceTests.cs` (NEU) & MCP Dispatcher Tests
- **Was:** Unit-Tests für Parsen von STATISTICS IO/TIME Messages, XML-Plan Warning Extraction, Graceful Degradation und Service-Guards.
- **Warum:** 100%ige Abdeckung.

## Tests

- [ ] `PerformanceMeasurementServiceTests.MeasurePerformanceAsync_ValidQuery_ReturnsMetrics`
- [ ] `PerformanceMeasurementServiceTests.MeasurePerformanceAsync_MissingShowplanPermission_DegradesGracefully`
- [ ] `PerformanceMeasurementServiceTests.MeasurePerformanceAsync_ParsesStatisticsIoAndDeviceReads`
- [ ] `PerformanceMeasurementServiceTests.MeasurePerformanceAsync_ParsesXmlPlanMissingIndexesAndImplicitConversions`

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command aus Tech-Stack-Notiz (`dotnet build SqlToAi.slnx`) grün
- [ ] Test-Command aus Tech-Stack-Notiz (`dotnet test SqlToAi.slnx`) grün
- [ ] Commit auf aktuellem Branch (Conventional Commit)
- [ ] `step-003/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#2` — Read-Only Guard & Safety-Check
- `.agents/rules/AiNetLinter.mdc` — C# 14 / .NET 10 Coding-Styles & Sealed Classes

## Bekannte Ausnahmen

- Keine

## Code-Skizze (optional)

```csharp
public sealed class PerformanceMeasurementService : IPerformanceMeasurementService { ... }
```
