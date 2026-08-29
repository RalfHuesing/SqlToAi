---
status: ready
type: konzept
project_kind: brownfield
estimated_scope: medium
rules_dir: .agents/rules
last_updated: 2026-08-29
open_questions: []
---

# Konzept: SQL-Ausführung über Skriptdateien (`sql_execute_file`)

## Ziel (Was)

Einführung eines neuen, dedizierten MCP-Tools `sql_execute_file`, mit dem Agenten und Entwickler lokale `.sql`-Dateien (über absolute oder relative Pfade) direkt auf einer Ziel-Datenbank ausführen können. Das Tool unterstützt Multi-Batch-Skripte (inklusive `GO`-Separatoren), konfigurierbares Transaktionsverhalten (`use_transaction`), liefert präzise Fehlerdiagnosen mit Batch-/Zeilennummern und gibt strukturierte Markdown-Berichte inkl. Ausführungsmetriken und Daten zurück.

## Warum / Kontext

- **Context-Window & Token-Schonung:** Agenten müssen große SQL-Dateien nicht erst vollständig in den LLM-Context laden und als riesige Strings über die MCP-Schnittstelle schicken.
- **Nahtloser Agenten-Workflow:** Ein Agent kann ein per `write_to_file` generiertes SQL-Skript (z. B. für komplexe Reports oder Schema-Updates) direkt zur Ausführung anstoßen.
- **Schlankes Tooling:** `sql_execute_file` bündelt Ausführung, Performancemetriken und Syntax-/Laufzeitfehlerdiagnose in einem Tool, ohne separate `_validate`- oder `_performance`-Varianten zu benötigen.

## Scope

### Muss-Haben

- **Neues MCP-Tool `sql_execute_file`:**
  - Parameter:
    - `file_path` (string, Pflicht): Pfad zur `.sql`-Datei (absolut oder relativ zum Server-Arbeitsverzeichnis).
    - `database` (string, Pflicht): Ziel-Datenbank.
    - `use_transaction` (bool, optional, Default: `true`): Bei `ReadWrite`-Datenbanken steuerbar, ob alle Batches in einer atomaren Transaktion laufen (`true`) oder mit Autocommit pro Batch (`false` für transaktionsinkompatible DDL wie `ALTER DATABASE`). Bei `ReadOnly` wird immer eine Rollback-Transaktion erzwungen.
    - `requested_row_limit` (int, optional): Begrenzung der Zeilenanzahl pro SELECT-Batch.
    - `parameters` (dict, optional): SQL-Parameter für parametrisierte Skripte.
- **Dateipfad-Auflösung & Sicherheits-Checks:**
  - Auflösung relativer Pfade gegen `Environment.CurrentDirectory` / Server-Root.
  - Prüfung auf Dateiexistenz und Endung `.sql`.
  - Dateigrößen-Limit über `MaxScriptFileSizeBytes` in `appsettings.json` (Default: 10 MB).
  - Encoding-Erkennung (UTF-8, UTF-8 mit BOM, UTF-16, ANSI).
- **Multi-Batch & `GO`-Unterstützung:**
  - Robuster Batch-Splitter (`SqlScriptBatchSplitter`), der `GO`-Zeilen (case-insensitive, optional mit Count, tolerant ggü. Leerzeichen/Kommentaren) erkennt und Zeilennummern mitspeichert.
- **Sicherheits-Guardrails:**
  - Bei `ReadOnly` und `ReadOnlyAnonymized`: Jeder Batch wird auf mutierende Befehle geprüft (Verstoß führt zum Abbruch); Rollback-Transaktion für das Gesamtskript; Anonymisierung auf allen Resultsets.
  - Bei `ReadWrite`: DDL und DML in Batches zulässig.
- **Strukturiertes Markdown-Ausgabeformat:**
  - Header: Skriptpfad, Ziel-DB, Gesamtlaufzeit (`elapsed_ms`), CPU/Reads, Status (Erfolg/Fehlschlag), Transaktionsmodus.
  - Pro Batch: Batch-Nummer, Zeilenbereich im Quellskript (z. B. `Zeile 1–15`), Status, betroffene Zeilen bzw. Resultset (als JSON-Lines / Code-Block).
  - Bei Fehler: Sofortige Identifikation des fehlerhaften Batches, Zeilennummer, SQL-Snippet und `SqlToAiError`-Code.

### Nice-to-Have (Zwischenspeicher — vor `status: ready` aufgelöst)

*(Leer)*

### Non-Goals (bewusst NICHT Teil davon)

- **Keine separaten Tools `sql_validate_file` / `sql_measure_performance_file`:** Nicht nötig, da `sql_execute_file` Metriken und Diagnosen direkt liefert.
- **Keine Remote-/Netzwerk-URLs:** Nur lokale Dateien auf dem Server-Dateisystem.
- **Kein interaktiver Eingabe-Prompt:** Skripte müssen vollständig und autonom ausführbar sein.

## Zielplattformen / Technischer Rahmen

- .NET 10 / C# 14
- ModelContextProtocol SDK (`McpServerTool.Create`)
- `Microsoft.Data.SqlClient` & Dapper
- `appsettings.json` und `IOptions<QueryExecutionOptions>`

## Verworfene Alternativen

- **Option B (Parameter an `sql_execute_query`):** verworfen, da duale Parameter die MCP-Toolsyntax verkomplizieren und Multi-Batch-Semantik sich von Single-Query unterscheidet.
- **Strikte Workspace-Sandbox:** verworfen, um maximale Flexibilität für lokale Entwickler- und Agentenskripte zu erhalten.
- **Rein atomare Transaktionen ohne Abschaltbarkeit:** verworfen, da DDL-Statements wie `ALTER DATABASE` in SQL Server außerhalb von Benutzertransaktionen laufen müssen.

## Wo im Projekt

- [src/SqlToAi/Mcp/McpConstants.cs](src/SqlToAi/Mcp/McpConstants.cs) — Toolname `sql_execute_file` und Argumentkonstanten (`file_path`, `use_transaction`).
- [src/SqlToAi/Mcp/SqlMcpToolRegistrations.cs](src/SqlToAi/Mcp/SqlMcpToolRegistrations.cs) — Registrierung von `sql_execute_file`.
- [src/SqlToAi/Mcp/ToolDispatcher.cs](src/SqlToAi/Mcp/ToolDispatcher.cs) — Dispatching und Formatierung des Markdown-Responses.
- [src/SqlToAi/Database/IScriptExecutionService.cs](src/SqlToAi/Database/IScriptExecutionService.cs) — Service-Vertrag für Datei- und Batch-Ausführung.
- [src/SqlToAi/Database/ScriptExecutionService.cs](src/SqlToAi/Database/ScriptExecutionService.cs) — Dateizugriff, Transaktionssteuerung, Batch-Iteration und Metrikenerfassung.
- [src/SqlToAi/Database/SqlScriptBatchSplitter.cs](src/SqlToAi/Database/SqlScriptBatchSplitter.cs) — Zerlegung in `GO`-Batches mit Zeilenoffset.
- [src/SqlToAi/Configuration/QueryExecutionOptions.cs](src/SqlToAi/Configuration/QueryExecutionOptions.cs) — Konfigurationsfeld `MaxScriptFileSizeBytes` (Default 10 MB).
- [src/SqlToAi/Domain/SqlToAiError.cs](src/SqlToAi/Domain/SqlToAiError.cs) — Fehlercodes für Dateifehler (`FileNotFound`, `FileTooLarge`, `InvalidFileExtension`).
- [src/SqlToAi/appsettings.json](src/SqlToAi/appsettings.json) — Konfigurations-Eintrag für `MaxScriptFileSizeBytes`.

## Entdeckte Mängel/Redundanzen

- **`SqlMultiStatementDetector` vs. Multi-Batch:**
  - **Gefunden:** `QuerySafetyValidator` blockiert standardmäßig Multi-Statements (`SQL-AI-0101`).
  - **Vorschlag:** `ScriptExecutionService` teilt Skripte vorab in Batches (`SqlScriptBatchSplitter`) und lässt jeden Batch einzeln gegen `QuerySafetyValidator` / Guardrails prüfen, bevor die Ausführung startet.

## Wie (grober Ansatz)

1. **Dateizugriff:** `ScriptExecutionService` validiert Pfad (Existenz, `.sql`-Endung, Dateigröße <= `MaxScriptFileSizeBytes`) und liest Datei ein.
2. **Batching:** `SqlScriptBatchSplitter` zerlegt den Inhalt anhand von `GO`-Trennzeichen in eine Liste von `SqlBatch`-Objekten (Text, Startzeile, Endzeile).
3. **Guardrail-Prüfung:** Jeder Batch wird validiert. Bei `ReadOnly`-DBs führt jeder mutierende Batch zum sofortigen Abbruch vor Verbindungs-/Transaktionsaufbau.
4. **Ausführung:**
   - Bei `ReadOnly`: Verbindung öffnen, Rollback-Transaktion starten, Batches sequenziell ausführen, Rollback erzwingen, Resultsets sammeln.
   - Bei `ReadWrite`: Je nach `use_transaction` Verbindung öffnen, optional Transaktion starten, Batches sequenziell ausführen, bei Erfolg committen (falls `use_transaction`), bei Fehler zurückrollen.
5. **Ergebnis:** Zusammenstellung des Markdown-Reports mit Gesamtlaufzeit, Batch-Status, Zeilenangaben und Resultsets (bzw. präziser Fehlerstelle).

## Definition of Done / Erfolgskriterien

- MCP-Tool `sql_execute_file` ist implementiert und aufrufbar.
- `.sql`-Dateien mit mehreren Batches und `GO`-Separatoren werden fehlerfrei sequenziell ausgeführt.
- Transaktionsmodus (`use_transaction: true/false`) funktioniert auf `ReadWrite`-Datenbanken wie konfiguriert.
- `ReadOnly`- und `ReadOnlyAnonymized`-Guardrails (inkl. PII-Anonymisierung und Rollback) greifen lückenlos für jeden Batch.
- Detaillierter Markdown-Report mit Metriken (Dauer, CPU, Reads) und Resultsets wird bei Erfolg zurückgegeben.
- Bei Fehlern (Datei nicht gefunden, falsche Endung, SQL-Fehler in Batch N, mutierender Befehl auf ReadOnly) wird ein präziser Markdown-Fehlerbericht mit Zeilennummer, Code und SQL-Snippet erzeugt.
- Unit- und Integrationstests (xUnit v3) decken alle Fälle ab (Erfolg, Multi-Batch, `GO`-Varianten, Transaktionsabbruch, Read-Only-Verletzung, Dateifehler).
- AiNetLinter ist 100% clean (0 Fehler, 0 Warnungen).
- `docs/architecture-spec.md` und `README.md` sind synchronisiert.

## Offene Punkte

*(Keine)*
