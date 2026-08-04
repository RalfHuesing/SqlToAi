---
task: sql-index-suggestions
completed_at: 2026-08-05T18:30:00+02:00
final_status: done  # done | aborted
total_iterations: 2
total_commits: 16
total_epics: 4
total_tech_debt_entries: 7
---

# Task Summary: sql-index-suggestions

## Ergebnis

Der MCP-Server bietet jetzt zwei komplementäre Index-Empfehlungs-Mechanismen: (1)
`sql_measure_performance` liefert pro Missing-Index-Warning ein fertiges
`CREATE NONCLUSTERED INDEX`-DDL (Equality/Inequality/Include-Spalten) als
zusätzliches Feld `missing_index_statement`; (2) das neue Tool
`sql_suggest_indexes` liefert serverweit kumulierte Empfehlungen aus den
SQL-Server-DMVs (`sys.dm_db_missing_index_*`), priorisiert nach
`improvement_score`, mit Pflicht-Restart-Hinweis und Graceful Degradation bei
fehlender `VIEW SERVER STATE`-Berechtigung. Beide DoD-Pakete sind vollständig
adressiert, die Doku (`architecture-spec.md` §4 Nr. 14+16, §H, `README.md`) ist
synchron, Build grün, 522/522 Tests grün (inkl. 4/4 Integration-Tests gegen die
reale SQL-Server-2025-Test-Instanz). Die im Reopen entdeckte
SQL-Server-2025-Inkompatibilität (CTE-Alias-Bug, `group_handle`-Spalten-Rename,
`sys.dm_db_missing_index_columns` als TVF via `CROSS APPLY`) wurde an der
Wurzel behoben. Nach diesem globalen Kritiker-Abschluss-Check ordnete der
Nutzer zwei Post-Completion-Runden an (EPIC-03, EPIC-04), die die in
`tech-debt.md` gesammelten Beobachtungen systematisch adressierten: TD-001
(Doku-Harmonisierung) und TD-003 (Generalisierung) wurden bereits vorher
erledigt; TD-002 (`DESC`-Sortierung) und TD-006 (Test-Toleranz) wurden in
EPIC-04 umgesetzt; TD-004 (SQL-2019/2022-Kompatibilität) wurde nach zwei
technisch fundierten, aber an der realen Test-Instanz gescheiterten
Implementierungsversuchen vom Nutzer bewusst als „won't fix" geschlossen;
TD-005 und TD-007 wurden als „grundsätzlich nicht" (Test-Infrastruktur-/
Architektur-Fragen außerhalb des Konzept-Scopes) geschlossen. `tech-debt.md`
ist damit vollständig leer — kein offener Eintrag verbleibt.

## Roadmap-Status

Alle vier Epics aus `roadmap.md` abgehakt (EPIC-01 bis EPIC-04):

- **EPIC-01** (Parser-Erweiterung in `sql_measure_performance`) — abgehakt in
  `step-001`, Code-Commit `86c0e48`, Result-Doku `4e4f6a2`, Review-Commit
  `4807042`, Verdict `approved`.
- **EPIC-02** (`sql_suggest_indexes` Service + Tool + Doku + Integrationstest) —
  abgehakt über drei Schritte: `step-002` (Code `3195a17` + Doku `50437e2`,
  initial `issues` wegen CTE-Bug auf verjointen Zeilen) → `step-002/fix-01`
  (CTE-Top-N pro `index_handle`, Code `bc488ec` + Doku `1a412cb`, Verdict
  `approved`) → `step-003` (Integrationstest gegen echte Test-DB, initial
  `blocked` weil Reopen-Lauf weitere SQL-Server-2025-Inkompatibilitäten
  aufgedeckt hat, Reopen-Code `0348e9d` + Doku `630f0ce`, final Verdict
  `approved`). EPIC-02 ist mit 522/522 grünen Tests abgeschlossen.
- **EPIC-03** (Post-Completion Tech-Debt Cleanup) — abgehakt in `step-004`
  (Commit `651c526` Code/Doku + `7c92a3a` Result, Verdict `approved`):
  TD-001 (Konzept-Index-Format-Harmonisierung) erledigt, TD-002/004/005/006/007
  vorläufig als out-of-scope markiert.
- **EPIC-04** (Post-Completion Tech-Debt Cleanup Round 2) — abgehakt über
  drei Steps: `step-005` (TD-002, Commit `a1492c6`, `approved`), `step-006` +
  `step-006/fix-01` (TD-004, zwei gescheiterte Versuche, Commits `2011331` /
  `75fb296`, dann Revert `09fa038` — Nutzer-Entscheidung „won't fix"),
  `step-007` (TD-006, Commit `0a71e9b`, `approved`). `tech-debt.md` ist
  danach vollständig leer.

Details zu Epic-Begründungen, Commit-Verweisen und beobachtetem Restbedarf siehe
`roadmap.md` Zeilen 66–228.

## Steps-Übersicht

| Step | Epic | Status | Title | Commit | Notiz |
|------|------|--------|-------|--------|-------|
| step-001 | EPIC-01 | done | Parser-Erweiterung — vollständige CREATE NONCLUSTERED INDEX-Statements | `86c0e48` (Code) + `4e4f6a2` (Result) | approved, 3 neue Tests + 12 bestehende grün |
| step-002 | EPIC-02 | done | Service + Tool-Registrierung + Doku-Sync für `sql_suggest_indexes` | `3195a17` (Code) + `50437e2` (Doku) | initial issues → fix-01; final approved |
| step-002/fix-01 | EPIC-02 | done | CTE-Top-N pro `index_handle` (Fix für CRITICAL aus step-002) | `bc488ec` (Code) + `1a412cb` (Result) | approved, 1 neuer Test (3 Handles à 2/5/3 Spalten) |
| step-003 | EPIC-02 | done | Integrationstest für `sql_suggest_indexes` gegen echte Test-DB | `2ac3668` (blocked-Lauf) + `0348e9d` (Reopen-Code) + `9a36678` (blocked-Result) + `630f0ce` (Reopen-Result) | blocked → Reopen → approved, 522/522 Tests grün |
| step-004 | EPIC-03 | done | Post-Completion Tech-Debt Cleanup — TD-001 fixen, Rest markieren | `651c526` (Code/Doku) + `7c92a3a` (Result) | approved, reine Doku-Änderung |
| step-005 | EPIC-04 | done | TD-002 — `DESC`-Sortierung in `BuildCreateIndexStatement` | `a1492c6` | approved, 4 neue Tests |
| step-006 | EPIC-04 | blocked → won't fix | TD-004 — feste SQL-2019/2022-Syntax (Annahme widerlegt) | `2011331` | kein Review; Annahme „SQL 2025 behält alte Spaltennamen als Alias" widerlegt |
| step-006/fix-01 | EPIC-04 | blocked → won't fix | TD-004 — versionsabhängige Query (Annahme erneut widerlegt) | `75fb296` | kein Review; reale Instanz meldet Hauptversion 16, nutzt aber 2025-Schema |
| step-006/revert | EPIC-04 | done | TD-004-Versuche zurückgesetzt, Nutzer-Entscheidung „won't fix" | `09fa038` | kein Review-Step (Revert), 4/4 Integrationstests wieder grün |
| step-007 | EPIC-04 | done | TD-006 — Test 1 Graceful-Degradation-Toleranz | `0a71e9b` | approved, 526/526 Tests grün |

**Total-Fix-Runden verbraucht: 2/12** — `step-002/fix-01` und `step-006/fix-01`
wurden als Fix-Steps gezählt (`task-state.md` `total_fix_rounds: 2`). Der
Reopen in `step-003` war keine neue Fix-Runde, sondern eine Reaktivierung des
bestehenden Steps mit vom Nutzer explizit aufgehobener Scope-Sperre. TD-004
(`step-006`) wurde nach Ausschöpfen einer Fix-Runde nicht ein drittes Mal
versucht, sondern vom Nutzer bewusst als „won't fix" geschlossen und per
Revert auf den zuletzt bekannt funktionierenden Stand zurückgesetzt — kein
Fix-Budget-Abbruch, sondern eine reguläre Nutzer-Entscheidung.

## Globale Audit-Befunde (Kritiker, Modus `global`)

### Konzept erfüllt?

Ja — beide Muss-Haven-Punkte aus `konzept.md` §Muss-Haven sind umgesetzt, alle
Non-Goals sind eingehalten, alle DoD-Punkte (inkl. Integrationstest) sind
adressiert.

- **Idee 1 (Parser-Erweiterung mit `CREATE NONCLUSTERED INDEX`-DDL):** vollständig
  umgesetzt in `step-001`. Konzept-Beispiel in `konzept.md` Zeile 161–172
  (XML-Plan → DDL) wird durch Test 2 in
  `PerformanceMeasurementServiceTests.cs` reproduziert. Kleine
  Konzept-Plan-Implementierung-Inkonsistenz beim Index-Name-Format
  (`IX_Orders_CustomerId_OrderDate` vs. `IX_Orders_CustomerId__OrderDate`) ist
  als TD-001 dokumentiert; beide Formen sind valide SQL-Identifier, die
  Prose-Lesart (`__` als Spalten-Trenner) ist explizit Planer-/Coder-Spec.
- **Idee 2 (Tool `sql_suggest_indexes` mit DMV-Abfragen und Graceful
  Degradation):** vollständig umgesetzt in `step-002` + `fix-01` + `step-003`.
  Tool existiert mit den vier Parametern (`database` Pflicht, `table_name` /
  `min_score` / `top` optional, `top` Default 10), `improvement_score` wird
  nach DMV-Spaltennamen `avg_total_user_cost × avg_user_impact ×
  (user_seeks + user_scans)` berechnet, Markdown-Output mit acht Spalten
  Score/Table/Equality/Inequality/Include/Seeks/Scans/LastSeek wird gerendert,
  Restart-Hinweis ist fester Bestandteil der Ausgabe, Graceful Degradation
  bei fehlender `VIEW SERVER STATE` analog zum `SHOWPLAN`-Pattern verifiziert
  (Unit-Test + Integrationstest gegen reale DB).
- **Non-Goals eingehalten:** kein DDL-Render in `sql_suggest_indexes` (nur
  Spalten-Listen als Markdown-Zellen), keine DTA-API, kein `DBCC AUTOPILOT`,
  keine Schreiboperationen, kein automatisches Index-Erstellen. Konzept §Wo
  im Projekt Zeile 124 (Vorbild `IsShowplanPermissionError`) ist in
  `IsPermissionError(SqlException, int, string)` generalisiert (TD-003
  erledigt).
- **DoD erfüllt:** alle Muss-Haven-Punkte beider Ideen, alle
  Doku-Sync-Punkte (`architecture-spec.md` §4 Nr. 14+16 + §H, `README.md`
  Feature-Bullet + Tool-Count 15→16 + Recommended-Permissions), Tests grün
  (12 Unit-Tests in `IndexSuggestionServiceTests` + 3 neue Parser-Tests in
  `PerformanceMeasurementServiceTests` + 4 Integration-Tests gegen reale
  Test-DB + 1 CTE-Regressionstest aus `fix-01`).

Kleine Doku-Konzept-Inkonsistenzen (Konzept-Formel `avg_user_cost` vs.
DMV-Spaltenname `avg_total_user_cost`, Konzept-Beispiel zeigt
Spalten-Namen `CustomerId`/`OrderDate` während der Code `column_id`-Werte
liefert) sind im Code die korrekte Interpretation der realen DMV-Semantik und
wurden bewusst nicht geharmonisiert — sie sind in `step-002/step-review.md`
und `step-003/step-review.md` dokumentiert. Kein Block, kein Scope-Drift.

### Seiteneffekte / Regressionen

Keine. Selbst am finalen globalen Abschluss-Check erneut über das
Gesamtprojekt nachgeprüft (nicht nur aus einem Step-Review übernommen):

```
dotnet build SqlToAi.slnx  → grün (0 Warnungen, 0 Fehler, TreatWarningsAsErrors=true)
dotnet test  SqlToAi.slnx  → grün (526 Tests, 0 Fehler, 0 übersprungen, ~4 s,
                                inkl. AiNetLinterTests.RecreateBaseline)
git status nach Testlauf   → clean (kein Baseline-Drift durch RecreateBaseline)
```

526 statt der zuvor gemeldeten 522 Tests spiegelt die vier neuen Testfälle
aus EPIC-04 wider (`step-005`: 4 neue DESC-Tests). TD-004 (`step-006`) und
TD-006 (`step-007`) haben netto keine Testanzahl verändert (Revert bzw. reine
Assertion-Erweiterung eines bestehenden Tests).

`AiNetLinterTests.RecreateBaseline` hat in jedem Step die `SqlToAi-baseline.json`
automatisch aktualisiert (kein manueller Eingriff, keine Hash-Rechnungen von
Hand) — bestätigt durch `step-002/fix-01/step-result.md` Beobachtung 1, den
eigenen `git status`-Check oben. Die fünf vorhandenen grandfathered
Violations (`PerformanceMeasurementService.cs`, `ToolDispatcher.cs`,
`GlobMatcherTests.cs`, `ToolDispatcherTests.cs` + ein weiterer) bleiben
unverändert, keine neuen Violations.

### Rules-Konformität (Stichproben)

Stichprobenartig gegengeprüft an drei Steps (`step-001`, `step-005`,
`step-007` — bewusst über beide Post-Completion-Runden verteilt, nicht nur
aus der ursprünglichen EPIC-01/02-Phase):

- **step-001** (`step-001/step-review.md`): AiNetLinter-Grenzwerte eingehalten
  — `BuildCreateIndexStatement` in `PerformanceMeasurementService.cs:373` liegt
  mit ~46 LOC deutlich unter dem 60-LOC-Limit, Parameteranzahl 4 exakt am
  Limit, `sealed` und `#nullable enable` der Datei unverändert. Conventional
  Commit, deutsch, imperativ, Suffix `[sql-index-suggestions]`. §4
  Doku-Sync-Pflicht eingehalten (architecture-spec + README).
- **step-005** (`step-005/step-review.md`): AiNetLinter-Grenzwerte eingehalten
  — `WithDescendingSuffix` (`PerformanceMeasurementService.cs:391-405`) 15 LOC,
  `ExtractMissingIndexWarnings` unverändert bei 46 LOC, Datei 477 LOC (Limit
  500), 3 Parameter (Limit 4). `TreatWarningsAsErrors` eingehalten, Baseline
  automatisch via `RecreateBaseline` aktualisiert. Conventional Commit,
  deutsch, imperativ, Suffix `[sql-index-suggestions]`.
- **step-007** (`step-007/step-review.md`): reine Test-Änderung, AiNetLinter
  LOC-Grenzwert für Tests (≤ 100) mit ~17 LOC deutlich unterschritten. Keine
  im Plan referenzierte Rule (nachvollziehbar begründet: reine
  Test-Assertion-Erweiterung ohne Produktionscode-Änderung). Baseline
  automatisch aktualisiert, Commit-Konventionen eingehalten.

**`TreatWarningsAsErrors`:** durchgehend eingehalten in allen geprüften
Steps. **`AiNetLinter-baseline.json`:** automatisch durch `RecreateBaseline`
aktualisiert, keine manuellen Hashes, kein Baseline-Drift beim eigenen
globalen Testlauf (`git status` clean danach). **Doku-Sync-Pflicht:** §4 in
`step-001`, `step-002` und `step-004` vollständig erfüllt; für rein interne
Änderungen ohne beobachtbares Verhalten (`fix-01`, `step-003`-Reopen,
`step-005`, `step-006`/Revert, `step-007`) im jeweiligen Step-Plan
nachvollziehbar entkräftet.

Keine Findings im globalen Modus.

## Tech-Debt-Zusammenfassung

`tech-debt.md` ist **leer** — bestätigt (Datei enthält nur den Hinweis-
Kommentar „aktuell keine offenen Einträge"). Alle sieben Einträge TD-001 bis
TD-007, die im Verlauf des Tasks entstanden sind, sind entweder erledigt oder
per expliziter Nutzer-Entscheidung geschlossen; Volltexte bleiben in der
Git-Historie von `tech-debt.md` erhalten (Status-Policy seit 2026-08-05).

Zusammenfassung aus der Git-Historie (Roadmap `EPIC-01`–`EPIC-04` und
`step-004`/`step-005`/`step-006`/`step-007`):

- **TD-001** (Konzept-Index-Name-Format `IX_Orders_CustomerId_OrderDate` vs.
  Code-Form `IX_Orders_CustomerId__OrderDate`) — **erledigt** in `step-004`:
  Konzept-Beispiel an die implementierte Form angepasst.
- **TD-002** (`DESC`-Sortierung in `ColumnGroup` wurde in
  `BuildCreateIndexStatement` ignoriert) — **erledigt** in `step-005`
  (Commit `a1492c6`): neuer Helper `WithDescendingSuffix`, 4 neue Tests.
- **TD-003** (`IsShowplanPermissionError` war `SHOWPLAN`-spezifisch) —
  **erledigt** in `step-002`: zu `IsPermissionError(SqlException, int,
  string)` generalisiert, für `VIEW SERVER STATE` wiederverwendet.
- **TD-004** (SQL-Server-Mindestversion — `IndexSuggestionService`-CTE
  funktioniert nur ab SQL Server 2025, nicht abwärtskompatibel zu 2019/2022)
  — **„won't fix", bewusste Nutzer-Entscheidung** nach zwei gescheiterten
  Implementierungsversuchen (`step-006`, `step-006/fix-01`): beide
  Versuche scheiterten an einer jeweils widerlegten Annahme über das
  DMV-Schema der realen Test-Instanz (siehe unten, „Besondere Würdigung
  TD-004"). Code per Revert-Commit `09fa038` auf den 2025-spezifischen,
  bekannt funktionierenden Stand zurückgesetzt.
- **TD-005** (Test-Environment-Setup `GRANT VIEW SERVER STATE TO [Agent]`
  nicht reproduzierbar dokumentiert) — **„grundsätzlich nicht"**, Nutzer-
  Entscheidung 2026-08-05: Test-Infrastruktur-/CI-CD-Frage, kein
  Konzept-Gegenstand.
- **TD-006** (Test 1 in `IndexSuggestionServiceIntegrationTests` akzeptierte
  den Graceful-Degradation-Pfad nicht, Asymmetrie zu Test 4) — **erledigt**
  in `step-007` (Commit `0a71e9b`): dritte Bedingung analog Test 4 ergänzt.
- **TD-007** (`DmvMockConnectionFactory` deckt SQL-Syntaxfehler nicht ab,
  systemischer Test-Coverage-Gap) — **„grundsätzlich nicht"**, Nutzer-
  Entscheidung 2026-08-05: Test-Strategie-/Architektur-Frage, Konzept
  schweigt dazu.

**Besondere Würdigung TD-004:** Dies ist keine unvollständige Aufgabe,
sondern eine bewusste, sachlich begründete Nutzer-Entscheidung. Beide
Implementierungsversuche waren technisch sauber geplant (feste
2019/2022-Syntax in `step-006`; versionsabhängige Query-Auswahl über
`connection.ServerVersion` in `step-006/fix-01`), scheiterten aber jeweils
daran, dass die reale Test-Instanz sich zwar als SQL Server 2022
(Hauptversion 16) meldet, intern jedoch bereits das SQL-Server-2025-DMV-
Spaltenschema verwendet — wodurch jede versionsnummernbasierte Erkennung
strukturell versagt (Details: `step-006/step-result.md`,
`step-006/fix-01/step-result.md`). Ein Try/Catch-Fallback oder eine
Schema-Introspektion wäre technisch machbar, aber mangels einer echten
SQL-Server-2019/2022-Instanz nicht verifizierbar gewesen. Der Nutzer hat
diese Unsicherheit korrekt erkannt und die Weiterverfolgung bewusst
gestoppt, statt einen unverifizierten Fix zu committen. Der Code wurde
sauber auf den zuletzt bekannt funktionierenden Stand zurückgesetzt
(`09fa038`), alle 4 Integrationstests laufen wieder grün — kein
Restschaden, kein halbfertiger Zustand.

## Offene Punkte

Keine. Alle Muss-Haben-Punkte aus `konzept.md` sind umgesetzt, alle Epics
sind abgehakt, `tech-debt.md` ist leer.

## Empfehlungen

- **Falls ein Folge-Task für SQL-Server-2019/2022-Kompatibilität geplant
  wird:** eine echte SQL-Server-2019- oder -2022-Testinstanz (nicht nur eine
  Instanz, die sich als solche meldet) bereitstellen, bevor ein neuer
  TD-004-Versuch gestartet wird — das strukturelle Verifikationsproblem
  aus `step-006`/`step-006/fix-01` besteht sonst unverändert fort.
- **Vor Push auf Remote / vor Release:** lokal `dotnet build && dotnet test`
  laufen lassen, um den 526/526-Stand zu bestätigen — der Coder hat
  durchgehend lokal gearbeitet (`Push: nein (lokal)` in den step-results),
  die Remote-Historie ist nicht aktualisiert.

## Statistik

- **Anzahl Epics:** 4, davon abgehakt: 4 (EPIC-01, EPIC-02, EPIC-03, EPIC-04)
- **Anzahl Steps:** 10 (`step-001` bis `step-007`, inkl. `step-002/fix-01`,
  `step-006/fix-01`, `step-006/revert`)
- **Davon approved:** 8 (`step-001`, `step-002`+`fix-01`, `step-003`,
  `step-004`, `step-005`, `step-006/revert`, `step-007`)
- **Davon blocked → won't fix (Nutzer-Entscheidung, kein offener Blocker):**
  2 (`step-006`, `step-006/fix-01` — beide durch `step-006/revert`
  aufgelöst)
- **Anzahl Commits:** 16 (siehe Steps-Übersicht)
- **Anzahl Tech-Debt-Einträge:** 7 (TD-001 bis TD-007) — Endstand: 3 erledigt
  im ursprünglichen Loop-Abschnitt zurückgemeldet (TD-001, TD-003), im
  Post-Completion-Verlauf weitere 2 erledigt (TD-002, TD-006), 1 „won't fix"
  (TD-004), 2 „grundsätzlich nicht" (TD-005, TD-007). `tech-debt.md` aktuell
  leer.
- **Loop-Iterationen (Fix-Runden):** 2 / 12 (Task-Not-Anker; `step-002/fix-01`
  und `step-006/fix-01` verbraucht; Reopen in `step-003` und Revert in
  `step-006` waren keine neuen Fix-Runden, sondern Reaktivierung bzw.
  Nutzer-Entscheidung)
- **Laufzeit:** 2026-08-04T11:02:33+02:00 (`started_at` aus `task-state.md`)
  bis 2026-08-05T18:30:00+02:00 (`last_updated` aus `task-state.md`,
  Abschluss von EPIC-04/`step-007`) — knapp 31,5 Stunden über zwei
  Loop-Abschnitte (ursprünglicher Task-Abschluss + zwei Post-Completion-
  Runden EPIC-03/EPIC-04).

## Verdict

- [x] **done** — Konzept vollständig adressiert (beide Muss-Haben-Ideen +
  DoD), alle vier Epics in `roadmap.md` abgehakt, alle Steps final approved
  oder als bewusste Nutzer-Entscheidung geschlossen, Build grün, 526/526
  Tests grün, Doku synchron, `tech-debt.md` vollständig leer (alle sieben
  Einträge erledigt oder bewusst geschlossen). Keine globalen
  Konzept-Verletzungen, keine schweren Build-/Test-Probleme, keine offenen
  Muss-Haben-Punkte. TD-004 als „won't fix" ist eine dokumentierte,
  sachlich begründete Nutzer-Entscheidung, kein unvollständiger
  Task-Abschluss.
- [ ] **aborted** — *nicht zutreffend*.

## Post-Completion-Tech-Debt-Cleanup (step-004)

Nach Abschluss des Tasks (`task-summary.md` Verdict `done`, alle 4 Steps
approved, 522/522 Tests grün) hat der Nutzer am 2026-08-05 angeordnet, die in
`tech-debt.md` gesammelten Tech-Debts nach Klassifikation (in-scope → fixen /
out-of-scope → explizit markieren) zu adressieren. Die Klassifizierung wurde in
der Orchestrator-Befragung 2026-08-05 abgestimmt; die Umsetzung erfolgt in
`step-004` (Epic EPIC-03 „Post-Completion Tech-Debt Cleanup", Risiko `low`,
kein Code-Change, kein Test-Change).

### Ergebnis pro Tech-Debt

- **TD-001** (Konzept-Index-Name-Format `IX_Orders_CustomerId_OrderDate` vs.
  Code `IX_Orders_CustomerId__OrderDate`) — **erledigt in step-004**:
  Konzept-Beispiel in `konzept.md` Zeile 172 an die implementierte Form
  angepasst. Kein Code-Change, kein Test-Change, 522/522 Tests grün bleiben.
  Konzept-Pfeil-Form war illustrativ; die `__`-Wahl in `step-001` war
  deliberate (Planer-Begründung: „bessere Lesbarkeit bei mehreren Spalten").
- **TD-002** (`DESC`-Sortierung in `ColumnGroup` ignoriert) — **out of scope,
  won't fix in diesem Task**: Konzept schweigt über `DESC`, Konzept-Beispiel
  hat keine absteigend indizierte Spalte. Eine Implementierung wäre eine
  konzeptuelle Erweiterung (kein Bugfix).
- **TD-003** (`IsShowplanPermissionError` generalisiert) — bereits in
  `step-002` erledigt, unverändert.
- **TD-004** (SQL-Server-2025-Spezifik, fehlende Versionsnotiz) — **out of
  scope, won't fix in diesem Task**: Konzept schweigt über
  SQL-Server-Mindestversion, die 2025-Spezifik ist emergente Eigenschaft der
  Test-Instanz. Eine Versionsnotiz wäre Architektur-/Setup-Entscheidung.
- **TD-005** (Test-Environment-Setup `GRANT VIEW SERVER STATE TO [Agent]`
  nicht reproduzierbar) — **out of scope, won't fix in diesem Task**:
  Test-Infrastruktur (CI/CD), kein Konzept-Gegenstand. Konzept verlangt nur
  „Integrationstest gegen eine echte Test-DB".
- **TD-006** (Test 1 akzeptiert Graceful-Degradation-Notiz nicht, Asymmetrie
  zu Test 4) — **out of scope, won't fix in diesem Task**: Test-Design-Detail,
  kein Konzept-Verstoß. Konzept verlangt nur „Tests vorhanden" für Graceful
  Degradation, keine Aussage zur Test-Toleranz.
- **TD-007** (`DmvMockConnectionFactory` deckt SQL-Syntaxfehler nicht ab,
  systemischer Test-Coverage-Gap) — **out of scope, won't fix in diesem
  Task**: Test-Strategie-/Architektur-Frage, Konzept schweigt. 80% des
  Problems bereits durch TD-005+TD-006 adressierbar; restliche 20% (statische
  Validierung) lohnen nur bei mehreren DMV-Tools.

### Endgültige Tech-Debt-Statistik

- **Vor step-004:** 6 offen + 1 erledigt (TD-003)
- **Nach step-004:** 0 offen-unmarkiert + 2 erledigt (TD-003, TD-001) +
  5 out-of-scope-markiert (TD-002, TD-004, TD-005, TD-006, TD-007)
- **Build-/Test-Stand:** 522/522 Tests grün, `dotnet build` 0 Warnungen
  (Smoke-Verifikation optional; bei Ausführung in `step-004/step-result.md`
  festgehalten).

### Epic- und Commit-Verweise

- **Epic:** EPIC-03 „Post-Completion Tech-Debt Cleanup" in `roadmap.md` (mit
  `step-004` abgehakt).
- **Step:** `step-004` (verbraucht keine Fix-Runde; keine `fix-XX/`-Unterordner,
  keine `issues`-Verdikte erwartet — der Step ist 100% Doku-Edit mit
  deterministisch grünem Smoke-Test).
- **Commits:** ein gemeinsamer Commit für alle Markdown-Edits +
  `step-004/step-plan.md`-Status-Update (konzept.md, tech-debt.md,
  task-summary.md, step-004/step-plan.md). Convention: `docs(task): …`,
  deutsch, imperativ, Suffix `[sql-index-suggestions]`.

## Post-Completion-Tech-Debt-Cleanup Round 2 (EPIC-04)

Nach `step-004`/EPIC-03 ordnete der Nutzer am 2026-08-05 eine zweite
Post-Completion-Runde an, mit verschärfter Policy: „tech debt soll nur
beinhalten was wirklich offen ist" — die verbleibenden sechs Einträge sollten
final entweder umgesetzt oder explizit aus `tech-debt.md` entfernt werden.
Umsetzung in drei separaten Steps (`step-005`, `step-006`, `step-007`), je
einer pro TD, `EPIC-04` in `roadmap.md`.

### Ergebnis pro Tech-Debt (Round 2)

- **TD-002** — **erledigt in `step-005`** (Commit `a1492c6`, Verdict
  `approved`): `DESC`-Sortierung wird jetzt korrekt in
  `BuildCreateIndexStatement` gerendert, 4 neue Tests, Eintrag aus
  `tech-debt.md` entfernt.
- **TD-004** — **„won't fix", Nutzer-Entscheidung** nach zwei gescheiterten
  Versuchen (`step-006` Commit `2011331`, `step-006/fix-01` Commit
  `75fb296`, siehe „Besondere Würdigung TD-004" oben). Code per
  Revert-Commit `09fa038` zurückgesetzt, Eintrag aus `tech-debt.md`
  entfernt.
- **TD-005** — **„grundsätzlich nicht"**, keine Umsetzung, Eintrag aus
  `tech-debt.md` entfernt (Nutzer-Vorgabe).
- **TD-006** — **erledigt in `step-007`** (Commit `0a71e9b`, Verdict
  `approved`): Test 1 akzeptiert jetzt den Graceful-Degradation-Pfad
  analog Test 4, Eintrag aus `tech-debt.md` entfernt.
- **TD-007** — **„grundsätzlich nicht"**, keine Umsetzung, Eintrag aus
  `tech-debt.md` entfernt (Nutzer-Vorgabe, gleicher Status wie TD-005).

### Endgültige Tech-Debt-Statistik (nach Round 2)

- **Vor Round 2:** 5 offen-out-of-scope-markiert + 2 erledigt (TD-001, TD-003)
- **Nach Round 2:** `tech-debt.md` vollständig leer — TD-002 und TD-006
  zusätzlich erledigt, TD-004 „won't fix", TD-005/TD-007 „grundsätzlich
  nicht" — alle fünf Einträge aus der Datei entfernt (Status-Policy).
- **Build-/Test-Stand am globalen Abschluss-Check:** `dotnet build
  SqlToAi.slnx` grün (0 Warnungen), `dotnet test SqlToAi.slnx` grün
  (526/526 Tests), `git status` clean danach — selbst vom globalen
  Kritiker nachgeprüft (nicht nur aus Step-Reviews übernommen).

### Epic- und Commit-Verweise (Round 2)

- **Epic:** EPIC-04 „Post-Completion Tech-Debt Cleanup Round 2" in
  `roadmap.md` (mit `step-005`, `step-006`(+`fix-01`,+Revert), `step-007`
  abgehakt).
- **Fix-Runde verbraucht:** 1 (`step-006/fix-01`) von den 2/12 Gesamt-
  Fix-Runden des Tasks.
- **Commits:** `a1492c6` (TD-002), `2011331` + `75fb296` + `09fa038`
  (TD-004-Versuche + Revert), `0a71e9b` (TD-006).
