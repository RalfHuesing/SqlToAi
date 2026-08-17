---
status: open
type: step-plan
task: sql-parser-refactoring
step: "001"
corrects: null
title: "ScriptDom NuGet-Paket einbinden und SqlScriptDomParser-Helper erstellen"
epic: EPIC-01
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: gemini-3.7-flash
created_by_model_knowledge_cutoff: "2026-01"
created_at: "2026-08-17T16:27:10+02:00"
related_to: []
---

# Step 001: ScriptDom NuGet-Paket einbinden und SqlScriptDomParser-Helper erstellen

## Bezug

- **Task:** `sql-parser-refactoring`
- **Epic:** `EPIC-01` aus `roadmap.md` — NuGet-Dependency ScriptDom & TSql150Parser Helper
- **Konzept-Referenz:** `konzept.md` §Muss-Haben und §Zielplattformen / Technischer Rahmen

## Aktueller Projektzustand (JIT-Kontext)

- `src/SqlToAi/SqlToAi.csproj` enthält noch keine Referenz auf `Microsoft.SqlServer.TransactSql.ScriptDom`.
- Es existiert noch keine AST-Parsing-Infrastruktur im Projekt.
- Parser-Konstruktion laut Konzept: `TSql150Parser(initialQuotedIdentifiers: true, SqlEngineType.All)`.
- Vorbereitung eines zentralen, internen Helpers `SqlScriptDomParser`, der `TSql150Parser` kapselt, `TSqlFragment` / `TSqlScript` zurückliefert und von `ReadOnlyGuard`, `SqlMultiStatementDetector` sowie `QueryDeconstructor` in den Folge-Steps wiederverwendet wird.

## Intention

Das offizielle Microsoft-Paket `Microsoft.SqlServer.TransactSql.ScriptDom` als Dependency in `SqlToAi.csproj` aufnehmen.
Einen wiederverwendbaren `SqlScriptDomParser`-Helper in `SqlToAi.Database` implementieren, der `TSql150Parser` für die AST-Erzeugung bereitstellt, inklusive Unit-Tests zur Verifikation der AST-Parsing-Fähigkeit und Fehlerbehandlung.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/SqlToAi.csproj`

- **Was:** `<PackageReference Include="Microsoft.SqlServer.TransactSql.ScriptDom" Version="150.4897.1" />` (oder kompatible 150.x / 16x/17x Version, die `TSql150Parser` bereitstellt) hinzufügen.
- **Warum:** Offizielle Microsoft AST-Parser-Bibliothek für T-SQL.

### Datei 2: `src/SqlToAi/Database/SqlScriptDomParser.cs` (neu)

- **Was:** Interne statische Hilfsklasse `SqlScriptDomParser` mit Methode `Parse(string sql, out IList<ParseError> errors) -> TSqlFragment?` und `ParseScript(string sql, out IList<ParseError> errors) -> TSqlScript?`.
- **Warum:** Zentrale Kapselung des `TSql150Parser` mit einheitlichen Flags (`initialQuotedIdentifiers: true, SqlEngineType.All`), um doppelten Boilerplate in den Konsumenten zu vermeiden.

### Datei 3: `tests/SqlToAi.Tests/Database/SqlScriptDomParserTests.cs` (neu)

- **Was:** Unit-Tests für `SqlScriptDomParser`, die valides SQL, syntaktisch ungültiges SQL und Quoted Identifiers prüfen.
- **Warum:** Sicherstellen, dass der Parser-Helper korrekt initialisiert wird und erwartungskonform funktioniert.

## Tests

- [ ] `SqlScriptDomParserTests.Parse_ValidSelect_ReturnsTSqlScriptWithZeroErrors`
- [ ] `SqlScriptDomParserTests.Parse_InvalidSql_ReturnsErrors`
- [ ] `SqlScriptDomParserTests.Parse_NullOrEmpty_ReturnsEmptyOrNull`

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command (`dotnet build`) grün (Zero-Warnings)
- [ ] Test-Command (`dotnet test`) grün
- [ ] Commit auf aktuellem Branch (Conventional Commit, Deutsch, Suffix `[sql-parser-refactoring]`)
- [ ] `step-001/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#5` — Zero-Warning-Direktive und Linter-Konformität
- `.agents/rules/AiNetLinter.mdc#Kurz-Stil` — `#nullable enable`, `sealed` / `static`, saubere Namensgebung
