---
title: "SQL-Parser-Refactoring: ScriptDom statt Custom-Parser"
status: ready
last_updated: "2026-08-03"
rules_dir: .agents/rules
project_kind: brownfield
estimated_scope: medium
open_questions: []
---

# SQL-Parser-Refactoring: ScriptDom statt Custom-Parser

## Ziel (Was)

Die drei sicherheits-/korrektheitsrelevanten SQL-Parsing-Schichten
(`ReadOnlyGuard`, `QueryDeconstructor`, `SqlMultiStatementDetector`) werden
von Regex-/String-Heuristiken auf einen echten AST-basierten Parser
(`Microsoft.SqlServer.TransactSql.ScriptDom`, `TSql150Parser`) umgestellt —
in einem Big-Bang-Schritt (alle drei Komponenten in einem Feature-Branch/PR),
um Falsch-Positive/-Negative bei der Erkennung mutierender Statements,
Multi-Statement-Batches und CTE/Preamble-Strukturen zu eliminieren.

## Warum / Kontext

Im Projekt existieren mehrere selbst gebaute SQL-Parsing-Schichten. Das
Gefühl, dass das fragil ist, ist berechtigt — nicht für alle Komponenten
gleich stark, aber es gibt konkrete Risikostellen (siehe Fragilitäts-
Bewertung unten).

### Ist-Stand: Eigene Parser-Schichten

```
SqlCharScanner (State-Machine: Normal/String/Comment/Bracket)
       │
       ├── SqlMultiStatementDetector  (Semikolon-Zählung + DECLARE-Erkennung)
       ├── QueryDeconstructor         (WITH-CTE, SELECT-Keyword, Preamble/Body)
       ├── ReadOnlyGuard              (Keyword-Regex nach Literal-Blanking)
       └── SqlLiteralScanner          (String-Literal-Ranges für Token-Substitution)
```

### Bewertung nach Fragilität

| Komponente | Fragilitäts-Risiko | Warum |
|:--|:--|:--|
| `SqlCharScanner` | 🟢 Gering | Einfache State-Machine, `''`-Escape, `[...]`, `--`/`/* */` korrekt |
| `SqlMultiStatementDetector` | 🟡 Mittel | DECLARE-Erkennung nur per Prefix — kein Verständnis für `SET`, `USE` oder andere Preamble-Konstrukte |
| `SqlLiteralScanner` | 🟢 Gering | Baut sauber auf SqlCharScanner auf, schmaler Scope |
| `QueryDeconstructor` | 🟠 Erhöht | `WITH`-Erkennung via `StartsWith` nach Strip — bricht bei ungewöhnlichen CTE-Formaten oder verschachtelten Subqueries mit `WITH` |
| `ReadOnlyGuard` | 🔴 Hoch (sicherheitsrelevant) | Regex auf Keywords — Edge Cases: `EXECUTE AS` (harmlos, aber geblockt), `INSERT INTO` vs. `SELECT INTO`, `MERGE` in komplexen CTEs |

### Das seriöse NuGet-Paket

**`Microsoft.SqlServer.TransactSql.ScriptDom`**

- **Quelle:** Microsoft, Open Source (GitHub: microsoft/sqlmanagementobjects)
- **Verbreitung:** Intern in SSMS, Azure Data Studio, SqlPackage, DacFx verwendet
- **API:** Echter AST (`TSqlFragment`) + Visitor-Pattern (`TSqlFragmentVisitor`)
- **Versionen:** `TSql150Parser` (SQL Server 2019) — hier gewählt, siehe „Zielplattformen"
- **Größe:** ~2 MB, keine weiteren Abhängigkeiten

```csharp
var parser = new TSql150Parser(initialQuotedIdentifiers: true, SqlEngineType.All);
IList<ParseError> errors;
using var reader = new StringReader(sql);
TSqlFragment ast = parser.Parse(reader, out errors);
```

### Was ScriptDom besser kann

| Aufgabe | Unser Regex/Heuristik | ScriptDom |
|:--|:--|:--|
| Ist ein Statement mutierend? | Keyword-Regex nach Strip | `DmlStatement`-Visitor exakt |
| Mehrere Statements? | Semikolon-Zählung | `TSqlScript.Batches[].Statements.Count` |
| CTEs identifizieren | `StartsWith("WITH")` | `SelectStatement.WithCtesAndXmlNamespaces` |
| `EXECUTE AS` vs. `EXEC` | Geblockt als mutierend | Typunterschied im AST |
| `SELECT INTO` vs. `INSERT INTO` | Regex-False-Positive möglich | `SelectStatement` vs. `InsertStatement` |
| `DECLARE` vs. andere Preamble | Nur DECLARE erkannt | `DeclareVariableStatement`, `SetVariableStatement`, etc. |

## Scope

### Muss-Haben

- `Microsoft.SqlServer.TransactSql.ScriptDom` als NuGet-Dependency in
  `SqlToAi.csproj` aufnehmen, `TSql150Parser` als Target (siehe
  „Zielplattformen")
- `ReadOnlyGuard`: Regex-Keyword-Matching durch AST-Visitor ersetzen
  (`DmlStatement`, `DDLStatement`, `AlterStatement`, etc.) —
  [src/SqlToAi/Security/ReadOnlyGuard.cs](../../src/SqlToAi/Security/ReadOnlyGuard.cs)
- `SqlMultiStatementDetector`: Semikolon-Zählung + DECLARE-Prefix durch
  `TSqlScript.Batches[].Statements`-Auswertung ersetzen —
  [src/SqlToAi/Database/SqlMultiStatementDetector.cs](../../src/SqlToAi/Database/SqlMultiStatementDetector.cs)
- `QueryDeconstructor`: `StartsWith("WITH")`-Heuristik durch AST-Navigation
  (`SelectStatement.WithCtesAndXmlNamespaces`) ersetzen —
  [src/SqlToAi/Database/QueryDeconstructor.cs](../../src/SqlToAi/Database/QueryDeconstructor.cs)
- Big-Bang: alle drei Komponenten in einem Feature-Branch/PR, kein
  Mischzustand über mehrere Commits/Releases
- Alle bestehenden Tests bleiben grün (Regressionsnetz, siehe „Wo im
  Projekt") — kein Test darf kippen
- Neue Edge-Cases aus der Fragilitäts-Analyse (`EXECUTE AS`, `SELECT INTO`
  vs. `INSERT INTO`, `SET`/`USE` als Preamble neben `DECLARE`) werden durch
  neue/erweiterte Tests abgedeckt

### Nice-to-Have (optional, spätere Iteration)

- Keine — Scope ist bewusst auf die drei sicherheits-/korrektheitsrelevanten
  Komponenten begrenzt (siehe Non-Goals)

### Non-Goals (bewusst NICHT Teil davon)

- `SqlCharScanner` / `SqlLiteralScanner` werden **nicht** migriert — bleiben
  auf eigener State-Machine (🟢 geringes Fragilitäts-Risiko, schmaler gut
  getesteter Scope, nicht Teil des eigentlichen Sicherheits-/
  Korrektheitsproblems dieses Refactorings)
- `TSql160Parser` (SQL Server 2022) wird **nicht** als Target gewählt —
  Kompatibilität mit SQL Server 2019 muss erhalten bleiben

## Zielplattformen / Technischer Rahmen

`Microsoft.SqlServer.TransactSql.ScriptDom` (NuGet, Microsoft/Open Source),
`TSql150Parser` — Begründung: Nutzer setzen teilweise noch SQL Server 2019
ein, `TSql160Parser` (2022) würde diese Kompatibilität brechen. ~2 MB, keine
weiteren Abhängigkeiten. Fügt sich ohne neues Architektur-Pattern in den
bestehenden .NET 10/C# 14-Stack ein.

## Verworfene Alternativen

- **Antlr4 TSQL-Grammatik:** verworfen, weil Community-gepflegt, nicht
  Microsoft-offiziell, größer
- **Roslyn (C# Analyzer):** verworfen, weil falsche Sprache
- **PetaParser / Sprache:** verworfen, weil kein T-SQL-Dialekt out-of-the-box
- **Manuell gepflegte Keyword-Liste ausbauen:** verworfen, weil mehr
  desselben Problems (Regex/Heuristik statt echtem Parser)
- **Schrittweise Migration (Komponente für Komponente):** verworfen zugunsten
  Big-Bang — vermeidet einen Zwischenzustand mit gemischten Parsern
  (alt + ScriptDom parallel) über mehrere Commits
- **`SqlCharScanner`/`SqlLiteralScanner` mitmigrieren:** verworfen, weil
  geringes Fragilitäts-Risiko und schmaler, gut getesteter Scope — kein
  Grund, funktionierenden Code anzufassen (siehe Non-Goals)
- **`TSql160Parser` als Target:** verworfen, weil Kompatibilität mit SQL
  Server 2019 erforderlich ist

## Wo im Projekt

- [src/SqlToAi/Security/ReadOnlyGuard.cs](../../src/SqlToAi/Security/ReadOnlyGuard.cs),
  [IReadOnlyGuard.cs](../../src/SqlToAi/Security/IReadOnlyGuard.cs) —
  Regex-basierter Read-Only-Guard, zu ersetzen
- [src/SqlToAi/Database/QueryDeconstructor.cs](../../src/SqlToAi/Database/QueryDeconstructor.cs) —
  CTE/Preamble-Erkennung, zu ersetzen
- [src/SqlToAi/Database/SqlMultiStatementDetector.cs](../../src/SqlToAi/Database/SqlMultiStatementDetector.cs) —
  Multi-Statement-Erkennung, zu ersetzen
- [src/SqlToAi/Database/SqlCharScanner.cs](../../src/SqlToAi/Database/SqlCharScanner.cs),
  [SqlLiteralScanner.cs](../../src/SqlToAi/Database/SqlLiteralScanner.cs) —
  bleiben unverändert (Non-Goal)
- [src/SqlToAi/Database/QueryExecutionService.cs](../../src/SqlToAi/Database/QueryExecutionService.cs),
  [QueryValidationService.cs](../../src/SqlToAi/Database/QueryValidationService.cs),
  [QueryTokenResolver.cs](../../src/SqlToAi/Database/QueryTokenResolver.cs),
  [QueryComparisonService.cs](../../src/SqlToAi/Database/QueryComparisonService.cs),
  [PerformanceMeasurementService.cs](../../src/SqlToAi/Database/PerformanceMeasurementService.cs),
  [Program.cs](../../src/SqlToAi/Program.cs) — Consumer der zu ersetzenden
  Komponenten (Aufrufer, DI-Wiring); Fundstellen, kein Anspruch auf aktuelles
  Verhalten
- [src/SqlToAi/SqlToAi.csproj](../../src/SqlToAi/SqlToAi.csproj) —
  NuGet-Dependency ergänzen
- [tests/SqlToAi.Tests/Security/ReadOnlyGuardTests.cs](../../tests/SqlToAi.Tests/Security/ReadOnlyGuardTests.cs),
  [tests/SqlToAi.Tests/Database/SqlMultiStatementDetectorTests.cs](../../tests/SqlToAi.Tests/Database/SqlMultiStatementDetectorTests.cs),
  [tests/SqlToAi.Tests/Database/QueryDeconstructorTests.cs](../../tests/SqlToAi.Tests/Database/QueryDeconstructorTests.cs) —
  bestehendes Regressionsnetz, muss grün bleiben

## Entdeckte Mängel/Redundanzen

Bei der Recherche (Konsumenten-Suche, `ScriptDom`-Grep, Sealed-Check gegen
`AiNetLinter.mdc`) keine zusätzlichen Mängel oder Redundanzen über das im
Konzept bereits beschriebene Fragilitäts-Risiko hinaus gefunden. `ScriptDom`
ist im Projekt noch nirgends referenziert (reiner Neuzugang), die
betroffenen Klassen sind `internal static class` (kein `sealed`-Verstoß).

## Wie (grober Ansatz)

### ReadOnlyGuard ersetzen

**Ist:**
```csharp
// Regex auf geblanktem SQL
private static readonly Regex MutatingKeywordsRegex = new(
    @"\b(insert|update|delete|drop|...)\b", ...);
```

**Soll:**
```csharp
// AST-Visitor: sucht nach DmlStatement, DDLStatement, etc.
class MutatingStatementVisitor : TSqlFragmentVisitor {
    public bool FoundMutating { get; private set; }
    public override void Visit(DmlStatement node) => FoundMutating = true;
    public override void Visit(AlterStatement node) => FoundMutating = true;
    // ...
}
```

Vorteil: `EXECUTE AS USER = 'someone'` wäre `ExecuteAsStatement` — korrekt
identifizierbar. `SELECT col INTO #tmp` wäre `SelectStatement` mit
`Into`-Klausel (diskutierbar — Temp-Tables sind READ, nicht WRITE, im
Zweifel als Test-Case klären). `INSERT INTO` ist `InsertStatement`.

### SqlMultiStatementDetector ersetzen

**Ist:** Semikolon-Zählung + DECLARE-Prefix.

**Soll:**
```csharp
// TSqlScript.Batches gibt Batches, jede Batch hat Statements
var nonDeclareStatements = batch.Statements
    .Where(s => s is not DeclareVariableStatement)
    .ToList();
return nonDeclareStatements.Count > 1;
```

### QueryDeconstructor ersetzen

**Ist:** `StartsWith("WITH")` für CTE-Erkennung, `IsWordAt` für SELECT.

**Soll:** AST-Navigation zu `SelectStatement.WithCtesAndXmlNamespaces` — kein
String-Hacking mehr.

Reihenfolge innerhalb des Big-Bang-Branches ist funktional irrelevant, da
alle drei Komponenten gemeinsam released werden; Tests validieren jede
Komponente unabhängig.

## Definition of Done / Erfolgskriterien

- `Microsoft.SqlServer.TransactSql.ScriptDom` als Dependency eingebunden,
  `TSql150Parser` als Target
- `ReadOnlyGuard`, `QueryDeconstructor`, `SqlMultiStatementDetector` nutzen
  AST-Visitor/AST-Navigation statt Regex/String-Heuristik
- Alle bestehenden Tests (siehe „Wo im Projekt") grün — kein Regressions-Test
  kippt
- Edge-Cases aus der Fragilitäts-Analyse (`EXECUTE AS`, `SELECT INTO` vs.
  `INSERT INTO`, `SET`/`USE` als Preamble) sind durch neue/erweiterte Tests
  abgedeckt
- `SqlCharScanner`/`SqlLiteralScanner` unverändert, weiterhin genutzt von der
  Anonymisierung
- `docs/architecture-spec.md` und `README.md` aktualisiert, falls die
  Architekturbeschreibung den alten Regex-Ansatz erwähnt (Pflicht laut
  [.agents/rules/SqlToAiRichtlinien.mdc](../../.agents/rules/SqlToAiRichtlinien.mdc) §4)

## Offene Punkte

Keine — alle vier ursprünglich offenen Fragen sind geklärt (Big-Bang,
Reihenfolge damit hinfällig, `SqlCharScanner` bleibt, `TSql150Parser`).
