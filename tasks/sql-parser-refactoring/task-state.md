---
status: done
task: sql-parser-refactoring
started_at: "2026-08-17T16:26:00+02:00"
last_updated: "2026-08-17T16:45:00+02:00"
rules_dir: .agents/rules
total_steps: 5
current_step: step-005
---

# Task State: sql-parser-refactoring

## Übersicht

- **Task-Status:** `done`
- **Steps gesamt:** 5 (regulär + Korrekturen — weicher Check-in bei jedem Vielfachen von `soft_step_checkin_interval`, siehe Config)
- **Aktueller Schritt:** `step-005`
- **Roadmap:** siehe `roadmap.md` für den Epic-Fortschritt
- **Tech-Debt:** siehe `tech-debt.md` für gesammelte, bewusst nicht gefixte Funde
- **Gestartet:** 2026-08-17T16:26:00+02:00
- **Zuletzt aktualisiert:** 2026-08-17T16:45:00+02:00

## Steps

| Step | Epic | Status | Title | Corrects | Coded | Reviewed | Commit |
|------|------|--------|-------|----------|-------|----------|--------|
| step-001 | EPIC-01 | done | ScriptDom NuGet-Paket einbinden und SqlScriptDomParser-Helper erstellen | - | 702d139 | approved | 702d139 |
| step-002 | EPIC-02 | done | SqlMultiStatementDetector auf ScriptDom AST umstellen | - | 19da170 | approved | 19da170 |
| step-003 | EPIC-03 | done | ReadOnlyGuard auf ScriptDom AST-Visitor umstellen | - | a6d5280 | approved | a6d5280 |
| step-004 | EPIC-04 | done | QueryDeconstructor auf ScriptDom AST-Navigation umstellen | - | e1f158c | approved | e1f158c |
| step-005 | EPIC-05 | done | Dokumentation synchronisieren und Gesamtabnahme | - | 6093ed1 | approved | 6093ed1 |

## Config (optional)

```
max_fix_rounds_per_step: 3
soft_step_checkin_interval: 40
max_batch_items: 8
max_batch_diff_lines: 40
build_command: dotnet build
test_command: dotnet test
target_branch: main
model_planer:
model_coder:
model_kritiker:
```

## Abbruch-/Pause-Bedingungen

- **Kettenbudget erreicht** (`max_fix_rounds_per_step`, Default 3, über die `corrects`-Kette gezählt, ohne `approved`): der zuletzt korrigierte Step → `blocked`, Loop pausiert für diese Kette, Nutzer klärt. **Kein** Task-Abbruch dadurch.
- **Weicher Deckel erreicht** (`soft_step_checkin_interval`, Default 40, bei jedem Vielfachen der Gesamt-Step-Zahl): Zwischenfrage an den Nutzer, kein automatischer Abbruch. Nur eine ausdrückliche Ablehnung → Task `aborted`, siehe `task-summary.md`.
- **Blocker aufgetreten** (Step mit Status `blocked`): Loop pausiert, Nutzer klärt.
- **Tech-Debt-Einträge lösen NIE einen Abbruch oder Blocker aus** — sie sind reine Beobachtung, kein Steuerungssignal (siehe `../spec.md` §9). Auch `auto_fixable: ja`-Einträge lösen nichts eigenständig aus, sie werden nur an ohnehin laufende Steps angehängt.
