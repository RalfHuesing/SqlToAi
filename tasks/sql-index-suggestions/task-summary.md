---
task: sql-index-suggestions
completed_at: 2026-08-05T10:05:00+02:00
final_status: done  # done | aborted
total_iterations: 1
total_commits: 8
total_epics: 2
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
Wurzel behoben.

## Roadmap-Status

Beide Epics aus `roadmap.md` abgehakt:

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

Details zu Epic-Begründungen, Commit-Verweisen und beobachtetem Restbedarf siehe
`roadmap.md` Zeilen 66–138.

## Steps-Übersicht

| Step | Epic | Status | Title | Commit | Notiz |
|------|------|--------|-------|--------|-------|
| step-001 | EPIC-01 | done | Parser-Erweiterung — vollständige CREATE NONCLUSTERED INDEX-Statements | `86c0e48` (Code) + `4e4f6a2` (Result) | approved, 3 neue Tests + 12 bestehende grün |
| step-002 | EPIC-02 | done | Service + Tool-Registrierung + Doku-Sync für `sql_suggest_indexes` | `3195a17` (Code) + `50437e2` (Doku) | initial issues → fix-01; final approved |
| step-002/fix-01 | EPIC-02 | done | CTE-Top-N pro `index_handle` (Fix für CRITICAL aus step-002) | `bc488ec` (Code) + `1a412cb` (Result) | approved, 1 neuer Test (3 Handles à 2/5/3 Spalten) |
| step-003 | EPIC-02 | done | Integrationstest für `sql_suggest_indexes` gegen echte Test-DB | `2ac3668` (blocked-Lauf) + `0348e9d` (Reopen-Code) + `9a36678` (blocked-Result) + `630f0ce` (Reopen-Result) | blocked → Reopen → approved, 522/522 Tests grün |

**Total-Fix-Runden verbraucht: 1/12** — nur `fix-01` wurde als Fix-Step gezählt.
Der Reopen in `step-003` war keine neue Fix-Runde, sondern eine Reaktivierung
des bestehenden Steps mit vom Nutzer explizit aufgehobener Scope-Sperre
(`step-003/step-result.md` §Reopen-Phase, „Der Nutzer hat die Scope-Sperre für
`step-002`-Code explizit aufgehoben"). `total_fix_rounds` in `task-state.md`
steht korrekt auf 1.

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

Keine. Der zuletzt gemeldene Stand (aus `step-003/step-review.md` Zeile 56,
selbst vom globalen Kritiker am Abschluss nachgeprüft):

```
dotnet build SqlToAi.slnx  → grün (0 Warnungen, 0 Fehler, TreatWarningsAsErrors=true)
dotnet test  SqlToAi.slnx  → grün (522 Tests, 0 Fehler, 0 übersprungen, ~5 s,
                                inkl. AiNetLinterTests.RecreateBaseline)
```

`AiNetLinterTests.RecreateBaseline` hat in jedem Step die `SqlToAi-baseline.json`
automatisch aktualisiert (kein manueller Eingriff, keine Hash-Rechnungen von
Hand) — bestätigt durch `step-002/fix-01/step-result.md` Beobachtung 1 und
`step-003/step-result.md` Beobachtung 3. Die fünf vorhandenen grandfathered
Violations (`PerformanceMeasurementService.cs`, `ToolDispatcher.cs`,
`GlobMatcherTests.cs`, `ToolDispatcherTests.cs` + ein weiterer) bleiben
unverändert, keine neuen Violations.

### Rules-Konformität (Stichproben)

Stichprobenartig gegenrpüft an den drei approved Steps:

- **step-001** (`step-001/step-review.md`): AiNetLinter-Grenzwerte eingehalten
  — `BuildCreateIndexStatement` in `PerformanceMeasurementService.cs:373` liegt
  mit ~46 LOC deutlich unter dem 60-LOC-Limit, Parameteranzahl 4 exakt am
  Limit, `sealed` und `#nullable enable` der Datei unverändert. Conventional
  Commit, deutsch, imperativ, Suffix `[sql-index-suggestions]`. §4
  Doku-Sync-Pflicht eingehalten (architecture-spec + README).
- **step-002** (`step-002/step-review.md`): Doku-Sync-Pflicht §4
  eingehalten — `architecture-spec.md` §4 Nr. 16 + §H (vierter Block
  `VIEW SERVER STATE`) + `README.md` Feature-Bullet + Tool-Count 15→16 +
  Recommended-Permissions alle mit-aktualisiert. AiNetLinter Zeile 11/12/13-14
  eingehalten (`sealed` auf `IndexSuggestionService`/`MissingIndexRow`/
  `SuggestionRawRow`/`DmvMockConnectionFactory`/`FakeIndexSuggestionService`,
  `#nullable enable` auf allen vier neuen Dateien, kein leeres `catch`).
  `MaxConstructorDependencies`=5 exakt am Limit (kein Verstoß);
  `ToolDispatcher` mit 9 Parametern / 8 Dependencies ist bestehender Status
  quo (vom Planer explizit als „Bekannte Ausnahme" markiert, nicht
  EPIC-02-spezifisch).
- **step-003** Reopen (`step-003/step-review.md`): §5 Zero-Warning-Direktive
  eingehalten, Build grün mit 0 Warnungen unter `TreatWarningsAsErrors=true`.
  CTE-Aliase `Scored`/`TopIndexes`/`Statement`/`IndexHandle`/`UserSeeks`/
  `UserScans`/`LastUserSeek`/`AvgTotalUserCost`/`AvgUserImpact`/
  `ImprovementScore`/`ColumnId`/`ColumnUsage` alle ASCII. Namespace-Mapping
  `IndexSuggestionService.cs` bleibt in `src/SqlToAi/Database/`. Conventional
  Commit-Format, deutsch, imperativ, Suffix `[sql-index-suggestions]`.

**`TreatWarningsAsErrors`:** durchgehend eingehalten in allen 4 Steps.
**`AiNetLinter-baseline.json`:** automatisch durch `RecreateBaseline`
aktualisiert, keine manuellen Hashes. **Doku-Sync-Pflicht:** §4 in
`step-001` und `step-002` vollständig erfüllt; für `fix-01` und
`step-003`-Reopen explizit entkräftet (interne SQL-Struktur ändert kein
beobachtbares Verhalten — Output-Spalten, Header, Score-Formel, Parameter,
Permission-Verhalten bleiben identisch).

Keine Findings im globalen Modus.

## Tech-Debt-Zusammenfassung

Aus `tech-debt.md` aggregiert (Volltext bleibt dort, hier nur Übersicht nach
Index-Tabelle):

- **Hoch:** 0 Einträge
- **Mittel:** 3 Einträge — `TD-001` (Konzept-Index-Name-Format-Harmonisierung),
  `TD-004` (SQL-Server-Mindestversion 2025 für `IndexSuggestionService`-CTE
  wegen `group_handle` + TVF; abwärtsinkompatibel zu 2019/2022),
  `TD-005` (Test-Environment-Setup `GRANT VIEW SERVER STATE TO [Agent]`
  einmalig lokal ausgeführt, kein reproduzierbares Setup-Skript)
- **Niedrig:** 3 Einträge — `TD-002` (`DESC`-Sortierung in
  `ColumnGroup`-Spalten wird in `BuildCreateIndexStatement` ignoriert),
  `TD-006` (Test 1 akzeptiert Graceful-Degradation-Notiz nicht, Asymmetrie
  zu Test 4), `TD-007` (`DmvMockConnectionFactory` deckt SQL-Syntaxfehler
  nicht ab — systemischer Test-Coverage-Gap, der CTE-Alias-Bug und
  2025-Inkompatibilitäten erst spät im Integrationstest sichtbar machte)
- **Erledigt:** 1 Eintrag — `TD-003` (`IsShowplanPermissionError` zu
  `IsPermissionError(SqlException, int, string)` generalisiert, in
  `step-002` umgesetzt, durch Test 11 in `IndexSuggestionServiceTests`
  abgesichert)

Volltext aller Einträge: `tech-debt.md` (Pointer-Prinzip — die ausführlichen
Befunde, Vorschläge und Pfad-Referenzen sind dort dokumentiert, nicht hier
dupliziert).

**Hinweis für den Nutzer:** Von den drei mittel-priorisierten Einträgen
erscheinen `TD-004` und `TD-005` aus technischer Sicht am dringendsten
(`TD-004` blockiert faktisch jeden Nutzer auf SQL Server < 2025, `TD-005`
macht CI/CD und frische Test-Instanzen nicht reproduzierbar). `TD-001` ist
eine reine Doku-Harmonisierung und kann jederzeit unabhängig erfolgen. Die
drei niedrig-priorisierten Einträge sind Nice-to-Have-Verbesserungen, deren
Behebung keinen Task-Block rechtfertigt.

## Offene Punkte

- [ ] **TD-001 (mittel)** — Konzept-Beispiel in `konzept.md` Zeile 172 zeigt
      `IX_Orders_CustomerId_OrderDate` (alle einfachen Unterstriche),
      `PerformanceMeasurementService.BuildCreateIndexStatement` Zeile 399–405
      verwendet `IX_Orders_CustomerId__OrderDate` (`__` als Spalten-Trenner).
      Harmonisierung an Konzept ODER an Code (Konzept-Beispiel ist
      Pfeil-Form, nicht normativ). Reine Doku-Aufgabe.
- [ ] **TD-002 (niedrig)** — `BuildCreateIndexStatement` in
      `PerformanceMeasurementService.cs:373` ignoriert das `Descending`-
      Attribut an `Column`-Elementen. 1-2-Zeilen-Erweiterung im Helper +
      Test-Erweiterung. Funktional nicht falsch, nur semantisch nicht
      deckungsgleich mit SQL-Server-Empfehlung bei absteigend indizierten
      Spalten.
- [ ] **TD-004 (mittel)** — `IndexSuggestionService.LoadSuggestionsAsync`
      SQL (Zeile 140–186) ist SQL-Server-2025-spezifisch
      (`migs.group_handle` + `CROSS APPLY sys.dm_db_missing_index_columns`).
      Eine Mindestversions-Notiz in `architecture-spec.md` §4 Nr. 16 oder §H
      wäre ergänzend wünschenswert. Für Rückwärtskompatibilität zu SQL
      Server 2019/2022 wäre eine versionsabhängige CTE-Konstruktion nötig
      (Try/Detect-Pattern, kosten mehrere Tests).
- [ ] **TD-005 (mittel)** — `GRANT VIEW SERVER STATE TO [Agent]` wurde
      einmalig lokal außerhalb des Repos ausgeführt (Coder-Notiz
      `step-003/step-result.md` §3). Es gibt kein reproduzierbares
      Setup-Skript in `scripts/` oder als Initialisierungs-Methode in
      `SqlServerFixture.cs`. Konsequenz: CI/CD und frische Test-Instanzen
      fallen in den Graceful-Degradation-Pfad, Test 1 schlägt fehl.
- [ ] **TD-006 (niedrig)** — Test 1 in
      `IndexSuggestionServiceIntegrationTests.cs:26-42` akzeptiert die
      Graceful-Degradation-Notiz nicht, Test 4 akzeptiert sie. 1-2-Zeilen-
      Erweiterung analog Test 4 würde Test 1 setup-tolerant machen (siehe
      TD-005).
- [ ] **TD-007 (niedrig)** — `DmvMockConnectionFactory` in
      `IndexSuggestionServiceTests.cs:370-424` liefert vorgegebene Rows,
      ohne die SQL zu parsen oder auszuführen. Folge: SQL-Syntaxfehler
      (CTE-Alias-Bug aus `step-002/fix-01`, 2025-Inkompatibilitäten aus
      `step-003`-Reopen) werden erst im Integrationstest sichtbar. Optionen:
      (a) statische DMV-Spalten-Validierung gegen Versions-Whitelist, (b)
      verpflichtender Integrationstest in CI/CD mit echtem SQL-Server-
      Container. Beides berührt Architekturentscheidungen jenseits dieses
      Tasks.

## Empfehlungen

- **TD-001 als kleines Refactoring-Epic in einem Folge-Task aufnehmen, falls
  die Konzept-/Code-Divergenz stört** — die Diskrepanz ist klein, die
  Harmonisierung trivial (eine `string.Join`-Zeile in `BuildCreateIndexStatement`
  ODER eine Beispiel-Korrektur in `konzept.md`). Aktuell nicht blockierend.
- **TD-004 und TD-005 zusammen in einem Folge-Task adressieren**, da sie
  denselben thematischen Kreis betreffen (Produktions-Deployment von
  `sql_suggest_indexes`): Mindestversions-Notiz in `architecture-spec.md`
  (TD-004) + Setup-Skript in `scripts/setup-test-permissions.sql` oder
  Initialisierungs-Methode in `SqlServerFixture.cs` (TD-005). Beides
  Voraussetzung für eine CI/CD-Pipeline, die diesen Test auf einer frischen
  SQL-Server-Instanz reproduzierbar grün bekommt.
- **TD-006 als 1-Zeilen-Folge-Patch** zu TD-005: Test 1 analog Test 4 um den
  Graceful-Degradation-Pfad erweitern, sobald das Setup-Skript
  (TD-005) existiert. Macht die Test-Sammlung insgesamt setup-tolerant.
- **TD-007 als Architektur-Entscheidung in einem größeren Folge-Refactor:**
  die Mock-Strategie für DMV-basierte Tools ist ein systemisches Thema, das
  alle künftigen DMV-Tools betrifft. Die einfachste und billigste Maßnahme
  ist TD-005 (CI/CD-Setup) + TD-006 (Test-Toleranz) — sie adressieren
  80% des Problems ohne Architektur-Change. Die restlichen 20% (statische
  DMV-Spalten-Validierung, Compile-Check gegen reales DMV-Schema) sind nur
  sinnvoll, wenn mehrere DMV-Tools geplant sind.
- **TD-002 als Micropatch in einem `audit-hardening`-ähnlichen Folge-Task**
  aufnehmen, falls `sql_measure_performance`-DDL-Ausgaben produktiv genutzt
  werden und absteigende Index-Sortierung ein realistischer Fall ist.
  Aktuell niedrigste Priorität.
- **Vor Push auf Remote / vor Release:** lokal `dotnet build && dotnet test`
  laufen lassen, um den 522/522-Stand zu bestätigen — der Coder hat
  ausschließlich lokal gearbeitet (`Push: nein (lokal)` in jedem
  step-result), die Remote-Historie ist nicht aktualisiert.

## Statistik

- **Anzahl Epics:** 2, davon abgehakt: 2 (EPIC-01, EPIC-02)
- **Anzahl Steps:** 4 (`step-001`, `step-002`, `step-002/fix-01`, `step-003`)
- **Davon approved:** 4 (alle 4 final approved — `step-002` war initial
  `issues`, in `fix-01` behoben; `step-003` war initial `blocked`, im
  Reopen behoben)
- **Davon blocked:** 0 (im finalen Stand; transient blocked während
  step-002/step-003-Lauf, aber durch Folge-Steps aufgelöst)
- **Anzahl Commits:** 8 (laut `task-state.md` Steps-Tabelle: je Step
  Code-Commit + Result-Commit; in `step-003` zählt der Reopen-Code-Commit
  `0348e9d` als verbindlicher Code-Commit, der initial blocked-Code-Commit
  `2ac3668` als Vor-Lauf-Zwischenstand)
- **Anzahl Tech-Debt-Einträge:** 7 (3 mittel, 3 niedrig, 1 erledigt)
- **Loop-Iterationen (Fix-Runden):** 1 / 12 (Task-Not-Anker; nur `fix-01`
  verbraucht, Reopen in `step-003` war keine neue Fix-Runde sondern eine
  Reaktivierung)
- **Laufzeit:** 2026-08-04T11:02:33+02:00 (`started_at` aus `task-state.md`)
  bis 2026-08-05T10:05:00+02:00 (`last_updated` aus `task-state.md`,
  zeitgleich mit dem finalen `approved`-Verdict für `step-003`) — knapp
  23 Stunden, davon die ersten ~16h für die zwei Approval-Phasen
  (`step-001` + `step-002/fix-01`), die restlichen ~7h für `step-003`
  inkl. Reopen.

## Verdict

- [x] **done** — Konzept vollständig adressiert (beide Muss-Haven-Ideen +
  DoD), beide Epics in `roadmap.md` abgehakt, alle Steps final approved,
  Build grün, 522/522 Tests grün, Doku synchron, Tech-Debt bewusst
  dokumentiert (6 offene + 1 erledigt). Keine globalen Konzept-Verletzungen,
  keine schweren Build-/Test-Probleme, keine offenen Muss-Haven-Punkte.
- [ ] **aborted** — *nicht zutreffend*.
