---
status: draft
type: konzept
project_kind: brownfield
estimated_scope: medium
rules_dir: .agents/rules
last_updated: 2026-08-03T09:25:00Z
open_questions:
  - Sollen wir für den Fall, dass SHOWPLAN-Berechtigungen fehlen, einen synthetischen Hinweis/Fallback an den Agenten zurückgeben?
  - Sollen Parameter für Abfragen in allen drei Tools unterstützt werden (z. B. JSON-Dictionary `params`), um Parameter-Sniffing realistisch zu testen?
---

# Konzept: MCP-Tools für SQL-Performance-Messung & Äquivalenzvergleich

## Ziel (Was)

Erweiterung von `SqlToAi` um drei dedizierte MCP-Tools, mit denen KI-Agenten zwei SQL-Abfragen auf semantische Gleichheit (Schema, Datentypen, Zeilenanzahl und Zeileninhalte) vergleichen und deren Performance (Laufzeit, Logical Reads, CPU-Zeit, Execution-Plan-Analysen wie Missing Indexes) präzise und empirisch messen können. Die Tools liefern rein technische Analyseergebnisse und kompakte Metriken an den Agenten (kein Streaming von großen Ergebnismengen).

## Warum / Kontext

Wenn KI-Agenten SQL-Abfragen optimieren, arbeiten sie ohne empirische Feedbackschleife oft "im Blindflug". Sie können aktuell weder sicherstellen, dass die optimierte Abfrage exakt dieselben Ergebnisse liefert, noch fundiert bewerten, ob eine Änderung die Performance verbessert hat. Reine Client-Laufzeiten sind durch Caching und Serverlast unzuverlässig; direkte Datenvergleiche im Agenten-Kontext scheitern an Datenmengen.

## Scope

### Muss-Haben

- **Tool 1: Ergebnis- & Äquivalenzvergleich (`sql_compare_queries`):**
  - **Schema-Check:** Spaltenanzahl, Spaltennamen/Aliasse und Datentypen.
  - **Count-Check:** Exakter Zeilenanzahl-Vergleich.
  - **Inhalts-Check (DB-seitig):** SQL-basierter Set-Differenz-Vergleich (`EXCEPT` / `UNION ALL`) ohne Übertragung aller Daten zum Client.
  - **Diff-Feedback:** Falls ungleich, Rückgabe von kompakten Diffs (Beispielzeilen), die in A oder B fehlen.
- **Tool 2: Performance-Messung (`sql_measure_performance`):**
  - **Hard-Metriken:** Server-seitige CPU-Zeit, Elapsed Time, Logical Reads / Physical Reads (via `STATISTICS IO, TIME`).
  - **Execution-Plan-Analyse (Actual Execution Plan):** Extraktion kompakter Warnungen aus dem XML-Plan (z. B. `Missing Indexes`, `Implicit Conversions`, `Table Scans` mit hoher Cost).
  - **Graceful Degradation bei fehlenden Berechtigungen:** Wenn dem DB-User das `SHOWPLAN`-Recht fehlt, schlägt das Tool nicht fehl, sondern liefert IO/Time-Metriken + Warnung ("SHOWPLAN permission missing").
  - **Warmup & Averaging:** Unterstützung für Mehrfachausführungen zur Caching-Kompensation.
- **Tool 3: Kombi-Benchmark (`sql_benchmark_optimization`):**
  - Führt Vergleich + Performance-Messung beider Abfragen in einem Schritt durch und liefert einen direkten Vorher-Nachher-Vergleichsbericht.
- **Sicherheits- & Doku-Vorgaben:**
  - Weiterhin strikte Einhaltung des Read-Only Guards (keine ändernden Queries).
  - Explizite Dokumentation benötigter DB-Berechtigungen (z. B. `GRANT SHOWPLAN TO ...`).

### Nice-to-Have (optional, spätere Iteration)

- **Graphische/Textuelle Plan-Visualisierung:** Weiterführende Baumdarstellung des Abfrageplans bei komplexen Joins.

### Non-Goals (bewusst NICHT Teil davon)

- **Automatisches Umschreiben / Optimieren von SQL:** Das Tool bietet nur die Mess- und Prüfinfrastruktur; die eigentliche SQL-Optimierung bleibt die Aufgabe des KI-Agenten.
- **Streaming großer Datenmengen:** Keine Rückgabe von Millionen Ergebnissätzen an den Agenten.
- **Rohes XML-Plan-Dumping:** Kein ungeparstes XML im LLM-Kontext (verhindert Prompt-Bloat).

## Zielplattformen / Technischer Rahmen

- **Language & Runtime:** .NET 10 / C# 14 (Standard von SqlToAi).
- **Datenbanken:** Primärer Fokus auf MS SQL Server (`STATISTICS IO/TIME`, `STATISTICS XML`).
- **Execution Engine:** Nutzung bestehender Dapper- / DbConnection-Strukturen in `SqlToAi.Database`.

## Verworfene Alternativen

- **Client-seitiger Vollvergleich aller Datenzeilen im .NET-Server:** Verworfen wegen hohem Speicher- und Netzwerk-Overhead bei großen Ergebnismengen. DB-seitige Set-Differenz (`EXCEPT`) ist drastisch effizienter.
- **Reines Messen der C#-Stopwatch Laufzeit:** Verworfen, da Client-Laufzeit durch Caching, Netzwerklatenz und Serverlast stark verfälscht wird. Server-Metriken (besonders Logical Reads) sind erforderlich.
- **Ungeparstes Ausführen von `SET SHOWPLAN_XML ON`:** Verworfen, da `SHOWPLAN_XML` die Abfrage gar nicht ausführt (nur Estimated Plan). Wir nutzen `STATISTICS XML ON` für den *tatsächlichen* Ausführungsplan.

## Wo im Projekt

- [QueryExecutionService.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Database/QueryExecutionService.cs): Ausführung von Performance-Analysen (STATISTICS IO/TIME/XML) & Set-Differenz-Queries.
- [IQueryExecutionService.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Database/IQueryExecutionService.cs): Erweiterung des Interfaces für Vergleichs- und Messfunktionen.
- [ToolRegistry.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Mcp/ToolRegistry.cs): Registrierung der neuen MCP-Tools (`sql_compare_queries`, `sql_measure_performance`, `sql_benchmark_optimization`).
- [ToolDispatcher.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Mcp/ToolDispatcher.cs): Routing und Aufrufabwicklung der neuen Tools.
- [docs/](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/docs/): Dokumentation der benötigten DB-Rechte (`SHOWPLAN`).

## Entdeckte Mängel/Redundanzen

*(Aktuell keine Mängel im Bestandscode identifiziert; bestehender `TransactionIntegrityGuard` und `QueryValidationService` können wiederverwendet werden).*

## Wie (grober Ansatz)

1. **Equivalence Checker Engine:**
   - Ausführen beider Queries in Subqueries/CTEs für Schema- & Count-Checks.
   - Generieren und Ausführen einer `EXCEPT`-Differenzabfrage auf der Ziel-DB.
   - Aufbereitung eines strukturierten Diff-Ergebnisses.
2. **Performance & Plan Parser Engine:**
   - Aktivieren von `SET STATISTICS IO, TIME, XML ON` auf der Verbindung.
   - Abfangen von Berechtigungsfehlern (`SqlException` bezüglich `SHOWPLAN`).
   - Parsen der T-SQL Informational Messages (Logical Reads) und des XML-Ausführungsplans (Extraktion von `MissingIndexes`, `Warnings`, `TableScan`).
3. **MCP Tool Binding:**
   - Bereitstellen von 3 MCP-Tools mit sauber typisierten JSON-Schemas.

## Definition of Done / Erfolgskriterien

- `sql_compare_queries` erkennt exakt, ob zwei Queries inhaltsgleich sind, und schlägt fehl/meldet Diffs, sobald sich Spalten, Typen, Zeilenanzahl oder Inhalte unterscheiden.
- `sql_measure_performance` liefert reproduzierbare Metriken (Logical Reads, CPU Time, Elapsed Time) und extrahiert Ausführungsplan-Hinweise (Missing Index, Table Scan), sofern Berechtigungen vorliegen.
- Bei fehlender `SHOWPLAN`-Berechtigung schlägt die Leistungsmessung nicht fehl, sondern degradiert sauber (Warnhinweis im Ergebnis).
- Benötigte DB-Berechtigungen sind in der Projektdokumentation klar festgehalten.
- `sql_benchmark_optimization` bietet den vollständigen Vergleich zweier Queries in einem Call.
- Neue Unit- und Integrationstests in `tests/` decken Äquivalenz-, Performance- und Fallback-Analysen ab.

## Offene Punkte

- Siehe `open_questions` im Frontmatter.

