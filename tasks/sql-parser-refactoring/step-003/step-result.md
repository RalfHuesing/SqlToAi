---
status: done
type: step-result
task: sql-parser-refactoring
step: "003"
epic: EPIC-03
step_type: single
coded_by: coder
coded_by_model: gemini-3.7-flash
coded_by_model_knowledge_cutoff: "2026-01"
coded_at: "2026-08-17T16:38:25+02:00"
code_commit_hash: a6d5280
status_after: done
blocker_category: n/a
---

# Result Step 003: ReadOnlyGuard auf ScriptDom AST-Visitor umstellen

## Zusammenfassung

`ReadOnlyGuard` wurde von Regex-Keyword-Matching und Kommentar-/Literal-Stripping auf einen AST-Visitor (`ReadOnlyStatementVisitor`) via `SqlScriptDomParser` umgestellt. Mutierende DML-, DDL-, Stored-Procedure- (`EXEC`, `sp_executesql`), Security- und Administrationskonstrukte werden typgenau über eine zentrale Typregistrierung erkannt. Sichere Bracket-Identifier (`SELECT [insert] FROM t`) sowie `EXECUTE AS` werden als Read-Only validiert, während `SELECT ... INTO` und mutierende Operationen deterministisch blockiert werden.

## Geänderte Dateien

- `src/SqlToAi/Security/ReadOnlyGuard.cs` — Umstellung auf AST-Visitor mit Registrierungs-Lookup und Lexer-Token-Fallback bei Syntax-Fehlern.
- `tests/SqlToAi.Tests/Security/ReadOnlyGuardTests.cs` — Testsuite mit erweiterten Fällen für sichere Identifier, `EXECUTE AS`, `SELECT INTO`, DDL und Security-Befehle aktualisiert.

## Commit

- **Code-Commit-Hash:** `a6d5280`
- **Message:**
  ```
  refactor(security): ReadOnlyGuard auf AST [sql-parser-refactoring]

  Refs: tasks/sql-parser-refactoring/step-003
  ```
- **Branch:** main
- **Push:** nein (lokal)

## Build-/Test-Output

```
dotnet build → grün
dotnet test  → grün (552 Tests, 0 Fehler)
```

## Abweichungen vom Plan

Keine — Plan 1:1 umgesetzt.

## Beobachtungen

Regex-Matches und `SqlCharScanner` in `ReadOnlyGuard` wurden vollständig entfernt.

## Bekannte Unschärfen

Keine.
