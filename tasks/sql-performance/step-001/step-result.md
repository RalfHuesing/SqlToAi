---
status: done
type: step-result
task: sql-performance
step: 001
epic: EPIC-01
step_type: single
coded_by: coder
coded_by_model: Claude Sonnet 5
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-03T13:38:31+02:00
code_commit_hash: 5c40cac86027287509c41e2c769a288947e04d64
status_after: done
blocker_category: n/a
---

# Result Step 001: PerformanceMetrics min/avg/max erweitern

## Zusammenfassung

`PerformanceMetrics` um nullable `min_elapsed_ms`/`max_elapsed_ms`/`min_cpu_ms`/`max_cpu_ms` erweitert.
`ExecuteMeasuredRunsAsync` erfasst die STATISTICS-Messages jetzt pro Run (statt über alle Runs
summiert in einer gemeinsamen Liste), `ProcessCapturedOutput` reicht diese Pro-Run-Listen an eine
neue statische Klasse `PerformanceMetricsCalculator` weiter, die Avg (bisherige Semantik) sowie
Min/Max daraus berechnet. Bei `execRuns == 1` bleiben Min/Max `null`.

Diese Session hat einen unfertigen, unvollständig committeten Stand einer abgebrochenen Vorgänger-Session
vorgefunden (abweichender Ansatz: separate Calculator-Klasse statt private Methode `ComputeRunMetrics`
im Service, siehe „Abweichungen vom Plan"), geprüft, vervollständigt und zwei Linter-Verstöße behoben,
die durch diesen Ansatz neu entstanden waren.

## Geänderte Dateien

- `src/SqlToAi/Domain/PerformanceMeasurementResult.cs` — `PerformanceMetrics` um 4 nullable Felder erweitert (mit `= null`-Defaults für Rückwärtskompatibilität an bestehenden Call-Sites).
- `src/SqlToAi/Database/PerformanceMeasurementService.cs` — `ExecuteMeasuredRunsAsync` sammelt jetzt `IReadOnlyList<IReadOnlyList<string>> perRunMessages` (Clear vor jedem Run, Snapshot danach); `ProcessCapturedOutput` delegiert die Berechnung an `PerformanceMetricsCalculator.Compute`; die beiden `CpuTimeRegex`/`IoReadsRegex`-Felder (jetzt ungenutzt, da in die neue Klasse verschoben) wurden entfernt.
- `src/SqlToAi/Database/PerformanceMetricsCalculator.cs` (neu) — `internal static class` mit `Compute(perRunMessages, execRuns)`; parst pro Run CPU/Elapsed/IO-Werte, aggregiert Avg (Summe/execRuns) sowie Min/Max über alle Runs mit Werten > 0. Min/Max-Ternäre in private Hilfsmethode `OrNullIfSingleRun` ausgelagert (Cyclomatic Complexity von `Compute` lag mit allen 4 Ternären inline bei 13 > Limit 12 laut `AiNetLinter.mdc`).
- `tests/SqlToAi.Tests/Database/PerformanceMetricsCalculatorTests.cs` (neu) — 4 Tests: 1 Run → Min/Max null; 3 Runs → korrekte Avg/Min/Max; 2 Runs → alle Felder befüllt; leere Message-Liste → Nullwerte, Min/Max null.
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` — automatisch durch `AiNetLinterTests.RecreateBaseline` (Teil von `dotnet test`) neu geschrieben, kein manueller Eingriff.

`tests/SqlToAi.Tests/Database/PerformanceMeasurementServiceTests.cs` wurde **nicht** geändert: die Tests dort rufen ausschließlich die öffentliche API (`MeasurePerformanceAsync`, `ParseExecutionPlanXml`) auf, keine der umgebauten privaten Methoden — Signaturänderungen an `ExecuteMeasuredRunsAsync`/`ProcessCapturedOutput` betreffen diese Datei nicht.

## Commit

- **Code-Commit-Hash:** `5c40cac86027287509c41e2c769a288947e04d64`
- **Message:**
  ```
  feat(database): Min/Avg/Max-Metriken für PerformanceMetrics ergänzen [sql-performance]

  Messages werden jetzt pro Run erfasst statt über alle Runs summiert, sodass
  min_elapsed_ms/max_elapsed_ms/min_cpu_ms/max_cpu_ms zusätzlich zum bisherigen
  Avg berechnet werden können. Die reine Berechnungslogik wurde in die neue,
  separat testbare Klasse PerformanceMetricsCalculator ausgelagert.

  Refs: tasks/sql-performance/step-001
  ```
- **Branch:** worktree-agent-a5ac8fe732153ef61
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit (Hash steht nicht hier drin — Selbstbezug, siehe `git log`).

## Build-/Test-Output

```
dotnet build → grün (0 Warnings, 0 Errors)
dotnet test  → grün (482 Tests, 0 Fehler)
```

## Abweichungen vom Plan

- **Berechnungslogik als separate Klasse statt private Methode:** Der Plan sah vor, die Min/Max-Berechnung
  als private Methode `ComputeRunMetrics` innerhalb von `PerformanceMeasurementService` zu belassen und
  markierte selbst als Problem, dass `ProcessCapturedOutput` (private static) nur schwer direkt testbar ist
  ("via reflection oder Messages-basierter Hilfsklasse testen"). Der vorgefundene Stand (aus der
  abgebrochenen Vorgänger-Session) hat stattdessen die reine Berechnung in eine eigene Klasse
  `PerformanceMetricsCalculator` ausgelagert, die ohne Datenbank-Infrastruktur direkt unit-testbar ist.
  Ich habe diesen Ansatz übernommen und vervollständigt, weil er das im Plan selbst benannte
  Testbarkeitsproblem sauberer löst als die im Plan skizzierte Reflection-Alternative, und die
  Abnahme-Kriterien (Felder, Nullable-Verhalten, min/avg/max-Korrektheit, 0 Warnings/Errors) unverändert
  erfüllt.
- **Klasse ist `static` statt `internal sealed class` mit Instanzmethode:** Der vorgefundene Stand hatte
  `Compute` als Instanzmethode einer `internal sealed class` mit Feld-Injektion (`_calculator = new()`)
  in `PerformanceMeasurementService`. `dotnet test`/`dotnet build` schlug initial mit `CA1822` fehl
  (`Compute` greift nicht auf Instanzdaten zu, muss `static` sein — Warning-as-Error). Da die Klasse
  vollständig zustandslos ist, habe ich sie zu `internal static class` mit statischer `Compute`-Methode
  umgebaut und die Instanz-Plumbing (`_calculator`-Feld, Parameter-Durchreichung durch
  `ExecuteMeasurementAsync`/`ProcessCapturedOutput`) entfernt; Tests rufen entsprechend
  `PerformanceMetricsCalculator.Compute(...)` statt über eine Instanz auf.
- **Zusätzliche Extraktion `OrNullIfSingleRun`:** nicht im Plan vorgesehen, aber notwendig, um die
  Cyclomatic Complexity von `Compute` (lag bei 13, Limit 12 laut `AiNetLinter.mdc`) einzuhalten.
- **Entfernte, jetzt ungenutzte `CpuTimeRegex`/`IoReadsRegex`-Felder in `PerformanceMeasurementService.cs`:**
  Diese lagen im vorgefundenen (unfertigen) Stand noch redundant in der Service-Klasse, obwohl die
  Berechnung bereits in `PerformanceMetricsCalculator` verschoben war — als Aufräumarbeit am selben,
  ohnehin für diesen Step angefassten Umbau entfernt (keine Scope-Erweiterung, sondern Abschluss des
  begonnenen Refactorings in derselben Datei/Methode).

## Beobachtungen

- Keine über den Step-Scope hinausgehenden Beobachtungen. Die vom Linter gemeldeten Verstöße zu
  `ExecuteWarmupRunsAsync`/`ExecuteMeasuredRunsAsync` (`MaxMethodParameterCount`, 7 gewertete Parameter)
  sowie zu `ToolDispatcher` (`MaxConstructorDependencies`) und `GlobMatcherTests`
  (`AvoidExcessiveMiddleMen`) bestanden bereits vor diesem Step unverändert (gleiche Parameterzahl in
  `ExecuteWarmupRunsAsync`/`ExecuteMeasuredRunsAsync` schon im Ausgangszustand) und sind über die
  Baseline abgedeckt — kein neuer Tech-Debt-Kandidat aus diesem Step.

## Bekannte Unschärfen

- Die Schwelle `runCpu > 0 || runElapsed > 0` als Kriterium für "gültiger Run" (nur solche Runs fließen
  in Min/Max ein) übernimmt 1:1 die implizite Logik aus dem vorgefundenen Stand; sie ist nicht explizit
  im Step-Plan spezifiziert. Bei tatsächlich 0ms CPU/Elapsed-Zeit (extrem schnelle Query) würde dieser
  Run von Min/Max ausgeschlossen — in der Praxis unwahrscheinlich, aber der Kritiker sollte prüfen, ob
  das die gewünschte Semantik ist.
