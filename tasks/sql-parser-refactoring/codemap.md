---
task: sql-parser-refactoring
type: codemap
maintained_by: planer, coder, kritiker
last_updated: "2026-08-17T16:26:30+02:00"
---

# CodeMap: sql-parser-refactoring

Task-scoped Landkarte — existiert nur für diesen Task, wird mit `<task-dir>` gelöscht, kein projektweites Artefakt. Enthält nur, was für diesen Task relevant ist.

## Pflege — wer trägt wann ein

- **Planer, Roadmap-Modus (einmalig):** befüllt die Map initial aus dem Grobüberblick.
- **Coder (jeder Step):** ergänzt/aktualisiert Einträge für tatsächlich angelegte oder geänderte Module vor dem Doku-Commit.
- **Planer, Step-Modus (jeder Step):** liest die Map vor dem Planen, Anti-Loop-Check.
- **Kritiker:** prüft stichprobenartig, ob die Map dem tatsächlichen Diff entspricht.

## Anti-Loop-Nutzen

Bevor der Planer im Step-Modus einen neuen Step plant, gleicht er sein Vorhaben gegen die hier verzeichneten Entscheidungen ab.

## Karte

- **`src/SqlToAi/SqlToAi.csproj`** — Projektdatei mit Paket-Referenzen (`Microsoft.SqlServer.TransactSql.ScriptDom`). (zuletzt: step-001)
- **`src/SqlToAi/Database/SqlScriptDomParser.cs`** — Zentraler AST-Parser-Helper für TSql150Parser und ScriptDom-Infrastruktur. (neu in step-001)
- **`src/SqlToAi/Security/ReadOnlyGuard.cs`** — Read-Only Guard Validierung zur Blockierung mutierender SQL-Befehle via AST-Visitor. (zuletzt: step-003)
- **`src/SqlToAi/Security/IReadOnlyGuard.cs`** — Interface für den Read-Only Guard. (initial)
- **`src/SqlToAi/Database/SqlMultiStatementDetector.cs`** — Erkennung von Multi-Statement-Batches und Preamble (`DECLARE`, `SET`, `USE`) via AST. (zuletzt: step-002)
- **`src/SqlToAi/Database/QueryDeconstructor.cs`** — Zerlegung von Queries in Preamble, CTEs (`WITH`) und Haupt-SELECT via AST-Navigation. (zuletzt: step-004)
- **`src/SqlToAi/Database/SqlCharScanner.cs`** — Zeichenweise State-Machine für Quotes, Kommentare, Brackets (bleibt für Anonymisierung unverändert). (initial)
- **`src/SqlToAi/Database/SqlLiteralScanner.cs`** — Literal-Scanner für Token-Substitution (bleibt unverändert). (initial)
- **`tests/SqlToAi.Tests/Security/ReadOnlyGuardTests.cs`** — Testsuite für ReadOnlyGuard inklusive Mutations- und Edge-Case-Tests. (initial)
- **`tests/SqlToAi.Tests/Database/SqlMultiStatementDetectorTests.cs`** — Testsuite für SqlMultiStatementDetector. (initial)
- **`tests/SqlToAi.Tests/Database/QueryDeconstructorTests.cs`** — Testsuite für QueryDeconstructor. (initial)
- **`docs/architecture-spec.md`** — Architekturspezifikation des Projekts (Read-Only Guard AST-Doku aktualisiert). (zuletzt: step-005)
- **`README.md`** — Hauptdokumentation des Projekts (Read-Only Guard AST-Doku aktualisiert). (zuletzt: step-005)
