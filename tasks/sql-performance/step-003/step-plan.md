---
status: done
type: step-plan
task: sql-performance
step: 003
title: "ToolRegistry Descriptions Rewrite (sql_measure_performance / sql_benchmark_optimization / sql_execute_query)"
epic: EPIC-03
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: Claude Sonnet 5
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-03T16:00:00+02:00
related_to: []
---

# Step 003: ToolRegistry Descriptions Rewrite

## Bezug

- **Task:** `sql-performance`
- **Epic:** `EPIC-03` aus `roadmap.md` — die drei Tool-Descriptions in `ToolRegistry.cs`
  (`BuildMeasurePerformance`, `BuildBenchmarkOptimization`, `BuildExecuteQuery`) beschreiben nur
  vage, was das jeweilige Tool tut, nennen aber keines der tatsächlichen JSON-Felder, keine
  Verdict-Werte, keine min/avg/max-Semantik und keinen `Execution Info`-Text — der Agent muss die
  Spec (`mcp-specification.md`) lesen, um zu wissen, was er im Ergebnis erwartet.
- **Konzept-Referenz:** `konzept.md` Muss-Haben 3 — „ToolRegistry Descriptions — vollständiger
  Rewrite (agentenlesbar ohne Spec)": `sql_measure_performance` soll die Server-Metriken-Feldnamen
  nennen und `execution_runs > 1` → min/avg/max erklären (inkl. SHOWPLAN-Fallback);
  `sql_benchmark_optimization` soll die Verdict-Werte und die Deltas-Struktur nennen;
  `sql_execute_query` soll den `Execution Info`-Block (Rows + ElapsedMs + cpu + logical reads)
  beschreiben.

## Aktueller Projektzustand (JIT-Kontext)

**`ToolRegistry.cs` (komplett gelesen, aktuell 307 Zeilen):**
- `BuildMeasurePerformance` (L246–263): Description aktuell `"Measures SQL query execution
  metrics (CPU time, elapsed time, logical/physical IO reads) and extracts warnings from the
  actual execution plan XML."` — nennt keine JSON-Feldnamen, keine min/avg/max-Semantik, keinen
  SHOWPLAN-Fallback-Hinweis. Parameter: `ArgDatabase`, `ArgQuery`, `ArgParameters`,
  `ArgWarmupRuns` ("Number of initial unmeasured warmup runs (default 1)."),
  `ArgExecutionRuns` ("Number of measured execution runs to average (default 1)." — **veraltet**:
  seit `step-001` liefert `execution_runs > 1` nicht mehr nur einen Durchschnitt, sondern
  zusätzlich min/max; dieser Parameter-Text muss mit angepasst werden), `ArgIncludePlanAnalysis`.
- `BuildBenchmarkOptimization` (L265–284): Description nennt "performance deltas (CPU, IO)" und
  "recommendation verdict", aber keine der vier tatsächlichen Verdict-Strings.
- `BuildExecuteQuery` (L209–224): Description erwähnt nur "returns the results as JSON lines" und
  Anonymisierung — der seit `step-002` bestehende `Execution Info`-Text (Rows/ElapsedMs/cpu/logical
  reads, siehe unten) ist nicht erwähnt.
- Alle drei Methoden folgen demselben Aufbau (`new() { Name, Description, InputSchema }`) — reine
  String-Änderungen, keine Struktur-/Schema-Änderung an `ToolDefinition`/`ToolParameterDefinition`
  nötig.

**`PerformanceMetrics` (`src/SqlToAi/Domain/PerformanceMeasurementResult.cs`, aus `step-001`,
komplett gelesen) — tatsächliche JSON-Feldnamen für die Description:**
```csharp
public sealed record PerformanceMetrics(
    long CpuTimeMs,        // "cpu_time_ms"       (avg wenn runs>1)
    long ElapsedTimeMs,    // "elapsed_time_ms"   (avg wenn runs>1)
    long LogicalReads,     // "logical_reads"     (avg wenn runs>1)
    long PhysicalReads,    // "physical_reads"    (avg wenn runs>1)
    long ReadAheadReads,   // "read_ahead_reads"  (avg wenn runs>1)
    long? MinElapsedMs,    // "min_elapsed_ms"    (null wenn runs=1)
    long? MaxElapsedMs,    // "max_elapsed_ms"    (null wenn runs=1)
    long? MinCpuMs,        // "min_cpu_ms"        (null wenn runs=1)
    long? MaxCpuMs);       // "max_cpu_ms"        (null wenn runs=1)
```
`PerformanceMeasurementResult` selbst trägt zusätzlich `runs_evaluated`, `warmup_runs`,
`warnings[]` (mit `type`/`severity`/`message`/`impact`), `has_showplan_permission`,
`showplan_note` — alles bereits in `konzept.md`s Soll-Description-Skizze berücksichtigt.

**`OptimizationBenchmarkResult`/`OptimizationBenchmarkService.cs` (komplett gelesen) — tatsächliche
Verdict-Werte (Feld `verdict`, `DetermineVerdictAndSummary`, L99–132), **es sind vier, nicht die
drei/vier grob skizzierten in `konzept.md`**:**
- `"UnsafeDueToDataMismatch"` — Query B liefert andere Ergebnisse/Schema als Query A (Vergleich
  `!comparison.IsEqual`).
- `"Recommended"` — äquivalent, UND CPU/logical reads beide verbessert oder gleich, UND mindestens
  eine davon strikt verbessert.
- `"NotRecommended"` — äquivalent, aber CPU oder logical reads schlechter als Baseline.
- `"Neutral"` — äquivalent, identischer Ressourcenverbrauch.

Zusätzlich `BenchmarkMetricsDelta` (Felder `cpu_time`, `elapsed_time`, `logical_reads`,
`physical_reads`, je ein `MetricDelta` mit `baseline_value`/`candidate_value`/`absolute_delta`/
`percentage_delta`) und `performance_a`/`performance_b` (je ein vollständiges
`PerformanceMeasurementResult` wie oben) — das ist die tatsächliche „Deltas-Struktur", die die
Description nennen soll.

**`ToolDispatcher.cs` L146 (aus `step-002`, bereits final) — tatsächlicher `Execution Info`-Text
für `sql_execute_query`:**
```csharp
string execInfoText = $"Execution Info: {queryResult.RowCount} rows returned in {queryResult.ElapsedMs} ms | cpu: {queryResult.CpuTimeMs} ms | logical reads: {queryResult.LogicalReads}.";
```
Genau dieses Format (`X rows returned in Y ms | cpu: Z ms | logical reads: W`) gehört wörtlich in
die `BuildExecuteQuery`-Description, damit der Agent es wiedererkennt, ohne den Quelltext zu lesen.

**Tests (`ToolRegistryTests.cs`, geprüft):** Es existiert nur
`AllTools_ShouldHaveNonEmptyDescription` (Assert auf nicht-leer) — kein Test verankert den
bisherigen Wortlaut, keine Anpassung an bestehenden Tests nötig, nur optionale Ergänzung neuer
Assertions (siehe „Tests" unten).

## Intention

Nach diesem Step beschreiben die drei `BuildXxx`-Methoden in `ToolRegistry.cs` vollständig
agentenlesbar (ohne Rückgriff auf `mcp-specification.md` nötig), welche JSON-Felder im jeweiligen
Tool-Ergebnis stehen, was `execution_runs > 1` bewirkt (min/avg/max), welche vier Verdict-Werte
`sql_benchmark_optimization` liefern kann, und wie der `Execution Info`-Text von
`sql_execute_query` aufgebaut ist. Reine Text-/Description-Änderung — keine Verhaltens-, Schema-
oder Parameter-Strukturänderung.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Mcp/ToolRegistry.cs` — `BuildMeasurePerformance` (L246–263)

- **Was:** Description ersetzen durch (Feldnamen + min/avg/max + SHOWPLAN-Fallback explizit):
  ```
  Measures SQL query performance via SET STATISTICS IO/TIME on the actual execution (not an
  estimated plan): returns JSON with metrics (cpu_time_ms, elapsed_time_ms, logical_reads,
  physical_reads, read_ahead_reads), runs_evaluated, warmup_runs, warnings[]
  (type/severity/message/impact from the actual execution plan XML), has_showplan_permission,
  showplan_note. Use warmup_runs to pre-warm the plan cache (default 1, not measured);
  execution_runs (default 1) controls how many measured runs are averaged into cpu_time_ms/
  elapsed_time_ms/logical_reads — when execution_runs > 1, metrics additionally include
  min_elapsed_ms/max_elapsed_ms/min_cpu_ms/max_cpu_ms (null when execution_runs = 1). Set
  include_plan_analysis to false to skip execution plan XML analysis. Degrades gracefully
  (has_showplan_permission=false, showplan_note explains why) if SHOWPLAN permission is missing —
  metrics are still returned.
  ```
  Zusätzlich `ArgExecutionRuns`-Parameterbeschreibung anpassen (veraltet, siehe „Aktueller
  Projektzustand"): `"Number of measured execution runs (default 1). When > 1, results include
  min/avg/max per metric instead of only the average."`
- **Warum:** `konzept.md` Muss-Haben 3, erster Punkt — Server-Metriken-Feldnamen, min/avg/max-
  Semantik, SHOWPLAN-Fallback müssen für den Agenten ohne Spec-Lookup sichtbar sein; der
  bestehende `ArgExecutionRuns`-Text ist seit `step-001` sachlich falsch (nur noch "average"
  erwähnt, kein Hinweis auf min/max).

### Datei 1 (Fortsetzung): `BuildBenchmarkOptimization` (L265–284)

- **Was:** Description ersetzen durch (alle vier Verdict-Werte + Deltas-Struktur explizit):
  ```
  Runs a full optimization benchmark comparing baseline (Query A) vs candidate (Query B): checks
  result set equivalence (via sql_compare_queries semantics) and measures both queries' performance
  (same mechanism as sql_measure_performance, using warmup_runs/execution_runs). Returns JSON with
  verdict (one of "Recommended" — equivalent and candidate uses less or equal CPU/logical reads
  with at least one strictly improved; "NotRecommended" — equivalent but candidate uses more CPU or
  logical reads; "Neutral" — equivalent with identical resource usage; "UnsafeDueToDataMismatch" —
  candidate produces different results or schema, cannot replace baseline), summary (human-readable
  explanation), comparison (schema/row-count/EXCEPT diff result), performance_a/performance_b (full
  sql_measure_performance-style results for each query), and deltas (cpu_time/elapsed_time/
  logical_reads/physical_reads, each with baseline_value/candidate_value/absolute_delta/
  percentage_delta — negative percentage_delta means the candidate improved).
  ```
- **Warum:** `konzept.md` Muss-Haben 3, zweiter Punkt — die vier tatsächlichen Verdict-Strings
  (aus `OptimizationBenchmarkService.DetermineVerdictAndSummary`) und die Deltas-Struktur müssen
  wörtlich benannt sein, damit der Agent das `verdict`-Feld ohne Rätselraten auswerten kann.

### Datei 1 (Fortsetzung): `BuildExecuteQuery` (L209–224)

- **Was:** Description ersetzen durch (Execution-Info-Format explizit):
  ```
  Executes a single read-only SELECT statement inside a rollback transaction and returns the
  results as JSON lines, followed by an "Execution Info: X rows returned in Y ms | cpu: Z ms |
  logical reads: W." line (server-side cpu_time_ms/logical_reads via SET STATISTICS IO/TIME,
  measured on every call, no parameter needed; Y is the client round-trip of the query itself).
  String columns are anonymized when the database access level requires it.
  ```
- **Warum:** `konzept.md` Muss-Haben 3, dritter Punkt — der seit `step-002` bestehende
  `Execution Info`-Text ist für den Agenten sonst nirgends dokumentiert außer im Quelltext/Spec.

## Tests

- Keine neuen Pflicht-Tests — reine Description-String-Änderung, `AllTools_ShouldHaveNonEmptyDescription`
  deckt bereits ab, dass keine Description leer wird, und prüft keinen Wortlaut, den dieser Step
  bricht.
- Optional (nicht zwingend, nur falls der Kritiker mehr Verankerung sehen will): drei neue gezielte
  Tests in `ToolRegistryTests.cs`, je einer pro geändertem Tool, die auf das Vorhandensein
  charakteristischer Substrings prüfen (z. B. `BuildMeasurePerformance().Description` enthält
  `"min_elapsed_ms"`; `BuildBenchmarkOptimization().Description` enthält `"UnsafeDueToDataMismatch"`;
  `BuildExecuteQuery().Description` enthält `"logical reads"`) — schützt vor künftigem
  versehentlichem Zurücksetzen auf die alten, unvollständigen Texte.

## Definition of Done

- [ ] Alle drei Descriptions (`BuildMeasurePerformance`, `BuildBenchmarkOptimization`,
      `BuildExecuteQuery`) wie oben umgesetzt
- [ ] `ArgExecutionRuns`-Parametertext in `BuildMeasurePerformance` aktualisiert
- [ ] `dotnet build` — 0 Warnings, 0 Errors
- [ ] `dotnet test` — alle Tests grün (inkl. `AiNetLinterTests.RecreateBaseline`)
- [ ] Commit auf aktuellem Branch (Conventional Commit, `[sql-performance]`-Suffix)
- [ ] `step-003/step-result.md` geschrieben
- [ ] `status` in diesem `step-plan.md` von `open` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/AiNetLinter.mdc` — `MaxLineCount` (500): `ToolRegistry.cs` wächst von 307 auf
  geschätzt ~330–340 Zeilen durch die längeren Description-Strings, bleibt klar unter dem Limit,
  kein Split nötig.
- `.agents/rules/SqlToAiRichtlinien.mdc` — Doku-Synchronisation (`mcp-specification.md` + `README.md`
  bei jeder Feature-Änderung) betrifft diesen Step **nicht direkt**: dieser Step ändert keine
  Tool-Semantik, nur die für den Agenten sichtbare Beschreibung bereits existierender Felder — die
  eigentliche Spec-Aktualisierung (`mcp-specification.md` §12/§14/§15) ist bewusst `EPIC-04`
  vorbehalten (siehe Roadmap-Reihenfolge), nicht in diesem Step vorwegnehmen.

## Bekannte Ausnahmen

- Keine.

## Notes

- **Nur `Description`-Strings ändern, keine `InputSchema`-Struktur.** Kein neuer Parameter, keine
  Typänderung, keine Required-Änderung — reine Text-Ergänzung an bestehenden drei
  `ToolDefinition`-Objekten.
- **`konzept.md`s Soll-Description-Skizze für `sql_measure_performance` ist nur ein Entwurf,
  keine wörtliche Vorgabe** — sie erwähnt z. B. nicht `has_showplan_permission`/`showplan_note`,
  die aber Teil von `PerformanceMeasurementResult` sind und laut Muss-Haben „SHOWPLAN-Fallback"
  explizit genannt werden sollen; dieser Plan ergänzt sie deshalb bewusst gegenüber der Skizze.
- **Verdict-Werte:** `konzept.md` nennt in seiner Einleitung testweise vier Namen
  ("Recommended/NotRecommended/Neutral/Unsafe…"), der tatsächliche Code verwendet
  `"UnsafeDueToDataMismatch"` (nicht nur `"Unsafe"`) — die Description muss den **tatsächlichen**
  String verwenden, nicht die Kurzform aus der Konzept-Einleitung.
- **Nicht in diesem Step:** `mcp-specification.md` §12/§14/§15 — folgt in `EPIC-04`, sobald dieser
  Step abgeschlossen ist.
