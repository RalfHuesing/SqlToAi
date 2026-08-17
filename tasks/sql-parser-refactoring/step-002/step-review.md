---
status: done
type: step-review
task: sql-parser-refactoring
step: "002"
epic: EPIC-02
step_type: single
reviewed_by: kritiker
reviewed_by_model: gemini-3.7-flash
reviewed_by_model_knowledge_cutoff: "2026-01"
reviewed_at: "2026-08-17T16:33:00+02:00"
verdict: approved
tech_debt_ids: []
---

# Review Step 002: SqlMultiStatementDetector auf ScriptDom AST umstellen

## Verdict

- [x] **approved** — alle vier Prüfebenen ok

## Geprüft

- [x] Plan-Erfüllung: alle im `step-plan.md` genannten Änderungen erfolgt
- [x] Rules-Konformität: `.agents/rules/**` eingehalten
- [x] Logische Korrektheit: Code macht was er soll, nicht nur „grün"
- [x] Konzept-Treue: passt die Umsetzung zu `konzept.md` (Scope, Non-Goals, Muss-Haben)
- [x] Build: selbst nachgeprüft, grün
- [x] Tests: selbst nachgeprüft, grün

## Befund

### Plan-Erfüllung

Die String- und Semikolon-Heuristik wurde vollständig durch die AST-Statement-Erkennung ersetzt und die Testsuite um alle geforderten Preamble-Fälle erweitert.

### Rules-Konformität

Regeln eingehalten; Out-Parameter im Parser-Helper wurden bereinigt und keine Linter-Warnungen vorhanden.

### Logische Korrektheit

Die Zählung von Nicht-Preamble-Statements über alle Batches (`TSqlScript.Batches[].Statements`) deckt sowohl Single-Batch- als auch Multi-Batch-Szenarien (`GO`) korrekt ab.

### Konzept-Treue (Ebene 4)

Entspricht exakt der Anforderung aus `konzept.md` zur Beseitigung von False-Positives/Negatives bei Multi-Statement- und Preamble-Erkennung.

### Build-/Test-Status

```
dotnet build → grün
dotnet test  → grün (545 Tests, 0 Fehler)
```
