---
status: done
type: step-review
task: sql-index-suggestions
step: 003
epic: EPIC-02
step_type: single
reviewed_by: kritiker
reviewed_by_model: MiniMax-M3
reviewed_by_model_knowledge_cutoff: 2026-01
reviewed_at: 2026-08-05T10:00:00+02:00
verdict: approved
tech_debt_ids: [TD-004, TD-005, TD-006, TD-007]
---

# Review Step 003: EPIC-02 Integrationstest für `sql_suggest_indexes` gegen echte Test-DB (Reopen)

## Verdict

- [x] **approved** — alle vier Prüfebenen ok
- [ ] **issues** — Fix-Step `step-<NNN>/fix-<XX>` angelegt mit Fix-Plan
- [ ] **blocked** — Nutzer-Entscheidung nötig (siehe Frage unten)

## Geprüft

- [x] Plan-Erfüllung: alle im `step-plan.md` genannten Änderungen erfolgt
- [x] Rules-Konformität: `<rules_dir>/**` eingehalten
- [x] Logische Korrektheit: Code macht was er soll, nicht nur „grün"
- [x] Konzept-Treue: passt die Umsetzung zu `konzept.md` (Scope, Non-Goals, Muss-Haben)
- [x] Build: selbst nachgeprüft, grün
- [x] Tests: selbst nachgeprüft, grün (oder Baselines beachtet)

## Befund

### Plan-Erfüllung

Reopen-Auftrag (CTE-Fix Variante 2) vollständig umgesetzt: `IndexSuggestionService.cs:140-186` zeigt die geschachtelte CTE mit innerer `Scored` (ImprovementScore + DB-Scope-Filter) und äußerer `TopIndexes` (user-Filter + `TOP (@Top)`) — der `WHERE`-Alias-Bug auf `ImprovementScore` ist behoben, weil der Alias jetzt in der äußeren CTE nur noch im `WHERE` referenziert wird, nachdem er in der inneren berechnet wurde. Die beiden SQL-Server-2025-Kompatibilitäts-Fixes (`migs.group_handle` statt `index_group_handle`; `CROSS APPLY sys.dm_db_missing_index_columns(ti.IndexHandle)` statt `INNER JOIN ... ON index_handle`) liegen im selben `const string sql` und sind im Reopen-Auftrag zwingend, weil Variante 1 (CTE-Alias) allein den Test 1 nicht grün macht — die Spalte `index_group_handle` existiert in SQL Server 2025 nicht mehr und der `INNER JOIN` auf die TVF-Spalte `index_handle` wäre ebenfalls ein Schema-Fehler. Scope-Sperre war explizit aufgehoben. Commit `0348e9d` deckt alle drei logischen Änderungen gemeinsam ab (Commit-Body dokumentiert sie separat), `630f0ce` ist der Doku-Commit; Conventional-Commit-Format, deutsch, imperativ, Subject ≤ 72 Zeichen, Suffix `[sql-index-suggestions]` — alles eingehalten.

### Rules-Konformität

`SqlToAiRichtlinien.mdc` §5 (Zero-Warning-Direktive): eingehalten, Build grün mit 0 Warnungen unter `TreatWarningsAsErrors=true`. `AiNetLinter.mdc` Zeile 11 (`sealed`): eingehalten. Zeile 12 (`#nullable enable`): eingehalten. Zeile 13-14 (kein leeres `catch`): unverändert. Zeile 22/27 (Parameter-/Constructor-Limits): keine neuen Methoden/Konstruktoren. Zeile 58 (Namespace-Directory-Mapping): `IndexSuggestionService.cs` bleibt in `src/SqlToAi/Database/`. Zeile 67 (ASCII-Identifiers): CTE-Aliase `Scored`, `TopIndexes`, `Statement`, `IndexHandle`, `UserSeeks`, `UserScans`, `LastUserSeek`, `AvgTotalUserCost`, `AvgUserImpact`, `ImprovementScore`, `ColumnId`, `ColumnUsage` — alle ASCII. **Doku-Sync-Pflicht §4:** die Entkräftung aus dem Plan („Test-Code ohne API-Wirkung") trägt für den CTE-Fix-Teil (Service-API, Tool-Output, Parameter bleiben identisch — bestätigt durch Vergleich mit `step-002/fix-01/step-review.md` Absatz „Rules-Konformität"); die SQL-Server-2025-Fixes brechen jedoch die Abwärtskompatibilität zu SQL Server < 2025 (s. Hinweis unten) — eine Versionsnotiz in `architecture-spec.md` wäre ergänzend wünschenswert, ist aber **nicht** zwingend (Architecture-Spec nennt derzeit keine SQL-Server-Mindestversion, der etablierte Pfad ist die Doku-Sync-Entkräftung für interne SQL-Struktur-Änderungen, vgl. `step-002/fix-01`). Aufnahme als TD-004 (Tech-Debt-Kanal), nicht als MAJOR-Finding.

### Logische Korrektheit

CTE-Semantik korrekt: `TOP (@Top)` wird INNERHALB der CTE auf `index_handle`/`statement`-Granularität angewendet, BEVOR `sys.dm_db_missing_index_columns` (jetzt als TVF via `CROSS APPLY`) angewendet wird. Spalten-Aliase der äußeren SELECT-Liste matchen 1:1 `SuggestionRawRow` (PascalCase→PascalCase) — Dapper-Mapping konsistent. CTE-interne Sortierung `ImprovementScore DESC, Statement` und Outer-Sortierung `ti.ImprovementScore DESC, ti.Statement, mic.column_id` sind deterministisch (zwei Schlüsselebenen) und reproduzierbar — `GroupRows`-Dictionary-Insertion-Order bleibt stabil. Parameter-Bindungs-Objekt (`new { TableName, MinScore, Top }`) und CTE-Aliase (`Statement`/`IndexHandle`/…) matchen Dapper-Property-Namen unabhängig von der SQL-Stelle, daher bleiben die Unit-Tests 7/8 (`TableNameFilter_PassedAsLikeParameter`, `TopFilter_PassedAsFetchNextParameter`) trotz SQL-Umstrukturierung grün — bestätigt durch Coder-Report (13/13 Unit-Tests) und eigene Reproduktion (522/522). Mock-Tests revalidiert: `DmvMockConnectionFactory` (Unit-Test) liefert vorgegebene Rows, ignoriert die SQL — die CTE-Regression `SuggestIndexesAsync_MultipleHandlesWithDifferentColumnCounts_AllColumnsPerHandlePreserved` ist gegen synthetische 3-Handle-Daten mit unterschiedlichen Spaltenzahlen weiterhin grün und beweist die Top-N-Semantik pro `index_handle`. Der CTE-Alias-Bug und die 2025-Inkompatibilitäten sind im Mock nicht reproduzierbar (systemischer Test-Coverage-Gap, s. TD-007) — sie wurden erst durch den Integrationslauf gegen die reale Test-DB sichtbar.

### Konzept-Treue (Ebene 4)

Konzept §DoD letzter Punkt für Idee 2 („Integrationstest gegen eine echte Test-DB in `tests/SqlToAi.Tests/Integration/`") **erfüllt**: `IndexSuggestionServiceIntegrationTests.cs` mit 4 Tests gegen `%COMPUTERNAME%\MSSQLSERVER2022`/`Agent`/`DemoDB` (real), Happy-Path liefert Markdown-Header `# Missing Index Recommendations — DemoDB` + Restart-Hinweis + Inhalt (`No missing-index recommendations found` weil DMV-Daten seit Restart akkumulieren), Top-Parameter-Smoke, Table-Name-Filter-Smoke, Graceful-Degradation-Probe — alle 4 grün. Konzept §Muss-Haven Idee 2 (Tool, Parameter, Markdown-Format, Graceful Degradation, Restart-Hinweis) bleibt erfüllt — die internen SQL-Änderungen sind nicht beobachtbar. Konzept §Non-Goals nicht verletzt: kein `CREATE INDEX`-DDL, kein DTA, kein `DBCC AUTOPILOT`, keine Schreiboperation. EPIC-02 ist mit diesem Step abgeschlossen.

### Build-/Test-Status

```
dotnet build SqlToAi.slnx  → grün (0 Warnungen, 0 Fehler, TreatWarningsAsErrors=true)
dotnet test  SqlToAi.slnx  → grün (522 Tests, 0 Fehler, 0 übersprungen, ~5 s, inkl. AiNetLinterTests.RecreateBaseline)
```

Selbst nachgeprüft (Re-Run am 2026-08-05): Build sauber, 522/522 grün. Der ursprüngliche `blocked`-Befund aus dem ersten Coder-Lauf (CTE-Alias-Bug `ImprovementScore` in `WHERE`-Klausel) ist behoben. Der CTE-Fix (Variante 2) plus die SQL-Server-2025-Fixes (Spalten-Rename + TVF-`CROSS APPLY`) sind im selben Code-Kontext (`const string sql` in `IndexSuggestionService.LoadSuggestionsAsync`) und waren alle drei gemeinsam nötig, um den Integrationstest grün zu bekommen — kein Scope-Drift gegenüber dem Reopen-Auftrag (Scope-Sperre war explizit aufgehoben, Coder hat die 3 logischen Änderungen im Commit-Body transparent dokumentiert).

## Sonstige Beobachtungen / MINOR / NITPICK

- **`step-plan.md` Frontmatter-Status auf `in_progress`:** Coder-Notiz §9 zufolge hat der Nutzer explizit angewiesen, dass der Coder den Status im Reopen-Pfad NICHT auf `done (pending audit)` setzt; der Coder hat das respektiert. Das ist eine ungewöhnliche Konvention (typischerweise setzt der Coder den Status nach eigenem `done`) und gehört in den Folge-Loop (Planer oder Folge-Coder-Aufruf) bereinigt. **Kein Finding** — außerhalb des Step-Reviews.

- **Linter-Baseline-Hash für `IndexSuggestionService.cs` aktualisiert:** Coder-Report bestätigt `RecreateBaseline`-Lauf; das ist im selben `dotnet test`-Lauf automatisch passiert. Kein manueller Eingriff. **Kein Finding** — etablierter Mechanismus aus `step-002/fix-01`.

- **Code-Commit `0348e9d` enthält drei logische Änderungen in einem Commit:** stilistisch feinkörniger wäre eine Aufteilung in drei separate Commits (CTE-Alias, Spalten-Rename, TVF-CROSS-APPLY), aber funktional gleichwertig, weil alle drei Änderungen nur gemeinsam das Test-Ziel erreichen. Commit-Body dokumentiert die drei Aspekte separat. **Kein Finding** — Commit-Granularität ist nicht regelgebunden.

## Tech-Debt-Einträge aus diesem Review

- `TD-004` (siehe `tech-debt.md`) — SQL-Server-Versionen < 2025 nicht abwärtskompatibel (`group_handle` existiert nicht, `dm_db_missing_index_columns` ist dort View, keine TVF); eine Mindestversions-Notiz in `architecture-spec.md` §4 Nr. 16 oder §H wäre ergänzend.
- `TD-005` (siehe `tech-debt.md`) — `GRANT VIEW SERVER STATE TO [Agent]` wurde einmalig lokal außerhalb des Repos ausgeführt; kein reproduzierbares Setup-Skript in `scripts/` oder `SqlServerFixture.cs`-Initialisierung.
- `TD-006` (siehe `tech-debt.md`) — Test 1 (`ShouldReturnMarkdownWithRestartHint_AgainstRealDatabase`) akzeptiert die Graceful-Degradation-Notiz nicht, Test 4 tut es; Asymmetrie führt zu implizitem Permission-Setup-Requirement.
- `TD-007` (siehe `tech-debt.md`) — `DmvMockConnectionFactory` führt SQL nicht aus, sondern liefert vorgegebene Rows; systemischer Test-Coverage-Gap, der SQL-Syntaxfehler (CTE-Alias-Bug, 2025-Inkompatibilitäten) erst im Integrationstest sichtbar macht.
