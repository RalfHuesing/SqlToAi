---
status: done (pending audit)
type: step-plan
task: sql-parser-refactoring
step: "002"
corrects: null
title: "SqlMultiStatementDetector auf ScriptDom AST umstellen"
epic: EPIC-02
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: gemini-3.7-flash
created_by_model_knowledge_cutoff: "2026-01"
created_at: "2026-08-17T16:30:15+02:00"
related_to: [step-001]
---

# Step 002: SqlMultiStatementDetector auf ScriptDom AST umstellen

## Bezug

- **Task:** `sql-parser-refactoring`
- **Epic:** `EPIC-02` aus `roadmap.md` — SqlMultiStatementDetector auf ScriptDom umstellen
- **Konzept-Referenz:** `konzept.md` §Scope, §Muss-Haben und §Wie (SqlMultiStatementDetector ersetzen)

## Aktueller Projektzustand (JIT-Kontext)

- `SqlScriptDomParser` wurde in Step 001 bereitgestellt und getestet.
- `SqlMultiStatementDetector` nutzt bisher eine String- und Semikolon-basierte Heuristik über `SqlCharScanner`.
- Bisher werden nur `DECLARE`-Statements als Preamble erkannt, nicht jedoch `SET`-Befehle (z. B. `SET NOCOUNT ON`, `SET @var = ...`) oder `USE`-Statements.

## Intention

Die Implementierung von `SqlMultiStatementDetector.ContainsMultipleStatements` vollständig auf den ScriptDom-AST umstellen (`SqlScriptDomParser.ParseScript`).
Preamble-Statements (`DeclareVariableStatement`, `SetVariableStatement`, `PredicateSetStatement`, `UseStatement`, etc.) zählen nicht als eigenständige Haupt-Statements; Batches/Skripte mit mehr als einem nicht-Preamble-Statement werden als Multi-Statement identifiziert.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Database/SqlMultiStatementDetector.cs`

- **Was:** Ersetzen der `SqlCharScanner`-basierten Semikolon-Logik durch Auswertung von `SqlScriptDomParser.ParseScript(query, out _)`. Zählen aller Nicht-Preamble-Statements über alle Batches hinweg.
- **Warum:** Beseitigung von String-Parsing-Fragilität und Unterstützung aller regulären T-SQL Preamble-Befehle (`DECLARE`, `SET`, `USE`).

### Datei 2: `tests/SqlToAi.Tests/Database/SqlMultiStatementDetectorTests.cs`

- **Was:** Bestehende Tests beibehalten und neue Test-Cases für Preamble-Muster (`SET @x = 1`, `SET NOCOUNT ON`, `USE [MyDb]`, gemischte Batches) ergänzen.
- **Warum:** Sicherstellen, dass neue Preamble-Konstrukte als Single-Statement-kompatibel eingestuft werden und Multi-Statements sicher erkannt werden.

## Tests

- [ ] `SingleStatement_ReturnsFalse` (bestehende Tests grün)
- [ ] `MultipleMainStatements_ReturnsTrue` (bestehende Tests grün)
- [ ] `DeclareStatementsPrecedingSingleQuery_ReturnsFalse` (bestehende Tests grün)
- [ ] `SetAndUseStatementsPrecedingSingleQuery_ReturnsFalse` (neue Tests für `SET` und `USE`)
- [ ] `MultipleBatches_ReturnsTrue` (mehrere Batches mit Statements)

## Definition of Done

- [ ] `SqlMultiStatementDetector` nutzt `SqlScriptDomParser`
- [ ] Alle bestehenden und neuen Tests in `SqlMultiStatementDetectorTests` sind grün
- [ ] `dotnet build` und `dotnet test` grün (Zero-Warnings)
- [ ] Code- und Doku-Commits mit Suffix `[sql-parser-refactoring]` erstellt
- [ ] `step-002/step-result.md` geschrieben und Status aktualisiert

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#2` — Sicherheitsprüfungen und Single-Statement-Erzwingung
- `.agents/rules/AiNetLinter.mdc#Kurz-Stil` — `#nullable enable`, `sealed` / `static`, saubere Methodenbegrenzung
