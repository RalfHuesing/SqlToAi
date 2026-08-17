---
status: done (pending audit)
type: step-plan
task: sql-parser-refactoring
step: "004"
corrects: null
title: "QueryDeconstructor auf ScriptDom AST-Navigation umstellen"
epic: EPIC-04
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: gemini-3.7-flash
created_by_model_knowledge_cutoff: "2026-01"
created_at: "2026-08-17T16:39:25+02:00"
related_to: [step-001]
---

# Step 004: QueryDeconstructor auf ScriptDom AST-Navigation umstellen

## Bezug

- **Task:** `sql-parser-refactoring`
- **Epic:** `EPIC-04` aus `roadmap.md` — QueryDeconstructor auf ScriptDom umstellen
- **Konzept-Referenz:** `konzept.md` §Scope, §Muss-Haben und §Wie (QueryDeconstructor ersetzen)

## Aktueller Projektzustand (JIT-Kontext)

- `QueryDeconstructor` zerlegt SQL-Queries bisher mittels String-Heuristik (`StartsWith("WITH")`, Klammerntiefen-Zählung mit `SqlCharScanner` zur Suche nach dem Haupt-`SELECT`).
- Dies ist fehleranfällig bei verschachtelten Subqueries, unkonventionellen Formatierungen oder Kommentaren innerhalb von CTE-Definitionen.
- `SqlScriptDomParser` ist bereitgestellt und liefert den vollständigen `SelectStatement`-Knoten inklusive `WithCtesAndXmlNamespaces`.

## Intention

Die Zerlegung in Preamble (`DECLARE`, `SET`, etc.), CTEs (`WITH ...`) und Haupt-`SELECT` in `QueryDeconstructor.Deconstruct` vollständig auf AST-Navigation umstellen.
`SelectStatement.WithCtesAndXmlNamespaces` und AST-Fragment-Offsets nutzen, um CTEs und den Haupt-SELECT präzise und robust zu extrahieren.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Database/QueryDeconstructor.cs`

- **Was:** Ersetzen von `FindMainSelectIndex`, `ExtractPreambleAndBody`, `BuildPreambleAndBody`, `IsWordAt` und `SqlCharScanner`-Aufrufen durch AST-Navigation über `SqlScriptDomParser.ParseScript`. Preamble aus AST-Statements vor dem Haupt-Statement ableiten, CTEs aus `WithCtesAndXmlNamespaces` und Haupt-SELECT aus dem verbleibenden Query-Body.
- **Warum:** Beseitigung aller String-Scanning-Fragilitäten bei CTE- und Preamble-Zerlegung.

### Datei 2: `tests/SqlToAi.Tests/Database/QueryDeconstructorTests.cs`

- **Was:** Bestehende Tests beibehalten und um komplexe CTE-Szenarien (verschachtelte Subqueries mit SELECT, mehrteilige Preambles mit SET und DECLARE, Kommentare im CTE-Header) erweitern.
- **Warum:** Sicherstellen, dass CTE-Zerlegung und Kombination für Query-Vergleiche fehlerfrei funktionieren.

## Tests

- [ ] `QueryDeconstructorTests.Deconstruct_PlainSelect_ReturnsEmptyPreambleAndCtes`
- [ ] `QueryDeconstructorTests.Deconstruct_DeclarePreambleAndSelect_ExtractsPreamble`
- [ ] `QueryDeconstructorTests.Deconstruct_CteAndSelect_ExtractsCtes`
- [ ] `QueryDeconstructorTests.Deconstruct_DeclareAndCteAndSelect_ExtractsPreambleAndCtes`
- [ ] `QueryDeconstructorTests.CombineCtes_JoinsTwoCtesWithSingleWith`
- [ ] `QueryDeconstructorTests.Deconstruct_ComplexNestedCteAndComments_ExtractsAccurately` (neu)

## Definition of Done

- [ ] `QueryDeconstructor` nutzt `SqlScriptDomParser` und AST-Navigation
- [ ] Alle Tests in `QueryDeconstructorTests` und abhängige Tests in `QueryComparisonServiceTests` sind grün
- [ ] `dotnet build` und `dotnet test` grün (Zero-Warnings)
- [ ] Code- und Doku-Commits mit Suffix `[sql-parser-refactoring]` erstellt
- [ ] `step-004/step-result.md` geschrieben und Status aktualisiert

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#1` — Performance und AST-gestützte Datenverarbeitung
- `.agents/rules/AiNetLinter.mdc#Kurz-Stil` — Flache Methoden, `#nullable enable`, `internal static`
