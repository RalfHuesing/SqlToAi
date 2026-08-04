---
status: active
task: sql-index-suggestions
derived_from: konzept.md
created_at: 2026-08-04T12:00:00+02:00
last_updated: 2026-08-05T15:30:00+02:00
created_by_model: MiniMax-M3
created_by_model_knowledge_cutoff: 2026-01
---

# Roadmap: sql-index-suggestions

Grober Anker, kein Detailplan — Detail-Steps entstehen erst JIT im
Step-Modus des Planers, siehe `../spec.md` §7.2. Diese Datei wird
laufend angepasst (Epics abgehakt, ergänzt, umformuliert oder als
obsolet markiert) — kein starres Vorab-Dokument.

## Tech-Stack-Notiz

- **Build-Command:** `dotnet build` (oder `dotnet build SqlToAi.slnx`)
- **Test-Command:** `dotnet test` (oder `dotnet test SqlToAi.slnx`)
- **Lint-Hinweis:** `AiNetLinterTests.RecreateBaseline` läuft automatisch
  in jedem `dotnet test`-Lauf mit und aktualisiert `SqlToAi-baseline.json`
  automatisch. **Kein** manuelles Hash-Rechnen. Falls `AiNetLinter.exe`
  fehlt, werden Lint-Tests per `Assert.Skip` übersprungen — kein Fehler,
  aber auch keine Baseline-Prüfung.
- **Code-Style-Kurzfassung** (aus `.agents/rules/**`):
  - `sealed` für konkrete Klassen, `#nullable enable` am Dateianfang.
  - Kurze Methoden (≤ 60 LOC Produktion / ≤ 100 LOC Tests; ≤ 150 LOC nur
    bei CC ≤ 3 und Cognitive ≤ 5).
  - `Result<T>` für Fehlerbehandlung an Tool-Grenzen; keine leeren
    `catch`-Blöcke; `async void` verboten; `.Wait()/.Result` verboten.
  - `out` nur in `Try*`-Methoden. Kein `dynamic`.
  - Ab 5 Parametern: Input-`record`. Parameter in `*Options`-Klassen
    statt Magic Values in Code-Pfaden; jede neue `appsettings.json`-Option
    lückenlos dokumentieren (AppSettingsMigrator).
  - Bezeichner PascalCase, ASCII-only, keine generischen Namen.
  - Doku-/Code-Sprache **Englisch**, Commit-Sprache **Deutsch**.
- **Compiler-Disziplin:** `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
  — neue Compiler-Warnungen sind Build-Fehler.
- **Commit-Konventionen:** Conventional Commits, Deutsch, imperativ,
  Subject ≤ 72 Zeichen, Suffix `[sql-index-suggestions]`. Commits
  entstehen autonom in sinnvollen Abständen (nicht auf Aufforderung
  warten).
- **Doku-Sync-Pflicht:** Bei jeder Code-Änderung müssen
  `docs/architecture-spec.md` und `README.md` ohne Aufforderung
  mitaktualisiert werden. Keine absoluten Pfade in Markdown-Links —
  immer repo-relative Pfade.
- **Test-Pflicht:** xUnit v3 für jede funktionale Änderung; vorhandene
  Tests grün halten. Integrationstest gegen echte Test-DB dort, wo
  Mocks nicht sinnvoll sind (DMV-Verhalten).

## Regel-Index

- `.agents/rules/SqlToAiRichtlinien.mdc` — Projekt-übergreifende
  Architektur-, Sicherheits- und Workflow-Richtlinien (Design-Philosophie,
  Guardrails, PowerShell-only, Doku-Sync-Pflicht, Commit-Disziplin,
  AppSettings-Konvention, AiNetLinter-Hinweis).
- `.agents/rules/AiNetLinter.mdc` — C#-Linter-Vorgaben mit konkreten
  Grenzwerten (MaxLineCount, MaxMethodLineCount, sealed, Result-Pattern,
  Naming, Nullable-Enable, async-Regeln, Architecture-Mapping,
  test-coverage-Sentinel).

## Epics

- [x] EPIC-01: Parser-Erweiterung in `sql_measure_performance` für
      vollständige `CREATE NONCLUSTERED INDEX`-Statements
      — Idee 1 aus `konzept.md` (Muss-Haven, §Wie-Idee-1, §DoD).
      Bisher liefert die XML-Plan-Auswertung pro `MissingIndex` nur
      Tabelle + Impact%; künftig soll daraus ein direkt ausführbares
      DDL-Statement (Equality/Inequality/Include-Spalten) als
      Bestandteil der Warnung zusammengesetzt werden, sodass ein
      Agent die Empfehlung 1:1 weiterverwenden kann. Bestehende
      `sql_measure_performance`-Tests müssen grün bleiben; neue
      Testfälle decken Equality-only, Equality+Inequality, mit/ohne
      Include ab. **Inkl. Doku-Sync für Idee 1:** Tool-Eintrag §4
      Nr. 14 in `architecture-spec.md` (erweiterte
      `PerformancePlanWarning`-Struktur) und das
      `sql_measure_performance`-Feature-Bullet in `README.md`.
      → **umgesetzt in step-001** (`verdict: approved`, 2026-08-04, Commit `86c0e48`).
      Beobachteter Restbedarf: TD-001 (Konzept-Beispiel zeigt
      `IX_Orders_CustomerId_OrderDate` mit durchgehenden einfachen
      Unterstrichen, Implementierung verwendet `IX_Orders_CustomerId__OrderDate`
      — Doku-Harmonisierung an Konzept oder Code), TD-002 (`DESC`-Sortierung
      in `ColumnGroup` wird ignoriert), TD-003 (Generalisierung
      `IsShowplanPermissionError` für EPIC-02 relevant).
      **Diese drei Tech-Debt-Einträge sind Beobachtungen, keine
      Pflicht-Findings — kein impliziter Nachzug in EPIC-02.**

- [x] EPIC-02: Neues Tool `sql_suggest_indexes` — serverweit
      kumulierte DMV-Index-Empfehlungen mit Graceful Degradation
      — Idee 2 aus `konzept.md` (Muss-Haben, §Permission-Handling,
      §Wie-Idee-2, §DoD).
      Neuer Service nach dem Muster bestehender DMV-Tools
      (Dapper/`SqlClient`-Zugriff, `Result<T>`-Rückgabe, Markdown-Output,
      Tool-Definition in `ToolRegistry`, Dispatch in `ToolDispatcher`,
      Konstanten in `McpConstants`); Abfrage von
      `sys.dm_db_missing_index_details` +
      `sys.dm_db_missing_index_group_stats` (CTE-basiert, Top-N auf
      `index_handle`-Ebene, siehe `fix-01`), Berechnung des
      `improvement_score`, Filter/Top-Limit über die Parameter
      `database` (Pflicht), `table_name`, `min_score`, `top` (Default
      10). Pflichtbestandteil der Ausgabe: Restart-Hinweis (DMV
      akkumuliert seit letztem Server-Neustart). Bei fehlender
      `VIEW SERVER STATE`: Permission-Fehler analog zum
      `SHOWPLAN`-Pattern abfangen, strukturierte Notiz statt
      Hard-Error. Testabdeckung: Unit-Tests (12 Tests, mit Mocks) +
      Integrationstest gegen echte Test-DB in
      `tests/SqlToAi.Tests/Integration/`, da DMV-Verhalten nicht
      sinnvoll mockbar ist. **Inkl. Doku-Sync für Idee 2:** neuer
      Tool-Eintrag §4 Nr. 16 in `architecture-spec.md` (Commit
      `50437e2`), `VIEW SERVER STATE` in §H, sowie das
      `sql_suggest_indexes`-Feature-Bullet und die Tool-Zählung
      (15 → 16) in `README.md` — allesamt bereits in `step-002`
      umgesetzt.
      → **umgesetzt in step-002 + step-002/fix-01** (`verdict:
      approved`, 2026-08-04, Commits `3195a17` Code + `50437e2` Doku
      bzw. `bc488ec` Code + `1a412cb` Result für `fix-01`).
      Code, Doku und Unit-Tests vollständig; CTE-Korrektur
      (Top-N pro `index_handle`) verifiziert.
      → **Integrationstest abgeschlossen in step-003** (Reopen, da der
      erste Lauf einen CTE-Alias-Bug und SQL-Server-2025-Inkompatibilitäten
      aufgedeckt hat; Reopen-Code-Commit `0348e9d`, Reopen-Doku-Commit
      `630f0ce`; final `verdict: approved`, 2026-08-05, 522/522 Tests
      grün inkl. 4/4 Integration-Tests gegen reale Test-DB).
      **EPIC-02 abgeschlossen.**
      Beobachteter Restbedarf: TD-001 (Konzept-Index-Format-Harmonisierung,
      offen), TD-002 (`DESC`-Sortierung in `ColumnGroup`, offen),
      TD-004 (SQL-Server-Mindestversion für `IndexSuggestionService`-CTE
      ist 2025, nicht abwärtskompatibel — architecture-spec-Eintrag
      wäre ergänzend, offen), TD-005 (`GRANT VIEW SERVER STATE TO [Agent]`
      nicht reproduzierbar dokumentiert, offen), TD-006 (Test 1 sollte
      Graceful-Degradation-Pfad akzeptieren, offen), TD-007
      (`DmvMockConnectionFactory` deckt SQL-Syntaxfehler nicht ab,
      systemischer Test-Coverage-Gap, offen).
      **Diese Tech-Debt-Einträge sind Beobachtungen, keine
      Pflicht-Findings — kein impliziter Nachzug in weitere Schritte.**

- [x] EPIC-03: Post-Completion Tech-Debt Cleanup
      (Reopen-Auftrag vom Nutzer nach Task-Abschluss)
      — `task-summary.md` final_status `done`, alle 4 Steps approved,
      522/522 Tests grün. Der Nutzer hat 2026-08-05 angeordnet, dass
      die in `tech-debt.md` gesammelten Tech-Debts nachgegangen wird,
      mit folgender Policy: **in-scope** (aus `konzept.md` ableitbar) →
      fixen, **out-of-scope** (Konzept schweigt / Architektur / Test-
      Strategie / Setup-Fragen) → explizit als „out of scope, won't
      fix in diesem Task" markiert. Klassifizierung wurde in der
      Orchestrator-Befragung 2026-08-05 vorab abgestimmt:
      TD-001 in-scope, TD-002/004/005/006/007 out-of-scope. TD-003
      bereits in `step-002` erledigt.
      → **umgesetzt in step-004** (`verdict: approved`, 2026-08-05,
      Commits `651c526` Code/Doku + `7c92a3a` Result): Konzept-Beispiel
      in `konzept.md` Zeile 172 an implementierte Form
      `IX_Orders_CustomerId__OrderDate` angepasst (TD-001 erledigt);
      `tech-debt.md` Status-Updates für alle 6 noch offenen Einträge
      vorgenommen (TD-001 erledigt, TD-002/004/005/006/007 explizit als
      out-of-scope markiert); `task-summary.md` um den Post-Completion-
      Abschnitt ergänzt. **Kein Code-Change, kein Test-Change**,
      522/522 Tests grün bleiben. Risiko `low`, `step_type: single`.
      **EPIC-03 abgeschlossen.**

- [x] EPIC-04: Post-Completion Tech-Debt Cleanup Round 2
      (Nutzer-Anordnung 2026-08-05 nach EPIC-03-Abschluss)
      — Nach EPIC-03 hat der Nutzer am 2026-08-05 eine neue Direktive
      erteilt: die verbleibenden Tech-Debts in `tech-debt.md` adressieren
      nach der Policy **„tech debt soll nur beinhalten was wirklich offen
      ist"**. Klassifizierung:
      - **TD-002** (DESC-Sortierung in `ColumnGroup`): umsetzen
      - **TD-004** (SQL-2019/2022-Syntax): umsetzen, minimal unterstützte
        SQL-Server-Version ist **2019** (per Nutzer-Vorgabe)
      - **TD-005** (Test-Environment-Setup `GRANT VIEW SERVER STATE`):
        grundsätzlich nicht machen
      - **TD-006** (Test 1 Graceful-Toleranz): umsetzen
      - **TD-007** (`DmvMockConnectionFactory` SQL-Validierung): skippen,
        gleicher Status wie TD-005 (grundsätzlich nicht)
      TD-001 und TD-003 (beide bereits erledigt) sowie TD-005 und TD-007
      (grundsätzlich nicht) werden aus `tech-debt.md` entfernt; nur
      TD-002, TD-004, TD-006 bleiben als offene Items.
      Umsetzung in **drei separaten Steps** (einer pro TD):
      - **step-005** (TD-002, EPIC-04) — `DESC`-Sortierung in
        `BuildCreateIndexStatement` (`PerformanceMeasurementService.cs:373`)
        + neue Tests; reine Code-/Test-Änderung in einer Datei, klein,
        risiko `low`.
      - **step-006** (TD-004, EPIC-04) — SQL-2019/2022-Syntax in
        `IndexSuggestionService.LoadSuggestionsAsync` (Zeile 140-186)
        zurückführen: `migs.index_group_handle` statt `group_handle`,
        `INNER JOIN sys.dm_db_missing_index_columns ... ON
        mic.index_handle` statt `CROSS APPLY ... (TVF)`. Annahme
        explizit dokumentiert: SQL 2025 behält alte Spalten-Namen
        als Aliases. + neue Tests, die die 2019/2022-Syntax validieren;
        Code-/Test-Änderung in einer Datei, risiko `low`.
      - **step-007** (TD-006, EPIC-04) — Test 1 in
        `IndexSuggestionServiceIntegrationTests.cs:26-42` um den
        Graceful-Degradation-Pfad erweitern (analog Test 4); reine
        Test-Änderung, risiko `low`.
      Nach jedem `approved`-Verdict: Eintrag aus `tech-debt.md` entfernen
      (per neuer Policy). Nach allen drei `approved`-Verdikten:
      `tech-debt.md` ist leer, `task-summary.md` mit
      „Post-Completion-Tech-Debt-Cleanup Round 2"-Abschnitt ergänzen,
      `task-state.md` Status auf `done` zurücksetzen.
      → **TD-002 in step-005 abgeschlossen** (`verdict: approved`,
      2026-08-05, Commit `a1492c6`); Eintrag aus `tech-debt.md` entfernt.
      → **TD-004 als „won't fix" geschlossen** (Nutzer-Entscheidung
      2026-08-05, nach zwei gescheiterten Versuchen): step-006 (feste
      2019/2022-Syntax, Commit `2011331`) scheiterte, weil SQL Server
      2025 die alten Spaltennamen entgegen der Annahme NICHT als Alias
      führt. step-006/fix-01 (versionsabhängige Query-Auswahl über
      `connection.ServerVersion`, Commit `75fb296`) scheiterte
      ebenfalls — die reale Test-Instanz meldet sich als SQL Server
      2022 (Hauptversion 16), verwendet aber bereits das
      2025-DMV-Spaltenschema, wodurch jede versionsnummernbasierte
      Erkennung strukturell versagt. Ein tragfähiger Fix (Try/Catch-
      Fallback oder Schema-Introspektion) wäre technisch machbar,
      aber gegen keine echte SQL-Server-2019/2022-Instanz verifizierbar
      — Nutzer hat entschieden, das nicht weiterzuverfolgen. Code per
      Revert-Commit `09fa038` auf den zuletzt bekannt funktionierenden
      Stand (SQL-Server-2025-spezifische Syntax, wie seit step-003)
      zurückgesetzt; alle 4 Integrationstests wieder grün. Details:
      `step-006/step-result.md`, `step-006/fix-01/step-result.md`.
      → **TD-006 in step-007 abgeschlossen** (`verdict: approved`,
      2026-08-05, Commit `0a71e9b`): Test 1 in
      `IndexSuggestionServiceIntegrationTests.cs` akzeptiert jetzt
      analog zu Test 4 auch den Graceful-Degradation-Pfad; Eintrag aus
      `tech-debt.md` entfernt.
      **EPIC-04 abgeschlossen.** `tech-debt.md` ist damit leer (alle
      Einträge TD-001–TD-007 sind entweder erledigt oder bewusst als
      „won't fix"/„grundsätzlich nicht" geschlossen und aus der Datei
      entfernt, Volltexte in der Git-Historie).
