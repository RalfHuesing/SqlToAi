---
status: open
type: step-plan
task: sql-optimization-tools
step: step-004
title: "Kombi-Benchmark (sql_benchmark_optimization) & Dokumentation implementieren"
epic: EPIC-04
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: gemini-3.6-flash
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-03T10:25:00+02:00
related_to: [step-001, step-002, step-003]
---

# Step 004: Kombi-Benchmark (sql_benchmark_optimization) & Dokumentation implementieren

## Bezug

- **Task:** `sql-optimization-tools`
- **Epic:** `EPIC-04` aus `roadmap.md` — Kombi-Benchmark (`sql_benchmark_optimization`) & Dokumentation
- **Konzept-Referenz:** `konzept.md` §Muss-Haben/Tool 3 & Doku (Automatisierter Workflow aus Äquivalenzvergleich & Performancemessung, Delta-Berechnung für CPU & Reads, Verdict & Empfehlung, Synchronisation der Spezifikationen in `docs/` und `README.md`)

## Aktueller Projektzustand (JIT-Kontext)

- `IQueryComparisonService` (step-002) vergleicht Schemas, Row Counts und EXCEPT-Set-Differenzen.
- `IPerformanceMeasurementService` (step-003) erfasst STATISTICS IO/TIME Metriken und parst XML-Pläne.
- `McpConstants.cs`, `ToolRegistry.cs` (Tools #1 bis #14) stehen bereit für Tool #15 (`sql_benchmark_optimization`).

## Intention

Implementierung des zusammengesetzten Benchmark-Services (`IOptimizationBenchmarkService` & `OptimizationBenchmarkService`) und des MCP-Tools `sql_benchmark_optimization`.
Das Tool kombiniert beide Analysen:
1. **Äquivalenzprüfung:** Aufruf von `IQueryComparisonService.CompareQueriesAsync`.
2. **Performancemessung:** Aufruf von `IPerformanceMeasurementService.MeasurePerformanceAsync` für Query A und Query B.
3. **Delta-Berechnung:** Errechnung von Absolut- und Prozentual-Deltas für CPU, Elapsed Time, Logical Reads und Physical Reads.
4. **Verdict & Empfehlung:** Ermittlung eines Urteils (`Recommended`, `NotRecommended`, `Neutral`, `UnsafeDueToDataMismatch`).
5. **Dokumentation:** Aktualisieren aller Tool-Beschreibungen in `docs/mcp-specification.md` und `README.md`.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Domain/OptimizationBenchmarkResult.cs` & `QueryBenchmarkArgs.cs` (NEU)
- **Was:** Domain-Records für Deltas (`MetricDelta`, `BenchmarkMetricsDelta`), Benchmark-Parameter (`QueryBenchmarkArgs`) und Gesamtergebnis (`OptimizationBenchmarkResult`).
- **Warum:** Strukturierte Rückgabe des kombinierten Benchmark-Ergebnisses.

### Datei 2: `src/SqlToAi/Database/IOptimizationBenchmarkService.cs` & `OptimizationBenchmarkService.cs` (NEU)
- **Was:** Service, der `IQueryComparisonService` und `IPerformanceMeasurementService` orchestriert, Deltas berechnet und ein Verdict fällt.
- **Warum:** All-in-One Benchmark-Funktionalität.

### Datei 3: `src/SqlToAi/Mcp/McpConstants.cs`
- **Was:** Ergänzen von `ToolBenchmarkOptimization = "sql_benchmark_optimization"`.
- **Warum:** Vermeidung von Magic Strings.

### Datei 4: `src/SqlToAi/Mcp/ToolRegistry.cs` & `ToolDispatcher.cs` & `McpJsonContext.cs`
- **Was:** Registrierung von `sql_benchmark_optimization` (Tool #15), Routing im Dispatcher und AOT-Context Registration.
- **Warum:** MCP Integration.

### Datei 5: `src/SqlToAi/Program.cs`
- **Was:** DI-Registrierung für `IOptimizationBenchmarkService`.
- **Warum:** DI-Verfügbarkeit.

### Datei 6: `docs/mcp-specification.md` & `README.md`
- **Was:** Dokumentieren der 3 neuen MCP-Tools (`sql_compare_queries`, `sql_measure_performance`, `sql_benchmark_optimization`) und Parameter-Support.
- **Warum:** Synchronisationspflicht laut `.agents/rules/SqlToAiRichtlinien.mdc`.

### Datei 7: `tests/SqlToAi.Tests/Database/OptimizationBenchmarkServiceTests.cs` (NEU) & MCP Tests
- **Was:** Unit-Tests für Delta-Berechnung, Verdicts (`Recommended`, `UnsafeDueToDataMismatch`) und Error Handling.
- **Warum:** 100%ige Abdeckung.

## Tests

- [ ] `OptimizationBenchmarkServiceTests.BenchmarkAsync_EqualQueriesWithPerformanceGain_ReturnsRecommendedVerdict`
- [ ] `OptimizationBenchmarkServiceTests.BenchmarkAsync_DataMismatch_ReturnsUnsafeVerdict`
- [ ] `OptimizationBenchmarkServiceTests.BenchmarkAsync_PerformanceRegression_ReturnsNotRecommendedVerdict`

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command aus Tech-Stack-Notiz (`dotnet build SqlToAi.slnx`) grün
- [ ] Test-Command aus Tech-Stack-Notiz (`dotnet test SqlToAi.slnx`) grün
- [ ] Commit auf aktuellem Branch (Conventional Commit)
- [ ] `step-004/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#5` — MCP-Spezifikation & docs/ Synchronisation
- `.agents/rules/AiNetLinter.mdc` — C# 14 / .NET 10 Coding-Styles & Sealed Classes

## Bekannte Ausnahmen

- Keine

## Code-Skizze (optional)

```csharp
public sealed class OptimizationBenchmarkService : IOptimizationBenchmarkService { ... }
```
