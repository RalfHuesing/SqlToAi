---
status: done
type: step-result
task: sql-performance
step: 001/fix-01
epic: EPIC-01
step_type: single
coded_by: coder
coded_by_model: Claude Sonnet 5
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-03T00:00:00+02:00
code_commit_hash: 4d8fe08
status_after: done
blocker_category: n/a
---

# Result Step 001/fix-01: Gültigkeits-Erkennung für Min/Max strukturell statt über Wert-Schwellenwert bestimmen

## Zusammenfassung

`ParseRunMessages` gibt jetzt zusätzlich `bool HasMatch` zurück (gesetzt,
sobald `CpuTimeRegex` für eine Message des Runs matcht). `Compute` gated
die Min/Max-Akkumulation auf `runHasMatch` statt auf `runCpu > 0 ||
runElapsed > 0`. Der XML-Doc-Kommentar von `OrNullIfSingleRun` wurde
entsprechend angepasst. Zwei neue Tests decken gemischte 0/Nicht-0-Runs
und durchgängig-0-Runs ab.

## Geänderte Dateien

- `src/SqlToAi/Database/PerformanceMetricsCalculator.cs` — `ParseRunMessages`-Rückgabe um `HasMatch` erweitert, `Compute`-Gating auf `runHasMatch` umgestellt, Doc-Kommentar korrigiert.
- `tests/SqlToAi.Tests/Database/PerformanceMetricsCalculatorTests.cs` — Tests `Compute_MixedZeroAndNonZeroRuns_MinIsZeroNotNull` und `Compute_AllRunsZero_MinMaxAreZeroNotNull` neu.
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` — automatisch durch `AiNetLinterTests.RecreateBaseline` im Test-Lauf neu geschrieben (kein manueller Eingriff).

## Commit

- **Code-Commit-Hash:** `4d8fe08`
- **Message:**
  ```
  fix(performance): Min/Max-Gueltigkeit ueber Regex-Match statt Wert [sql-performance]

  Die Gueltigkeitspruefung fuer Min/Max-CPU/Elapsed in
  PerformanceMetricsCalculator.Compute basierte bisher auf einem
  Wert-Schwellenwert (runCpu > 0 || runElapsed > 0), wodurch echte
  0-ms-Messungen faelschlich wie fehlende STATISTICS-Matches behandelt
  wurden. ParseRunMessages gibt nun zusaetzlich HasMatch zurueck, das
  strukturell erfasst, ob die CPU-Time-Regex fuer den Run gematcht hat;
  Compute gated darauf statt auf den Wert. Zwei neue Tests decken
  gemischte 0/Nicht-0-Runs sowie durchgaengig 0-Runs ab.

  Refs: tasks/sql-performance/step-001/fix-01
  ```
- **Branch:** main
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit (Hash steht nicht hier drin — Selbstbezug, siehe `git log`).

## Build-/Test-Output

```
dotnet build → grün
dotnet test --filter "Category!=Integration" → grün (450 Tests, 0 Fehler)
```

## Abweichungen vom Plan

Keine — Plan 1:1 umgesetzt.

## Beobachtungen

Keine neuen über die im Plan bereits dokumentierten Scope-Grenzen hinaus.

## Bekannte Unschärfen

Keine.
