---
status: draft
type: konzept
project_kind: brownfield
estimated_scope: medium
rules_dir: .agents/rules
last_updated: 2026-08-03T11:13:00+02:00
open_questions:
  - Soll `elapsedMs` in `sql_execute_query` standardmäßig immer in den Metadaten zurückgegeben werden oder über ein optionales Parameter-Flag (`include_timing`)?
  - Sollen T-SQL `DECLARE`-Anweisungen am Anfang einer Query (z. B. in Site-SQL-Dateien) als ein einzelner Read-Only Batch erlaubt werden?
---

# Konzept: Erweiterte Performance-Metadaten & T-SQL-Batch-Support in SqlToAi

## Ziel (Was)

Erweiterung von `SqlToAi` um direkte Laufzeit-Metadaten (`elapsedMs`, `rowCount`) in `sql_execute_query` sowie Unterstützung für T-SQL-Skript-Batches mit `DECLARE`-Variablen-Deklarationen in lesenden Abfragen. Dies ermöglicht es KI-Agenten, bestehende Site-SQL-Dateien 1:1 auszuführen und deren Performance direkt im MCP-Kontext zu bewerten, ohne auf externe PowerShell-Skripte oder `sqlcmd` ausweichen zu müssen.

## Warum / Kontext

In realen Optimierungs-Workflows (z. B. Aumann Plantafel / ERP-Analysen) stehen KI-Agenten vor zwei Hürden:
1. Reale SQL-Skriptdateien beginnen häufig mit `DECLARE @Mandant int = 3; DECLARE @Filter ...;` gefolgt vom Haupt-`SELECT`. Aktuell blockiert `SqlMultiStatementDetector` solche Skripte pauschal als Mehrfach-Statements.
2. Bei regulären Abfragen über `sql_execute_query` fehlte bisher ein direkter `elapsedMs`-Wert im Ergebnis-Header/Metadata, um schnelle Laufzeit-Tendenzen sofort ohne Aufruf des dedizierten Mess-Tools zu erkennen.

Cursor und andere Agenten wichen deshalb auf benutzerdefinierte PowerShell/SqlClient-Skripte aus. Durch diese Erweiterung bleibt der Agent vollständig innerhalb von `SqlToAi`.

## Scope

### Muss-Haben

- **`elapsedMs` Metadaten in `sql_execute_query`:**
  - Rückgabe der serverseitigen/ausgeführten Laufzeit in Millisekunden (`elapsedMs`) und Zeilenanzahl (`rowCount`) in den Tool-Metadaten / Antwortstrukturen.
- **T-SQL `DECLARE`- & Single-Batch-Support in `sql_execute_query` & `sql_measure_performance`:**
  - Erlauben von `DECLARE @Var Typ = Wert;`-Skriptblöcken am Anfang einer lesenden Abfrage, sofern das Gesamtskript weiterhin schreibgeschützt (read-only) ist und keine DML/DDL-Mutationen enthält.
- **Konsistente Metriken-Rückgabe:**
  - Erweiterung von `QueryExecutionResult` um `ElapsedTimeMs` und `RowCount`.

### Nice-to-Have (optional, spätere Iteration)

- **`logicalReads` & `cpuMs` als optionale Quick-Stats in `sql_execute_query`:**
  - Schnelle Anzeige serverseitiger Reads direkt in der Ausführungsantwort.

### Non-Goals (bewusst NICHT Teil davon)

- **Freigabe echter Multi-Statement DML-Ketten:**
  - Keine Aufhebung des Schutzes gegen mutierende Mehrfach-Statements (`INSERT; DELETE; UPDATE`). Nur `DECLARE` + lesendes `SELECT` / CTEs sind im Scope.

## Zielplattformen / Technischer Rahmen

- **Language & Runtime:** .NET 10 / C# 14.
- **Engine:** Dapper & `Microsoft.Data.SqlClient`.
- **Parsing:** Anpassen von `SqlMultiStatementDetector.cs` und `ReadOnlyGuard.cs`, um vorangestellte `DECLARE`-Variablenblöcke als einen einzelnen logischen Read-Only-Batch zu klassifizieren.

## Verworfene Alternativen

- **Verweis auf externe PowerShell-SqlClient-Skripte:** Verworfen, da KI-Agenten nativ über MCP arbeiten sollen, ohne lokales Skript-Scaffolding zu erzeugen.
- **Erzwingen von `sql_measure_performance` für jede einfache Abfrage:** Verworfen, da Entwickler und Agenten auch bei `sql_execute_query` eine schnelle Rückmeldung über `elapsedMs` erwarten.

## Wo im Projekt

- [QueryExecutionService.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Database/QueryExecutionService.cs): Erfassung der Stopuhr-/Server-Laufzeit (`ElapsedTimeMs`) und Weitergabe im `QueryExecutionResult`.
- [SqlMultiStatementDetector.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Security/SqlMultiStatementDetector.cs): Differenzierte Erkennung von `DECLARE`-Blöcken vor lesenden Statements.
- [ToolDispatcher.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Mcp/ToolDispatcher.cs): Ergänzung der Laufzeit-Notiz/Metadaten im `ToolCallResult` für `sql_execute_query`.
- [ToolRegistry.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Mcp/ToolRegistry.cs): Aktualisierung der Parameter- und Tool-Beschreibungen.

## Entdeckte Mängel/Redundanzen

- **Pauschales Blockieren von `DECLARE`-Skripten:**
  - **Gefunden:** `SqlMultiStatementDetector.cs` prüft auf Semikolons und blockiert `DECLARE @x int = 1; SELECT ...`.
  - **Bezug:** Richtlinien zur flexiblen SQL-Analyse.
  - **Vorschlag:** Erkennen von `DECLARE`-Variablenvereinbarungen als erlaubte Variablenblöcke für lesende Abfragen.
  - **Entscheidung:** übernommen ins Scope (→ siehe Muss-Haben „T-SQL `DECLARE`- & Single-Batch-Support“).

## Wie (grober Ansatz)

1. **Stoppuhr & Metadaten in `QueryExecutionService`:**
   - Messen der Netto-Ausführungszeit beim `ExecuteReaderAsync` und Zurückgeben im `QueryExecutionResult`.
   - Einbinden der `elapsedMs` in den Tool-Antworttext in `ToolDispatcher.cs`.
2. **`DECLARE`-Batch-Erkennung:**
   - Anpassen von `SqlMultiStatementDetector`, sodass `DECLARE @var data_type [= expr];` am Anfang einer Abfrage erlaubt ist, solange kein mutierender SQL-Befehl folgt.

## Definition of Done / Erfolgskriterien

- `sql_execute_query` liefert bei jeder Abfrage `elapsedMs` und `rowCount` in den Rückgabedaten.
- Abfragen mit vorangestellten `DECLARE @...`-Variablendeklarationen (z. B. 1:1 kopierte Site-SQL-Dateien) werden von `sql_execute_query` und `sql_measure_performance` fehlerfrei ausgeführt, sofern sie rein lesend sind.
- Alle Unit- und Integrationstests laufen grün durch.
- `docs/mcp-specification.md` und `README.md` sind aktualisiert.

## Offene Punkte

- Soll `elapsedMs` in `sql_execute_query` standardmäßig immer in den Metadaten zurückgegeben werden oder über ein optionales Parameter-Flag (`include_timing`)?
- Sollen T-SQL `DECLARE`-Anweisungen am Anfang einer Query (z. B. in Site-SQL-Dateien) als ein einzelner Read-Only Batch erlaubt werden?
