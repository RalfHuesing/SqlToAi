---
status: done
type: step-result
task: sql-file-execution
step: 003
epic: EPIC-01
step_type: single
coded_by: coder
coded_by_model: GPT-5
coded_by_model_knowledge_cutoff: not provided by runtime
coded_at: 2026-08-29T08:18:51+02:00
code_commit_hash: aee3abc8bd8d7b7228ed564ca62d7d2c35f64014
status_after: done
blocker_category: n/a
---

# Result Step 003: Local SQL script file intake and encoding contract

## Zusammenfassung

The step adds the internal immutable SQL script file value and a Result-based
reader for validated local paths, size limits, file errors, and the required
UTF-8/UTF-16/Windows-ANSI decoding contract. The configured size limit is
synchronized across options, the factory JSON template, and documentation, and
the three file-error catalog entries are covered by focused tests.

## Geänderte Dateien

- `src/SqlToAi/Database/SqlScriptFile.cs` (neu) — immutable validated script file value.
- `src/SqlToAi/Database/SqlScriptFileReader.cs` (neu) — local path, size, file, and encoding intake boundary.
- `src/SqlToAi/Configuration/SqlToAiOptions.cs` — adds the 10 MB script file-size option.
- `src/SqlToAi/appsettings.json` — adds the factory default for the new option.
- `src/SqlToAi/Domain/SqlToAiError.cs` — adds file-not-found, file-too-large, and invalid-extension catalog entries.
- `Directory.Packages.props` and `src/SqlToAi/SqlToAi.csproj` — adds the direct CodePages dependency and its central version.
- `tests/SqlToAi.Tests/Database/SqlScriptFileReaderTests.cs` (neu) — covers local paths, validation, limits, and all required encodings.
- `tests/SqlToAi.Tests/Configuration/SqlToAiOptionsTests.cs` — covers the default and JSON binding of the file-size option.
- `tests/SqlToAi.Tests/Domain/SqlToAiErrorTests.cs` — covers the three new catalog codes and context.
- `README.md` and `docs/architecture-spec.md` — synchronizes the configuration and error-catalog documentation.

## Commit

- **Code-Commit-Hash:** `aee3abc8bd8d7b7228ed564ca62d7d2c35f64014`
- **Message:**
  ```
  feat(database): Implementiere lokale SQL-Dateiaufnahme [sql-file-execution]

  Refs: tasks/sql-file-execution/step-003
  ```
- **Branch:** main
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit nach diesem Resultat.

## Build-/Test-Output

```
dotnet test tests/SqlToAi.Tests --filter FullyQualifiedName~SqlScriptFileReaderTests → grün (17 Tests, 0 Fehler)
dotnet test tests/SqlToAi.Tests --filter FullyQualifiedName~SqlToAiOptionsTests → grün (7 Tests, 0 Fehler)
dotnet test tests/SqlToAi.Tests --filter FullyQualifiedName~SqlToAiErrorTests → grün (14 Tests, 0 Fehler)
dotnet build SqlToAi.slnx → grün (0 Warnungen, 0 Fehler)
dotnet test SqlToAi.slnx → grün (570 Tests, 0 Fehler, 0 übersprungen; genau einmal vor dem Code-Commit)
```

## Abweichungen vom Plan

Keine in der Umsetzung — der Plan wurde 1:1 innerhalb des vorgesehenen
Produktions-, Test- und Dokumentationsumfangs umgesetzt. Der Step-Plan stand
zu Beginn auf `open`; der Status wurde gemäß Definition of Done auf `done
(pending audit)` gesetzt.

## Beobachtungen

Der .NET-10-SDK meldet die explizit geplante `System.Text.Encoding.CodePages`
PackageReference als NU1510, weil die Assembly im Framework verfügbar ist. Die
Warnung ist deshalb projektbezogen über `NoWarn` unterdrückt; die zentrale
Paketversion und die direkte Referenz bleiben für den dokumentierten Dependency-
Vertrag erhalten.

Während der gezielten Iteration wurden zwei Test-Fixture-Fehler korrigiert:
Der relative Testpfad zeigte zunächst nicht auf das angelegte Temp-Verzeichnis,
und die Teststrings für die Größenkante hatten zunächst nicht die behaupteten
16/17 Bytes. Die abschließenden gezielten Läufe und der vollständige Gate-Lauf
sind grün.

## Bekannte Unschärfen

Keine funktionalen Unschärfen bekannt. Die stabilen Metadatenwerte für erkannte
Encodings sind `UTF-8`, `UTF-16 LE`, `UTF-16 BE` und `Windows-ANSI`.
