---
status: done
type: step-result
task: sql-parser-refactoring
step: "004"
epic: EPIC-04
step_type: single
coded_by: coder
coded_by_model: gemini-3.7-flash
coded_by_model_knowledge_cutoff: "2026-01"
coded_at: "2026-08-17T16:40:50+02:00"
code_commit_hash: e1f158c
status_after: done
blocker_category: n/a
---

# Result Step 004: QueryDeconstructor auf ScriptDom AST-Navigation umstellen

## Zusammenfassung

`QueryDeconstructor` wurde von String-Scanning (`StartsWith("WITH")`, Klammerntiefen-Zählung mit `SqlCharScanner`) auf AST-Navigation mit `SqlScriptDomParser.ParseScript` umgestellt. Preambles (`DECLARE`, `SET`) werden aus den AST-Statements vor dem Haupt-Statement extrahiert, CTE-Klauseln via `SelectStatement.WithCtesAndXmlNamespaces` und der Haupt-SELECT aus den verbleibenden Token-Offsets. Die Testsuite wurde um komplexe CTE- und Preamble-Fälle erweitert und läuft vollständig grün.

## Geänderte Dateien

- `src/SqlToAi/Database/QueryDeconstructor.cs` — Umstellung auf AST-Navigation und Bereinigung alter String-Scanner-Methoden.
- `tests/SqlToAi.Tests/Database/QueryDeconstructorTests.cs` — Erweiterte Tests für verschachtelte CTEs, Semicolon-Trimming und Preambles.

## Commit

- **Code-Commit-Hash:** `e1f158c`
- **Message:**
  ```
  refactor(database): QueryDeconstructor auf AST [sql-parser-refactoring]

  Refs: tasks/sql-parser-refactoring/step-004
  ```
- **Branch:** main
- **Push:** nein (lokal)

## Build-/Test-Output

```
dotnet build → grün
dotnet test  → grün (556 Tests, 0 Fehler)
```

## Abweichungen vom Plan

Keine — Plan 1:1 umgesetzt.

## Beobachtungen

`SqlCharScanner` wird in `QueryDeconstructor` nicht mehr benötigt.

## Bekannte Unschärfen

Keine.
