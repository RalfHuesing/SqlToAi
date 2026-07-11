# SqlToAi MCP Server: Technische Spezifikation & Konzept

Dieses Dokument definiert das vollständige Konzept, die Sicherheitsmechanismen und das Transport-Mapping des **SqlToAi MCP-Servers** (Model Context Protocol). Der Server fungiert als sicheres Bindeglied zwischen KI-Agenten (z. B. in Cursor, Claude Desktop, Windsurf) und Microsoft SQL Server Datenbanken.

---

## 1. Design-Prinzipien

1. **Lokaler Fokus (Developer-First):** Der Server kommuniziert primär über **Stdio (Standard Input/Output)** mit dem AI-Client. Dies ist extrem performant, benötigt keine Internetverbindung oder separate Ports und läuft direkt im Kontext des Entwicklers.
2. **Sicherheit durch Isolation (Safety-by-Design):** Der Server schützt sensible Datenbanken aktiv vor unautorisierten Schreibzugriffen und verhindert den Datenabfluss von personenbezogenen Daten (PII).
3. **Plattform-Agnostik:** Keine Abhängigkeiten zu speziellen Plattform-Boilerplates oder geschlossenen Systemen. Alle Konzepte sind verallgemeinert und über Standard-SQL-Queries konfigurierbar.
4. **Markdown für Agenten, JSON für Strukturen:** Metadaten und Schemainformationen werden als lesbares Markdown an das LLM übergeben. Datenabfragen liefern strukturierte JSON-Zeilen.

---

## 2. Sicherheits- & Guardrail-Konzepte

### A. Dynamischer Safety- & Demo-Check
Um zu verhindern, dass die KI auf echten Produktionsdatenbanken arbeitet (z. B. bei einer Fehlkonfiguration), prüft der Server jede Zieldatenbank vor dem ersten Zugriff.

* **Konfiguration:**
  ```json
  "SqlDatabase": {
    "EnforceSafetyCheck": true,
    "SafetyCheckSql": "SELECT 1 WHERE DB_NAME() LIKE '%demo%' OR DB_NAME() LIKE '%test%'"
  }
  }
  ```
* **Mechanismus:** Vor Ausführung eines Tools auf einer Datenbank wird die `SafetyCheckSql`-Query ausgeführt. Wenn die Abfrage kein Ergebnis liefert oder einen Fehler wirft, wird der gesamte Zugriff blockiert und der Fehler `SQL-AI-0104` zurückgegeben.

### B. Konfigurierbarer Schreibschutz (Read-Only Mode)
Der Schreibschutz schützt die Datenbank vor destruktiven oder verändernden Befehlen der KI. Er kann global oder granular pro Datenbank aktiviert werden.

* **Konfiguration:**
  ```json
  "SqlDatabase": {
    "ReadOnly": true
  }
  ```
* **Sicherheitsstufen:**
  1. **Verbindungs-Ebene:** Der ConnectionString wird um `ApplicationIntent=ReadOnly` ergänzt.
  2. **Transaktions-Ebene:** Abfragen werden in einer schreibgeschützten Transaktion ausgeführt, die am Ende verworfen (`Rollback`) wird.
  3. **Parser-Ebene:** Einfache Validierung (Regex), die mutierende SQL-Schlüsselwörter (`INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `TRUNCATE` etc.) blockiert. Versucht die KI ein solches Statement auszuführen, bricht der Server mit `SQL-AI-0107` ab.

### C. On-the-Fly String-Anonymisierung
Um Kunden- und Personendaten zu schützen, anonymisiert der Server alle String-Werte in den Abfrageergebnissen von `sql_execute_query` im Arbeitsspeicher, bevor sie an die KI gesendet werden.

* **Konfiguration:**
  ```json
  "Anonymizer": {
    "Enabled": true,
    "Mode": "ScramblePattern", // Optionen: ScramblePattern, Hash, Mask
    "ExcludedColumns": ["Id", "Code", "Type"] // Technische Spalten nicht anonymisieren
  }
  ```
* **Algorithmen:**
  * **ScramblePattern (Standard):** Erhält das strukturelle Muster des Strings. Großbuchstaben werden durch ein zufälliges `'X'`, Kleinbuchstaben durch `'x'` und Ziffern durch `'9'` ersetzt. E-Mail-Adressen, Postleitzahlen und Telefonnummern bleiben für die KI strukturell erkennbar (z. B. `Max.Mustermann@mail.de` $\rightarrow$ `Xxx.Xxxxxxxxxx@xxxx.xx`), enthalten aber keinerlei PII mehr.
  * **Hash (Consistency-Hashing):** Generiert einen eindeutigen, reproduzierbaren Hash-Wert pro Text. Dadurch bleiben Relationen und Gruppen (z. B. gleiche Kundennamen in verschiedenen Tabellen) für das LLM logisch verknüpfbar.

---

## 3. Schema-Enrichment (Dokumentations-Kopplung)

Oft sind technische Datenbankstrukturen kryptisch. Der Server kann Schemaabfragen automatisch um fachliche Beschreibungen anreichern, die in einer separaten Tabelle (oder derselben DB) gepflegt werden.

* **Konfiguration:**
  ```json
  "MetadataProvider": {
    "Enabled": true,
    "ConnectionString": "", // Leer = nutzt dieselbe DB
    "TableMetadataQuery": "SELECT Description FROM dbo.TableDocs WHERE TableName = @TableName",
    "ColumnMetadataQuery": "SELECT ColumnName, Description FROM dbo.ColumnDocs WHERE TableName = @TableName"
  }
  ```
* **Funktionsweise:** Bei einem Aufruf von `sql_get_schema` führt der Server parallel die konfigurierten Queries aus und fügt die gefundenen Texte nahtlos in das erzeugte Markdown-Dokument ein.

---

## 4. MCP Tool-Spezifikationen

Der Server stellt der KI folgende Tools zur Verfügung. Jedes Tool gibt bei Fehlern ein strukturiertes JSON mit `IsSuccess=false` und einem Fehlercode zurück. Bei Erfolg wird ein Markdown-Text oder eine JSON-Payload geliefert.

### 1. `sql_list_databases`
* **Zweck:** Listet alle sichtbaren Datenbanken auf dem SQL-Server auf.
* **Filterung:** Nur Datenbanken, auf die der SQL-Login Zugriff hat (`HAS_DBACCESS = 1`), abzüglich explizit ausgeschlossener System-DBs (`master`, `tempdb` etc.) und konfigurierter Ausschlüsse (`ExcludedDatabases`).
* **Rückgabe:** JSON-Array von Datenbanknamen.

### 2. `sql_search_databases`
* **Argumente:** `search_term` (String, Pflicht)
* **Zweck:** Filtert die Liste der sichtbaren Datenbanken nach einem Teilstring.
* **Besonderheit:** Ein leerer Suchbegriff liefert eine leere Liste mit `IsSuccess=true` (kein Fehler).

### 3. `sql_validate_query`
* **Argumente:** `query` (String, Pflicht), `database` (String, optional)
* **Zweck:** Prüft eine SQL-Abfrage fachlich und technisch (Syntax-Check über `PARSEONLY`), ohne sie auszuführen.
* **Rückgabe:** `IsValid` (Boolean), `Reason` (String, falls ungültig).

### 4. `sql_search_objects`
* **Argumente:** `search_term` (String, Pflicht), `max_results` (Int, optional), `database` (String, optional)
* **Zweck:** Sucht nach Objektnamen (Tabellen, Sichten, Prozeduren, Trigger) in `sys.objects` per `LIKE %search_term%`.
* **Rückgabe:** Liste von Objekten mit Name und Typ-Kürzel.

### 5. `sql_get_schema`
* **Argumente:** `object_name` (String, Pflicht), `database` (String, optional)
* **Zweck:** Liefert das primäre Schema eines Objekts als Markdown-Dokument.
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
* **Einschränkung:** Nur ein einzelnes Statement erlaubt (Semikolon-Trennung mehrerer Queries führt zu Fehler).
* **Datenverarbeitung:** Anwendbare Limits greifen (Default: 100 Zeilen). String-Spalten werden anonymisiert, falls aktiviert.
* **Rückgabe:** Strukturierte JSON-Zeilen + Zeilenanzahl.

---

## 5. Fehlercodes (Error-Catalog)

Tritt bei der Ausführung eines Tools ein Fehler auf, wird das Tool-Ergebnis als fehlgeschlagen markiert und einer der folgenden standardisierten Fehlercodes zurückgegeben:

| Fehlercode | Bezeichnung | Bedeutung / Ursache |
| :--- | :--- | :--- |
| **SQL-AI-0001** | Ungültige Parameter | Die an das Tool übergebenen Argumente sind ungültig oder unvollständig. |
| **SQL-AI-0101** | Mehrfach-Statements verboten | Die Ausführung von mehreren SQL-Statements (z. B. getrennt durch `;`) ist nicht erlaubt. |
| **SQL-AI-0102** | Abfragefehler | Der SQL-Server hat einen Fehler bei der Syntax oder Ausführung der Query gemeldet. |
| **SQL-AI-0103** | Objekt nicht gefunden | Das angeforderte Datenbankobjekt (Tabelle, Prozedur etc.) existiert nicht. |
| **SQL-AI-0104** | Safety-Check fehlgeschlagen | Die Datenbank wurde durch den konfigurierten `SafetyCheckSql` als unsicher eingestuft. |
| **SQL-AI-0105** | Infrastrukturfehler | Verbindung zum SQL-Server konnte nicht aufgebaut werden oder brach ab. |
| **SQL-AI-0106** | Timeout | Die Ausführung der SQL-Abfrage hat das konfigurierte Zeitlimit überschritten. |
| **SQL-AI-0107** | Schreiboperation blockiert | Ein mutierendes Statement (`INSERT`/`UPDATE` etc.) wurde im Read-Only-Modus abgewiesen. |
| **SQL-AI-0108** | Ungültiger Typ für Referenzen | Objektreferenzen können nur für Tabellen (`TABLE`) und Sichten (`VIEW`) abgefragt werden. |
| **SQL-AI-0109** | Ungültiger Typ für Parameter | Routine-Parameter können nur für Prozeduren (`PROCEDURE`) und Funktionen (`FUNCTION`) gelesen werden. |
