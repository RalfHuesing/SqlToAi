---
title: "SQL Performance Tools — Erweiterungen & Verbesserungen"
status: ready
last_updated: "2026-08-03"
rules_dir: .agents/rules
project_kind: brownfield
estimated_scope: small
open_questions: []
---

# SQL Performance Tools — Erweiterungen & Verbesserungen

## Hintergrund & Kontext

Die Performance-Tools (`sql_measure_performance`, `sql_benchmark_optimization`) wurden bereits
implementiert und funktionieren (STATISTICS IO/TIME, XML-Plan, Warmup/ExecutionRuns, logicalReads,
cpuMs, Verdict).

Ein Audit-Durchlauf hat folgende offene Punkte identifiziert:

### Was implementiert ist (funktioniert)

| Feature | Status |
|:--|:--|
| `SET STATISTICS TIME/IO ON` | ✅ PerformanceMeasurementService |
| `warmup_runs` / `execution_runs` | ✅ Dispatcher + Args |
| `logicalReads`, `cpuMs` in Ergebnis | ✅ PerformanceMetrics |
| XML-Plan (MissingIndex, ImplicitConvert, TableScan) | ✅ |
| Graceful Degradation (kein SHOWPLAN) | ✅ |
| `sql_benchmark_optimization` Verdict | ✅ |

### Was noch fehlt (dieser Scope)

| Feature | Lücke | Entscheidung |
|:--|:--|:--|
| `runs: N` → min/avg/max | Nur Durchschnitt in `PerformanceMetrics`, kein Min/Max | ✅ `PerformanceMetrics` direkt erweitern: nullable `min_*/max_*` Felder |
| Tool-Beschreibungen (ToolRegistry) | Ausgabeformat + Runs-Semantik nicht sichtbar | ✅ Vollständiger Rewrite aller Performance-/Execute-Descriptions |
| `logicalReads`/`cpuMs` in `sql_execute_query` Execution Info | QueryExecutionService hat kein STATISTICS | ✅ Implementieren — gleiche Connection/Transaction, kein Extra-Roundtrip |
| `sql_execute_batch` | Multi-Statement | ❌ Non-Goal (SQL-AI-0101, sqlcmd-Territorium) |

---

## Scope (dieser Task)

### Muss-Haben (alle drei bestätigt)

1. **min/avg/max bei `execution_runs > 1`** — `PerformanceMetrics` direkt um nullable Felder erweitern:
   - `min_elapsed_ms?: long` / `max_elapsed_ms?: long` (null wenn runs=1)
   - `min_cpu_ms?: long` / `max_cpu_ms?: long` (null wenn runs=1)
   - `avg_*` = bisherige Semantik (rückwärtskompatibel)
   - Betrifft: `PerformanceMeasurementResult.cs`, `PerformanceMeasurementService.cs` (ProcessCapturedOutput),
     `McpJsonContext.cs`, Tests

2. **`logicalReads`/`cpuMs` in `sql_execute_query` Execution Info**:
   - `QueryExecutionService` setzt `SET STATISTICS IO ON` / `SET STATISTICS TIME ON` auf der
     bestehenden Connection+Transaction vor der Query-Ausführung
   - InfoMessage-Handler analog zu `PerformanceMeasurementService` — parst `CpuTimeMs` + `LogicalReads`
   - `Execution Info`-Text wird erweitert: `X rows returned in Y ms | cpu: Z ms | logical reads: W`
   - Betrifft: `QueryExecutionService.cs`, `QueryExecutionResult.cs` (neue Felder), `ToolDispatcher.cs`
     (Execution Info Text), Tests

3. **ToolRegistry Descriptions — vollständiger Rewrite (agentenlesbar ohne Spec)**:
   - `sql_measure_performance`: Server-Metriken (cpu_time_ms, elapsed_time_ms, logical_reads…) im
     JSON-Format explizit nennen; `execution_runs > 1` → min/avg/max erklären; SHOWPLAN-Fallback
   - `sql_benchmark_optimization`: Verdict-Werte (Recommended/NotRecommended/Neutral/Unsafe…)
     nennen; Deltas-Struktur erwähnen
   - `sql_execute_query`: Execution Info-Block beschreiben (Rows + ElapsedMs + cpu + logical reads)
   - Betrifft: `ToolRegistry.cs` (alle drei BuildXxx-Methoden)

---

## Wo im Projekt (Fundstellen)

| Datei | Relevanz |
|:--|:--|
| [PerformanceMeasurementResult.cs](../../src/SqlToAi/Domain/PerformanceMeasurementResult.cs) | PerformanceMetrics + PerformanceMeasurementResult Records |
| [PerformanceMeasurementService.cs](../../src/SqlToAi/Database/PerformanceMeasurementService.cs) | ProcessCapturedOutput — dort wird avg berechnet (L325–330) |
| [QueryPerformanceArgs.cs](../../src/SqlToAi/Domain/QueryPerformanceArgs.cs) | Args-Record (ExecutionRuns ist bereits drin) |
| [ToolRegistry.cs](../../src/SqlToAi/Mcp/ToolRegistry.cs) | BuildMeasurePerformance / BuildBenchmarkOptimization / BuildExecuteQuery |
| [McpConstants.cs](../../src/SqlToAi/Mcp/McpConstants.cs) | ArgWarmupRuns, ArgExecutionRuns (bereits vorhanden) |
| [McpJsonContext.cs](../../src/SqlToAi/Mcp/McpJsonContext.cs) | JSON-Serialisierungskontext (evtl. neue Types registrieren) |
| [mcp-specification.md](../../docs/mcp-specification.md) | §14 sql_measure_performance, §15 sql_benchmark_optimization |

---

## Technische Analyse: min/avg/max

### Ist-Zustand (`ProcessCapturedOutput`, Zeile 302–337)

```csharp
long totalCpu = 0, totalElapsed = 0, ...;
foreach (string msg in messages) { /* addiert alle Runs */ }

var metrics = new PerformanceMetrics(
    CpuTimeMs: totalCpu / execRuns,   // Durchschnitt
    ElapsedTimeMs: totalElapsed / execRuns,
    ...);
```

Das Problem: Die STATISTICS TIME/IO Meldungen kommen pro Query-Lauf als InfoMessages rein. Die
aktuelle Implementierung summiert alle und dividiert. Für min/max müssen die Messages **per Run**
zugeordnet werden.

### Gewählte Lösung: PerformanceMetrics direkt erweitern (Option A)

```csharp
public sealed record PerformanceMetrics(
    long CpuTimeMs,        // avg
    long ElapsedTimeMs,    // avg (rückwärtskompatibel)
    long LogicalReads,     // avg
    long PhysicalReads,    // avg
    long ReadAheadReads,   // avg
    long? MinElapsedMs,    // null wenn runs=1
    long? MaxElapsedMs,    // null wenn runs=1
    long? MinCpuMs,        // null wenn runs=1
    long? MaxCpuMs)        // null wenn runs=1
```

In `ProcessCapturedOutput`: `messages.Clear()` nach jedem Run, pro Run parsen, dann min/avg/max
berechnen. `PerformanceMeasurementResult` bleibt unverändertes `record` (neues `Metrics` enthält
alles).

---

## Technische Analyse: Tool-Descriptions

### Ist-Zustand `BuildMeasurePerformance` (ToolRegistry.cs, L246–263)

Description: `"Measures SQL query execution metrics (CPU time, elapsed time, logical/physical IO
reads) and extracts warnings from the actual execution plan XML."`

Nicht sichtbar für den Agenten:
- Welche Felder im JSON-Result stehen (cpu_time_ms, logical_reads, etc.)
- Dass `execution_runs > 1` averaging macht (→ soll min/avg/max machen)
- Was `Execution Info` ist

### Soll-Description (Skizze)

```
Measures SQL query performance via SET STATISTICS IO/TIME: captures cpu_time_ms,
elapsed_time_ms, logical_reads, physical_reads, read_ahead_reads (server-side, not
round-trip). Optional execution plan XML analysis (MissingIndex, ImplicitConversion,
TableScan). Use warmup_runs to pre-warm the plan cache (default 1); execution_runs
controls how many measured runs are averaged — when > 1, result includes min/avg/max
per metric. Returns JSON with metrics, warnings[], runs_evaluated, warmup_runs.
Degrades gracefully if SHOWPLAN permission is missing.
```

---

## Entdeckte Mängel / Redundanzen

| Fund | Entscheidung |
|:--|:--|
| `sql_execute_batch` fehlt | Bewusst Non-Goal (SQL-AI-0101 bleibt, sqlcmd-Territorium) |
| `Execution Info` in `sql_execute_query` fehlt logicalReads | Nice-to-Have, Entscheidung offen |
| ToolRegistry Descriptions unvollständig | In Scope (Muss) |
| min/avg/max fehlt | In Scope (Muss) |

---

## Verifikationsplan

1. `dotnet build` — Zero Warnings
2. `dotnet test` — alle Tests grün (inkl. AiNetLinter RecreateBaseline)
3. Manuell: `sql_measure_performance` mit `execution_runs: 3` aufrufen → min/avg/max im JSON
4. `GetMcpTools` überprüfen → Description sichtbar für Agenten
5. `mcp-specification.md` §14 + §15 aktualisieren

---

## Non-Goals (explizit)

- `sql_execute_batch` (Multi-Statement, SET STATISTICS-Batch) — bleibt sqlcmd-Territorium
- Persistente Performance-Logs / Verlaufsdaten
- `include_statistics` / `include_io_statistics` als separate Bool-Parameter (bereits durch
  `include_plan_analysis` und STATISTICS IO/TIME abgedeckt)
