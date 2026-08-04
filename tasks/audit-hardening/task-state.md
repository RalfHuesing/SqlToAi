---
status: executing  # executing | done | aborted
task: audit-hardening
started_at: 2026-08-04T00:00:00+02:00
last_updated: 2026-08-04T00:00:00+02:00
rules_dir: .agents/rules
total_fix_rounds: 1  # Summe aller Fix-Runden über alle Steps (Task-weiter Not-Anker, siehe Config)
current_step: step-001
---

# Task State: audit-hardening

## Übersicht

- **Task-Status:** `executing`
- **Fix-Runden gesamt:** 1 (Not-Anker bei `max_total_fix_rounds`, siehe Config)
- **Aktueller Schritt:** `step-001`
- **Roadmap:** siehe `roadmap.md` für den Epic-Fortschritt
- **Tech-Debt:** siehe `tech-debt.md` für gesammelte, bewusst nicht gefixte Funde
- **Gestartet:** 2026-08-04T00:00:00+02:00
- **Zuletzt aktualisiert:** 2026-08-04T00:00:00+02:00

## Steps

<Diese Tabelle wächst mit jedem Planer-Aufruf im Step-Modus um genau
eine Zeile.>

| Step | Epic | Status | Title | Fix-Runden | Coded | Reviewed | Commit |
|------|------|--------|-------|------------|-------|----------|--------|
| step-001 | EPIC-01 | done | CommandTimeout-Konfigurierbarkeit & Umbenennung | 0/3 | 32d1aab | approved | 32d1aab |
| step-002 | EPIC-02 | done | Serverseitiges Row-Limit via SET ROWCOUNT | 0/3 | 27d7259 | approved | 27d7259 |
| step-003 | EPIC-03 | done | MCP-Trail-Redaction via Anonymizer-Reuse | 1/3 | d64241d | approved | d64241d |
| step-004 | EPIC-04 | done | QueryValidationService: korrekte Command-Timeout-Option (TD-001) | 0/3 | 7becaf3 | approved | 7becaf3 |
| step-005 | EPIC-05 | done | Anonymizer ExcludedColumns-Doku-Korrektur (TD-002) | 0/3 | 6c83cc6 | approved | 6c83cc6 |
| step-006 | EPIC-06 | done | McpTrailWriter Content-Block-Kontext praezisieren (TD-003) | 0/3 | e21a934 | approved | e21a934 |

## Config (optional)

Falls `<task-dir>/config.md` existiert, hier die Overrides dokumentieren.
Andernfalls gelten die Defaults aus `../spec.md`.

```
max_fix_rounds_per_step: 3
max_total_fix_rounds: 12
max_batch_items: 8          # siehe ../spec.md §10.6 (Micro-Batches innerhalb eines Epics)
max_batch_diff_lines: 40    # siehe ../spec.md §10.6
build_command: <aus roadmap.md Tech-Stack-Notiz>
test_command: <aus roadmap.md Tech-Stack-Notiz>
target_branch: main
model_planer: Sonnet 5, Stufe High
model_coder: Sonnet 5, Stufe Medium
model_kritiker: Sonnet 5, Stufe Medium
```

## Abbruch-Bedingungen

- **Fix-Budget eines Steps erreicht** (`max_fix_rounds_per_step`, Default
  3, ohne `approved`): dieser eine Step → `blocked`, Loop pausiert,
  Nutzer klärt.
- **Task-weiter Not-Anker erreicht** (`max_total_fix_rounds`, Default 12,
  über alle Steps summiert): Task → `aborted`, siehe `task-summary.md`.
- **Blocker aufgetreten** (Step mit Status `blocked`): Loop pausiert,
  Nutzer klärt.
- **Tech-Debt-Einträge lösen NIE einen Abbruch oder Blocker aus** — sie
  sind reine Beobachtung, kein Steuerungssignal (siehe `../spec.md` §9).
