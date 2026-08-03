---
status: done
type: step-review
task: sql-performance
step: 001
epic: EPIC-01
step_type: single
reviewed_by: kritiker
reviewed_by_model: Claude Sonnet 5
reviewed_by_model_knowledge_cutoff: 2026-01
reviewed_at: 2026-08-03T15:20:00+02:00
verdict: issues
tech_debt_ids: []
---

# Review Step 001: PerformanceMetrics min/avg/max erweitern

## Verdict

- [ ] **approved** — alle vier Prüfebenen ok
- [x] **issues** — Fix-Step `step-001/fix-01` angelegt mit Fix-Plan
- [ ] **blocked** — Nutzer-Entscheidung nötig (siehe Frage unten)

## Geprüft

- [x] Plan-Erfüllung: alle im `step-plan.md` genannten Änderungen erfolgt
- [x] Rules-Konformität: `.agents/rules/AiNetLinter.mdc` + `.agents/rules/SqlToAiRichtlinien.mdc` eingehalten
- [ ] Logische Korrektheit: Code macht was er soll, nicht nur „grün" — ein Edge-Case-Fehler gefunden
- [x] Konzept-Treue: passt die Umsetzung zu `konzept.md` (Scope, Non-Goals, Muss-Haben)
- [x] Build: selbst nachgeprüft, grün
- [x] Tests: selbst nachgeprüft, grün (448 Tests bei `dotnet test --filter "Category!=Integration"`; `SqlToAi-baseline.json` wurde durch den Lauf automatisch neu geschrieben und danach wieder auf Commit-Stand zurückgesetzt, um den Review sauber zu halten — kein inhaltlicher Unterschied, nur Neuberechnung derselben Hashes)

## Befund

### Plan-Erfüllung

Alle vier geplanten Änderungen sind umgesetzt: `PerformanceMetrics` um die vier nullable Felder erweitert (`PerformanceMeasurementResult.cs:11-18`), `ExecuteMeasuredRunsAsync` sammelt jetzt Pro-Run-Snapshots (`PerformanceMeasurementService.cs`), `ProcessCapturedOutput` delegiert an die neue `PerformanceMetricsCalculator`-Klasse, und `PerformanceMetricsCalculatorTests.cs` deckt das min/avg/max-Verhalten ab. `McpJsonContext.cs` wurde plangemäß nicht angefasst. Abweichung (separate Calculator-Klasse statt privater Methode `ComputeRunMetrics`) ist im Step-Result nachvollziehbar begründet (Testbarkeit — löst das im Plan selbst benannte Problem sauberer als die dort skizzierte Reflection-Alternative) und bewegt sich nicht außerhalb des Step-Scopes (gleiche Datei/gleicher Umbau, nur anders geschnitten). Kein Finding hierzu.

### Rules-Konformität

`AiNetLinter.mdc`: `PerformanceMetricsCalculator` ist `internal static class` (implizit sealed), Methoden ≤60 Zeilen, `Compute` hat nach der Extraktion von `OrNullIfSingleRun` niedrige zyklomatische Komplexität (deutlich unter dem Limit 12) — die im Step-Result dokumentierte Notwendigkeit der Extraktion (CC 13 vor der Auslagerung) ist plausibel und durch den grünen Build bestätigt. `#nullable enable` vorhanden, `sealed`/PascalCase/ASCII-Bezeichner eingehalten. Test-Sentinel (`// @covers PerformanceMetricsCalculator`) vorhanden in der neuen Testdatei. `SqlToAiRichtlinien.mdc`: keine neuen Magic Values/Config nötig (§4 „Keine hartkodierten Werte" betrifft hier nicht zutreffend, da keine neue Konfigurationsoption eingeführt wurde), Zero-Warning-Direktive eingehalten (0 Warnings im eigenen Build-Lauf bestätigt), Baseline-Update automatisch über `RecreateBaseline`-Test (kein manuelles Hash-Rechnen) — konform zu §5. Kein Finding hierzu.

### Logische Korrektheit

Ein Edge-Case-Fehler in `PerformanceMetricsCalculator.Compute` (siehe Findings unten): Die Schwelle `runCpu > 0 || runElapsed > 0` zur Erkennung eines „gültigen" Runs für Min/Max verwechselt zwei unterschiedliche Fälle — „Run hat keine STATISTICS-Message erzeugt" (echtes Fehlersignal, kommt so im normalen Ablauf aber praktisch nicht vor, da nur erfolgreich abgeschlossene Runs überhaupt in `perRunMessages` landen) und „Run hat echte 0 ms CPU-/Elapsed-Zeit gemessen" (legitimes Messergebnis bei sehr schnellen Queries, bei den in diesem Projekt üblichen leichten Metadaten-/Selektions-Queries nicht unrealistisch). Im zweiten Fall werden `MinCpuMs`/`MaxCpuMs`/`MinElapsedMs`/`MaxElapsedMs` fälschlich `null` statt `0` zurückgegeben, obwohl `execRuns > 1` ist — das widerspricht Abnahme-Kriterium 5 aus dem Plan („Bei execRuns>1: min/max korrekt berechnet"). Der Coder hat dies selbst im Abschnitt „Bekannte Unschärfen" als klärungsbedürftig markiert; nach Prüfung stufe ich es als echten, in der Praxis nicht nur theoretischen Fehlerfall ein (kein Test deckt eine Mischung aus 0-ms- und Nicht-0-ms-Runs oder durchgängig 0-ms-Runs bei `execRuns > 1` ab).

### Konzept-Treue (Ebene 4)

Umsetzung entspricht `konzept.md` Muss-Haben-Punkt 1 (Felder, Avg-Semantik rückwärtskompatibel, `null` bei `execRuns == 1`) exakt im Scope; kein Non-Goal berührt, kein Scope-Creep (Punkte 2 und 3 aus `konzept.md` sind laut Plan explizit „Nicht in diesem Step"). Der oben beschriebene Logikfehler betrifft indirekt auch die Konzept-Ebene, da er das für `execRuns > 1` zugesicherte „korrekt berechnet" verfehlt — siehe Finding.

### Build-/Test-Status

```
dotnet build                                    → grün (0 Warnings, 0 Errors)
dotnet test --filter "Category!=Integration"    → grün (448 Tests, 0 Fehler)
```

## Findings

1. `src/SqlToAi/Database/PerformanceMetricsCalculator.cs:43-49` (Berechnungslogik) und `:61-64,71-72` (`OrNullIfSingleRun`) — [MAJOR] [Logische Korrektheit / Konzept-Treue] Die Gültigkeits-Schwelle `runCpu > 0 || runElapsed > 0` schließt Runs mit echten `0 ms`-Messwerten von Min/Max aus. Bei `execRuns > 1` und mindestens einem genuinen `0 ms`-Run (realistisch bei schnellen Metadaten-Queries) werden `MinCpuMs`/`MaxCpuMs`/`MinElapsedMs`/`MaxElapsedMs` fälschlich `null` statt dem korrekten Wert `0` zurückgegeben — verfehlt Abnahme-Kriterium 5 des Plans. **Fix:** Gültigkeit eines Runs nicht über einen Wert-Schwellenwert, sondern strukturell bestimmen (z. B. Rückgabe eines `bool HasMatch` aus `ParseRunMessages`, das direkt widerspiegelt, ob die Regex-Patterns für diesen Run überhaupt gematcht haben, unabhängig vom geparsten Zahlenwert), und Min/Max nur über tatsächlich geparste Runs bilden. Testfälle ergänzen: (a) `execRuns > 1` mit mindestens einem echten `0`/`0`-Run gemischt mit Nicht-Null-Runs → Min muss `0` sein, nicht `null`; (b) `execRuns > 1` mit durchgängig `0`/`0`-Runs → Min/Max müssen `0` sein, nicht `null`.
