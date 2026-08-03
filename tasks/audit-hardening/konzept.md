---
status: draft
type: konzept
project_kind: brownfield
estimated_scope: medium
rules_dir: .agents/rules
last_updated: 2026-08-03T16:50:00+02:00
open_questions:
  - "Row-Limit-Strategie: Bevorzugen wir ein serverseitiges 'SET ROWCOUNT' per Session, eine SQL-Rewrite-Logik für SELECT (z.B. TOP N injection via AST/Regex), oder beides?"
  - "MCP Trail Redaction: Soll McpTrailWriter in der Produktion standardmäßig sensiblen Content/Puffer redigieren, verschlüsseln oder rein opt-in konfigurierbar sein?"
  - "CI Pipeline: Reicht uns eine GitHub Actions Pipeline mit Unit Tests + Build, oder wollen wir auch SQL Server Integrationstests via SQL Server Container / LocalDB in CI einbinden?"
---

# Konzept: Audit-Befunde & Härtung (Audit Hardening)

## Ziel (Was)

Behebung der im externen Audit identifizierten realen Sicherheits-, Stabilitäts- und Performance-Schwachstellen im `SqlToAi` Server. Dazu gehören die Eliminierung unbegrenzter Command-Timeouts, serverseitige Begrenzung von Row-Limits zur Entlastung des SQL Servers, Schutz vor PII-Leaks im MCP-Trail-Logging, Härtung der Tokenauflösung und die Einrichtung einer automatisierten GitHub Actions CI-Pipeline.

## Warum / Kontext

Das externe Audit hat kritische Punkte aufgedeckt:
- `command.CommandTimeout = 0` hebt jegliche Timeout-Schranken auf und kann bei geblockten/komplexen Abfragen den Server unbegrenzt hängen lassen.
- Die Row-Limitierung erfolgt aktuell erst im C#-Client (`while (rowCount < args.RowLimit)`), wodurch SQL Server dennoch die vollständige Ergebnismenge liest und verarbeitet (hoher CPU/IO-Verbrauch).
- Der MCP Trail schreibt Requests und Parameter unredigiert auf die Festplatte, was bei aktivierter Protokollierung PII-Lecks verursachen kann.
- Es fehlt eine GitHub Actions CI-Pipeline für automatische Build- und Test-Validierung bei Pull Requests.

## Scope

### Muss-Haben

- **CommandTimeout Konfigurierbarkeit & Härtung:** Entfernen von `CommandTimeout = 0` in [QueryExecutionService.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Database/QueryExecutionService.cs#L251). Einbinden von `CommandTimeoutSeconds` aus den Optionen via `appsettings.json`.
- **Serverseitiges Row-Limit Enforcement:** Verhindern unnötiger SQL Server CPU/IO-Last bei großen Tabellen durch serverseitige Limitierung (z.B. `SET ROWCOUNT` Session-Einstellung oder AST-basiertes `TOP (N)` Injection).
- **MCP Trail Redaction & Privacy Guard:** Schutz vor PII-Exposure in [McpTrailWriter.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Mcp/McpTrailWriter.cs) durch Sanitisierung/Redaktion sensibler Parameter und optionales Deaktivieren / Verschlüsseln von Trails.
- **GitHub Actions CI Workflow:** Erstellen einer `.github/workflows/ci.yml` für automatisierten Build (`dotnet build`) und Testlauf (`dotnet test`).

### Nice-to-Have (optional, spätere Iteration)

- **Streaming / Memory Optimization:** Ersatz der `StringBuilder`-Gesamtpufferung in `ExecuteAndSerializeAsync` durch ein zeilenweises Output-Streaming / `IAsyncEnumerable`.
- **Tokenization Type Safety:** Weitere Absicherung der `QueryTokenResolver`-Detokenisierung bezüglich SQL-Typkonvertierungen und Parameterbindung.

### Non-Goals (bewusst NICHT Teil davon)

- **Vollständiges Server-Re-Architecture:** Kein Neuaufbau der Kern-Services nötig, da die bestehende Interface-Driven-Architektur tragfähig ist.

## Zielplattformen / Technischer Rahmen

- **.NET 10 / C# 14 & ADO.NET (`Microsoft.Data.SqlClient`)**
- **Konfiguration via `appsettings.json` und `IOptions<SqlToAiOptions>`** (gemäß [SqlToAiRichtlinien.mdc](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/.agents/rules/SqlToAiRichtlinien.mdc))
- **GitHub Actions CI Runner (Windows / Ubuntu)**

## Verworfene Alternativen

- **Reiner Client-seitiger Row-Limit Stop:** Verworfene Praxis, da der SQL Server dabei weiterhin teure Abfragen vollständig verarbeitet.

## Wo im Projekt

- [QueryExecutionService.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Database/QueryExecutionService.cs): `CommandTimeout = 0` und Row-Limit Schleifen-Handling.
- [McpTrailWriter.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Mcp/McpTrailWriter.cs): Schreiben unredigierter Requests/Responses.
- [SqlToAiOptions.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Configuration/SqlToAiOptions.cs): Konfigurationsoptionen für Query Execution & Timeouts.
- [appsettings.json](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/appsettings.json): Standardwerte für Timeouts und Redaction Flags.
- `.github/workflows/ci.yml`: [NEW] GitHub Actions Pipeline.

## Entdeckte Mängel/Redundanzen

- **Hartkodiertes `CommandTimeout = 0`**
  - **Gefunden:** [QueryExecutionService.cs:251](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Database/QueryExecutionService.cs#L251) (`command.CommandTimeout = 0;`)
  - **Bezug:** Verstoß gegen [SqlToAiRichtlinien.mdc §4](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/.agents/rules/SqlToAiRichtlinien.mdc#L66) ("Keine hartkodierten Werte & AppSettings-Pflicht").
  - **Vorschlag:** Wert aus `options.QueryExecution.CommandTimeoutSeconds` (bzw. `SqlServerOptions`) binden.
  - **Entscheidung:** übernommen ins Scope (→ siehe Muss-Haben)

- **Rein clientseitiges Row-Limit**
  - **Gefunden:** [QueryExecutionService.cs:263](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Database/QueryExecutionService.cs#L263) (`while (rowCount < args.RowLimit && await reader.ReadAsync())`)
  - **Bezug:** Performance- & Ressourcen-Mangel bei großen Tabellen.
  - **Vorschlag:** Serverseitiges `SET ROWCOUNT` oder AST/Regex `TOP (N)` Injection.
  - **Entscheidung:** übernommen ins Scope (→ siehe Muss-Haben)

- **Unredigierte MCP Trail Logs**
  - **Gefunden:** [McpTrailWriter.cs:112-129](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Mcp/McpTrailWriter.cs#L112-L129)
  - **Bezug:** Datensicherheits- / PII-Risiko bei Aufzeichnung unanonymisierter Requests.
  - **Vorschlag:** Parameter-Redaktion und sichere Defaults einführen.
  - **Entscheidung:** übernommen ins Scope (→ siehe Muss-Haben)

- **Fehlende CI Workflow Datei**
  - **Gefunden:** Keines vorhanden unter `.github/workflows/`
  - **Bezug:** Entwicklungsrichtlinie für automatisiertes Testen und Verhindern von Regressionen.
  - **Vorschlag:** Erstellung `.github/workflows/ci.yml`.
  - **Entscheidung:** übernommen ins Scope (→ siehe Muss-Haben)

## Wie (grober Ansatz)

1. `QueryExecutionOptions` um `CommandTimeoutSeconds` ergänzen und in `appsettings.json` hinterlegen. `QueryExecutionService` stellt den Wert an `DbCommand.CommandTimeout` ein.
2. Für serverseitige Row-Limits die Option prüfen, vor `ExecuteReaderAsync` innerhalb der Transaktion/Verbindung `SET ROWCOUNT @limit` auszuführen (oder `TOP (N)` Rewrite).
3. `McpTrailWriter` erweitern, um sensible Eingaben/Parameter optional zu maskieren oder Trail nur im Log-Level `Trace`/`Debug` voll zu schreiben.
4. `.github/workflows/ci.yml` für `dotnet build` & `dotnet test` anlegen.

## Definition of Done / Erfolgskriterien

- Alle `dotnet test` Testläufe (inkl. `AiNetLinter` und Integrationstests) verlaufen grün.
- Kein `CommandTimeout = 0` mehr im Quellcode vorhanden.
- Konfigurierbarer Command Timeout aus `appsettings.json` greift bei Abfragen.
- SQL Server wird bei Row-Limits durch serverseitige Steuerung entlastet.
- GitHub Actions Workflow baut und testet das Projekt erfolgreich.

## Offene Punkte

- Klärung der bevorzugten Row-Limit-Technik (`SET ROWCOUNT` vs. `TOP (N)` Rewrite).
- Klärung der MCP-Trail-Redaktionsstufe.
