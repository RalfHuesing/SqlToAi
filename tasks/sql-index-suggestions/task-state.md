---
status: executing  # executing | done | aborted
task: sql-index-suggestions
started_at: 2026-08-04T11:02:33+02:00
last_updated: 2026-08-04T11:02:33+02:00
rules_dir: .agents/rules  # aus konzept.md Frontmatter uebernommen
total_fix_rounds: 0  # Summe aller Fix-Runden ueber alle Steps (Task-weiter Not-Anker, siehe Config)
current_step: step-001
---

# Task State: sql-index-suggestions

## Uebersicht

- **Task-Status:** `executing`
- **Fix-Runden gesamt:** 0 (Not-Anker bei `max_total_fix_rounds`, siehe Config)
- **Aktueller Schritt:** `step-001`
- **Roadmap:** siehe `roadmap.md` fuer den Epic-Fortschritt
- **Tech-Debt:** siehe `tech-debt.md` fuer gesammelte, bewusst nicht gefixte Funde
- **Gestartet:** 2026-08-04T11:02:33+02:00
- **Zuletzt aktualisiert:** 2026-08-04T11:02:33+02:00

## Steps

<Diese Tabelle waechst mit jedem Planer-Aufruf im Step-Modus um genau
eine Zeile.>

| Step | Epic | Status | Title | Fix-Runden | Coded | Reviewed | Commit |
|------|------|--------|-------|------------|-------|----------|--------|
| step-001 | EPIC-01 | open | Parser-Erweiterung — vollständige CREATE NONCLUSTERED INDEX-Statements | 0/3 | - | - | - |

## Config (optional)

```
max_fix_rounds_per_step: 3
max_total_fix_rounds: 12
max_batch_items: 8          # siehe ../spec.md S10.6 (Micro-Batches innerhalb eines Epics)
max_batch_diff_lines: 40    # siehe ../spec.md S10.6
build_command: dotnet build
test_command: dotnet test
target_branch: main
model_planer: <nicht festgelegt>
model_coder: <nicht festgelegt>
model_kritiker: <nicht festgelegt>
```

<Die drei `model_*`-Felder sind optional und halten eine vom Nutzer
genannte, rollenabhaengige Modellwahl fest. Nicht gesetzt = keine Vorgabe,
der Orchestrator fragt auch nicht nach. Siehe `../spec.md` S10.8.>

## Abbruch-Bedingungen

- **Fix-Budget eines Steps erreicht** (`max_fix_rounds_per_step`, Default
  3, ohne `approved`): dieser eine Step → `blocked`, Loop pausiert,
  Nutzer klaert.
- **Task-weiter Not-Anker erreicht** (`max_total_fix_rounds`, Default 12,
  ueber alle Steps summiert): Task → `aborted`, siehe `task-summary.md`.
- **Blocker aufgetreten** (Step mit Status `blocked`): Loop pausiert,
  Nutzer klaert.
- **Tech-Debt-Eintraege loesen NIE einen Abbruch oder Blocker aus** — sie
  sind reine Beobachtung, kein Steuerungssignal (siehe `../spec.md` S9).
