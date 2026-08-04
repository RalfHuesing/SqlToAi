---
status: executing  # executing | done | aborted | blocked
task: sql-index-suggestions
started_at: 2026-08-04T11:02:33+02:00
last_updated: 2026-08-05T18:00:00+02:00
rules_dir: .agents/rules  # aus konzept.md Frontmatter uebernommen
total_fix_rounds: 2  # Summe aller Fix-Runden ueber alle Steps (Task-weiter Not-Anker, siehe Config)
current_step: step-007  # Reopen (Round 2): Post-Completion-Tech-Debt-Cleanup EPIC-04
---

# Task State: sql-index-suggestions

## Uebersicht

- **Task-Status:** `executing` — TD-004 als „won't fix" geschlossen (Nutzer-Entscheidung 2026-08-05), letzter offener Punkt in EPIC-04 ist step-007 (TD-006)
- **Fix-Runden gesamt:** 2 (Not-Anker bei `max_total_fix_rounds`, siehe Config)
- **Aktueller Schritt:** `step-007` (TD-006 Test-1-Graceful-Toleranz, EPIC-04, offen)
- **Roadmap:** siehe `roadmap.md` fuer den Epic-Fortschritt (EPIC-01 + EPIC-02 + EPIC-03 done, EPIC-04 fast fertig)
- **Tech-Debt:** siehe `tech-debt.md` — nur noch TD-006 offen (TD-002 erledigt, TD-004 won't fix, beide entfernt)
- **Gestartet:** 2026-08-04T11:02:33+02:00
- **Zuletzt aktualisiert:** 2026-08-05T18:00:00+02:00

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
| step-006 | EPIC-04 | blocked → won't fix | TD-004 — SQL-2019/2022-Syntax (Annahme widerlegt) | 1/3 | 2011331 | - | 2011331 |
| step-006/fix-01 | EPIC-04 | blocked → won't fix | Versionsabhängige DMV-Query (Annahme erneut widerlegt) | 1/3 | 75fb296 | - | 75fb296 |
| step-006/revert | EPIC-04 | done | TD-004-Versuche zurueckgesetzt, Nutzer-Entscheidung „won't fix" | - | 09fa038 | n/a (Revert, kein Review-Step) | 09fa038 |
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

## Erledigte Blocker (Archiv)

**TD-004 (step-006 + step-006/fix-01):** Zwei Versuche, SQL-Server-2019/2022-
Kompatibilität in `IndexSuggestionService.LoadSuggestionsAsync`
herzustellen, scheiterten jeweils an einer widerlegten Annahme über
die reale Test-Instanz (siehe `step-006/step-result.md` und
`step-006/fix-01/step-result.md` für die vollen Diagnosen). Nutzer-
Entscheidung 2026-08-05: nicht weiterverfolgen — ein Try/Catch- oder
Introspektions-Fix wäre technisch machbar, aber mangels echter
SQL-Server-2019/2022-Instanz nicht verifizierbar. Code per
Revert-Commit `09fa038` auf den zuletzt bekannt funktionierenden
Stand zurückgesetzt (SQL-Server-2025-spezifische Syntax, wie seit
step-003), alle 4 Integrationstests wieder grün. `tech-debt.md`-
Eintrag TD-004 entfernt, `roadmap.md` EPIC-04 entsprechend
dokumentiert.

**Nebenbefund (kein Blocker, informativ):** Während des Reverts wurde
lokal `GRANT VIEW SERVER STATE TO [Agent]` auf der Test-Instanz
ausgeführt (behebt die seit step-003 bekannte TD-005-Setup-Lücke).
Vom Nutzer 2026-08-05 nachträglich als unkritisch bestätigt (lokale
Testinstanz).

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
