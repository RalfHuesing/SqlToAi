---
status: executing  # executing | done | aborted
task: sql-performance
started_at: 2026-08-03T10:04:00Z
last_updated: 2026-08-03T10:04:00Z
rules_dir: .agents/rules
total_fix_rounds: 1
current_step: step-001
---

# Task State: sql-performance

## Übersicht

- **Task-Status:** `executing`
- **Fix-Runden gesamt:** 0 (Not-Anker bei `max_total_fix_rounds`, siehe Config)
- **Aktueller Schritt:** `step-001`
- **Roadmap:** siehe `roadmap.md` für den Epic-Fortschritt
- **Tech-Debt:** siehe `tech-debt.md` für gesammelte, bewusst nicht gefixte Funde
- **Gestartet:** 2026-08-03T10:04:00Z
- **Zuletzt aktualisiert:** 2026-08-03T10:04:00Z

## Steps

<Diese Tabelle wächst mit jedem Planer-Aufruf im Step-Modus um genau
eine Zeile.>

| Step | Epic | Status | Title | Fix-Runden | Coded | Reviewed | Commit |
|------|------|--------|-------|------------|-------|----------|--------|
| step-001 | EPIC-01 | done | PerformanceMetrics min/avg/max erweitern | 1/3 | ja | approved (nach fix-01) | 5c40cac8 / 4d8fe08 |

## Config (optional)

```
max_fix_rounds_per_step: 3
max_total_fix_rounds: 12
max_batch_items: 8
max_batch_diff_lines: 40
build_command: dotnet build
test_command: dotnet test
target_branch: (aktueller Branch)
model_planer: Claude Sonnet 5, reasoning effort high
model_coder: Claude Sonnet 5, reasoning effort medium
model_kritiker: Claude Sonnet 5, reasoning effort high
```

## Abbruch-Bedingungen

- **Fix-Budget eines Steps erreicht** (`max_fix_rounds_per_step`, Default 3, ohne `approved`): dieser eine Step → `blocked`, Loop pausiert, Nutzer klärt.
- **Task-weiter Not-Anker erreicht** (`max_total_fix_rounds`, Default 12, über alle Steps summiert): Task → `aborted`, siehe `task-summary.md`.
- **Blocker aufgetreten** (Step mit Status `blocked`): Loop pausiert, Nutzer klärt.
- **Tech-Debt-Einträge lösen NIE einen Abbruch oder Blocker aus** — sie sind reine Beobachtung, kein Steuerungssignal.
