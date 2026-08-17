---
status: done
type: task-audit
task: sql-parser-refactoring
derived_from: konzept.md
created_by: planer
created_by_model: gemini-3.7-flash
created_by_model_knowledge_cutoff: "2026-01"
created_at: "2026-08-17T16:44:50+02:00"
---

# Task-Audit: sql-parser-refactoring

## Abgleich mit konzept.md

### 1. Scope & Muss-Haben

- [x] **Microsoft ScriptDom NuGet-Paket einbinden:** `Microsoft.SqlServer.TransactSql.ScriptDom` Version `170.3.0` via CPM eingebunden (Step 001).
- [x] **TSql150Parser Helper:** `SqlScriptDomParser.cs` mit `Parse` und `ParseScript` ohne verbotene non-Try `out`-Parameter implementiert (Step 001).
- [x] **SqlMultiStatementDetector auf ScriptDom umstellen:** Ersetzt `SqlCharScanner` durch AST-Statements (`TSqlScript.Batches[].Statements`) und unterstützt Preamble-Befehle `SET`, `USE`, `DECLARE` (Step 002).
- [x] **ReadOnlyGuard auf ScriptDom AST-Visitor umstellen:** Ersetzt Regex-Matching und Keyword-Scanning durch `ReadOnlyStatementVisitor` mit Typregistrierung für DML, DDL, `EXEC`, `sp_executesql`, `SELECT ... INTO` und Sicherheitsbefehle; unterstützt `EXECUTE AS` und unkritische Bracket-Identifier wie `SELECT [insert] FROM t` (Step 003).
- [x] **QueryDeconstructor auf ScriptDom umstellen:** Ersetzt `StartsWith("WITH")` und Klammerntiefen-Scan durch AST-Navigation über `WithCtesAndXmlNamespaces` und Statement-Offsets (Step 004).
- [x] **Dokumentation & Gesamtabnahme:** `docs/architecture-spec.md` und `README.md` synchronisiert (Step 005).

### 2. Kann-Haben & Non-Goals

- **Non-Goals eingehalten:**
  - Keine Umstellung der String-Anonymisierung (`SqlCharScanner` und `SqlLiteralScanner` verbleiben unberührt für Maskierung und Token-Substitution).
  - Keine Performance-Regressionen; AST-Parsing wird gezielt in Validierungs- und Guard-Pfaden eingesetzt.

### 3. Definition of Done

- [x] Alle 5 Epics vollständig umgesetzt und gereviewt (Steps 001 bis 005)
- [x] Alle Unit- und Integrationstests grün (556 Tests, 0 Fehler)
- [x] `dotnet build` fehlerfrei mit Zero-Warnings (`TreatWarningsAsErrors=true`)
- [x] `AiNetLinter` Clean-Check ohne Violations
- [x] Drift-Audit (`find_duplicates`) durchgeführt: 0 Duplikat-Cluster
- [x] Konventionelle Commits mit Suffix `[sql-parser-refactoring]` durchgängig verwendet

## Fazit

Die Umstellung auf den offiziellen Microsoft T-SQL ScriptDom Parser ist vollständig, sicher und konform mit allen Architektur- und Code-Quality-Richtlinien implementiert.
