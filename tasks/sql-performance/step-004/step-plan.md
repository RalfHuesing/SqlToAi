---
status: done (pending audit)
type: step-plan
task: sql-performance
step: 004
title: "mcp-specification.md: sql_execute_query/sql_measure_performance/sql_benchmark_optimization synchronisieren"
epic: EPIC-04
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: Claude Sonnet 5
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-03T18:00:00+02:00
related_to: [tasks/sql-performance/step-001, tasks/sql-performance/step-002, tasks/sql-performance/step-003]
---

# Step 004: mcp-specification.md Tool-Spezifikationen aktualisieren

## Bezug

- **Task:** `sql-performance`
- **Epic:** `EPIC-04` aus `roadmap.md` — letztes offenes Epic: `docs/mcp-specification.md`
  Abschnitt „4. MCP Tool-Spezifikationen", Punkte 12 (`sql_execute_query`), 14
  (`sql_measure_performance`), 15 (`sql_benchmark_optimization`) hinken dem
  tatsächlichen Code-/Feature-Stand aus `step-001`..`step-003` hinterher.
- **Konzept-Referenz:** `konzept.md` §Muss-Haben 1-3 (Min/Avg/Max-Metriken,
  STATISTICS IO/TIME in `sql_execute_query`, agentenlesbare Tool-Descriptions) —
  dieser Step schließt die für diese Muss-Haben-Punkte noch ausstehende
  Doku-Synchronisation ab (Pflicht laut `.agents/rules/SqlToAiRichtlinien.mdc`
  Abschnitt 4 „Dokumentations-Synchronisation").

## Aktueller Projektzustand (JIT-Kontext)

Ich habe `docs/mcp-specification.md` (Abschnitt 4, Punkte 12/14/15), die drei
Step-Results (`step-001`, `step-002`, `step-003`) sowie den aktuellen Code
(`src/SqlToAi/Domain/PerformanceMeasurementResult.cs`,
`src/SqlToAi/Domain/QueryExecutionResult.cs`,
`src/SqlToAi/Domain/OptimizationBenchmarkResult.cs`,
`src/SqlToAi/Mcp/ToolRegistry.cs`, `src/SqlToAi/Mcp/ToolDispatcher.cs:146`)
gelesen. Konkrete Diskrepanzen zwischen Doku (Ist) und Code (Soll):

- **Punkt 12 (`sql_execute_query`):** Doku nennt im „Mehrfach-Content-Rückgabe"-
  Abschnitt nur `Execution Info: X rows returned in Y ms.` — der tatsächliche
  Text (`ToolDispatcher.cs:146`, seit `step-002`) lautet
  `Execution Info: X rows returned in Y ms | cpu: Z ms | logical reads: W.`
  Die Doku erwähnt `cpu_time_ms`/`logical_reads` nirgends.
- **Punkt 14 (`sql_measure_performance`):** Doku beschreibt nur die
  Metrik-*Namen* in Prosa (CPU-Zeit, Elapsed Time, Logical/Physical/Read-Ahead
  Reads), nennt aber weder die tatsächlichen JSON-Feldnamen
  (`PerformanceMeasurementResult`: `database`, `runs_evaluated`, `warmup_runs`,
  `metrics`, `warnings[]`, `has_showplan_permission`, `showplan_note`; `metrics`
  = `cpu_time_ms`/`elapsed_time_ms`/`logical_reads`/`physical_reads`/
  `read_ahead_reads` plus die seit `step-001` neuen nullable
  `min_elapsed_ms`/`max_elapsed_ms`/`min_cpu_ms`/`max_cpu_ms`) noch die
  min/avg/max-Semantik (nur befüllt bei `execution_runs > 1`, sonst `null`;
  Min/Max existieren nur für `elapsed`/`cpu`, nicht für die drei Reads-Felder —
  von `step-003`s Review als korrekte Einschränkung bestätigt). `warmup_runs`/
  `execution_runs`-Parameter fehlen als benannte Argumente komplett in der
  Prosa (nur implizit über die Argument-Tabelle erwähnt).
- **Punkt 15 (`sql_benchmark_optimization`):** Die vier Verdict-Werte sind
  bereits korrekt genannt. Es fehlt aber die JSON-Struktur der Rückgabe:
  `OptimizationBenchmarkResult` liefert `database`, `verdict`, `summary`,
  `comparison` (volles `sql_compare_queries`-Ergebnis),
  `performance_a`/`performance_b` (je ein volles
  `sql_measure_performance`-Ergebnis) und `deltas` (`BenchmarkMetricsDelta`:
  `cpu_time`/`elapsed_time`/`logical_reads`/`physical_reads`, je ein
  `MetricDelta` mit `baseline_value`/`candidate_value`/`absolute_delta`/
  `percentage_delta`). Nichts davon steht aktuell in der Doku.
- Die in `ToolRegistry.cs` (`step-003`) bereits vollständig agentenlesbar
  formulierten Tool-Descriptions sind die zuverlässigste Quelle für die exakten
  Feldnamen/Semantik-Formulierungen — dieser Step übernimmt diese Formulierungen
  sinngemäß nach `mcp-specification.md`, statt sie neu zu erfinden (Wiederver-
  wendung bestehender, bereits geprüfter Texte statt Duplikat-Drift).
- `README.md` wurde geprüft (Feature-Bullets Zeilen 12-14, 24; Tabelle Zeile 55;
  Abschnitt „Least Privilege" Zeile 94): Dort stehen nur High-Level-
  Beschreibungen ohne Feldnamen-/JSON-Detailgrad — diese sind weiterhin
  zutreffend und werden von diesem Step **nicht** geändert (Doku-Sync-Pflicht
  bezieht sich auf tatsächlich veraltete Aussagen, nicht auf pauschales Anfassen
  jeder Datei; `roadmap.md` EPIC-04 nennt explizit nur `mcp-specification.md`).

## Intention

`docs/mcp-specification.md` Abschnitt 4, Punkte 12/14/15 auf den tatsächlichen
Code-Stand bringen: konkrete JSON-Feldnamen, die min/avg/max-Semantik, den
erweiterten `Execution Info`-Text sowie die vollständige `deltas`/
`performance_a`/`performance_b`-Struktur nennen — analog zum Detailgrad, den
`step-003` bereits für die MCP-Tool-Descriptions selbst hergestellt hat. Damit
ist EPIC-04 das letzte offene Epic aus `roadmap.md` abgeschlossen.

## Konkrete Änderungen

### Datei 1: `docs/mcp-specification.md` — Punkt 12 `sql_execute_query` (aktuell Zeile 257-267)

- **Was:** Im Unterpunkt „Mehrfach-Content-Rückgabe & Laufzeit-Metadaten",
  Punkt 2 (`Execution Info`-Header) präzisieren: Text lautet tatsächlich
  `Execution Info: X rows returned in Y ms | cpu: Z ms | logical reads: W.`
  Ergänzen, dass `cpu_time_ms`/`logical_reads` serverseitig über
  `SET STATISTICS IO/TIME ON` bei jedem Aufruf gemessen werden (kein
  Parameter, kein Extra-Roundtrip) und `Y` weiterhin die reine Client-
  Laufzeit der Abfrage selbst ist (nicht identisch mit `Z`).
- **Warum:** Doku nennt aktuell nur `X rows returned in Y ms.` — die seit
  `step-002` produzierte Erweiterung fehlt komplett; ein Agent, der nur die
  Doku liest, kennt `cpu`/`logical reads` im Execution-Info-Text nicht.

### Datei 1 (Fortsetzung): Punkt 14 `sql_measure_performance` (aktuell Zeile 277-280)

- **Was:** Absatz um die konkrete JSON-Struktur ergänzen: Rückgabe ist
  `PerformanceMeasurementResult` mit `database`, `runs_evaluated`,
  `warmup_runs`, `metrics`, `warnings[]` (je `type`/`severity`/`message`/
  `impact`), `has_showplan_permission`, `showplan_note`. `metrics` enthält
  `cpu_time_ms`/`elapsed_time_ms`/`logical_reads`/`physical_reads`/
  `read_ahead_reads` sowie die nullable `min_elapsed_ms`/`max_elapsed_ms`/
  `min_cpu_ms`/`max_cpu_ms` — letztere vier sind nur bei `execution_runs > 1`
  befüllt (sonst `null`) und existieren nur für `elapsed`/`cpu`, nicht für
  die drei Reads-Felder. `warmup_runs`- und `execution_runs`-Parameter im
  Fließtext explizit als steuernde Argumente benennen (Default je 1).
- **Warum:** Doku beschreibt aktuell nur Metrik-Namen in Prosa ohne
  JSON-Feldnamen und ohne die in `step-001` eingeführte Min/Max-Semantik —
  das für die Doku-Pflicht relevante „Muss-Haben 1" aus `konzept.md` ist
  damit dokumentarisch noch nicht abgeschlossen.

### Datei 1 (Fortsetzung): Punkt 15 `sql_benchmark_optimization` (aktuell Zeile 282-284)

- **Was:** Absatz um die vollständige Rückgabestruktur ergänzen:
  `database`, `verdict`, `summary`, `comparison` (volles
  `sql_compare_queries`-Ergebnis), `performance_a`/`performance_b` (je ein
  vollständiges `sql_measure_performance`-Ergebnis wie in Punkt 14
  beschrieben) und `deltas` mit `cpu_time`/`elapsed_time`/`logical_reads`/
  `physical_reads`, je ein Objekt mit `baseline_value`/`candidate_value`/
  `absolute_delta`/`percentage_delta` (negativer `percentage_delta` = Kandidat
  verbessert). Die vier Verdict-Werte bleiben wie bereits vorhanden.
- **Warum:** Verdict-Werte sind schon korrekt dokumentiert, aber die
  JSON-Struktur der eigentlichen Antwort (`deltas`, `performance_a/b`) fehlt
  komplett — ein Agent müsste sonst den Code lesen, um die Feldnamen zu
  kennen (genau das Problem, das `step-003` für die Tool-Descriptions bereits
  gelöst hat).

## Tests

Keine — reine Dokumentations-Änderung ohne Code-/Verhaltensänderung; es gibt
keinen automatisierten Test, der `mcp-specification.md`-Prosa gegen den Code
abgleicht (anders als `AllTools_ShouldHaveNonEmptyDescription`, das nur prüft,
dass Descriptions nicht leer sind).

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt (Punkte 12, 14, 15 in
      `docs/mcp-specification.md`)
- [ ] Build-Command aus Tech-Stack-Notiz (`roadmap.md`) grün (`dotnet build`)
- [ ] Test-Command aus Tech-Stack-Notiz grün (`dotnet test`) — hier nur als
      Regressions-Nachweis, da diese Änderung rein dokumentarisch ist
- [ ] Commit auf aktuellem Branch (Conventional Commit)
- [ ] `step-004/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)`
      gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc` Abschnitt 4 „Updates, Dokumentation &
  Sprachen" — Doku-Synchronisations-Pflicht (`mcp-specification.md` +
  `README.md`) sowie die Sprachvorgabe, dass alle Markdown-Dokumentation in
  `docs/` auf Englisch verfasst sein muss (`mcp-specification.md` ist aktuell
  komplett auf Deutsch — das ist ein vorbestehender Zustand, den dieser Step
  nicht behebt, siehe „Bekannte Ausnahmen" unten).

## Bekannte Ausnahmen

- `mcp-specification.md` ist derzeit komplett in deutscher Prosa verfasst,
  obwohl `SqlToAiRichtlinien.mdc` für `docs/**` Englisch vorschreibt. Das ist
  ein bestehender, projektweiter Zustand (nicht durch `step-001`..`step-003`
  verursacht) und außerhalb des Scopes von EPIC-04 (das nur die *inhaltliche*
  Aktualität der Punkte 12/14/15 zum Ziel hat). Neue Sätze in diesem Step
  werden auf Deutsch ergänzt, um stilistisch konsistent mit dem Rest der Datei
  zu bleiben — eine vollständige Übersetzung von `mcp-specification.md` ist ein
  eigenständiger, deutlich größerer Schritt und keinesfalls Teil dieses Steps.
  Sollte der Kritiker das als Konzept-Treue-Verstoß werten, ist das eine
  Nutzer-Entscheidung (neues Epic), keine automatische Erweiterung dieses Plans.

## Code-Skizze (optional)

Keine — reine Markdown-Textänderung, kein Code betroffen.

## Notes

- Die Formulierungen für die JSON-Feldnamen/Semantik sollten sich eng an die
  bereits geprüften (Kritiker-approved) Tool-Descriptions in
  `src/SqlToAi/Mcp/ToolRegistry.cs` (`BuildMeasurePerformance`,
  `BuildBenchmarkOptimization`, `BuildExecuteQuery`, seit `step-003`)
  anlehnen, statt neue, potenziell abweichende Formulierungen zu erfinden —
  das minimiert das Risiko einer erneuten Doku/Code-Drift zwischen den beiden
  Quellen.
- Dies ist nach aktuellem Stand von `roadmap.md` das **letzte offene Epic**
  des Tasks `sql-performance`. Sollte dieser Step approved werden und keine
  neuen Muss-Haben-Punkte aus `konzept.md` auftauchen, meldet der nächste
  Planer-Aufruf (Step-Modus, Schritt 1) voraussichtlich „keine offenen Epics
  mehr".
