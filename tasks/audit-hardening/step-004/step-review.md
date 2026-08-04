---
status: done
type: step-review
task: audit-hardening
step: "004"
epic: EPIC-04
step_type: single
reviewed_by: kritiker
reviewed_by_model: Claude Sonnet 5
reviewed_by_model_knowledge_cutoff: 2026-01
reviewed_at: 2026-08-04T21:45:00+02:00
verdict: approved
tech_debt_ids: []
---

# Review Step 004: QueryValidationService: Command-Timeout statt ConnectTimeoutSeconds verwenden

## Verdict

- [x] **approved** — alle vier Prüfebenen ok
- [ ] **issues**
- [ ] **blocked**

## Geprüft

- [x] Plan-Erfüllung: alle im `step-plan.md` genannten Änderungen erfolgt
- [x] Rules-Konformität: `<rules_dir>/**` eingehalten
- [x] Logische Korrektheit: Code macht was er soll, nicht nur „grün"
- [x] Konzept-Treue: passt die Umsetzung zu `konzept.md` (Scope, Non-Goals, Muss-Haben)
- [x] Build: selbst nachgeprüft, grün
- [x] Tests: selbst nachgeprüft, grün (oder Baselines beachtet)

## Befund

### Plan-Erfüllung

Alle drei geplanten Änderungen (Feld/Konstruktor-Umstellung auf `QueryExecutionOptions`,
drei `CommandTimeout`-Zeilen in `ExecuteParseonlyValidationAsync`, XML-Doku-Erweiterung,
neuer Unit-Test) 1:1 per `git show 7becaf3` verifiziert, keine Abweichung.

### Rules-Konformität

Eingehalten: keine neue `appsettings.json`-Property (Wiederverwendung von
`QueryExecution.CommandTimeoutSeconds`), Konstruktor-Parameterzahl unverändert (weiterhin 6,
`IOptions<SqlToAiOptions> options` bleibt der einzige Options-Parameter).

### Logische Korrektheit

Alle drei `CommandTimeout`-Zuweisungen (Zeilen 143/151/160) nutzen jetzt
`_queryExecutionOptions.CommandTimeoutSeconds` statt `_dbOptions.ConnectTimeoutSeconds`. Der
neue Test setzt bewusst unterschiedliche Werte (`ConnectTimeoutSeconds = 99`,
`CommandTimeoutSeconds = 42`) und prüft über `ValidationMockConnectionFactory.ObservedCommandTimeouts`,
dass alle drei ausgeführten Commands `42` sehen — das unterscheidet die beiden Options-Quellen
eindeutig und wäre bei einer Regression auf die alte Quelle rot. `SecondaryConnectionBuilder.cs`
wurde (per `git show 7becaf3 --stat`) nicht angefasst, wie im Plan vorgesehen.

### Konzept-Treue (Ebene 4)

Kein direkter `konzept.md`-Bezug (Step stammt aus TD-001), aber auch kein Widerspruch: Die
Wiederverwendung von `QueryExecutionOptions.CommandTimeoutSeconds` statt einer neuen Option folgt
demselben Prinzip wie Muss-Haben 1 (eine Command-Timeout-Option pro Verwendungszweck, keine
unnötige Options-Vermehrung).

### Build-/Test-Status

```
dotnet build → grün (0 Warnungen, 0 Fehler)
dotnet test  → grün (500 Tests, 0 Fehler)
```

## Tech-Debt-Update

TD-001 ist durch diesen Step vollständig gelöst — in `tech-debt.md` auf `erledigt` gesetzt
(Index-Zeile durchgestrichen + Volltext-Status aktualisiert), mit Verweis auf `step-004`/Commit
`7becaf3`.
