# SqlToAi MCP Server: Technische Spezifikation & Konzept

Dieses Dokument definiert das vollständige Konzept, die Sicherheitsmechanismen und das Transport-Mapping des **SqlToAi MCP-Servers** (Model Context Protocol). Der Server fungiert als sicheres, konfigurierbares Bindeglied zwischen KI-Agenten (z. B. in Cursor, Claude Desktop, Windsurf) und Microsoft SQL Server Datenbanken.

---

## 1. Design-Prinzipien

1. **Lokaler Fokus (Developer-First):** Der Server kommuniziert primär über **Stdio (Standard Input/Output)** mit dem AI-Client. Dies ist extrem performant, benötigt keine Internetverbindung oder separate offene Ports und läuft direkt im Kontext des Entwicklers.
2. **Sicherheit durch Isolation (Safety-by-Design):** Der Server schützt sensible Datenbanken aktiv vor unautorisierten Schreibzugriffen und verhindert den Abfluss von personenbezogenen Daten (PII) durch konfigurierbare Anonymisierungs-Pipelines.
3. **Sicherheits-Grundhaltung (Secure-by-Default):** Ohne explizite Freischaltung ist jeglicher Zugriff auf Datenbanken gesperrt. Alle Verbindungsaufbauten und Datenabfragen müssen durch statische und dynamische Filter freigegeben werden.
4. **Markdown für Agenten, JSON für Strukturen:** Metadaten und Schemainformationen werden als lesbares Markdown an das LLM übergeben. Datenabfragen liefern strukturierte JSON-Zeilen.

---

## 2. Sicherheits- & Guardrail-Konzepte

### A. Multi-Datenbank-Sicherheit (Static Whitelisting)
Ein SQL Server besitzt typischerweise viele System- und Benutzerdatenbanken. Standardmäßig verweigert der MCP-Server jeglichen Zugriff. Der Administrator muss explizite Freigabemuster definieren.

* **Konfiguration:**
  ```json
  "Databases": {
    "Default": "DemoDb",
    "Allowed": ["Demo_*", "TestDb", "Reporting_ReadOnly"],
    "Blocked": ["master", "msdb", "tempdb", "model", "HR_Payroll"],
    "CacheTtlSeconds": 300
  }
  ```
* **Mechanismus:**
  1. Jede Anfrage an ein Tool enthält einen optionalen Parameter `database`. Fehlt dieser, wird `Default` verwendet.
  2. Der Name der Zieldatenbank wird gegen die Listen `Allowed` und `Blocked` geprüft (Unterstützung von einfachen Wildcards wie `*`).
  3. Passt der Name nicht auf ein Muster in `Allowed` oder passt er auf ein Muster in `Blocked`, wird die Anfrage sofort blockiert (`SQL-AI-0104`).
* **Credentials-Sicherheit:**
  Der Connection String kann in der JSON-Konfiguration hinterlegt werden, es wird jedoch dringend empfohlen, ihn über die Umgebungsvariable `SQLTOAI_CONNECTION_STRING` an den MCP-Server zu übergeben, um Zugangsdaten nicht in Konfigurationsdateien einzuchecken.

---

### B. Dynamischer Access- & Permission-Check (Access Levels)
Nach dem statischen Namensabgleich führt der Server einen dynamischen Check direkt in der Zieldatenbank aus. Dieser bestimmt das maximale Zugriffslevel für die aktuelle Verbindung.

* **Konfiguration:**
  ```json
  "Databases": {
    "AccessCheckSql": "SELECT AccessLevel = CASE WHEN DB_NAME() LIKE '%demo%' THEN 'ReadOnly' WHEN SYSTEM_USER = 'readonly_ai' THEN 'SchemaOnly' ELSE 'None' END"
  }
  ```
* **Rückgabewerte (Access Levels):**
  Die SQL-Query muss eine Spalte namens `AccessLevel` zurückgeben (oder einen Skalarwert liefern). Der Wert wird wie folgt interpretiert:

  | Wert (Int) | Wert (String) | Bedeutung / Berechtigung |
  | :--- | :--- | :--- |
  | `0` | `None` | **Gesamter Zugriff gesperrt.** Alle Tools für diese Datenbank schlagen mit `SQL-AI-0104` fehl. |
  | `1` | `SchemaOnly` | **Nur Metadaten.** Alle Schema- und Suchtools sind erlaubt. Abfragen über `sql_execute_query` werden mit `SQL-AI-0107` blockiert. |
  | `2` | `ReadOnlyAnonymized` | **Lesezugriff, anonymisiert.** Schema-Tools und Leseoperationen über `sql_execute_query` sind erlaubt; String-Spalten werden vor der Rückgabe per Anonymizer maskiert (siehe Abschnitt D). |
  | `3` | `ReadOnly` | **Lesezugriff, Klartext.** Schema-Tools und Leseoperationen sind erlaubt, ohne Anonymisierung. |
  | `4` | `ReadWrite` | **Vollzugriff.** Alle Aktionen (inklusive Schreiboperationen über `sql_execute_query`) sind erlaubt. Dies ist die einzige Stufe, die den Read-Only Guard (Abschnitt C) umgeht — es gibt keinen zusätzlichen globalen Schalter. |

* **Fehlerbehandlung:** Wenn die Ausführung von `AccessCheckSql` einen SQL-Fehler wirft oder kein Ergebnis liefert, wird das Level restriktiv auf `0` (`None`) gesetzt.
* **Session- & TTL-Caching:**
  Um die Latenz zu minimieren, wird das ermittelte Access-Level für die Dauer der MCP-Sitzung gecacht. Über `CacheTtlSeconds` kann optional eine maximale Gültigkeitsdauer (in Sekunden) konfiguriert werden, nach der das Level erneut per SQL-Abfrage validiert wird (z. B. bei Berechtigungsänderungen im laufenden Betrieb).

---

### C. Konfigurierbarer Schreibschutz (Read-Only Guard)
Für jede Datenbank außer solchen mit Access Level `ReadWrite` (Abschnitt B) wird ein mehrstufiger Schreibschutz erzwungen:

1. **Parser-Ebene:** Der Server validiert Statements vor der Ausführung per robusten Regulären Ausdrücken (String-Literale und Kommentare werden dabei ausgeblendet, damit z. B. `SELECT 'DELETE' AS Status` nicht fälschlich blockiert wird). Mutierende SQL-Befehle (`INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `TRUNCATE`, `EXEC`/`EXECUTE` etc.) werden abgewiesen und brechen mit `SQL-AI-0107` ab.
2. **Transaktions-Ebene:** Alle Abfragen werden innerhalb einer expliziten Transaktion ausgeführt. Am Ende der Ausführung wird ein `ROLLBACK` ausgeführt, sodass versehentliche oder böswillige Datenänderungen verworfen werden.
3. **Least-Privilege-Empfehlung:** Der Schreibschutz des Servers dient als "Defense-in-Depth". Die primäre Absicherung muss stets über einen SQL-Login mit minimalen Rechten (z. B. nur Mitgliedschaft in der Rolle `db_datareader`) realisiert werden.

**Ausnahme bei `ReadWrite`:** Ist das ermittelte Access Level `ReadWrite`, überspringt der Server Schritt 1 vollständig (auch `INSERT`/`UPDATE`/`DELETE`/`EXEC` sind erlaubt) und committet die Transaktion aus Schritt 2 statt sie zurückzurollen. Es gibt **keinen separaten globalen Schalter** dafür — die Freigabe erfolgt ausschließlich pro Datenbank über `AccessCheckSql` (Abschnitt B). Der Mehrfach-Statement-Schutz (`SQL-AI-0101`) bleibt davon unberührt und gilt immer.

---

### D. Per-DB String-Anonymisierung (AccessLevel-gesteuert)
Zum Schutz von PII (Personally Identifiable Information) anonymisiert der Server String-Werte im Arbeitsspeicher, bevor sie an den KI-Agenten übertragen werden. Die Entscheidung *ob* anonymisiert wird, fällt pro Datenbank am `AccessLevel` (siehe Abschnitt B): Liefert `AccessCheckSql` `ReadOnlyAnonymized`/`2`, wird jede zurückgegebene String-Spalte anonymisiert; bei `ReadOnly`/`3` (Klartext) nicht. Ein separater Muster-Block zur Pauschal-Aktivierung existiert nicht mehr — pauschal wird *jede* nicht ausgeschlossene String-Spalte anonymisiert, sobald das AccessLevel es verlangt.

* **Konfiguration:**
  ```json
  "Anonymizer": {
    "Enabled": true,
    "DefaultMode": "ScramblePattern",
    "ExcludedColumns": ["*Id", "Id", "*Code", "*Type", "Status", "State", "Category"]
  }
  ```
* **Verhalten:**
  Spalten, die auf eines der Muster in `ExcludedColumns` passen, werden *nie* anonymisiert. Alle anderen String-Spalten werden anonymisiert, sofern `Enabled: true` ist und das AccessLevel der Zieldatenbank `ReadOnlyAnonymized` ergibt.
* **Algorithmen:**
  * **ScramblePattern:** Erhält das strukturelle Muster des Strings. Großbuchstaben werden durch ein zufälliges `'X'`, Kleinbuchstaben durch `'x'` und Ziffern durch `'9'` ersetzt (z. B. `Max.Mustermann@mail.de` $\rightarrow$ `Xxx.Xxxxxxxxxx@xxxx.xx`). E-Mail-Adressen, Postleitzahlen und Telefonnummern bleiben für die KI strukturell erkennbar, enthalten aber keinerlei PII mehr.
  * **Hash (Consistency-Hashing):** Generiert einen eindeutigen, reproduzierbaren SHA-256-Hash-Wert pro Text. Dadurch bleiben Relationen und Gruppen (z. B. gleiche Kundennamen in verschiedenen Tabellen) für das LLM logisch verknüpfbar.

---

## 3. Schema-Enrichment (Dokumentations-Kopplung)

Um kryptische Tabellen- und Spaltennamen für das LLM verständlicher zu machen, reichert der Server Schemaabfragen automatisch mit fachlichen Beschreibungen an.

* **Integrierter Default-Provider:**
  Wenn kein Custom-Provider konfiguriert ist, liest der Server automatisch die in SQL Server integrierten Beschreibungen aus den Extended Properties (`MS_Description`) aus.
* **Custom-Metadata-Provider (Optional):**
  Falls Dokumentationen in separaten Tabellen gepflegt werden, können diese über SQL-Queries abgefragt werden:
  ```json
  "MetadataProvider": {
    "Enabled": true,
    "ConnectionString": "", // Leer = nutzt dieselbe DB
    "TableMetadataQuery": "SELECT Description FROM dbo.TableDocs WHERE TableName = @TableName",
    "ColumnMetadataQuery": "SELECT ColumnName, Description FROM dbo.ColumnDocs WHERE TableName = @TableName"
  }
  ```
* **Funktionsweise:** Bei Aufruf von `sql_get_schema` führt der Server parallel die Beschreibungsabfragen aus und fügt die gefundenen Beschreibungen nahtlos in das erzeugte Markdown-Dokument ein.

---

## 4. MCP Tool-Spezifikationen

Jedes Tool gibt bei Fehlern ein strukturiertes JSON mit `IsSuccess=false` und einem Fehlercode zurück. Bei Erfolg wird ein Markdown-Text oder eine JSON-Payload geliefert.

### 1. `sql_list_databases`
* **Zweck:** Listet alle freigegebenen Datenbanken auf dem SQL-Server auf.
* **Filterung:** Der Server ermittelt alle sichtbaren Datenbanken auf dem Server, filtert sie anhand der Konfiguration (`Allowed` / `Blocked` Muster) und gibt nur die erlaubten zurück.
* **Rückgabe:** JSON-Array von Datenbanknamen.

### 2. `sql_search_databases`
* **Argumente:** `search_term` (String, Pflicht)
* **Zweck:** Filtert die Liste der freigegebenen Datenbanken nach einem Teilstring.

### 3. `sql_validate_query`
* **Argumente:** `query` (String, Pflicht), `database` (String, optional)
* **Zweck:** Prüft eine SQL-Abfrage fachlich und technisch (Syntax-Check über `PARSEONLY` im Kontext der Zieldatenbank), ohne sie auszuführen.

### 4. `sql_search_objects`
* **Argumente:** `search_term` (String, Pflicht), `max_results` (Int, optional), `database` (String, optional)
* **Zweck:** Sucht nach Objektnamen (Tabellen, Sichten, Prozeduren, Trigger) in `sys.objects` der Zieldatenbank per `LIKE %search_term%`.

### 5. `sql_get_schema`
* **Argumente:** `object_name` (String, Pflicht), `database` (String, optional)
* **Zweck:** Liefert das primäre Schema eines Objekts als Markdown-Dokument, angereichert mit Extended Properties / Metadaten.
* **Inhalt:**
  * **TABLE/VIEW:** Spalten-Tabelle (Typ, Nullable, PK, Identity, Custom-Beschreibung) + Trigger-Übersicht (Name, Events, Disabled-Status) + **Discovery-Index** (Zähler für Fremdschlüssel, Indizes, Constraints).
  * **PROCEDURE/FUNCTION:** DDL-Definitionstext aus `sys.sql_modules` + **Routine-Parameter-Discovery**.

### 6. `sql_get_schema_foreign_keys`
* **Argumente:** `object_name` (String, Pflicht), `database` (String, optional)
* **Zweck:** Progressive Disclosure: Liefert alle ausgehenden und eingehenden Fremdschlüssel einer Tabelle als Markdown-Tabelle.

### 7. `sql_get_schema_indexes`
* **Argumente:** `object_name` (String, Pflicht), `database` (String, optional)
* **Zweck:** Liefert alle Indizes (PK, Unique, Non-Clustered) inklusive Schlüssel- und `INCLUDE`-Spalten als Markdown.

### 8. `sql_get_schema_constraints`
* **Argumente:** `object_name` (String, Pflicht), `database` (String, optional)
* **Zweck:** Liefert alle Default- und Check-Constraints inklusive ihrer Definitionstexte als Markdown.

### 9. `sql_get_trigger_definition`
* **Argumente:** `object_name` (Parent, Pflicht), `trigger_name` (Trigger, Pflicht), `database` (String, optional)
* **Zweck:** Liefert die vollständige DDL-Definition (`CREATE TRIGGER ...`) eines DML-Triggers.

### 10. `sql_get_object_references`
* **Argumente:** `object_name` (String, Pflicht), `database` (String, optional)
* **Zweck:** Zeigt statische Referenzen ("Wo wird diese Tabelle/Sicht verwendet?") über die DMV `sys.dm_sql_referencing_entities`.

### 11. `sql_get_routine_parameters`
* **Argumente:** `object_name` (String, Pflicht), `database` (String, optional)
* **Zweck:** Liefert Parameter, Typen und Rückgabestrukturen für Prozeduren und Funktionen.

### 12. `sql_execute_query`
* **Argumente:** `query` (String, Pflicht), `requested_row_limit` (Int, optional), `database` (String, optional)
* **Zweck:** Führt ein einzelnes SQL-SELECT-Statement aus.
* **Einschränkung:** Nur ein einzelnes Statement erlaubt (Semikolon-Trennung mehrerer Queries führt zu Fehler `SQL-AI-0101`).
* **Datenverarbeitung:** Anwendbare Limits greifen (Default: 100 Zeilen). String-Spalten werden anonymisiert, falls aktiviert und passend zu den Regeln.
* **Berechtigungsprüfung:** Schlägt fehl mit `SQL-AI-0107`, falls das Access-Level der Datenbank nur `SchemaOnly` oder `None` is.

---

## 5. Fehlercodes (Error-Catalog)

Tritt bei der Ausführung eines Tools ein Fehler auf, wird das Tool-Ergebnis als fehlgeschlagen markiert (`IsSuccess = false`) und einer der folgenden standardisierten Fehlercodes zurückgegeben:

| Fehlercode | Bezeichnung | Bedeutung / Ursache |
| :--- | :--- | :--- |
| **SQL-AI-0001** | Ungültige Parameter | Die an das Tool übergebenen Argumente sind ungültig oder unvollständig. |
| **SQL-AI-0101** | Mehrfach-Statements verboten | Die Ausführung von mehreren SQL-Statements (z. B. getrennt durch `;`) ist nicht erlaubt. |
| **SQL-AI-0102** | Abfragefehler | Der SQL-Server hat einen Fehler bei der Syntax oder Ausführung der Query gemeldet. |
| **SQL-AI-0103** | Objekt nicht gefunden | Das angeforderte Datenbankobjekt (Tabelle, Prozedur etc.) existiert nicht. |
| **SQL-AI-0104** | Safety-Check fehlgeschlagen | Die Zieldatenbank wurde durch die statische Whitelist blockiert oder der dynamische `AccessCheckSql` lieferte das Level `None`/`0`. |
| **SQL-AI-0105** | Infrastrukturfehler | Verbindung zum SQL-Server konnte nicht aufgebaut werden oder brach ab. |
| **SQL-AI-0106** | Timeout | Die Ausführung der SQL-Abfrage hat das konfigurierte Zeitlimit überschritten. |
| **SQL-AI-0107** | Schreiboperation blockiert | Ein mutierendes Statement wurde im Read-Only-Modus abgewiesen oder der Zugriff auf Datenabfragen wurde durch das Access-Level `SchemaOnly` blockiert. |
| **SQL-AI-0108** | Ungültiger Typ für Referenzen | Objektreferenzen können nur für Tabellen (`TABLE`) und Sichten (`VIEW`) abgefragt werden. |
| **SQL-AI-0109** | Ungültiger Typ für Parameter | Routine-Parameter können nur für Prozeduren (`PROCEDURE`) und Funktionen (`FUNCTION`) gelesen werden. |

---

## 6. Audit-Trail (MCP Call Log)

Neben dem JSON-RPC-Stream über `stdio` schreibt der Server für jede eingehende MCP-Methode
einen strukturierten Eintrag in `log/mcp/YYYY-MM-DD/HH-MM-SS-{id}-call.jsonl`. Jeder Eintrag
enthält Zeitstempel, Korrelations-ID (JSON-RPC-`id` oder generierte UUID für Notifications),
Methode, Tool-Name (bei `tools/call`), Roh-Args und die **exakte Response, die an das LLM
ging** — inklusive der ggf. angewendeten Anonymisierung. Damit ist der Trail eine 1:1-
Reproduktion des LLM-Datenflusses, nicht eine Zusammenfassung.

Aufbewahrung und Pfad konfigurierbar unter `SqlToAi:Logging:McpTrail` in `appsettings.json`.
