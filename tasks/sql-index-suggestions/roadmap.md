---
status: active
task: sql-index-suggestions
derived_from: konzept.md
created_at: 2026-08-04T12:00:00+02:00
last_updated: 2026-08-04T12:00:00+02:00
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

- [ ] EPIC-02: Neues Tool `sql_suggest_indexes` — serverweit
      kumulierte DMV-Index-Empfehlungen mit Graceful Degradation
      — Idee 2 aus `konzept.md` (Muss-Haven, §Permission-Handling,
      §Wie-Idee-2, §DoD).
      Neuer Service nach dem Muster bestehender DMV-Tools
      (Dapper/`SqlClient`-Zugriff, `Result<T>`-Rückgabe, Markdown-Output,
      Tool-Definition in `ToolRegistry`, Dispatch in `ToolDispatcher`,
      Konstanten in `McpConstants`); Abfrage von
      `sys.dm_db_missing_index_details` +
      `sys.dm_db_missing_index_group_stats`, Berechnung des
      `improvement_score`, Filter/Top-Limit über die Parameter
      `database` (Pflicht), `table_name`, `min_score`, `top` (Default
      10). Pflichtbestandteil der Ausgabe: Restart-Hinweis (DMV
      akkumuliert seit letztem Server-Neustart). Bei fehlender
      `VIEW SERVER STATE`: Permission-Fehler analog zum
      `SHOWPLAN`-Pattern abfangen, strukturierte Notiz statt
      Hard-Error. Testabdeckung: Unit-Tests (mit Mocks) +
      Integrationstest gegen echte Test-DB in
      `tests/SqlToAi.Tests/Integration/`, da DMV-Verhalten nicht
      sinnvoll mockbar ist. **Inkl. Doku-Sync für Idee 2:** neuer
      Tool-Eintrag §4 Nr. 16 in `architecture-spec.md`,
      `VIEW SERVER STATE` in §H, sowie das `sql_suggest_indexes`-
      Feature-Bullet und die Tool-Zählung (15 → 16) in `README.md`.
      *(in Arbeit → step-002: Service `IIndexSuggestionService` +
      Dapper-Abfragen + Markdown-Renderer + Tool-Definition +
      Dispatch + DI-Registrierung + Unit-Tests mit Mocks;
      noch offen für step-003: Doku-Sync in
      `architecture-spec.md` §4 Nr. 16 / §H und in `README.md`
      sowie Integrationstest gegen echte Test-DB in
      `tests/SqlToAi.Tests/Integration/`.)*
