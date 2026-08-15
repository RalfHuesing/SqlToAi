---
status: active  # active | done
task: audit-try-magicvalues
derived_from: konzept.md
created_at: 2026-08-15T21:38:00+02:00
last_updated: 2026-08-15T21:38:00+02:00
created_by_model: MiniMax-M3
created_by_model_knowledge_cutoff: 2026-01
---

# Roadmap: audit-try-magicvalues

Grober Anker, kein Detailplan — Detail-Steps entstehen erst JIT im
Step-Modus des Planers, siehe `../spec.md` §7.2. Diese Datei wird
laufend angepasst (Epics abgehakt, ergänzt, umformuliert oder als
obsolet markiert) — kein starres Vorab-Dokument.

## Tech-Stack-Notiz

- **Build-Command:** `dotnet build` (oder `dotnet build SqlToAi.slnx`)
- **Test-Command:** `dotnet test` (xUnit v3)
- **Lint-Command:** AiNetLinter (siehe `.agents/rules/AiNetLinter.mdc`); `RunLinterShouldBeClean` per `Assert.Skip`, falls `AiNetLinter.exe` fehlt
- **Code-Style-Kurzfassung:**
  - `sealed` für konkrete Klassen, Methoden ≤ 60 Zeilen (≤ 100 in `*.Tests`)
  - `#nullable enable` am Dateianfang, `#region`/`#endregion` vermeiden
  - Result-Pattern (`Result<T>`, `SqlToAiError`-Katalog) für Fehlerbehandlung
  - Klassen-Footprint ≤ 2500 transitive Zeilen eigener Typen
  - Keine `async void` (außer Event-Handler), kein leeres `catch`, kein `dynamic`
  - Constructor-Dependencies ≤ 5; ≤ 4 Methodenparameter, sonst Input-`record`
  - PascalCase, ASCII-Identifiers, `record`/`readonly struct` für `*ValueObject`
- **Commit-Konventionen:** Conventional Commits (z. B. `feat:`, `fix:`, `refactor:`, `chore:`, `build:`, `docs:`), deutsch, imperativ, Subject ≤ 72 Zeichen, autonome Commits in sinnvollen Abständen
- **Code-Sprache:** Englisch (Klassen-/Methodennamen, XML-Kommentare); Kommunikation/Kommits: Deutsch
- **Doku-Sync-Pflicht:** `docs/architecture-spec.md` + `README.md` synchron halten, repo-relative Markdown-Links (kein `file:///c:/...`)

## Regel-Index

- `.agents/rules/SqlToAiRichtlinien.mdc` — Architektur-, Sicherheits- und Workflow-Richtlinien (Safety-by-Design, Guardrails, Zero-Warning-Direktive, Result-Pattern, AppSettings-Pflicht, Commit-/Sprachregeln, Doku-Sync).
- `.agents/rules/AiNetLinter.mdc` — Automatisch generierte C#-Codequalitäts-Grenzwerte (Sealed-Klassen, Methodenlänge, Cyclomatic/Cognitive Complexity, DuplicateCode, Namespace-Mapping, agent-resilience) inkl. Test-Override (`MaxMethodLineCount = 100`, kein Sealed-Zwang in `*.Tests`).

## Epics

- [x] EPIC-01: Konstanten-Zentralisierung (Phase 1, Quick Wins) — Sämtliche MV-1 bis MV-7-Befunde aus `audit-dry-magicvalues.md` Abschnitt 3 auf benannte Konstanten/Enums umstellen, sodass keine rohen SQL-Fehlercodes, Benchmark-Verdicts, Anonymisierungs-Modi, FNV-1a-Parameter, Gewichtungsfaktoren, Objekttyp-Strings, Regex-Timeouts und `DdlUnavailableNote`-Texte mehr im Code verstreut sind; zusätzlich `OptionalStringParam`-Scheinduplikat und `BuildDetailTool`-Helper-Konsolidierung in `ToolRegistry.cs` umsetzen. Bezug: `konzept.md` §"Muss-Haben" Pkt. 1 sowie `audit-dry-magicvalues.md` DRY-2/DRY-3/DRY-4/MV-1..MV-7. (→ step-001)
- [x] EPIC-02: Guardrail-Pipeline (Phase 2, DRY-1) — Zentralen `IQuerySafetyValidator` / `QuerySafetyValidator` (mit `sealed record QuerySafetyCheckResult`) für die 6-stufige Validierung einführen und die vier Guardrail-Services (`QueryExecutionService`, `QueryValidationService`, `PerformanceMeasurementService`, `QueryComparisonService`) darauf migrieren; redundante Constructor-Dependencies (`ISecurityGuard`, `IAccessLevelProvider`, `IReadOnlyGuard`) in den Services reduzieren und identische Fehlertexte vereinheitlichen. Bezug: `konzept.md` §"Muss-Haben" Pkt. 2 sowie `audit-dry-magicvalues.md` DRY-1. (→ step-002)
- [x] EPIC-03: Test-Suite-Konsolidierung (Phase 3, DRY-T1..T3) — Verstreute Test-Fakes in `TestSupport/` (bzw. `ToolDispatcherTestFakes.cs`) bündeln, `ShowPlanTestHelper` zur Beseitigung der 8 duplizierten ShowPlan-XML-Blöcke in `PerformanceMeasurementServiceTests` einführen und die 33 redundanten Negativ-Guardrail-Tests aus fünf Service-Testklassen in dedizierte `QuerySafetyValidatorTests` konsolidieren. Bezug: `konzept.md` §"Muss-Haben" Pkt. 3 sowie `audit-dry-magicvalues.md` DRY-T1/DRY-T2/DRY-T3. (→ step-003, step-004)
