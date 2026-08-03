---
status: completed
task: sql-optimization-tools
derived_from: konzept.md
created_at: 2026-08-03T10:12:00+02:00
last_updated: 2026-08-03T10:27:30+02:00
created_by_model: gemini-3.6-flash
created_by_model_knowledge_cutoff: 2026-01
---

# Roadmap: sql-optimization-tools

Grober Anker, kein Detailplan — Detail-Steps entstehen erst JIT im Step-Modus des Planers. Diese Datei wird laufend angepasst (Epics abgehakt, ergänzt, umformuliert oder als obsolet markiert).

## Tech-Stack-Notiz

- **Build-Command:** `dotnet build SqlToAi.slnx`
- **Test-Command:** `dotnet test SqlToAi.slnx`
- **Lint-Command:** `dotnet test SqlToAi.slnx` (führt `AiNetLinterTests` zur automatischen Baseline-Prüfung/Aktualisierung aus)
- **Code-Style-Kurzfassung:** C# 14 / .NET 10, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `sealed` Klassen, File-scoped Namespaces, Result-Pattern für Fehlerbehandlung, Dapper & `Microsoft.Data.SqlClient`.
- **Commit-Konventionen:** Conventional Commits (Deutsch, imperativ, z. B. `feat:`, `fix:`, `docs:`, `chore:`) mit Task-Suffix `[sql-optimization-tools]` im Subject.

## Regel-Index

- `.agents/rules/AiNetLinter.mdc` — Linter-Vorgaben für C#/.NET 10 (Sealed Classes, Naming, Nullability, Clean Code).
- `.agents/rules/SqlToAiRichtlinien.mdc` — Architektur-, Sicherheits- & Guardrail-Richtlinien (Read-Only Guard, Parameter-Anonymisierung, MCP-Tools, Dapper, appsettings.json Sync, xUnit v3, docs/ Synchronization).

## Epics

- [x] EPIC-01: Parameter-Support in bestehenden Execution- & Validation-Tools — Nachrüsten typisierter SQL-Parameter (Auto-Detection + DB-Typ Override) in `QueryExecutionService`, `ToolRegistry` & `ToolDispatcher` für `sql_execute_query` & `sql_validate_query` (→ step-001).
- [x] EPIC-02: Ergebnissatz- & Äquivalenzvergleich (`sql_compare_queries`) — Implementierung von DB-seitigem Set-Differenzvergleich (`EXCEPT` / `UNION ALL`), Schema- & Count-Checks sowie kompakter Diff-Ausgabe (→ step-002).
- [x] EPIC-03: Performance- & Plan-Analyse Engine (`sql_measure_performance`) — Messung von Server-Metriken (`STATISTICS IO, TIME`) und XML-Plan-Parsing (`Missing Indexes`, `CONVERT_IMPLICIT`, Table Scans) mit Graceful Degradation (→ step-003).
- [x] EPIC-04: Kombi-Benchmark (`sql_benchmark_optimization`) & Dokumentation — Bereitstellung des kombinierten Benchmark-Tools und Dokumentation benötigter DB-Berechtigungen (`SHOWPLAN`) in `docs/` (→ step-004).
