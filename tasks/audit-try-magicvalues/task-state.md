---
status: executing
task: audit-try-magicvalues
started_at: 2026-08-15T21:34:00+02:00
last_updated: 2026-08-15T21:34:00+02:00
rules_dir: .agents/rules
total_steps: 0
current_step: step-001
---

# Task State: audit-try-magicvalues

## Übersicht

- **Task-Status:** `executing`
- **Steps gesamt:** 0 (regulär + Korrekturen — weicher Check-in bei
  jedem Vielfachen von `soft_step_checkin_interval`, siehe Config)
- **Aktueller Schritt:** `step-001`
- **Roadmap:** siehe `roadmap.md` für den Epic-Fortschritt
- **Tech-Debt:** siehe `tech-debt.md` für gesammelte, bewusst nicht gefixte Funde
- **Gestartet:** 2026-08-15T21:34:00+02:00
- **Zuletzt aktualisiert:** 2026-08-15T21:34:00+02:00

## Steps

| Step | Epic | Status | Title | Corrects | Coded | Reviewed | Commit |
|------|------|--------|-------|----------|-------|----------|--------|
| ... | ... | ... | ... | ... | ... | ... | ... |

## Config

```
max_fix_rounds_per_step: 3        # Kettenlänge über `corrects`, siehe ../spec.md §10.5
soft_step_checkin_interval: 40    # weicher Deckel, kein Hard-Abort — siehe ../spec.md §10.5
max_batch_items: 8                # siehe ../spec.md §10.6 (Micro-Batches innerhalb eines Epics)
max_batch_diff_lines: 60          # großzügig, da Audit-Refactorings viele Dateien berühren
build_command: dotnet build
test_command: dotnet test
target_branch: main
model_planer: <nicht festgelegt>
model_coder: <nicht festgelegt>
model_kritiker: <nicht festgelegt>
```

## Abbruch-/Pause-Bedingungen

- **Kettenbudget erreicht** (`max_fix_rounds_per_step`, Default 3): Loop pausiert.
- **Weicher Deckel erreicht** (`soft_step_checkin_interval`, Default 40): Zwischenfrage.
- **Blocker aufgetreten** (Step mit Status `blocked`): Loop pausiert.
- **Tech-Debt-Einträge lösen NIE einen Abbruch aus.**
