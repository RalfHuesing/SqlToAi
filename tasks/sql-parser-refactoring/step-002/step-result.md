---
status: done
type: step-result
task: sql-parser-refactoring
step: "002"
epic: EPIC-02
step_type: single
coded_by: coder
coded_by_model: gemini-3.7-flash
coded_by_model_knowledge_cutoff: "2026-01"
coded_at: "2026-08-17T16:32:35+02:00"
code_commit_hash: 19da170
status_after: done
blocker_category: n/a
---

# Result Step 002: SqlMultiStatementDetector auf ScriptDom AST umstellen

## Zusammenfassung

`SqlMultiStatementDetector.ContainsMultipleStatements` wurde von String-/Semikolon-Scanning auf AST-Parsing mit `SqlScriptDomParser.ParseScript` umgestellt. Preamble-Befehle (`DECLARE`, `SET`, `USE`, `SET TRANSACTION ISOLATION LEVEL`, `SET NOCOUNT`) werden über T-SQL AST-Typen identifiziert und zählen nicht als Haupt-Statements. Die Testsuite wurde um Edge-Cases für `SET`, `USE` und Batch-Trennungen erweitert und läuft vollständig grün.

## Geänderte Dateien

- `src/SqlToAi/Database/SqlMultiStatementDetector.cs` — Umstellung auf AST-Statement-Erkennung via `SqlScriptDomParser`.
- `src/SqlToAi/Database/SqlScriptDomParser.cs` — `SqlParseResult` und `SqlScriptParseResult` Structs zur Vermeidung von `out`-Parametern.
- `tests/SqlToAi.Tests/Database/SqlMultiStatementDetectorTests.cs` — Erweiterte Tests für `SET`, `USE` und Multi-Batch-Statements.
- `tests/SqlToAi.Tests/Database/SqlScriptDomParserTests.cs` — Tests an neue Result-Signaturen angepasst.

## Commit

- **Code-Commit-Hash:** `19da170`
- **Message:**
  ```
  refactor(database): MultiStatement auf AST [sql-parser-refactoring]

  Refs: tasks/sql-parser-refactoring/step-002
  ```
- **Branch:** main
- **Push:** nein (lokal)

## Build-/Test-Output

```
dotnet build → grün
dotnet test  → grün (545 Tests, 0 Fehler)
```

## Abweichungen vom Plan

Keine — Plan 1:1 umgesetzt.

## Beobachtungen

`SqlCharScanner` wird in `SqlMultiStatementDetector` nicht mehr benötigt.

## Bekannte Unschärfen

Keine.
