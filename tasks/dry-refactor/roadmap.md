---
status: active
task: dry-refactor
derived_from: konzept.md
created_at: 2026-08-15T18:20:00+02:00
last_updated: 2026-08-15T18:20:00+02:00
created_by_model: Gemini 3.7 Flash (High)
created_by_model_knowledge_cutoff: 2026-01
---

# Roadmap: dry-refactor

## Tech-Stack-Notiz

- **Build-Command:** `dotnet build`
- **Test-Command:** `dotnet test`
- **Lint-Command:** `AiNetLinter` MCP / `AiNetLinterTests`
- **Code-Style-Kurzfassung:** C# 14 / .NET 10, Zero-Warning (`<TreatWarningsAsErrors>true`), `sealed` konkrete Klassen, flache Methoden, max. 5 Konstruktor-Abhängigkeiten, max. 15 Public Members pro Typ, keine Magic Values.
- **Commit-Konventionen:** Conventional Commits (`feat:`, `fix:`, `refactor:`, `docs:`), Deutsch, imperativ.

## Regel-Index

- `.agents/rules/SqlToAiRichtlinien.mdc` — Architektur- & Entwicklungsrichtlinien für SqlToAi (Sicherheit, Anonymisierung, Doku, Commits, Baseline).
- `.agents/rules/AiNetLinter.mdc` — C#-Codequalitäts- und Roslyn-Linter-Grenzwerte (MaxLineCount, SealedClasses, DuplicateCode, etc.).

## Epics

- [x] EPIC-01: Baseline-Eliminierung & Zero-Warning-Setup — Löschen der Baseline-Datei, Bereinigung der Linter-Tests und Richtliniendokumente. (→ step-001)
- [x] EPIC-02: Linter-Errors & Core C#-Fixes — `sealed` Ergänzungen (`McpJsonContext`, `FakeDbConnection`) und Parameter-Records in `PerformanceMeasurementService`. (→ step-002)
- [x] EPIC-03: DRY-Konsolidierung (Produktionscode) — Migration von `QueryDeconstructor` und `SqlMultiStatementDetector` auf `SqlCharScanner`, Vereinheitlichung von `ExecuteSetOptionAsync`. (→ step-003)
- [x] EPIC-04: Architektur: Facade & Dispatcher-Entlastung — Einführung von `IDatabaseAnalysisService` / `DatabaseAnalysisServices` zur Entlastung des `ToolDispatcher`. (→ step-004)
- [x] EPIC-05: Test-Infrastruktur & Testklassen-Splits — Zentrale Test-Helper in `TestSupport`, Aufteilung überbreiter Testklassen (`QueryExecutionServiceTests`, `SchemaServiceTests`, `SchemaServiceIntegrationTests`, `ToolDispatcherTests`), `GlobMatcherTests`-Bereinigung. (→ step-005)
- [ ] EPIC-06: Neutralitäts-Audit & Safeguard 10/10 Gate — Neutrale englische Sprache in Docs/Kommentaren, Verifikation aller Tests und Safeguard Score 10.00/10, globaler Kritiker-Review.
