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
  "Databases": {
    "AnonymizerExclusionSql": "SELECT TableName, ColumnName FROM dbo.AnonymizerExclusions"
  },
  "Anonymizer": {
    "Enabled": true,
    "DefaultMode": "ScramblePattern",
    "ExcludedColumns": ["*Id", "Id", "*Code", "*Type", "Status", "State", "Category"],
    "ExclusionTableName": "dbo.AnonymizerExclusions"
  }
  ```
* **Verhalten:**
  Spalten, die auf eines der Muster in `ExcludedColumns` passen, werden *nie* anonymisiert. Ebenso werden Spalten von der Anonymisierung ausgenommen, die über das datenbankspezifische `AnonymizerExclusionSql` oder die über `ExclusionTableName` definierte Tabelle als Ausnahme zurückgegeben werden. Alle anderen String-Spalten werden anonymisiert, sofern `Enabled: true` ist und das AccessLevel der Zieldatenbank `ReadOnlyAnonymized` ergibt.
* **Zentrale Ausschluss-Tabelle (`ExclusionTableName`):**
  Über diese Option kann optional und zentral ein Tabellenname definiert werden. Wenn diese Tabelle in der jeweiligen Zieldatenbank vorhanden ist, liest der Server automatisch alle Ausnahmen aus ihr aus.
  - **Existenzprüfung:** Der Server prüft die Existenz der Tabelle per `OBJECT_ID` in SQL Server, um Fehler bei nicht vorhandener Tabelle zu vermeiden.
  - **Erwartete Struktur:** Die Tabelle muss mindestens die Spalten `TableName` und `ColumnName` besitzen.
  - **Resilienz:** Ist die Tabelle in einer Datenbank nicht vorhanden oder schlägt die Abfrage fehl, wird dies stillschweigend ignoriert und der Server fällt auf die übrigen Ausschluss-Mechanismen zurück.
* **Datenbank-spezifische Ausnahmen (`AnonymizerExclusionSql`):**
  Über diese Option kann eine SQL-Abfrage definiert werden, die in der jeweiligen Datenbank ausgeführt wird (Unterstützung für SQL-Dateipfade analog zu `AccessCheckSql` ist gegeben). Die Abfrage muss zwei Spalten zurückgeben: die erste enthält den Tabellennamen (z. B. `Kunden`), die zweite den Spaltennamen (z. B. `Name`). Die entsprechenden Felder bleiben bei Abfrageergebnissen im Klartext.
* **Algorithmen:**
  * **ScramblePattern:** Erhält das strukturelle Muster des Strings. Großbuchstaben werden durch ein zufälliges `'X'`, Kleinbuchstaben durch `'x'` und Ziffern durch `'9'` ersetzt (z. B. `Max.Mustermann@mail.de` $\rightarrow$ `Xxx.Xxxxxxxxxx@xxxx.xx`). E-Mail-Adressen, Postleitzahlen und Telefonnummern bleiben für die KI strukturell erkennbar, enthalten aber keinerlei PII mehr.
  * **Hash (Consistency-Hashing):** Generiert einen eindeutigen, reproduzierbaren SHA-256-Hash-Wert pro Text. Dadurch bleiben Relationen und Gruppen (z. B. gleiche Kundennamen in verschiedenen Tabellen) für das LLM logisch verknüpfbar.
* **Bekannte Grenze:** Die Anonymisierung greift ausschließlich bei String-Werten. Eine Spalte, die numerisch typisiert ist (z. B. `INT`/`BIGINT`, etwa eine als Zahl gespeicherte Kundennummer), wird nie anonymisiert, unabhängig von Konfiguration oder Regeln — das ist bei der Modellierung sensibler numerischer Spalten zu beachten.

---

### E. Zentrale, datenbankübergreifende Anonymisierungsregeln (`AnonymizationRules`, optional)

Die Abschnitte `ExclusionTableName`/`AnonymizerExclusionSql` oben leben *in* der jeweiligen Kundendatenbank — praktisch für eine einzelne DB, aber unpraktisch, wenn ein Kunden-Backup eingespielt wird (die Ausnahmen werden dabei mit überschrieben) oder wenn dieselbe Regel für viele, unterschiedlich benannte Kundendatenbanken gelten soll. `AnonymizationRules` löst das, indem die Regeln in einer eigenen, unabhängig konfigurierbaren Datenbank liegen (Server/Datenbank/Zugangsdaten getrennt von der Kundenverbindung — analog zu `MetadataProvider`).

* **Konfiguration:**
  ```json
  "AnonymizationRules": {
    "Enabled": true,
    "Server": "central-sql-server",
    "Database": "SqlToAiConfig",
    "UserId": "config_reader",
    "Password": "...",
    "IntegratedSecurity": false,
    "TableName": "dbo.AnonymizationRules",
    "CommandTimeoutSeconds": 30,
    "CacheTtlSeconds": 300
  }
  ```
  Ist `Server` leer, wird stattdessen die reguläre Kundenverbindung verwendet (Fallback wie bei `MetadataProvider`).
* **Tabellenschema** (siehe [`sql-scripts/03_anonymization_rules.sql`](../sql-scripts/03_anonymization_rules.sql)):

  | Spalte | Typ | Bedeutung |
  | :--- | :--- | :--- |
  | `DatabasePattern` | `NVARCHAR` | SQL-`LIKE`-Muster (`%`, `_`) für den Datenbanknamen, z. B. `%`, `Kunde_%`, exakter Name. |
  | `TablePattern` | `NVARCHAR` | `LIKE`-Muster für den Tabellennamen. |
  | `ColumnPattern` | `NVARCHAR` | `LIKE`-Muster für den Spaltennamen. |
  | `Anonymize` | `BIT` | `0` = Spalte im Klartext zeigen, `1` = anonymisieren. |
  | `IsActive` | `BIT` | Regel temporär deaktivieren, ohne sie zu löschen. |
  | `Comment` | `NVARCHAR` | Freitext-Begründung (empfohlen, da die Regel jetzt kundenübergreifend wirkt). |

* **Auflösung (spezifischste Regel gewinnt):** Für ein konkretes (Datenbank, Tabelle, Spalte)-Tripel werden alle aktiven Regeln ausgewertet, deren Muster passen. Jedes Muster erhält einen Spezifitäts-Score (`2` = exakter Text, `1` = Teil-Wildcard wie `Kunde%Gruppe`, `0` = reiner Platzhalter `%`), gewichtet `DatabasePattern` > `TablePattern` > `ColumnPattern`. Die Regel mit dem höchsten Gesamt-Score gewinnt; bei keinem Treffer bleibt es beim Standardverhalten (anonymisieren). Damit lassen sich beide Kernszenarien ohne Sonderfall abbilden:
  * *Tabelle öffnen, eine Spalte davon ausnehmen:* Regel `(%, FakeConsultants, %, Anonymize=0)` plus eine spezifischere Regel `(%, FakeConsultants, FullName, Anonymize=1)` — `FullName` bleibt anonymisiert, alle anderen Spalten der Tabelle nicht.
  * *Datenbank nur als Allow-List:* Für eine hochsensible Datenbank wird schlicht keine breite `%`-Regel angelegt — nur einzelne, explizite `Anonymize=0`-Regeln pro freigegebener Spalte. Alles andere bleibt beim Default (anonymisieren).
* **Zusammenspiel mit den bestehenden Mechanismen:** `AnonymizationRules` ist ein zusätzlicher, additiver Ausschluss-Kanal neben `ExclusionTableName`/`AnonymizerExclusionSql` — eine Spalte gilt als ausgenommen, sobald *einer* der Mechanismen sie freigibt. In der Praxis nutzt eine Installation typischerweise nur einen der beiden Wege.
* **Caching:** Anders als die Exclusion-Provider oben (die pro Kundendatenbank cachen) lädt `AnonymizationRules` das komplette Regelwerk einmal pro `CacheTtlSeconds` — unabhängig davon, welche Kundendatenbank gerade abgefragt wird.

### F. Proaktive Kennzeichnung & agentische Verhaltenssteuerung

* **`sql_get_schema` markiert Spalten proaktiv:** Die Spalten-Tabelle enthält eine zusätzliche Spalte **„Anonymized“** (`Yes`/`No`), berechnet über dieselben Ausschluss-Quellen wie zur Abfragezeit — das LLM sieht so schon beim Schema-Erkunden, welche Spalten maskiert würden, bevor überhaupt eine Query geschrieben wird. Nicht-String-Typen (siehe Bekannte Grenze oben) werden immer als `No` ausgewiesen.
* **`sql_execute_query`-Hinweis referenziert konkrete Spalten:** Die Anonymisierungs-Notiz (siehe Tool-Spezifikation unten) nennt betroffene Spalten als `Tabelle.Spalte` (sofern die Basistabelle auflösbar ist) statt nur des Spalten-Alias, und enthält eine konkrete Handlungsanweisung: den Nutzer informieren und eine Freischaltung vorschlagen, statt den maskierten Wert als echte Daten zu behandeln. Bei Sichten/Aggregationen wird das LLM angehalten, zunächst mit `sql_get_object_references` die tatsächliche Quelltabelle zu ermitteln.
* **MCP `instructions`-Feld:** Die `initialize`-Antwort enthält ein `instructions`-Feld mit genau dieser Verhaltensrichtlinie in kompakter Form — einmalig beim Verbindungsaufbau an den Client übergeben, statt in jeder Tool-Beschreibung oder jedem Ergebnis wiederholt zu werden.

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
    "Server": "", // Leer = nutzt dieselbe DB
    "Database": "", // Optional: Name einer zentralen Metadatendatenbank (z. B. "Knowhow")
    "UserId": "",
    "Password": "",
    "IntegratedSecurity": false,
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
* **Argumente:** `search_term` (String, Pflicht), `max_results` (Int, optional), `object_type` (String, optional), `database` (String, optional)
* **Zweck:** Sucht nach Objektnamen (Tabellen, Sichten, Prozeduren, Trigger) in `sys.objects` der Zieldatenbank per `LIKE %search_term%`.
* **`object_type`:** Optionaler Filter auf `type_desc` (z. B. `USER_TABLE`, `VIEW`, `SQL_STORED_PROCEDURE`, `SQL_TRIGGER`, `SQL_SCALAR_FUNCTION`), unterstützt LIKE-Wildcards (z. B. `SQL_%`). Nützlich, um gezielt nach Tabellen statt einer Mischung aus Tabellen, Constraints und Triggern zu suchen.
* **Ranking ohne `object_type`:** Tabellen, Sichten, Prozeduren/Funktionen und Trigger werden vor Constraint-Objekten (`FOREIGN_KEY_CONSTRAINT`, `PRIMARY_KEY_CONSTRAINT`, `DEFAULT_CONSTRAINT`, `CHECK_CONSTRAINT`) einsortiert, da letztere zahlenmäßig meist überwiegen und alphabetisch vor `USER_TABLE` sortieren würden.

### 5. `sql_get_schema`
* **Argumente:** `object_name` (String, Pflicht), `database` (String, optional)
* **Zweck:** Liefert das primäre Schema eines Objekts als Markdown-Dokument, angereichert mit Extended Properties / Metadaten.
* **Inhalt:**
  * **TABLE/VIEW:** Spalten-Tabelle (Typ, Nullable, PK, Identity, **Anonymized** (proaktive Kennzeichnung, siehe Abschnitt 2.F), Custom-Beschreibung) + Trigger-Übersicht (Name, Events, Disabled-Status) + **Discovery-Index** (Zähler für Fremdschlüssel, Indizes, Constraints, sowie die Trigger-Namen selbst zur direkten Verwendung mit `sql_get_trigger_definition`).
  * **PROCEDURE/FUNCTION:** DDL-Definitionstext aus `sys.sql_modules` + **Routine-Parameter-Discovery**.

### 6. `sql_get_schema_foreign_keys`
* **Argumente:** `object_name` (String, Pflicht), `database` (String, optional)
* **Zweck:** Progressive Disclosure: Liefert alle ausgehenden und eingehenden Fremdschlüssel einer Tabelle als Markdown-Tabelle.
* **Typ-Prüfung:** Nur für Tabellen und Sichten zulässig; bei anderen Objekttypen (z. B. Prozeduren) schlägt der Aufruf mit `SQL-AI-0110` fehl, statt fälschlich ein leeres Ergebnis zu liefern.
* **Composite Keys:** Ein Fremdschlüssel über mehrere Spalten erscheint als **eine** Zeile (`Tabelle (Spalte1, Spalte2)`) statt einer Zeile pro Spalte, damit die Zeilenanzahl mit dem Discovery-Index-Zähler übereinstimmt.

### 7. `sql_get_schema_indexes`
* **Argumente:** `object_name` (String, Pflicht), `database` (String, optional)
* **Zweck:** Liefert alle Indizes (PK, Unique, Non-Clustered) inklusive Schlüssel- und `INCLUDE`-Spalten als Markdown.
* **Typ-Prüfung:** Nur für Tabellen und Sichten zulässig; siehe `SQL-AI-0110` oben.

### 8. `sql_get_schema_constraints`
* **Argumente:** `object_name` (String, Pflicht), `database` (String, optional)
* **Zweck:** Liefert alle Default- und Check-Constraints inklusive ihrer Definitionstexte als Markdown.
* **Typ-Prüfung:** Nur für Tabellen und Sichten zulässig; siehe `SQL-AI-0110` oben.

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
* **Mehrfach-Content-Rückgabe bei Anonymisierung:** Wurden bei der Abfrage tatsächlich Spalten anonymisiert, liefert das Tool zwei Inhaltsblöcke (`Content` im MCP-Protokoll) zurück:
  1. Einen Hinweis für das LLM, welche `Tabelle.Spalte`-Kombinationen mit welchem Modus anonymisiert wurden, inklusive einer Handlungsanweisung (Nutzer informieren, Freischaltung vorschlagen statt die Werte als echte Daten zu behandeln; siehe Abschnitt 2.F).
  2. Die eigentlichen JSON-Zeilen der Abfrageergebnisse.
  Wurden keine Daten anonymisiert, wird nur der Datenblock zurückgegeben (spart Token).
* **Berechtigungsprüfung:** Schlägt fehl mit `SQL-AI-0107`, falls das Access-Level der Datenbank nur `SchemaOnly` oder `None` ist.

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
| **SQL-AI-0110** | Ungültiger Typ für Detailabfrage | Fremdschlüssel, Indizes und Constraints können nur für Tabellen (`TABLE`) und Sichten (`VIEW`) abgefragt werden. |

---

## 6. Audit-Trail (MCP Call Log)

Neben dem JSON-RPC-Stream über `stdio` schreibt der Server für jede eingehende MCP-Methode
einen strukturierten Eintrag in `log/mcp/YYYY-MM-DD/HH-MM-SS-{id}-call.jsonl`. Jeder Eintrag
enthält Zeitstempel, Korrelations-ID (JSON-RPC-`id` oder generierte UUID für Notifications),
Methode, Tool-Name (bei `tools/call`), Roh-Args und die **exakte Response, die an das LLM
ging** — inklusive der ggf. angewendeten Anonymisierung. Damit ist der Trail eine 1:1-
Reproduktion des LLM-Datenflusses, nicht eine Zusammenfassung.

Aufbewahrung und Pfad konfigurierbar unter `SqlToAi:Logging:McpTrail` in `appsettings.json`.
