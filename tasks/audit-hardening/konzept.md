---
status: ready
type: konzept
project_kind: brownfield
estimated_scope: medium
rules_dir: .agents/rules
last_updated: 2026-08-03T19:45:00+02:00
open_questions: []
---

# Konzept: Audit-Befunde & Härtung (Audit Hardening)

## Ziel (Was)

Behebung der im externen Audit identifizierten realen Sicherheits- und Performance-Schwachstellen im `SqlToAi` Server: Eliminierung unbegrenzter Command-Timeouts (inkl. Bereinigung einer dabei entdeckten irreführenden Options-Benennung), serverseitige Begrenzung von Row-Limits zur Entlastung des SQL Servers, sowie Schutz vor PII-Exposure im MCP-Trail-Logging durch Wiederverwendung der bestehenden Anonymisierungs-Infrastruktur. Eine GitHub-Actions-CI-Pipeline für Build/Test wird bewusst **nicht** eingeführt (siehe Non-Goals).

## Warum / Kontext

Das externe Audit hat folgende Punkte aufgedeckt:
- `command.CommandTimeout = 0` hebt jegliche Timeout-Schranken auf und kann bei geblockten/komplexen Abfragen den Server unbegrenzt hängen lassen.
- Die Row-Limitierung erfolgt aktuell erst im C#-Client (`while (rowCount < args.RowLimit)`), wodurch SQL Server dennoch die vollständige Ergebnismenge liest und verarbeitet (hoher CPU/IO-Verbrauch).
- Der MCP Trail schreibt Requests und Parameter unredigiert auf die Festplatte.

Bei der Konzeption wurde zusätzlich klar: der eigentliche Risikofall beim MCP Trail ist nicht "das LLM sieht Rohdaten über die MCP-Response" (das ist über `AccessLevel` pro Datenbank ohnehin bewusst geregelt), sondern dass ein lokal laufender Agent die Trail-Dateien direkt vom Dateisystem lesen und damit die Zugriffssteuerung über den MCP-Kanal umgehen könnte. Vollständige Verschlüsselung wurde dafür als unverhältnismäßig verworfen (kein Schlüsselmanagement gewünscht) — stattdessen wird die bestehende Anonymisierung wiederverwendet.

Eine GitHub-Actions-CI-Pipeline wurde ebenfalls diskutiert, aber bewusst aus dem Scope genommen: Alleinentwickler-Projekt mit genau einem externen Nutzer, `dotnet test` läuft bereits manuell vor jedem Release; der Pflegeaufwand einer Pipeline steht aktuell in keinem Verhältnis zum Nutzen.

## Scope

### Muss-Haben

- **CommandTimeout Konfigurierbarkeit & Härtung:** Entfernen von `CommandTimeout = 0` in [QueryExecutionService.cs:251](src/SqlToAi/Database/QueryExecutionService.cs#L251). Neue `QueryExecutionOptions.CommandTimeoutSeconds` aus `appsettings.json`, gebunden an `DbCommand.CommandTimeout`. Im Zuge dessen: Umbenennung der bestehenden, aber irreführend benannten `SqlServerOptions.CommandTimeoutSeconds` → `ConnectTimeoutSeconds` (siehe „Entdeckte Mängel" unten — sie wird heute bereits als `ConnectTimeout` verwendet, nicht als Command-Timeout).
- **Serverseitiges Row-Limit Enforcement:** `SET ROWCOUNT` als Session-Setting vor Ausführung setzen (analog zum bestehenden `ExecuteSetOptionAsync`-Helper) — robust gegenüber beliebigen, LLM-generierten SELECT-Formen, ohne den Query-Text anzufassen. Die bestehende clientseitige Zeilenbegrenzung (`while (rowCount < args.RowLimit ...)`) bleibt zusätzlich als Sicherheitsnetz bestehen. Das harte Limit kommt weiterhin aus `QueryExecutionOptions.MaxRowLimit` (appsettings.json) — unverändert.
- **MCP Trail Redaction (Anonymizer-Reuse):** [McpTrailWriter.cs](src/SqlToAi/Mcp/McpTrailWriter.cs) wendet vor dem Schreiben auf Festplatte dieselbe bestehende Anonymisierung (PII-Glob-Patterns, Hash/ScramblePattern) an wie die Query-Ergebnisse selbst — unabhängig vom `AccessLevel` der jeweiligen Datenbank. Ziel: ein lokal laufender Agent, der die Trail-Dateien direkt vom Dateisystem liest, bekommt dieselbe reduzierte Sicht wie über den MCP-Kanal, nicht mehr. Kein neues Krypto-/Key-Management, keine separate Redaction-Engine — reine Wiederverwendung vorhandener Infrastruktur.

### Nice-to-Have (optional, spätere Iteration)

- **Streaming / Memory Optimization:** Ersatz der `StringBuilder`-Gesamtpufferung in `ExecuteAndSerializeAsync` durch ein zeilenweises Output-Streaming / `IAsyncEnumerable`.
- **Tokenization Type Safety:** Weitere Absicherung der `QueryTokenResolver`-Detokenisierung bezüglich SQL-Typkonvertierungen und Parameterbindung.

### Non-Goals (bewusst NICHT Teil davon)

- **Vollständiges Server-Re-Architecture:** Kein Neuaufbau der Kern-Services nötig, da die bestehende Interface-Driven-Architektur tragfähig ist.
- **GitHub Actions CI Pipeline (Build/Test):** Bewusst nicht eingeführt. Begründung: Alleinentwickler + genau ein externer Nutzer, `dotnet test` bereits etablierte manuelle Praxis vor jedem Release; Aufwand/Nutzen-Verhältnis aktuell nicht gerechtfertigt. Die bestehende `.github/workflows/release.yml` (Build+Publish bei Tag-Push) bleibt unverändert und ist von dieser Entscheidung nicht betroffen.
- **Verschlüsselung des MCP Trail at-rest:** Verworfen zugunsten der Anonymizer-Wiederverwendung (siehe Muss-Haben) — kein zusätzliches Schlüsselmanagement gewünscht, und die Anonymisierung deckt das eigentliche Risiko (Zugriff über Dateisystem statt MCP-Kanal) bereits ab.

## Zielplattformen / Technischer Rahmen

- **.NET 10 / C# 14 & ADO.NET (`Microsoft.Data.SqlClient`)**
- **Konfiguration via `appsettings.json` und `IOptions<SqlToAiOptions>`** (gemäß [SqlToAiRichtlinien.mdc](.agents/rules/SqlToAiRichtlinien.mdc))
- Server läuft plattformübergreifend (Windows/Linux/macOS, siehe `release.yml`-Build-Matrix) — relevant dafür, dass keine Windows-only-Mechanismen (z.B. DPAPI) in Betracht gezogen wurden.

## Verworfene Alternativen

- **Reiner Client-seitiger Row-Limit Stop als alleinige Technik:** Verworfen, da SQL Server dabei weiterhin die vollständige Ergebnismenge verarbeitet. Bleibt aber als sekundäres Sicherheitsnetz neben `SET ROWCOUNT` bestehen.
- **TOP(N) AST/Regex-Rewrite der Query:** Verworfen — bei beliebigen, LLM-generierten SELECT-Formen (CTEs, UNIONs, bereits vorhandenes TOP/ORDER BY) zu fehleranfällig gegenüber dem Nutzen; `SET ROWCOUNT` erreicht dasselbe Ziel ohne den Query-Text anzufassen.
- **MCP Trail Verschlüsselung (AES mit Env-Var-Key oder DPAPI):** Verworfen — zu hoher Aufwand/Schlüsselmanagement für den Nutzen; die Anonymizer-Wiederverwendung erreicht denselben Schutzzweck einfacher.
- **MCP Trail komplett unverändert lassen (kein Schutz):** Verworfen — der Nutzer ist zwar SQL-Admin mit ohnehin vollem DB-Zugriff, aber ein lokal laufender *Agent* mit Dateisystemzugriff auf die Trail-Logs ist ein separater, zusätzlicher Zugriffsweg, der nicht über die MCP-eigene Zugriffssteuerung läuft.

## Wo im Projekt

- [QueryExecutionService.cs](src/SqlToAi/Database/QueryExecutionService.cs): `CommandTimeout = 0` entfernen und an `QueryExecutionOptions.CommandTimeoutSeconds` binden; `SET ROWCOUNT` vor `ExecuteReaderAsync` setzen.
- [SqlToAiOptions.cs](src/SqlToAi/Configuration/SqlToAiOptions.cs): `SqlServerOptions.CommandTimeoutSeconds` → `ConnectTimeoutSeconds` umbenennen; neue `QueryExecutionOptions.CommandTimeoutSeconds` ergänzen.
- [SqlConnectionFactory.cs:44](src/SqlToAi/Database/SqlConnectionFactory.cs#L44): Referenz auf den umbenannten `SqlServerOptions.ConnectTimeoutSeconds` anpassen.
- [appsettings.json](src/SqlToAi/appsettings.json): `SqlServer.CommandTimeoutSeconds` → `ConnectTimeoutSeconds`; neuer `QueryExecution.CommandTimeoutSeconds`-Eintrag mit sinnvollem Default.
- [McpTrailWriter.cs](src/SqlToAi/Mcp/McpTrailWriter.cs): Anonymisierung der geschriebenen Argumente/Response vor `File.AppendAllText`/`File.WriteAllText` einbauen, unter Wiederverwendung von `IAnonymizer`.
- `.github/workflows/`: keine neue Datei (bewusst, siehe Non-Goals); bestehende `release.yml` unverändert.

## Entdeckte Mängel/Redundanzen

- **Hartkodiertes `CommandTimeout = 0`**
  - **Gefunden:** [QueryExecutionService.cs:251](src/SqlToAi/Database/QueryExecutionService.cs#L251) (`command.CommandTimeout = 0;`)
  - **Bezug:** Verstoß gegen [SqlToAiRichtlinien.mdc §4](.agents/rules/SqlToAiRichtlinien.mdc#L66) ("Keine hartkodierten Werte & AppSettings-Pflicht").
  - **Entscheidung:** übernommen ins Scope (→ siehe Muss-Haben)

- **Rein clientseitiges Row-Limit**
  - **Gefunden:** [QueryExecutionService.cs:263](src/SqlToAi/Database/QueryExecutionService.cs#L263) (`while (rowCount < args.RowLimit && await reader.ReadAsync())`)
  - **Bezug:** Performance- & Ressourcen-Mangel bei großen Tabellen.
  - **Entscheidung:** als alleinige Technik verworfen, bleibt aber als sekundäres Sicherheitsnetz neben `SET ROWCOUNT` bestehen (→ siehe Muss-Haben & Verworfene Alternativen)

- **Unredigierte MCP Trail Logs**
  - **Gefunden:** [McpTrailWriter.cs:112-129](src/SqlToAi/Mcp/McpTrailWriter.cs#L112-L129)
  - **Bezug:** Zugriffsweg über das lokale Dateisystem, der an der MCP-eigenen Zugriffssteuerung vorbeiführt.
  - **Entscheidung:** übernommen ins Scope, aber mit angepasster Technik — Anonymizer-Wiederverwendung statt Verschlüsselung (→ siehe Muss-Haben; Verschlüsselung explizit verworfen, siehe Verworfene Alternativen)

- **Fehlende CI Workflow Datei**
  - **Gefunden:** Keine `ci.yml` (Build+Test bei Push/PR) vorhanden unter `.github/workflows/` — nur `release.yml` (Build+Publish bei Tag-Push).
  - **Bezug:** Entwicklungsrichtlinie für automatisiertes Testen und Verhindern von Regressionen.
  - **Entscheidung:** ABGELEHNT — Alleinentwickler-Kontext mit genau einem externen Nutzer, manueller Testlauf vor Release bereits etabliert, Aufwand/Nutzen nicht gerechtfertigt (→ siehe Non-Goals)

- **NEU — Irreführende Benennung `SqlServerOptions.CommandTimeoutSeconds`**
  - **Gefunden:** [SqlConnectionFactory.cs:44](src/SqlToAi/Database/SqlConnectionFactory.cs#L44) verwendet `SqlServerOptions.CommandTimeoutSeconds` als `ConnectTimeout` — nicht als Command-Timeout, obwohl Property-Name und `appsettings.json`-Schlüssel das Gegenteil suggerieren.
  - **Bezug:** Irreführende Konfiguration, direkt im Weg der geplanten CommandTimeout-Härtung entdeckt (eine zweite, korrekt benannte `CommandTimeoutSeconds`-Option hätte sonst zu einer verwirrenden Namenskollision geführt).
  - **Entscheidung:** übernommen ins Scope — Umbenennung zu `ConnectTimeoutSeconds` (→ siehe Muss-Haben)

## Wie (grober Ansatz)

1. `SqlServerOptions.CommandTimeoutSeconds` → `ConnectTimeoutSeconds` umbenennen (Options-Klasse, `appsettings.json`, `SqlConnectionFactory.cs`). Neue `QueryExecutionOptions.CommandTimeoutSeconds` ergänzen (`appsettings.json` mit sinnvollem Default); `QueryExecutionService` bindet den Wert an `DbCommand.CommandTimeout` statt `0`.
2. Vor `ExecuteReaderAsync` innerhalb der bestehenden Connection/Transaction `SET ROWCOUNT @limit` ausführen (analog zum bestehenden `ExecuteSetOptionAsync`-Helper); bestehende clientseitige `while`-Schleife bleibt unverändert als Fallback bestehen.
3. `McpTrailWriter` um Anonymisierung der zu schreibenden Argumente/Response erweitern, unter Wiederverwendung von `IAnonymizer` — unabhängig vom `AccessLevel` der jeweiligen Datenbank.

## Definition of Done / Erfolgskriterien

- Alle `dotnet test` Testläufe (inkl. `AiNetLinter` und Integrationstests) verlaufen grün.
- Kein `CommandTimeout = 0` mehr im Quellcode vorhanden.
- Konfigurierbarer Command Timeout aus `appsettings.json` (`QueryExecutionOptions.CommandTimeoutSeconds`) greift bei Abfragen.
- `SqlServerOptions.CommandTimeoutSeconds` ist zu `ConnectTimeoutSeconds` umbenannt und konsistent so verwendet (kein verbliebener alter Name im Code oder in `appsettings.json`).
- SQL Server wird bei Row-Limits durch `SET ROWCOUNT` serverseitig entlastet; das bestehende clientseitige Row-Limit bleibt als Fallback aktiv.
- MCP-Trail-Dateien enthalten dieselbe Anonymisierung wie die an das LLM gesendeten Query-Ergebnisse — unabhängig vom `AccessLevel` der abgefragten Datenbank.

## Offene Punkte

Keine mehr.
