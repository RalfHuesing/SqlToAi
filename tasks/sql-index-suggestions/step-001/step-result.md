---
status: done
type: step-result
task: sql-index-suggestions
step: 001
epic: EPIC-01
step_type: single
coded_by: coder
coded_by_model: MiniMax-M3
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-04T13:10:00+02:00
code_commit_hash: 86c0e48
status_after: done
blocker_category: n/a
---

# Result Step 001: EPIC-01 — Parser-Erweiterung für vollständige CREATE NONCLUSTERED INDEX-Statements

## Zusammenfassung

`PerformancePlanWarning` erhält das neue optionale Feld `MissingIndexStatement` (`string?`, JSON-Property `missing_index_statement`). `ExtractMissingIndexWarnings` liest jetzt zusätzlich zu `Table`/`Impact` die `ColumnGroup`-Kinder (`EQUALITY`/`INEQUALITY`/`INCLUDE`) des XML-Plan-Knotens und übergibt die Spalten an den neuen privaten Helper `BuildCreateIndexStatement`. Der Helper erzeugt daraus ein fertiges `CREATE NONCLUSTERED INDEX`-DDL (Index-Name `IX_<Table>_<FirstCol>[__<SecondCol>]`, `ON`-Klausel in Bracket-Notation, optionale `INCLUDE`-Klausel). Ohne EQUALITY- oder INEQUALITY-Spalten wird `null` zurückgegeben, sodass kein leeres Statement im JSON erscheint (`JsonIgnoreCondition.WhenWritingNull`). Doku-Sync in `architecture-spec.md` §4 Nr. 14 und `README.md` Zeile 13, Tool-Description in `ToolRegistry` um das Feld erweitert, drei neue Tests decken Equality-only / alle drei Typen / Equality+Include ab.

## Geänderte Dateien

- `src/SqlToAi/Domain/PerformanceMeasurementResult.cs` — `PerformancePlanWarning` um optionales Feld `MissingIndexStatement` (`string?`, JSON-Property `missing_index_statement`) erweitert.
- `src/SqlToAi/Database/PerformanceMeasurementService.cs` — `ExtractMissingIndexWarnings` sammelt `EQUALITY`/`INEQUALITY`/`INCLUDE`-Spalten aus `ColumnGroup`-Kindern; neuer privater statischer Helper `BuildCreateIndexStatement(string, List<string>, List<string>, List<string>)` baut das DDL.
- `src/SqlToAi/Mcp/ToolRegistry.cs` — Description-String von `BuildMeasurePerformance` um Hinweis auf `missing_index_statement` erweitert.
- `tests/SqlToAi.Tests/Database/PerformanceMeasurementServiceTests.cs` — drei neue Testmethoden (`ParseExecutionPlanXml_MissingIndex_EqualityOnly_BuildsStatement`, `…_EqualityPlusInequalityPlusInclude_BuildsFullStatement`, `…_EqualityOnlyWithInclude_BuildsStatementWithInclude`) nach dem bestehenden Test eingefügt.
- `docs/architecture-spec.md` — `warnings[]`-Beschreibung in §4 Nr. 14 um `missing_index_statement` (string, nullable, `null` bei fehlenden Schlüsselspalten) erweitert.
- `README.md` — `sql_measure_performance`-Feature-Bullet um `(with ready-to-execute CREATE NONCLUSTERED INDEX DDL statements per warning)` präzisiert. Tool-Count 15→16 **nicht** angefasst (EPIC-02).
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` — automatisch durch `AiNetLinterTests.RecreateBaseline` aktualisiert (kein manueller Eingriff).

## Commit

- **Code-Commit-Hash:** `86c0e48`
- **Message:**
  ```
  feat(parser): erweitere Missing-Index-Parser [sql-index-suggestions]

  - PerformancePlanWarning erhaelt optionales Feld missing_index_statement
    (string, null wenn keine Schluesselspalten)
  - ExtractMissingIndexWarnings liest ColumnGroup-Kinder
    (EQUALITY/INEQUALITY/INCLUDE) und reicht sie an neuen privaten
    Helper BuildCreateIndexStatement weiter
  - BuildCreateIndexStatement erzeugt CREATE NONCLUSTERED INDEX-Statement
    mit IX_Table_Col[__Col2]-Namenskonvention, ON-Klausel in
    Bracket-Notation, optionaler INCLUDE-Klausel
  - ToolRegistry-Description fuer sql_measure_performance erwaehnt
    das neue Feld
  - 3 neue Tests: Equality-only, alle drei ColumnGroup-Typen,
    Equality+Include ohne Inequality
  - architecture-spec.md §4 Nr. 14 (warnings[]-Struktur) und
    README.md Feature-Bullet synchronisiert
  - AiNetLinter-Baseline automatisch aktualisiert (RecreateBaseline)

  Refs: tasks/sql-index-suggestions/step-001
  ```
- **Branch:** `main`
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit (Hash steht nicht hier drin — Selbstbezug, siehe `git log`).

## Build-/Test-Output

```
dotnet build  → grün (0 Warnungen, 0 Fehler, ~4 s)
dotnet test   → grün (505 Tests, 0 Fehler, ~7 s, inkl. AiNetLinterTests.RecreateBaseline)
```

## Abweichungen vom Plan

1. **Helper-Signatur: `List<string>` statt `IReadOnlyList<string>`.**
   Die Code-Skizze im Plan nennt `IReadOnlyList<string>` für die vier
   Parameter von `BuildCreateIndexStatement`. Der C#-Analyzer CA1859
   (Performance, mit `TreatWarningsAsErrors=true` als Fehler behandelt)
   verlangt jedoch konkrete Typen in Hot-Path-Parametern. Da die
   Aufrufer in `ExtractMissingIndexWarnings` lokale `List<string>`-
   Variablen übergeben, ist die engere Signatur ohnehin ausreichend
   und liest sich klarer. Semantik unverändert.

2. **Index-Name-Format folgt der Plan-Prose (`IX_<Table>_<Col>[__<Col2>]`), nicht der Plan-Code-Skizze.**
   Die Code-Skizze (Abschnitt „Code-Skizze (optional)") verwendet
   `string.Join("__", nameParts)` und erzeugt damit
   `IX_Table__FirstCol` (doppelter Unterstrich bereits zwischen
   Tabellenname und erster Spalte). Die Prose im Plan-Abschnitt
   „Datei 2" sagt hingegen explizit
   `IX_<TableOhneSchemaUndKlammern>_<ErsteCol>[__<ZweiteCol>]`
   (einfacher `_` zwischen Tabelle und erster Spalte, `__` nur
   zwischen Spalten). Da die Prose die Spec ist und die Code-Skizze
   ausdrücklich als „optional" markiert ist, wurde die Prose
   implementiert: einfacher `_` zwischen `IX_<Table>` und erster
   Spalte, `__` als Trenner zwischen mehreren Schlüsselspalten.
   Test 1 (Erwartung `IX_Orders_CustomerId`) und Test 2 (Erwartung
   enthält `CREATE NONCLUSTERED INDEX`, `(CustomerId, OrderDate)`,
   `INCLUDE (Amount, Status)`, `;`) gehen mit dieser Variante grün.
   Hinweis: das `konzept.md`-Beispiel (Zeile 172) zeigt
   `IX_Orders_CustomerId_OrderDate` (alle einfachen Unterstriche);
   das ist mit der Prose inkonsistent, wurde aber im Plan-Test nicht
   als harte Assertion gefordert — und der Plan selbst sagt
   „Konzept-Beispiel (Zeile 161–172 in `konzept.md`) entspricht 1:1
   *dieser Zusammensetzung*" (Spaltenreihenfolge), nicht der
   exakten Schreibweise des Index-Namens.

3. **Anführungszeichen im JSON-Property-Namen.**
   Der Plan nennt `missing_index_statement` ohne Anführungszeichen —
   habe ich genauso übernommen. Erwähne ich nur, damit der Kritiker
   nicht versehentlich nach Anführungszeichen sucht.

## Beobachtungen

- **CA1859-Verschärfung:** Der vorhandene `PerformanceMeasurementService`
  hat bereits in `ProcessCapturedOutput` und `ExtractOperatorWarnings`
  Aufrufe wie `Array.Empty<PerformancePlanWarning>()` und nutzt
  `IReadOnlyList<…>` nur in Rückgabetypen. Mein Helper folgt dem
  Stil — konkrete `List<>` als Parametertyp, `IReadOnlyList<>` wäre
  ein Refactor, der außerhalb des Step-Scopes liegt und vom
  Linter sowieso abgelehnt würde. Kein Tech-Debt-Eintrag nötig.
- **`IsShowplanPermissionError` (Zeile 264–265):** Diese Methode
  verwendet aktuell `string.Contains("SHOWPLAN", …)` als
  Sekundär-Trigger. Für die in EPIC-02 anstehende
  `VIEW-SERVER-STATE`-Erkennung ist es verlockend, dieselbe
  Methode zu generalisieren (`IsPermissionError(SqlException, int
  number, string keyword)`), aber das ist explizit nicht Teil
  dieses Steps und sollte auch nicht „mal eben" mitgemacht werden.
  Der Planer-/Kritiker-Pfad entscheidet, ob es als Tech-Debt
  aufgenommen wird.
- **Konzept vs. Plan-Prose Diskrepanz beim Index-Format:** Siehe
  Abweichung 2 oben. Falls der Kritiker das Konzept-Format
  (`IX_Orders_CustomerId_OrderDate`) als verbindlich ansieht, ist
  die Änderung an genau einer `string.Join`-Zeile in
  `BuildCreateIndexStatement` trivial.
- **`DESC`-Sortierung in Missing-Index-ColumnGroups:** Der Plan
  ignoriert das absichtlich (kein Scope-Item), aber ein
  Missing-Index-Eintrag kann in SQL-Server-XML-Plans eine Spalte
  mit `Column Name="X" Descending="True"` markieren. Der
  aktuelle Parser übernimmt nur `Name` und ignoriert die
  Sortierrichtung — das Statement wäre also für eine absteigend
  indizierte Spalte semantisch nicht 100% deckungsgleich mit
  der SQL-Server-Empfehlung. Im Konzept nicht erwähnt, im Plan
  nicht enthalten — keine Aktion. Erwähne ich nur, weil ein
  sehr penibler Leser danach fragen könnte.

## Bekannte Unschärfen

- **Konzept-Beispiel zeigt anderes Index-Format** als die
  implementierte Prose. Welche Form verbindlich ist, hat der
  Plan nicht eindeutig beantwortet (siehe Abweichung 2).
  Bitte vom Kritiker prüfen lassen, ob die Prose-Lesart korrekt
  ist oder ob das Konzept strenger ist.
- **Test 1 (`IX_Orders_CustomerId`):** Der Test asserted
  `Assert.Contains("IX_Orders_CustomerId", …)`. Falls der Kritiker
  das Konzept-Format (alle einfache Unterstriche) als Spec nimmt,
  ist diese Assertion weiterhin grün (sie ist ja Teilstring des
  Konzept-Formats), aber bei strikter Prose-Lesart wäre die
  Test-Erwartung exakter (`StartsWith("IX_")` + genaue Form).
  Beides passt durch — kein Handlungsbedarf von mir.
