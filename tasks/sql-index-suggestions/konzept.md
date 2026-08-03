---
title: "SQL Index-Analyse & Vorschläge"
status: ready
last_updated: "2026-08-03"
rules_dir: .agents/rules
project_kind: brownfield
estimated_scope: medium
open_questions: []
---

# Konzept: SQL Index-Analyse & Vorschläge

## Ziel (Was)

Der Agent soll SQL-Server-eigene Index-Empfehlungen ohne SSMS direkt nutzbar
machen — über zwei komplementäre Mechanismen: (1) vollständige
`CREATE NONCLUSTERED INDEX`-Statements aus dem Ausführungsplan einer
einzelnen Query (Erweiterung von `sql_measure_performance`), und (2) ein
neues Tool `sql_suggest_indexes`, das serverweit kumulierte
Index-Empfehlungen aus den SQL-Server-DMVs seit dem letzten Neustart liefert
(dieselbe Quelle, aus der SSMS seine "Missing Index"-Hinweise zieht).

## Warum / Kontext

SSMS zeigt "Missing Index"-Vorschläge an, die aus SQL-Server-internen
Quellen stammen. Diese sind heute nur über SSMS einsehbar. Der MCP-Server
soll sie direkt für den KI-Agenten zugänglich machen, ohne dass ein
Mensch dafür SSMS öffnen muss.

## Scope

### Muss-Haben

- **Parser-Erweiterung (Idee 1):** `PerformanceMeasurementService.ExtractMissingIndexWarnings`
  ([PerformanceMeasurementService.cs:328](../../src/SqlToAi/Database/PerformanceMeasurementService.cs#L328))
  liefert heute nur `Table + Impact%`. Der XML-Plan enthält aber vollständige
  Spalteninformationen (`EQUALITY`/`INEQUALITY`/`INCLUDE`-ColumnGroups) — diese
  sollen zu einem vollständigen `CREATE NONCLUSTERED INDEX`-Statement direkt
  im `PerformancePlanWarning` zusammengesetzt werden.
  - Aufwand: gering, reiner Parser-Ausbau in bestehender Datei.
  - Permission: keine zusätzliche (`SHOWPLAN` reicht, bereits vorhanden).
- **Neues Tool `sql_suggest_indexes` (Idee 2):** Fragt
  `sys.dm_db_missing_index_details` + `sys.dm_db_missing_index_group_stats`
  ab — kumulativ über alle Queries seit dem letzten Server-Neustart,
  priorisiert nach `improvement_score` (`avg_user_cost × avg_user_impact ×
  (seeks + scans)`).
  - Parameter: `database` (Pflicht), `table_name` (optional, Filter),
    `min_score` (optional), `top` (optional, Default 10).
  - Ausgabe: Markdown-Tabelle mit Score, Table, Equality/Inequality/Include
    Columns, Seeks, Scans, Last Seek.
  - Permission: `VIEW SERVER STATE` — siehe Permission-Handling unten.
  - Hinweis im Tool-Output (Pflichtbestandteil, nicht optional): DMV-Daten
    sind seit dem letzten Server-Neustart akkumuliert — auf frisch
    gestarteten Servern liefert das Tool wenig/nichts, auf lang laufenden
    Prod-Servern ist es aussagekräftig.

**Beziehung der beiden Mechanismen:** komplementär, nicht konkurrierend.
Idee 1 bleibt Teil von `sql_measure_performance` (pro Einzel-Query, aus
deren Ausführungsplan). Idee 2 wird ein eigenständiges neues Tool
(serverweit, kumulativ aus DMVs, unabhängig von einer konkreten Query).
Unterschiedliche Datenquellen — kein Überschneidungskonflikt.

**Permission-Handling bei fehlender `VIEW SERVER STATE`:** Graceful
Degradation analog zum bestehenden `SHOWPLAN`-Pattern in
`PerformanceMeasurementService` ([PerformanceMeasurementService.cs:167](../../src/SqlToAi/Database/PerformanceMeasurementService.cs#L167)):
DMV-Query versuchen, Permission-Fehler abfangen, strukturierte Notiz statt
Hard-Error zurückgeben.

### Nice-to-Have (optional, spätere Iteration)

- Keine — siehe Verworfene Alternativen (Idee 3/4 wurden explizit verworfen,
  nicht auf später verschoben).

### Non-Goals (bewusst NICHT Teil davon)

- **Database Tuning Advisor (DTA) API:** Nicht per SQL erreichbar, COM-Objekt,
  Windows-only.
- **`DBCC AUTOPILOT`:** Intern, undokumentiert, nicht für Produktionseinsatz
  geeignet.
- **Automatisches Index-Erstellen:** Schreiboperation, außerhalb des
  ReadOnly-Scopes dieses MCP-Servers.
- **Ungenutzte Indizes finden (ehemals Idee 3) und Fragmentierungsanalyse
  (ehemals Idee 4):** siehe Verworfene Alternativen — bewusst nicht
  umgesetzt.

## Zielplattformen / Technischer Rahmen

Kein neuer Stack — reine Erweiterung des bestehenden .NET-10/C#-14-MCP-Servers.
Idee 1 nutzt das bestehende XML-Parsing in `PerformanceMeasurementService`.
Idee 2 folgt demselben Architektur-Pattern wie bestehende DMV-Tools (z. B.
`sql_get_object_references` über `sys.dm_sql_referencing_entities`,
[architecture-spec.md:251](../../docs/architecture-spec.md#L251)) — direkter
Dapper/`SqlClient`-Zugriff, kein neues Framework.

## Verworfene Alternativen

- **Database Tuning Advisor (DTA) API:** verworfen, weil nicht per SQL
  erreichbar (COM-Objekt, Windows-only).
- **`DBCC AUTOPILOT`:** verworfen, weil intern/undokumentiert und nicht für
  Produktionseinsatz vorgesehen.
- **Automatisches Index-Erstellen:** verworfen, weil Schreiboperation
  außerhalb des ReadOnly-Scopes.
- **Idee 3 — Ungenutzte Indizes finden (`sys.dm_db_index_usage_stats`):**
  verworfen — nicht Teil dieses Tasks, wird nicht umgesetzt.
- **Idee 4 — Fragmentierungsanalyse (`sys.dm_db_index_physical_stats`):**
  verworfen — nicht Teil dieses Tasks, wird nicht umgesetzt (zusätzlich
  fachlich riskant: die DMV kann bei großen Tabellen selbst signifikante
  I/O verursachen).
- **`sql_suggest_indexes` deckt beides ab (Idee 1 komplett darin
  aufgehen lassen):** verworfen — Idee 1 ist pro-Query und passt inhaltlich
  zu `sql_measure_performance`, Idee 2 ist serverweit-kumulativ und
  query-unabhängig. Ein gemeinsames Tool hätte zwei unterschiedliche
  Bedienmodi in einem Parameter-Interface vermischt.
- **Hard-Block über `AccessLevel` bei fehlender `VIEW SERVER STATE`:**
  verworfen zugunsten Graceful Degradation (siehe oben) — konsistent mit
  dem etablierten `SHOWPLAN`-Pattern statt eines neuen, abweichenden
  Fehlerverhaltens.

## Wo im Projekt

- [PerformanceMeasurementService.cs:328](../../src/SqlToAi/Database/PerformanceMeasurementService.cs#L328) —
  `ExtractMissingIndexWarnings`, Ansatzpunkt für die Parser-Erweiterung (Idee 1).
- [PerformanceMeasurementService.cs:264](../../src/SqlToAi/Database/PerformanceMeasurementService.cs#L264) —
  `IsShowplanPermissionError`, Vorbild für das Graceful-Degradation-Pattern,
  das `sql_suggest_indexes` bei fehlender `VIEW SERVER STATE` übernehmen soll.
- [ToolRegistry.cs](../../src/SqlToAi/Mcp/ToolRegistry.cs) — zentrale Liste
  aller MCP-Tool-Definitionen (`BuildTools()`), hier muss `sql_suggest_indexes`
  registriert werden (analog `BuildMeasurePerformance()`).
- [ToolDispatcher.cs](../../src/SqlToAi/Mcp/ToolDispatcher.cs) — Dispatch der
  eingehenden Tool-Aufrufe, Gegenstück zur Registry.
- [docs/architecture-spec.md:152](../../docs/architecture-spec.md#L152) —
  §H, Empfohlene SQL-Server-Berechtigungen — hier muss `VIEW SERVER STATE`
  als zusätzliche, für `sql_suggest_indexes` benötigte Berechtigung ergänzt
  werden.
- [docs/architecture-spec.md:199](../../docs/architecture-spec.md#L199) —
  §4, MCP Tool-Spezifikationen — neuer Eintrag Nr. 16 für `sql_suggest_indexes`,
  Eintrag Nr. 14 (`sql_measure_performance`) um die erweiterte
  `PerformancePlanWarning`-Struktur ergänzen.
- [README.md:13](../../README.md#L13) und [README.md:27](../../README.md#L27) —
  Feature-Bullet für `sql_measure_performance` sowie die Tool-Zählung
  ("15 Progressive Disclosure Schema Tools" → 16) müssen aktualisiert werden.
- [tests/SqlToAi.Tests/Database/PerformanceMeasurementServiceTests.cs](../../tests/SqlToAi.Tests/Database/PerformanceMeasurementServiceTests.cs) —
  bestehende Tests für den Parser, Vorbild für neue Testfälle zu Idee 1.
- [tests/SqlToAi.Tests/Integration/](../../tests/SqlToAi.Tests/Integration/) —
  bestehender Ordner für Integrationstests gegen eine echte DB (z. B.
  `AccessLevelProviderIntegrationTests.cs`), passender Ort für DMV-Tests zu
  `sql_suggest_indexes`, die sich nicht sinnvoll mocken lassen.

## Entdeckte Mängel/Redundanzen

Keine gefunden — es existiert noch kein DMV-basiertes kumulatives
Index-Tool und keine vergleichbare Struktur, die stattdessen
wiederverwendet werden könnte.

## Wie (grober Ansatz)

**Idee 1 — Parser-Erweiterung:**
`ExtractMissingIndexWarnings` liest zusätzlich zu `Table`/`Impact` die
`ColumnGroup`-Elemente mit `Usage="EQUALITY"|"INEQUALITY"|"INCLUDE"` und
setzt daraus ein vollständiges Statement zusammen, z. B.:

```xml
<MissingIndexGroup Impact="85.7">
  <MissingIndex Table="[dbo].[Orders]">
    <ColumnGroup Usage="EQUALITY"><Column Name="CustomerId" /></ColumnGroup>
    <ColumnGroup Usage="INEQUALITY"><Column Name="OrderDate" /></ColumnGroup>
    <ColumnGroup Usage="INCLUDE"><Column Name="Amount" /><Column Name="Status" /></ColumnGroup>
  </MissingIndex>
</MissingIndexGroup>
```

→ `CREATE NONCLUSTERED INDEX IX_Orders_CustomerId_OrderDate ON [dbo].[Orders] (CustomerId, OrderDate) INCLUDE (Amount, Status);`

Das fertige Statement landet als zusätzliches Feld in `PerformancePlanWarning`.

**Idee 2 — `sql_suggest_indexes`:**
Neuer Service (Pattern wie `PerformanceMeasurementService`/bestehende
DMV-Tools) fragt `sys.dm_db_missing_index_details` +
`sys.dm_db_missing_index_group_stats` + `sys.dm_db_missing_index_columns`
ab, berechnet `improvement_score`, filtert/sortiert nach den Parametern
und rendert als Markdown-Tabelle:

```markdown
## Missing Index Recommendations — MyDB

| Score | Table | Equality Columns | Inequality Columns | Include Columns | Seeks | Scans | Last Seek |
|------:|:------|:-----------------|:-------------------|:----------------|------:|------:|:----------|
| 1247  | dbo.Orders | CustomerId | OrderDate | Amount, Status | 45230 | 12 | 2026-08-03 |
```

Der Restart-Hinweis wird als fester Bestandteil der Tool-Ausgabe
mitgeliefert (kein separater Parameter). Bei fehlender
`VIEW SERVER STATE`-Berechtigung: Fehler abfangen (analog
`IsShowplanPermissionError`), strukturierte Notiz statt Hard-Error.

Registrierung erfolgt in `ToolRegistry.BuildTools()` /
`ToolDispatcher`, Doku-Updates in `docs/architecture-spec.md` (§4, §H)
und `README.md` gemäß Projektregel zur Dokumentations-Synchronisation
([SqlToAiRichtlinien.mdc](../../.agents/rules/SqlToAiRichtlinien.mdc)).

## Definition of Done / Erfolgskriterien

- `ExtractMissingIndexWarnings` liefert ein vollständiges
  `CREATE NONCLUSTERED INDEX`-Statement pro Missing-Index-Warning; bestehende
  Tests in `PerformanceMeasurementServiceTests.cs` bleiben grün, neue Tests
  decken Equality-only, Equality+Inequality, mit/ohne Include-Spalten ab.
- Neues Tool `sql_suggest_indexes` implementiert, in `ToolRegistry` und
  `ToolDispatcher` registriert, mit Parametern `database` (Pflicht),
  `table_name`, `min_score`, `top` (alle optional).
- Graceful Degradation bei fehlender `VIEW SERVER STATE` verifiziert (Unit-
  oder Integrationstest, der den Permission-Fehler simuliert/auslöst).
- Restart-Hinweis ist fester Bestandteil der `sql_suggest_indexes`-Ausgabe.
- Integrationstest gegen eine echte Test-DB in
  `tests/SqlToAi.Tests/Integration/` (DMV-Verhalten lässt sich nicht
  sinnvoll mocken).
- `dotnet build` und `dotnet test` grün, inkl. `AiNetLinterTests.RecreateBaseline`
  (Baseline aktualisiert sich automatisch, siehe
  [SqlToAiRichtlinien.mdc](../../.agents/rules/SqlToAiRichtlinien.mdc) §5) —
  keine neuen Compiler-Warnungen (`TreatWarningsAsErrors`).
- `docs/architecture-spec.md` aktualisiert: neuer Tool-Eintrag §4 Nr. 16,
  `VIEW SERVER STATE` in §H ergänzt, erweiterte `PerformancePlanWarning`-Struktur
  bei Tool Nr. 14 dokumentiert.
- `README.md` aktualisiert: Feature-Bullet + Tool-Zählung (15 → 16).
- Commits pro abgeschlossenem Schritt (Parser-Erweiterung, neues Tool,
  Doku-Sync), Conventional-Commit-Format, Deutsch, imperativ.

## Offene Punkte

Keine.
