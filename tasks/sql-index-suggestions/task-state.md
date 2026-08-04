---
status: blocked  # executing | done | aborted | blocked
task: sql-index-suggestions
started_at: 2026-08-04T11:02:33+02:00
last_updated: 2026-08-05T16:40:00+02:00
rules_dir: .agents/rules  # aus konzept.md Frontmatter uebernommen
total_fix_rounds: 1  # Summe aller Fix-Runden ueber alle Steps (Task-weiter Not-Anker, siehe Config)
current_step: step-006  # Reopen (Round 2): Post-Completion-Tech-Debt-Cleanup EPIC-04
---

# Task State: sql-index-suggestions

## Uebersicht

- **Task-Status:** `blocked` — step-006 (TD-004) wartet auf Nutzer-Entscheidung, siehe „Blocker" unten
- **Fix-Runden gesamt:** 1 (Not-Anker bei `max_total_fix_rounds`, siehe Config)
- **Aktueller Schritt:** `step-006` (TD-004 SQL-2019/2022-Syntax, EPIC-04, `blocked`)
- **Roadmap:** siehe `roadmap.md` fuer den Epic-Fortschritt (EPIC-01 + EPIC-02 + EPIC-03 done, EPIC-04 in Bearbeitung)
- **Tech-Debt:** siehe `tech-debt.md` fuer gesammelte, bewusst nicht gefixte Funde (neue Policy: nur offene Items, ab 2026-08-05; aktuell TD-004, TD-006 offen; TD-002 mit step-005-`approved` entfernt)
- **Gestartet:** 2026-08-04T11:02:33+02:00
- **Zuletzt aktualisiert:** 2026-08-05T16:10:00+02:00

## Steps

<Diese Tabelle waechst mit jedem Planer-Aufruf im Step-Modus um genau
eine Zeile.>

| Step | Epic | Status | Title | Fix-Runden | Coded | Reviewed | Commit |
|------|------|--------|-------|------------|-------|----------|--------|
| step-001 | EPIC-01 | done | Parser-Erweiterung — vollständige CREATE NONCLUSTERED INDEX-Statements | 0/3 | 86c0e48 | 2026-08-04 approved | 86c0e48 |
| step-002 | EPIC-02 | done | Service + Tool-Registrierung + Doku-Sync für sql_suggest_indexes | 1/3 | 3195a17 | 2026-08-04 issues → fix-01 approved | 50437e2 |
| step-002/fix-01 | EPIC-02 | done | CTE-Top-N pro index_handle (Fix für CRITICAL aus step-002) | 0/3 | bc488ec | 2026-08-04 approved | 1a412cb |
| step-003 | EPIC-02 | done | Integrationstest für sql_suggest_indexes gegen echte Test-DB | 0/3 | 2ac3668, 0348e9d | 2026-08-05 approved (Reopen) | 9a36678, 630f0ce |
| step-004 | EPIC-03 | done | Post-Completion Tech-Debt Cleanup — TD-001 fixen, Rest als out-of-scope markieren | 0/3 | 651c526 | 2026-08-05 approved | 7c92a3a |
| step-005 | EPIC-04 | done | TD-002 — `DESC`-Sortierung in `BuildCreateIndexStatement` | 0/3 | a1492c6 | 2026-08-05 approved | a1492c6 |
| step-006 | EPIC-04 | blocked | TD-004 — SQL-2019/2022-Syntax in `IndexSuggestionService` CTE | 0/3 | 2011331 | - | 2011331 |
| step-007 | EPIC-04 | open | TD-006 — Test 1 Graceful-Degradation-Toleranz | 0/3 | - | - | - |

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

## Blocker (aktuell offen)

**step-006 (TD-004):** Die vom Nutzer akzeptierte Annahme „SQL Server
2025 führt die alten DMV-Namen (`index_group_handle`,
`sys.dm_db_missing_index_columns` als View statt TVF) als
Rückwärtskompatibilitäts-Alias weiter" ist **empirisch widerlegt**. Der
Coder hat die 2019/2022-Syntax exakt nach Plan umgesetzt (Commit
`2011331`), aber gegen die reale Test-DB (SQL Server 2025 RTM
17.0.1000.7) schlagen alle vier Integrationstests mit
`SqlException: Ungültiger Spaltenname "index_group_handle"` fehl.
Details: `tasks/sql-index-suggestions/step-006/step-result.md`.

Offene Frage an den Nutzer — wie soll TD-004 weiterverfolgt werden?
1. **Revert** von Commit `2011331`, TD-004 bleibt offen/wird als
   `won't fix` markiert (Ziel SQL-Server-2019-Kompatibilität nicht
   erreichbar ohne Versionsweiche).
2. **Versionsabhängige Query-Konstruktion** (Try/Detect-Pattern:
   2019/2022-Syntax **und** 2025-Syntax, Laufzeit-Entscheidung) — neuer
   Fix-Step, deutlich größerer Scope als ursprünglich geplant.
3. **Andere Vorgabe** (z. B. Mindestversion doch bei 2025 belassen,
   TD-004 schließen ohne Codeänderung, Commit `2011331` so stehen
   lassen mit Test-Anpassung o.ä.).

Bis zur Klärung bleibt der Loop hier stehen (kein automatisches
Fortsetzen).

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
