---
title: "SQL-Parser-Refactoring: ScriptDom statt Custom-Parser"
status: draft
last_updated: "2026-08-03"
rules_dir: .agents/rules
project_kind: brownfield
estimated_scope: medium
open_questions:
  - "Schrittweise Migration (Komponente für Komponente) oder Big-Bang-Ersatz?"
  - "ReadOnlyGuard zuerst (sicherheitsrelevant) oder QueryDeconstructor (korrektheitskritisch)?"
  - "SqlCharScanner als Fallback behalten oder vollständig entfernen?"
  - "TSql160Parser oder TSql150Parser als Target? (Abhängig von Mindest-SQL-Server-Version)"
---

# SQL-Parser-Refactoring: ScriptDom statt Custom-Parser

## Hintergrund

Im Projekt existieren mehrere selbst gebaute SQL-Parsing-Schichten. Das Gefühl,
dass das fragil ist, ist berechtigt — nicht für alle Komponenten gleich stark,
aber es gibt konkrete Risikostellen.

---

## Ist-Stand: Eigene Parser-Schichten

### Architektur

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

---

## Das seriöse NuGet-Paket

**`Microsoft.SqlServer.TransactSql.ScriptDom`**

- **Quelle:** Microsoft, Open Source (GitHub: microsoft/sqlmanagementobjects)
- **Verbreitung:** Intern in SSMS, Azure Data Studio, SqlPackage, DacFx verwendet
- **API:** Echter AST (`TSqlFragment`) + Visitor-Pattern (`TSqlFragmentVisitor`)
- **Versionen:** `TSql150Parser` (SQL Server 2019), `TSql160Parser` (SQL Server 2022)
- **Größe:** ~2 MB, keine weiteren Abhängigkeiten

```csharp
var parser = new TSql160Parser(initialQuotedIdentifiers: true, SqlEngineType.All);
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

---

## Migrationsideen (noch nicht priorisiert)

### Idee 1 — ReadOnlyGuard ersetzen (höchste Priorität, sicherheitsrelevant)

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

**Vorteil:** `EXECUTE AS USER = 'someone'` wäre `ExecuteAsStatement` — korrekt
identifizierbar. `SELECT col INTO #tmp` wäre `SelectStatement` mit `Into`-Klausel
(diskutierbar — Temp-Tables sind READ, nicht WRITE). `INSERT INTO` ist `InsertStatement`.

### Idee 2 — SqlMultiStatementDetector ersetzen

**Ist:** Semikolon-Zählung + DECLARE-Prefix.

**Soll:**
```csharp
// TSqlScript.Batches gibt Batches, jede Batch hat Statements
var nonDeclareStatements = batch.Statements
    .Where(s => s is not DeclareVariableStatement)
    .ToList();
return nonDeclareStatements.Count > 1;
```

### Idee 3 — QueryDeconstructor ersetzen

**Ist:** `StartsWith("WITH")` für CTE-Erkennung, `IsWordAt` für SELECT.

**Soll:** AST-Navigation zu `SelectStatement.WithCtesAndXmlNamespaces` — kein
String-Hacking mehr.

### Idee 4 — SqlCharScanner behalten oder entfernen?

`SqlCharScanner` ist die Basis für `SqlLiteralScanner` (Token-Substitution in
Anonymisierung). Wenn `QueryDeconstructor` und `ReadOnlyGuard` auf ScriptDom
migrieren, bleibt `SqlLiteralScanner` als einziger Nutzer. Optionen:
- `SqlLiteralScanner` ebenfalls auf ScriptDom migrieren → `SqlCharScanner` entfällt
- `SqlLiteralScanner` bleibt (schmaler, gut getesteter Scope) → `SqlCharScanner` bleibt

---

## Nicht-Ideen (bewusst ausgeschlossen)

| Idee | Grund |
|:--|:--|
| Antlr4 TSQL-Grammatik | Community-gepflegt, nicht Microsoft-offiziell, größer |
| Roslyn (C# Analyzer) | Falsche Sprache |
| PetaParser / Sprache | Kein T-SQL-Dialekt out-of-the-box |
| Manuell gepflegte Keyword-Liste ausbauen | Mehr desselben Problems |

---

## Nächste Schritte (wenn dieser Task geöffnet wird)

1. `TSql160Parser` ausprobieren: Kann er alle aktuellen Test-Queries korrekt parsen?
2. Reihenfolge festlegen: `ReadOnlyGuard` zuerst (sicherheitskritisch)?
3. Schrittweise Migration oder Feature-Branch?
4. Bestehende Tests als Regressions-Netz nutzen (kein Test darf kipppen)
5. `SqlToAi.csproj` um `Microsoft.SqlServer.TransactSql.ScriptDom` erweitern
