---
status: open
type: step-plan
task: sql-index-suggestions
step: 001
title: "EPIC-01: Parser-Erweiterung — vollständige CREATE NONCLUSTERED INDEX-Statements aus MissingIndex-XML"
epic: EPIC-01
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: MiniMax-M3
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-04T12:30:00+02:00
related_to: []
---

# Step 001: EPIC-01 — Parser-Erweiterung für vollständige CREATE NONCLUSTERED INDEX-Statements

## Bezug

- **Task:** `sql-index-suggestions`
- **Epic:** `EPIC-01` aus `roadmap.md` — Parser-Erweiterung in
  `sql_measure_performance`, sodass pro `MissingIndex`-Warning zusätzlich zu
  Tabelle/Impact% ein direkt ausführbares `CREATE NONCLUSTERED INDEX`-Statement
  (Equality/Inequality/Include-Spalten) zurückgegeben wird. Doku-Sync für
  Idee 1 ist Teil dieses Epics.
- **Konzept-Referenz:** `konzept.md` §Muss-Haven Idee 1, §Wie Idee 1, §DoD
  (Zeilen 33–41, 157–174, 201–225), §Wo im Projekt (Zeilen 121–125, 138–141).

## Aktueller Projektzustand (JIT-Kontext)

Beim Lesen des relevanten Codes habe ich folgende Strukturen vorgefunden,
die der Step nutzt bzw. erweitert:

- **`PerformanceMeasurementService.ExtractMissingIndexWarnings`
  (Zeile 328–342):** nutzt bereits `XDocument.Parse` + `doc.Descendants(ns +
  "MissingIndex")` und liest pro Element `Table` (Schema+Table in
  `[Schema].[Table]`-Form) und `Impact` (vom Parent `MissingIndexGroup`).
  Die Methode ist `private static`, lebt in einer `sealed class`, folgt
  dem Pattern `Attribute("X")?.Value ?? "<default>"` mit
  `double.TryParse(..., NumberStyles.Float, CultureInfo.InvariantCulture,
  out ...)`. Diese Linie muss der Coder für die Spalten-Extraktion
  beibehalten — gleiches Pattern, gleiche defensive Null-Behandlung.
- **`PerformancePlanWarning` (Domain/PerformanceMeasurementResult.cs
  Zeile 24–28):** `public sealed record` mit 4 Feldern
  (`Type`/`Severity`/`Message`/`Impact`), JSON-Properties via
  `[property: JsonPropertyName(...)]`. Die JSON-Serialisierung läuft über
  `McpJsonContext` (Zeile 39 — `[JsonSerializable(typeof(Domain.PerformancePlanWarning))]`)
  mit `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull` — das
  heißt: ein neues optionales Feld mit `null` erscheint im JSON-Output gar
  nicht, kein Breaking Change für bestehende Konsumenten.
- **`ToolDispatcher` (Zeile 177–187):** serialisiert `PerformanceMeasurementResult`
  via `JsonSerializer.Serialize(..., typeof(PerformanceMeasurementResult),
  McpJsonContext.Default)`. **Kein Code-Change im Dispatcher nötig** — das
  neue Feld auf `PerformancePlanWarning` wird automatisch mit-serialisiert.
- **`ToolRegistry.BuildMeasurePerformance()` (Zeile 250–277):** der
  AI-sichtbare Tool-Description-String (Zeile 253–263) beschreibt
  `warnings[] (type/severity/message/impact ...)` — diese Beschreibung muss
  mit-aktualisiert werden, damit der AI-Client weiß, dass das neue Feld
  verfügbar ist. Reine Textänderung, kein struktureller Eingriff.
- **`ParseExecutionPlanXml` (Zeile 309–326):** `public static`, bereits
  von Tests direkt aufgerufen — d. h. der Coder kann den XML-Pfad ohne
  DB-Mock testen (Bestätigung am bestehenden Test
  `ParseExecutionPlanXml_MissingIndexAndImplicitConversion_ParsesWarningsCorrectly`).
- **Bestehender Test `MissingIndex Table="[dbo].[Orders]"` ohne
  `ColumnGroup`-Kinder** (Zeile 105–107 in
  `PerformanceMeasurementServiceTests.cs`): produziert heute 1 Warning;
  nach dem Step weiterhin genau 1 Warning, aber mit
  `MissingIndexStatement = null` (keine Schlüsselspalten vorhanden → kein
  Statement baubar). Der Test asserted nur `Count == 3`, `Type`, `Impact`
  — bleibt grün.
- **XPath-Realität:** Im SQL-Server-XML-Plan liegt `<MissingIndex>` als
  Kind von `<MissingIndexGroup>`; die `ColumnGroup`-Elemente sind direkte
  Kinder von `<MissingIndex>`. `Table="[dbo].[Orders]"` ist eine
  **Bracket-Notation in einem einzigen Attribut** (Schema+Table inkl.
  eckiger Klammern), nicht zwei getrennte Elemente — das `Table`-Attribut
  wird also 1:1 in die `ON [Schema].[Table]`-Klausel übernommen.
- **Index-Name-Konvention** (aus Konzept-Beispiel):
  `IX_<Table>_<FirstEqualityCol>[_<NextEqualityOrInequalityCol>...]`.
  Pragmatische Vereinfachung: die ersten 1–2 Spalten genügen für den
  Agent zur Identifikation; der Agent kann den Namen trivial anpassen.
- **Edge-Case:** SQL-Server-`MissingIndex` ohne jegliche `EQUALITY`-
  oder `INEQUALITY`-Spalten ist technisch unvollständig (Index braucht
  Schlüsselspalten). Der Parser muss `MissingIndexStatement` in diesem
  Fall auf `null` belassen — kein leeres String-Statement ausgeben.

Der Plan erweitert damit eine bestehende private Methode und einen
bestehenden `record` um genau ein neues optionales Feld — keine neue
Klasse, kein neues Pattern, keine Architekturänderung.

## Intention

Nach diesem Step liefert `sql_measure_performance` pro Missing-Index-
Warning ein direkt ausführbares `CREATE NONCLUSTERED INDEX`-Statement
zurück (Equality/Inequality/Include-Spalten aus dem XML-Plan korrekt
zusammengesetzt, Schema+Table in Bracket-Notation übernommen). Ein
KI-Agent kann die Empfehlung damit 1:1 aus dem Tool-Output übernehmen,
statt die Spalteninformationen aus drei separaten Warnungen oder dem
rohen XML-Plan rekonstruieren zu müssen. Doku (architecture-spec §4
Nr. 14 + README-Bullet) ist konsistent mit der neuen Struktur.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Domain/PerformanceMeasurementResult.cs` (Zeile 24–28)

- **Was:** Neues optionales Feld am `PerformancePlanWarning`-Record:
  `string? MissingIndexStatement` mit
  `[property: JsonPropertyName("missing_index_statement")]`. Default `null`,
  am Ende der Parameterliste (additive Änderung, keine Reihenfolge-Änderung
  an bestehenden Feldern).
- **Warum:** Transportiert das fertige `CREATE NONCLUSTERED INDEX`-
  Statement vom Parser zum MCP-Output. Null = nicht anwendbar (kein
  Missing-Index oder keine Schlüsselspalten vorhanden). Über
  `JsonIgnoreCondition.WhenWritingNull` aus `McpJsonContext` erscheint
  `null` nicht im JSON-Output → kein Breaking Change.

### Datei 2: `src/SqlToAi/Database/PerformanceMeasurementService.cs` (Zeile 328–342)

- **Was:**
  1. In `ExtractMissingIndexWarnings` zusätzlich zu `Table`/`Impact` alle
     `ColumnGroup`-Kinder des aktuellen `MissingIndex`-Elements auswerten
     (`Descendants(ns + "ColumnGroup")` oder direkte
     `Elements(ns + "ColumnGroup")`-Aufzählung — letzteres ist
     semantisch korrekter, da direkte Kinder).
  2. Drei `List<string>` aufbauen: `equality` (Usage="EQUALITY"),
     `inequality` (Usage="INEQUALITY"), `include` (Usage="INCLUDE") — jede
     gefüllt mit den `Name`-Attributen der `Column`-Kinder in
     Dokument-Reihenfolge.
  3. Eine neue kleine private Hilfsmethode
     `BuildCreateIndexStatement(string table, IReadOnlyList<string> equality,
     IReadOnlyList<string> inequality, IReadOnlyList<string> include)`
     extrahieren (Linter: ≤60 LOC, separate Verantwortlichkeit, gut
     testbar). Rückgabe: `null` wenn `equality.Count == 0 && inequality.Count == 0`,
     sonst das fertige `CREATE NONCLUSTERED INDEX IX_<Table>_<...> ON <table>
     (<equality>, <inequality>) INCLUDE (<include>);` (ohne `INCLUDE`-Klausel
     wenn `include.Count == 0`).
  4. Index-Name-Konvention: `IX_<TableOhneSchemaUndKlammern>_<ErsteCol>
     [__<ZweiteCol>]` (maximal 2 Spalten, mit `__`-Trenner zur besseren
     Lesbarkeit bei mehreren Spalten). Tabellen-Name ohne `[`/`]` und
     ohne Schema-Präfix: aus `[dbo].[Orders]` wird `Orders`.
  5. Den `new PerformancePlanWarning(...)`-Aufruf um
     `MissingIndexStatement: statement` erweitern.
- **Warum:** Reiner Parser-Ausbau, keine neuen externen Abhängigkeiten.
  SQL-Server liefert die Spalten bereits sortiert (Equality zuerst,
  dann Inequality, dann Include) — keine eigene Sortierung nötig.
  Konzept-Beispiel (Zeile 161–172 in `konzept.md`) entspricht 1:1
  dieser Zusammensetzung.

### Datei 3: `src/SqlToAi/Mcp/ToolRegistry.cs` (Zeile 253–263)

- **Was:** Im Description-String von `BuildMeasurePerformance()` die
  Passage `warnings[] (type/severity/message/impact from the actual
  execution plan XML)` erweitern um einen Hinweis auf das neue Feld,
  z. B.: `warnings[] (type/severity/message/impact from the actual
  execution plan XML; MissingIndex warnings additionally include
  missing_index_statement with a ready-to-execute CREATE NONCLUSTERED
  INDEX DDL string when key columns are present, null otherwise)`.
- **Warum:** AI-Client-sichtbare Tool-Beschreibung — ohne dieses Update
  weiß der Agent nicht, dass das neue Feld existiert. Reine
  Textänderung, keine strukturelle Auswirkung.

### Datei 4: `tests/SqlToAi.Tests/Database/PerformanceMeasurementServiceTests.cs` (nach Zeile 128)

- **Was:** Drei neue Testmethoden hinzufügen, die `ParseExecutionPlanXml`
  mit verschiedenen `<ColumnGroup>`-Strukturen aufrufen:
  1. **`ParseExecutionPlanXml_MissingIndex_EqualityOnly_BuildsStatement`**
     — XML mit `Usage="EQUALITY"` (eine Spalte), kein Include.
     Erwartet: genau 1 Warning, `Type="MissingIndex"`,
     `MissingIndexStatement` enthält `CREATE NONCLUSTERED INDEX
     IX_Orders_CustomerId ON [dbo].[Orders] (CustomerId);` (exakt oder
     per `Contains`-Assertions auf die Schlüssel-Bestandteile).
  2. **`ParseExecutionPlanXml_MissingIndex_EqualityPlusInequalityPlusInclude_BuildsFullStatement`**
     — XML mit allen drei `ColumnGroup`-Typen wie im Konzept-Beispiel
     (Zeile 161–172). Erwartet: `MissingIndexStatement` enthält
     `CREATE NONCLUSTERED INDEX`, `(CustomerId, OrderDate)`,
     `INCLUDE (Amount, Status)`, `;` am Ende.
  3. **`ParseExecutionPlanXml_MissingIndex_EqualityOnlyWithInclude_BuildsStatementWithInclude`**
     — XML mit `EQUALITY` (eine Spalte) + `INCLUDE` (zwei Spalten), ohne
     `INEQUALITY`. Erwartet: `MissingIndexStatement` enthält
     `ON [dbo].[Orders] (CustomerId) INCLUDE (Amount, Status);`.
  - **Bestehender Test bleibt unverändert grün** (er deckt den Fall
    ohne `ColumnGroup`-Kinder ab → `MissingIndexStatement == null`,
    was die bestehenden `Count`/`Type`/`Impact`-Assertions nicht
    bricht).
- **Warum:** DoD aus `konzept.md` (Zeile 204–206): „neue Tests decken
  Equality-only, Equality+Inequality, mit/ohne Include-Spalten ab."

### Datei 5: `docs/architecture-spec.md` §4 Nr. 14 (Zeile 280–290)

- **Was:** Im Tool-Eintrag `sql_measure_performance` die Beschreibung
  der `warnings[]`-Rückgabestruktur erweitern. Aktueller Text (Zeile
  284–286): `warnings[] (je type/severity/message/impact aus dem
  tatsächlichen Ausführungsplan-XML)`. Ergänzen: bei `MissingIndex`-
  Warnings ist zusätzlich `missing_index_statement` (string, nullable)
  enthalten — das fertige `CREATE NONCLUSTERED INDEX`-DDL; `null` wenn
  keine Schlüsselspalten (kein baubares Statement).
- **Warum:** Doku-Sync-Pflicht aus
  `SqlToAiRichtlinien.mdc` §4 (Zeile 61). Konsistenz zwischen Code-
  Verhalten und öffentlicher Spezifikation.
- **Nicht Teil dieses Steps:** Der `VIEW SERVER STATE`-Eintrag in §H
  sowie ein neuer Tool-Eintrag §4 Nr. 16 für `sql_suggest_indexes`
  gehören zu EPIC-02, nicht hierhin. Die README-Tool-Zählung
  (15 → 16) ebenfalls erst in EPIC-02.

### Datei 6: `README.md` Zeile 13

- **Was:** Im `sql_measure_performance`-Feature-Bullet den Halbsatz
  `... for missing index recommendations, table scans, and implicit
  data type conversions ...` präzisieren zu `... for missing index
  recommendations (with ready-to-execute CREATE NONCLUSTERED INDEX
  DDL statements per warning), table scans, and implicit data type
  conversions ...`.
- **Warum:** Doku-Sync-Pflicht; macht das neue Verhalten für einen
  Leser der README sofort sichtbar, ohne dass er in den Architecture
  Spec schauen muss. **Nicht** die Tool-Zählung (15 → 16) anpassen —
  das ist EPIC-02.

## Tests

- [ ] `ParseExecutionPlanXml_MissingIndex_EqualityOnly_BuildsStatement`
      — Equality-Spalte allein, kein Include. Erwartet exaktes DDL im
      Statement-Feld.
- [ ] `ParseExecutionPlanXml_MissingIndex_EqualityPlusInequalityPlusInclude_BuildsFullStatement`
      — alle drei ColumnGroup-Typen. Erwartet vollständiges DDL inkl.
      `INCLUDE`-Klausel.
- [ ] `ParseExecutionPlanXml_MissingIndex_EqualityOnlyWithInclude_BuildsStatementWithInclude`
      — Equality + Include, ohne Inequality. Erwartet DDL mit
      `INCLUDE`-Klausel.
- [ ] Bestehender Test
      `ParseExecutionPlanXml_MissingIndexAndImplicitConversion_ParsesWarningsCorrectly`
      bleibt grün (`Count == 3`, `Type`/`Impact`-Assertions nicht
      gebrochen).
- [ ] Bestehende `MeasurePerformanceAsync_*`-Tests bleiben grün
      (keine Änderung am Validierungs-/Security-Pfad).

## Definition of Done

- [ ] `PerformancePlanWarning` hat das neue optionales Feld
      `MissingIndexStatement` (`string?`, JSON-Property
      `missing_index_statement`).
- [ ] `ExtractMissingIndexWarnings` setzt das Feld für
      Missing-Index-XML-Elemente mit mindestens einer EQUALITY-
      oder INEQUALITY-Spalte; belässt es sonst auf `null`.
- [ ] `BuildCreateIndexStatement` (oder gleichnamige private Helfer)
      ist eine separate private static Methode, hält AiNetLinter-
      Grenzwerte ein (≤60 LOC, CC ≤ 12, Cognitive ≤ 15).
- [ ] Drei neue Tests (siehe Abschnitt „Tests") vorhanden und grün.
- [ ] Bestehende Tests grün.
- [ ] `dotnet build` grün, keine neuen Compiler-Warnungen
      (`TreatWarningsAsErrors=true`).
- [ ] `dotnet test` grün, inkl.
      `AiNetLinterTests.RecreateBaseline` (aktualisiert Baseline
      automatisch — kein manuelles Hash-Rechnen).
- [ ] `docs/architecture-spec.md` §4 Nr. 14: erweiterte
      `warnings[]`-Beschreibung mit Hinweis auf
      `missing_index_statement`.
- [ ] `README.md` Zeile 13: präzisiertes `sql_measure_performance`-
      Feature-Bullet.
- [ ] `ToolRegistry.BuildMeasurePerformance()`-Description (Zeile
      253–263) erwähnt das neue Feld.
- [ ] Commit auf Branch `main` (Conventional Commit, Deutsch,
      imperativ, Subject ≤ 72 Zeichen, Suffix
      `[sql-index-suggestions]`).
- [ ] `step-001/step-result.md` geschrieben.

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4` — **Dokumentations-
  Synchronisation (Pflicht)**: Code-Änderung → architecture-spec.md +
  README.md ohne Aufforderung mit-aktualisieren. Inhaltlich
  abgedeckt durch Datei 5 und 6 in „Konkrete Änderungen".
- `.agents/rules/SqlToAiRichtlinien.mdc#4` — **Commits (Pflicht)**:
  Conventional Commit, Deutsch, imperativ, Suffix
  `[sql-index-suggestions]`, autonom in sinnvollen Abständen.
- `.agents/rules/SqlToAiRichtlinien.mdc#5` — **AiNetLinter-Hinweis**:
  `RecreateBaseline` läuft automatisch in jedem `dotnet test`, kein
  manuelles Hash-Rechnen.
- `.agents/rules/AiNetLinter.mdc` — **Grenzwerte Produktion**:
  `MaxLineCount` 500, `MaxMethodLineCount` 60 (mit Compound-
  Suppression bis 150 bei CC ≤ 3 & Cognitive ≤ 5),
  `MaxCyclomaticComplexity` 12, `MaxCognitiveComplexity` 15,
  `MaxMethodParameterCount` 4 (Record-Pattern nicht nötig für
  `BuildCreateIndexStatement` mit 4 Parametern, exakt am Limit).
  `sealed`-Klassen, `#nullable enable`, keine leeren `catch`-Blöcke
  sind ohnehin in der bestehenden Datei eingehalten und bleiben es.

## Bekannte Ausnahmen

- Keine. Die `MissingIndex`-XML-Variante ohne jegliche `ColumnGroup`-
  Kinder (bestehender Test-Fall) liefert `MissingIndexStatement =
  null`; das ist kein Fehler, sondern dokumentierter Edge-Case (kein
  baubares Index-Statement ohne Schlüsselspalten). Im JSON-Output
  erscheint das Feld nicht (`JsonIgnoreCondition.WhenWritingNull`).

## Code-Skizze (optional)

```csharp
// In PerformanceMeasurementService.cs, neue private static Methode:
private static string? BuildCreateIndexStatement(
    string table,
    IReadOnlyList<string> equality,
    IReadOnlyList<string> inequality,
    IReadOnlyList<string> include)
{
    if (equality.Count == 0 && inequality.Count == 0)
    {
        return null;
    }

    // Tabellen-Name für IX_Namen: Schema+Brackets weg, dot auch weg.
    // "[dbo].[Orders]" -> "Orders"
    var tableForName = table
        .Replace("[", string.Empty, StringComparison.Ordinal)
        .Replace("]", string.Empty, StringComparison.Ordinal);
    var dotIndex = tableForName.IndexOf('.');
    if (dotIndex >= 0)
    {
        tableForName = tableForName[(dotIndex + 1)..];
    }

    var keyCols = equality.Concat(inequality).ToList();
    var nameParts = new[] { tableForName }.Concat(keyCols.Take(2));
    var indexName = "IX_" + string.Join("__", nameParts);

    var keyClause = string.Join(", ", keyCols);
    var sb = new System.Text.StringBuilder();
    sb.Append("CREATE NONCLUSTERED INDEX ").Append(indexName)
      .Append(" ON ").Append(table)
      .Append(" (").Append(keyClause).Append(')');
    if (include.Count > 0)
    {
        sb.Append(" INCLUDE (").Append(string.Join(", ", include)).Append(')');
    }
    sb.Append(';');
    return sb.ToString();
}
```

## Notes

- **Wiederverwendete Strukturen:** keine neue Klasse, keine neue
  Schnittstelle. Erweiterung eines bestehenden `record` + Erweiterung
  einer bestehenden privaten Methode + ein neuer privater statischer
  Helper in derselben Datei. JSON-Serialisierung läuft vollautomatisch
  über `McpJsonContext` (kein Dispatcher-Change, kein
  Schema-Generator-Change — der `JsonSerializable(typeof(PerformancePlanWarning))`-
  Eintrag greift ohnehin).
- **Sortierung:** SQL-Server-XML liefert ColumnGroups in der
  Dokument-Reihenfolge (Equality, dann Inequality, dann Include); die
  Spalten innerhalb einer ColumnGroup sind ebenfalls in
  Schlüsselreihenfolge. Keine eigene Sortierung nötig.
- **Schema/Table-Format:** `Table="[dbo].[Orders]"` ist im XML ein
  einzelnes Attribut mit Bracket-Notation. Übernahme 1:1 in die
  `ON [Schema].[Table]`-Klausel. Für den Index-Namen werden
  Brackets und Schema-Präfix entfernt.
- **Index-Name-Länge:** SQL-Server erlaubt bis zu 128 Zeichen für
  Index-Namen. Bei nur 1 Equality-Spalte + 1 Inequality-Spalte liegt
  der Name (`IX_Orders_CustomerId__OrderDate`) sicher unter diesem
  Limit. Tabellen mit langen Namen + viele Spalten könnten das Limit
  reissen — das ist ein bekannter Trade-off, im Konzept nicht
  weiter spezifiziert. Pragmatische Lösung: max. 2 Spalten im Namen
  (siehe `Take(2)` in der Skizze). Wenn die ersten 2 Spalten +
  Tabellen-Name das Limit überschreiten, wird der Name ohnehin
  abgeschnitten / kollidiert — der Agent erkennt das und korrigiert
  selbst. **Kein Hard-Error.**
- **Tool-Count (15 → 16) und §H `VIEW SERVER STATE`:** explizit
  **nicht** Teil dieses Steps. Gehört zu EPIC-02.
- **JSON-Property-Name:** `missing_index_statement` (snake_case,
  konsistent mit dem bestehenden Schema
  `type`/`severity`/`message`/`impact`).
