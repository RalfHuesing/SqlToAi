---
status: done
type: step-result
task: sql-parser-refactoring
step: "001"
epic: EPIC-01
step_type: single
coded_by: coder
coded_by_model: gemini-3.7-flash
coded_by_model_knowledge_cutoff: "2026-01"
coded_at: "2026-08-17T16:29:15+02:00"
code_commit_hash: 702d139
status_after: done
blocker_category: n/a
---

# Result Step 001: ScriptDom NuGet-Paket einbinden und SqlScriptDomParser-Helper erstellen

## Zusammenfassung

`Microsoft.SqlServer.TransactSql.ScriptDom` (Version 170.3.0) wurde via CPM in `Directory.Packages.props` und `SqlToAi.csproj` eingebunden. Die interne statische Hilfsklasse `SqlScriptDomParser` wurde in `SqlToAi.Database` mit Methoden zur Erzeugung und Ausführung des `TSql150Parser` erstellt. Die zugehörigen Unit-Tests in `SqlScriptDomParserTests` verifizieren das Verhalten und sind vollständig grün.

## Geänderte Dateien

- `Directory.Packages.props` — PackageVersion für `Microsoft.SqlServer.TransactSql.ScriptDom` hinzugefügt.
- `src/SqlToAi/SqlToAi.csproj` — PackageReference für `Microsoft.SqlServer.TransactSql.ScriptDom` ergänzt.
- `src/SqlToAi/Database/SqlScriptDomParser.cs` (neu) — Hilfsklasse zur Kapselung von `TSql150Parser`.
- `tests/SqlToAi.Tests/Database/SqlScriptDomParserTests.cs` (neu) — Unit-Tests für `SqlScriptDomParser`.

## Commit

- **Code-Commit-Hash:** `702d139`
- **Message:**
  ```
  feat(database): ScriptDom-Parser einbinden [sql-parser-refactoring]

  Refs: tasks/sql-parser-refactoring/step-001
  ```
- **Branch:** main
- **Push:** nein (lokal)

## Build-/Test-Output

```
dotnet build → grün
dotnet test  → grün (537 Tests, 0 Fehler)
```

## Abweichungen vom Plan

Keine — Plan 1:1 umgesetzt.

## Beobachtungen

`Microsoft.SqlServer.TransactSql.ScriptDom` 170.3.0 enthält die vollständige Hierarchie inklusive `TSql150Parser` und `TSqlFragmentVisitor`.

## Bekannte Unschärfen

Keine.
