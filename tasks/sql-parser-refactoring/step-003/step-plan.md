---
status: open
type: step-plan
task: sql-parser-refactoring
step: "003"
corrects: null
title: "ReadOnlyGuard auf ScriptDom AST-Visitor umstellen"
epic: EPIC-03
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: gemini-3.7-flash
created_by_model_knowledge_cutoff: "2026-01"
created_at: "2026-08-17T16:33:40+02:00"
related_to: [step-001]
---

# Step 003: ReadOnlyGuard auf ScriptDom AST-Visitor umstellen

## Bezug

- **Task:** `sql-parser-refactoring`
- **Epic:** `EPIC-03` aus `roadmap.md` — ReadOnlyGuard auf ScriptDom AST-Visitor umstellen
- **Konzept-Referenz:** `konzept.md` §Scope, §Muss-Haben und §Wie (ReadOnlyGuard ersetzen)

## Aktueller Projektzustand (JIT-Kontext)

- `ReadOnlyGuard` verwendet bisher `Regex` auf vorkomprimiertem SQL (nach Kommentar-/Literal-Strip mit `SqlCharScanner`).
- Dadurch wurden harmlose Identifier wie `SELECT [insert] FROM t` oder `EXECUTE AS` fälschlicherweise als mutierend erkannt bzw. erforderten komplexe Regex-Sonderbehandlungen.
- `SqlScriptDomParser` ist vorhanden und liefert standardkonforme T-SQL ASTs.

## Intention

`ReadOnlyGuard` vollständig auf einen `TSqlFragmentVisitor` umstellen.
Der Visitor traversiert den AST und weist alle DML-Modifikationen (`InsertStatement`, `UpdateStatement`, `DeleteStatement`, `MergeStatement`), DDL-Befehle (`Create*`, `Alter*`, `Drop*`, `TruncateTableStatement`), `SELECT ... INTO`, Stored-Procedure-Aufrufe (`ExecuteStatement`, `ExecuteSpecification`, `sp_executesql`) sowie administrative Kommandos (`SecurityStatement`, `BackupStatement`, `DbccStatement` etc.) deterministisch ab.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Security/ReadOnlyGuard.cs`

- **Was:** Ersetzen von `MutatingKeywordsRegex` und `StripCommentsAndStringLiterals` durch `TSqlFragmentVisitor` (z. B. private statische / innere Visitor-Klasse), die vom `ReadOnlyGuard` auf den durch `SqlScriptDomParser.Parse(query)` erzeugten AST angewendet wird.
- **Warum:** AST-basierte Typsicherheit zur vollständigen Eliminierung von Regex-False-Positives und Erkennung mutierender Konstrukte in beliebigen Tiefen (Subqueries, CTEs).

### Datei 2: `tests/SqlToAi.Tests/Security/ReadOnlyGuardTests.cs`

- **Was:** Aktualisierung und Erweiterung der Testsuite:
  - Validierung von sicheren Bracket-Identifiern (`SELECT [insert] FROM t`, `SELECT [drop] FROM t`) als Safe.
  - Validierung von `EXECUTE AS USER = 'someone'` als Safe.
  - Validierung von `SELECT ... INTO` als Mutating (Unsafe).
  - Alle bestehenden echten Mutating- und Read-Only-Tests validieren.
- **Warum:** Sicherstellen, dass das Sicherheitsnetz intakt bleibt und die False-Positive-Beseitigung getestet ist.

## Tests

- [ ] `ReadOnlyGuardTests.IsQuerySafe_ShouldReturnTrue_ForSafeQueries` (inkl. `SELECT [insert]`, `EXECUTE AS`)
- [ ] `ReadOnlyGuardTests.IsQuerySafe_ShouldReturnFalse_ForMutatingQueries` (DML, DDL, SELECT INTO, EXEC, sp_executesql)
- [ ] `ReadOnlyGuardTests.IsQuerySafe_ShouldReturnFalse_ForEmptyOrNullQuery`

## Definition of Done

- [ ] `ReadOnlyGuard` nutzt `TSqlFragmentVisitor` und `SqlScriptDomParser`
- [ ] Keine Regex-/String-Heuristiken mehr in `ReadOnlyGuard`
- [ ] Alle Tests in `ReadOnlyGuardTests` sind grün
- [ ] `dotnet build` und `dotnet test` grün (Zero-Warnings)
- [ ] Code- und Doku-Commits mit Suffix `[sql-parser-refactoring]` erstellt
- [ ] `step-003/step-result.md` geschrieben und Status aktualisiert

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#2` — Mehrstufiger Schreibschutz (Read-Only Guard)
- `.agents/rules/AiNetLinter.mdc#Kurz-Stil` — `#nullable enable`, `sealed` Klassen, Methoden-Längen ≤ 60
