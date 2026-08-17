---
status: active
task: sql-parser-refactoring
derived_from: konzept.md
created_at: "2026-08-17T16:26:30+02:00"
last_updated: "2026-08-17T16:26:30+02:00"
created_by_model: gemini-3.7-flash
created_by_model_knowledge_cutoff: "2026-01"
---

# Roadmap: sql-parser-refactoring

Grober Anker, kein Detailplan — Detail-Steps entstehen erst JIT im Step-Modus des Planers, siehe `../spec.md` §7.2. Diese Datei wird laufend angepasst (Epics abgehakt, ergänzt, umformuliert oder als obsolet markiert) — kein starres Vorab-Dokument.

## Tech-Stack-Notiz

- **Build-Command:** `dotnet build`
- **Test-Command:** `dotnet test`
- **Lint-Command:** `ainetlinter` MCP / `dotnet test` (Zero-Warning-Direktive & AiNetLinter Clean-Check)
- **Code-Style-Kurzfassung:** C# 14 / .NET 10, `#nullable enable`, `sealed` für konkrete Klassen, flache Methoden (≤60 Zeilen), keine magischen Werte (AppSettings), Result-Pattern, xUnit v3.
- **Commit-Konventionen:** Conventional Commits (z. B. `feat:`, `fix:`, `refactor:`, `chore:`, `docs:`), Deutsch, Imperativ, Suffix `[sql-parser-refactoring]`

## Regel-Index

- `.agents/rules/SqlToAiRichtlinien.mdc` — Entwicklungs-, Sicherheits- (Safety-by-Design, Read-Only Guard, Anonymisierung) und Dokumentationsrichtlinien für SqlToAi (.NET 10, C# 14).
- `.agents/rules/AiNetLinter.mdc` — C#-Codequalitäts- und Linter-Grenzwerte (sealed, Methoden- und Dateilängen, Komplexität, Nullable, keine magischen Werte).

## Epics

- [ ] EPIC-01: NuGet-Dependency ScriptDom & TSql150Parser Helper — `Microsoft.SqlServer.TransactSql.ScriptDom` in `SqlToAi.csproj` einbinden und gemeinsamen Parser-Helper/Infrastruktur bereitstellen. (Bezug: `konzept.md` §Muss-Haben)
- [ ] EPIC-02: SqlMultiStatementDetector auf ScriptDom umstellen — AST-basierte Batch-/Statement-Auswertung (`TSqlScript.Batches[].Statements`) statt Semikolon-Zählung; Preamble-Erweiterung (`SET`, `USE`, `DECLARE`). (Bezug: `konzept.md` §Muss-Haben)
- [ ] EPIC-03: ReadOnlyGuard auf ScriptDom AST-Visitor umstellen — `TSqlFragmentVisitor` (`DmlStatement`, `DDLStatement`, `AlterStatement`, etc.) statt Keyword-Regex; Edge-Cases wie `EXECUTE AS`, `SELECT INTO` vs. `INSERT INTO`. (Bezug: `konzept.md` §Muss-Haben)
- [ ] EPIC-04: QueryDeconstructor auf ScriptDom umstellen — AST-Navigation (`SelectStatement.WithCtesAndXmlNamespaces`) statt `StartsWith("WITH")` und String-Scanning. (Bezug: `konzept.md` §Muss-Haben)
- [ ] EPIC-05: Doku-Synchronisation & Gesamtabnahme — `docs/architecture-spec.md` und `README.md` mit den neuen AST-Parser-Details synchronisieren; Gesamttestnetz validieren. (Bezug: `konzept.md` §Definition of Done)
