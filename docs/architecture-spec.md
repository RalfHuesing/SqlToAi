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

### A. Multi-Database Access & Level-Based Whitelisting

SQL Server instances typically host multiple databases. By default, the MCP server enforces strict Default-Deny: any database not explicitly configured under an access level is blocked (`AccessLevel.None`).

* **Configuration Structure:**
  ```json
  "Databases": {
    "CacheTtlSeconds": 300,
    "ReadWrite": [ "DemoDB" ],
    "ReadOnly": [ "ReportingDB" ],
    "ReadOnlyAnonymized": [ "CustomerDB" ],
    "SchemaOnly": [ "StagingDB" ]
  }
  ```

* **Access Levels & Permissions:**

  | Level Name | Description / Permissions |
  | :--- | :--- |
  | `None` | **Access Blocked (Default).** Any database not listed in `ReadWrite`, `ReadOnly`, `ReadOnlyAnonymized`, or `SchemaOnly` resolves to `None`. All tools fail with `SQL-AI-0104`. |
  | `SchemaOnly` | **Metadata Only.** Schema and search tools (`sql_get_schema`, `sql_search_objects`, etc.) are allowed. `sql_execute_query` is blocked with `SQL-AI-0107`. |
  | `ReadOnlyAnonymized` | **Anonymized Read Access.** Schema tools and `sql_execute_query` SELECTs are allowed; string columns are anonymized. |
  | `ReadOnly` | **Raw Read Access.** Schema tools and `sql_execute_query` SELECTs are allowed without anonymization. |
  | `ReadWrite` | **Full Read & Write Access.** All tools including data-modifying queries via `sql_execute_query` are permitted. Read-Only Guard is bypassed for DML/DDL execution. |

* **Matching & Conflict Resolution Rules:**
  1. **Exact Matching:** Database names are matched case-insensitively using exact string equality (no globs/wildcards).
  2. **Fail-Safe Conflict Resolution:** If a database is listed under multiple access level arrays, the most restrictive level wins:
     $$\text{SchemaOnly} > \text{ReadOnlyAnonymized} > \text{ReadOnly} > \text{ReadWrite}$$
  3. **Global Exclusions:** Databases matching patterns in `SqlServer.ExcludedDatabases` are always blocked regardless of access level declarations.

---

### B. Konfigurierbarer Schreibschutz (Read-Only Guard)
Für jede Datenbank außer solchen mit Access Level `ReadWrite` (Abschnitt A) wird ein mehrstufiger Schreibschutz erzwungen:

1. **Parser-Ebene:** Der Server validiert Statements vor der Ausführung per robusten Regulären Ausdrücken (String-Literale und Kommentare werden dabei ausgeblendet, damit z. B. `SELECT 'DELETE' AS Status` nicht fälschlich blockiert wird). Mutierende SQL-Befehle (`INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `TRUNCATE`, `EXEC`/`EXECUTE` etc.) werden abgewiesen und brechen mit `SQL-AI-0107` ab.
2. **Transaktions-Ebene:** Alle Abfragen werden innerhalb einer expliziten Transaktion ausgeführt. Am Ende der Ausführung wird ein `ROLLBACK` ausgeführt, sodass versehentliche oder böswillige Datenänderungen verworfen werden.
3. **Least-Privilege-Empfehlung:** Der Schreibschutz des Servers dient als "Defense-in-Depth". Die primäre Absicherung muss stets über einen SQL-Login mit minimalen Rechten (z. B. nur Mitgliedschaft in der Rolle `db_datareader`) realisiert werden.

**Ausnahme bei `ReadWrite`:** Ist das ermittelte Access Level `ReadWrite` (Datenbank in `Databases.ReadWrite` konfiguriert), überspringt der Server Schritt 1 vollständig (auch `INSERT`/`UPDATE`/`DELETE`/`EXEC` sind erlaubt) und committet die Transaktion aus Schritt 2 statt sie zurückzurollen. Es gibt **keinen separaten globalen Schalter** dafür — die Freigabe erfolgt ausschließlich pro Datenbank über die `ReadWrite`-Liste (Abschnitt A). Der Mehrfach-Statement-Schutz (`SQL-AI-0101`) bleibt davon unberührt und gilt immer.

---

### C. Per-DB String-Anonymisierung (AccessLevel-gesteuert)
Zum Schutz von PII (Personally Identifiable Information) anonymisiert der Server String-Werte im Arbeitsspeicher, bevor sie an den KI-Agenten übertragen werden. Die Entscheidung *ob* anonymisiert wird, fällt pro Datenbank am `AccessLevel` (siehe Abschnitt A): Befindet sich die Datenbank in der Liste `ReadOnlyAnonymized`, wird jede zurückgegebene String-Spalte anonymisiert; bei `ReadOnly` (Klartext) nicht.

* **Konfiguration:**
  ```json
  "Anonymizer": {
    "Enabled": true,
    "DefaultMode": "ScramblePattern"
  }
  ```
* **Verhalten:**
  Jede String-Spalte wird anonymisiert, sofern `Enabled: true` ist und das AccessLevel der Zieldatenbank `ReadOnlyAnonymized` ergibt, es sei denn, eine passende Regel in `AnonymizationRules` schließt die Spalte explizit von der Anonymisierung aus (`Anonymize=0`, siehe Abschnitt D).
* **Algorithmen:**
  * **ScramblePattern:** Erhält das strukturelle Muster des Strings. Großbuchstaben werden durch ein zufälliges `'X'`, Kleinbuchstaben durch `'x'` und Ziffern durch `'9'` ersetzt (z. B. `Max.Mustermann@mail.de` $\rightarrow$ `Xxx.Xxxxxxxxxx@xxxx.xx`). E-Mail-Adressen, Postleitzahlen und Telefonnummern bleiben für die KI strukturell erkennbar, enthalten aber keinerlei PII mehr.
  * **Hash (Consistency-Hashing):** Generiert einen eindeutigen, reproduzierbaren SHA-256-Hash-Wert pro Text. Dadurch bleiben Relationen und Gruppen (z. B. gleiche Kundennamen in verschiedenen Tabellen) für das LLM logisch verknüpfbar.
* **Bekannte Grenze:** Die Anonymisierung greift ausschließlich bei String-Werten. Eine Spalte, die numerisch typisiert ist (z. B. `INT`/`BIGINT`, etwa eine als Zahl gespeicherte Kundennummer), wird nie anonymisiert, unabhängig von Konfiguration oder Regeln — das ist bei der Modellierung sensibler numerischer Spalten zu beachten.

---

### D. Zentrale Anonymisierungsregeln (`AnonymizationRules`, optional)

Sämtliche Anonymisierungs-Regeln und -Ausschlüsse werden zentral über die `AnonymizationRules`-Konfiguration gesteuert (Server/Datenbank/Zugangsdaten getrennt von der Kundenverbindung — analog zu `MetadataProvider`).

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
  | `Comment` | `NVARCHAR` | Freitext-Begründung (empfohlen, da die Regel kundenübergreifend wirkt). |

* **Auflösung (spezifischste Regel gewinnt):** Für ein konkretes (Datenbank, Tabelle, Spalte)-Tripel werden alle aktiven Regeln ausgewertet, deren Muster passen. Jedes Muster erhält einen Spezifitäts-Score (`2` = exakter Text, `1` = Teil-Wildcard wie `Kunde%Gruppe`, `0` = reiner Platzhalter `%`), gewichtet `DatabasePattern` > `TablePattern` > `ColumnPattern`. Die Regel mit dem höchsten Gesamt-Score gewinnt; bei keinem Treffer bleibt es beim Standardverhalten (anonymisieren). Damit lassen sich beide Kernszenarien ohne Sonderfall abbilden:
  * *Tabelle öffnen, eine Spalte davon ausnehmen:* Regel `(%, FakeConsultants, %, Anonymize=0)` plus eine spezifischere Regel `(%, FakeConsultants, FullName, Anonymize=1)` — `FullName` bleibt anonymisiert, alle anderen Spalten der Tabelle nicht.
  * *Datenbank nur als Allow-List:* Für eine hochsensible Datenbank wird schlicht keine breite `%`-Regel angelegt — nur einzelne, explizite `Anonymize=0`-Regeln pro freigegebener Spalte. Alles andere bleibt beim Default (anonymisieren).
* **Caching:** `AnonymizationRules` lädt das komplette Regelwerk einmal pro `CacheTtlSeconds` — unabhängig davon, welche Kundendatenbank gerade abgefragt wird.

---

### E. Reversible, durchsuchbare Tokenisierung (`Anonymizer.Tokenization`, optional)

Normale Anonymisierung (Abschnitt C) ist bewusst eine Einbahnstraße: Der Server maskiert einen Wert beim Herausgeben, kann ihn aber nicht zurückrechnen. Bei vielen Datenbanken mit hunderten Tabellen kann das zu restriktiv sein — die KI soll denselben Wert über mehere Tabellen hinweg wiederfinden können (`WHERE`, `JOIN`, `LIKE`, Bereichsvergleiche), ohne den Klartext je zu sehen. `Tokenization` löst das durch reversible, hochkompakte Kurz-Tokens.

* **Globaler Modus-Schalter, kein Pro-Spalten-Opt-in:** `Tokenization.Enabled` funktioniert genau wie `DefaultMode` — ist es aktiv, wird *jede* Spalte, die ohnehin anonymisiert würde, tokenisiert statt maskiert. Es gibt bewusst keine Spalten-Allowlist zu pflegen. *Ob* eine Spalte überhaupt anonymisiert wird, entscheiden ausschließlich die `AnonymizationRules` (mit `Anonymize=0`, siehe Abschnitt D) — `Tokenization` ändert nur *wie* eine bereits anonymisierte Spalte anonymisiert wird.
* **Funktionsweise:**
  1. **Ausgabe (Egress):** Für jede anonymisierte Spalte erzeugt der Server ein kompaktes Kurz-Token Schema (`§§§T1§§§`, `§§§T2§§§`, etc. mit ~7 Zeichen / 2-3 LLM Tokens) anstelle langer Base64 Hashes. Derselbe Wert innerhalb einer Sitzung ergibt über den bi-direktionalen `TokenVault` garantiert immer dasselbe Kurz-Token. Der Server merkt sich `Wert ↔ Token` im In-Memory `TokenVault` für die Laufzeit des Prozesses.
  2. **Eingabe (Ingress):** Bevor eine Abfrage gegen `sql_execute_query` ausgeführt wird, durchsucht der Server jedes String-Literal (niemals Kommentare, `[...]`-Bezeichner oder SQL-Schlüsselwörter) nach dem Token-Muster. Ein erkanntes, im Vault bekanntes Token wird durch den Realwert ersetzt — SQL Server sieht danach eine ganz normale Abfrage gegen echte Daten. Ein unbekanntes (geratenes/gefälschtes) Token bleibt unverändert stehen; das Prädikat findet dann schlicht keine Treffer, statt einen Fehler zu werfen.
  3. **Wichtige Eigenschaft:** Da die Datenbank selbst nie verändert wird — nur der Text, den die KI schreibt, wird vor der Ausführung textuell ersetzt —, funktionieren praktisch alle Operatoren (`=`, `IN`, `LIKE '%...%'`, `>=`/`<=`, `JOIN ... ON`), solange die KI ein zuvor tatsächlich ausgehändigtes, vollständiges Token verwendet. Was nicht geht (und auch nicht gehen soll): Ein *Teil* eines Tokens erraten oder selbst konstruieren — das Token trägt keine positionale Beziehung zum Realwert.
* **Konfiguration:**
  ```json
  "Anonymizer": {
    "Tokenization": {
      "Enabled": false,
      "Prefix": "§§§",
      "Suffix": "§§§"
    }
  }
  ```
  * `Enabled`: Hauptschalter.
  * `Prefix`/`Suffix`: Umschließen jedes Token eindeutig, damit die Ingress-Erkennung Tokens sicher von normalem Text unterscheidet.
* **Bekannte Grenzen:**
  * **Cross-Referenz:** Da Tokens deterministisch sind, kann die KI Token↔Wert selbst zuordnen, sobald derselbe Wert irgendwo (z. B. über eine Ausschlussregel) im Klartext sichtbar ist. Freigabe-Entscheidungen sollten daher konsistent gepflegt werden.
  * **Speicherung:** Der Token-Vault lebt nur im Arbeitsspeicher des laufenden Server-Prozesses (kein Neustart-Überstand) — ausreichend für eine laufende Analyse-Sitzung.
  * Gilt, wie die reguläre Anonymisierung, nur für String-Werte.

### F. Proaktive Kennzeichnung & agentische Verhaltenssteuerung

* **`sql_get_schema` markiert Spalten proaktiv:** Die Spalten-Tabelle enthält eine zusätzliche Spalte **„Anonymized“** mit drei möglichen Werten — `No`, `Yes` (Scramble/Hash-Maskierung), oder `Yes (searchable)` (reversibles Token, siehe Abschnitt E) — berechnet über dieselben Ausschluss-/Tokenisierungs-Quellen wie zur Abfragezeit. Das LLM sieht so schon beim Schema-Erkunden, welche Spalten maskiert *oder* tokenisiert würden, bevor überhaupt eine Query geschrieben wird. Nicht-String-Typen (siehe Bekannte Grenze oben) werden immer als `No` ausgewiesen.
* **`sql_execute_query`-Hinweis referenziert konkrete Spalten:** Die Anonymisierungs-Notiz (siehe Tool-Spezifikation unten) nennt betroffene Spalten als `Tabelle.Spalte` (sofern die Basistabelle auflösbar ist) statt nur des Spalten-Alias, und enthält eine konkrete Handlungsanweisung: den Nutzer informieren und eine Freischaltung vorschlagen, statt den maskierten Wert als echte Daten zu behandeln. Bei Sichten/Aggregationen wird das LLM angehalten, zunächst mit `sql_get_object_references` die tatsächliche Quelltabelle zu ermitteln. Sind darunter Spalten mit reversiblem Token (Abschnitt E), ergänzt die Notiz einen zweiten, nur dann angehängten Satz: welche der genannten Spalten Tokens statt maskierten Text liefern, und dass dieser Wert unverändert in eine spätere `WHERE`/`JOIN`/`LIKE`/`IN`/Bereichs-Bedingung übernommen werden kann — der Server löst ihn vor der Ausführung zum Realwert auf. Ohne tokenisierte Spalten im Ergebnis entfällt dieser Satz komplett (Token-Effizienz).
* **MCP `instructions`-Feld:** Die `initialize`-Antwort enthält ein `instructions`-Feld mit genau dieser Verhaltensrichtlinie in kompakter Form — einmalig beim Verbindungsaufbau an den Client übergeben, statt in jeder Tool-Beschreibung oder jedem Ergebnis wiederholt zu werden. Es erklärt seit Einführung der Tokenisierung zusätzlich, dass ein Token unverändert wiederverwendet werden darf/soll, aber nie selbst konstruiert oder verändert werden darf.

---

### G. Empfohlene SQL-Server-Berechtigungen für den DB-User

Für eine optimale Analysefähigkeit des KI-Agenten bei minimalem Rechtetransfer (Least Privilege) werden folgende SQL Server-Berechtigungen auf der Zieldatenbank empfohlen:

```sql
USE [Zieldatenbank];

-- 1. Lesezugriff auf Tabellen & Sichten (für sql_execute_query, sql_compare_queries)
ALTER ROLE [db_datareader] ADD MEMBER [SqlToAiUser];

-- 2. DDL-Quelltexte von Sichten, Prozeduren, Funktionen & Triggern lesen (für sql_get_schema, sql_get_trigger_definition)
GRANT VIEW DEFINITION TO [SqlToAiUser];

-- 3. Ausführungsplan-XML & Indexempfehlungen analysieren (für sql_measure_performance, sql_benchmark_optimization)
GRANT SHOWPLAN TO [SqlToAiUser];

-- 4. Serverweit kumulierte DMV-Index-Empfehlungen abfragen (für sql_suggest_indexes)
GRANT VIEW SERVER STATE TO [SqlToAiUser];
```

* **`db_datareader`**: Erlaubt das Lesen aller Tabellendaten für Abfragen und Äquivalenzvergleiche.
* **`VIEW DEFINITION`**: Ermöglicht das Auslesen der Quelltexte von Views, Stored Procedures, Funktionen und Triggern aus `sys.sql_modules`. Ohne dieses Recht maskiert SQL Server die Definitionstexte für Nicht-Eigentümer.
* **`SHOWPLAN`**: Schaltet den tatsächlichen XML-Ausführungsplan (`STATISTICS XML`) und Index-Empfehlungen für die Performance-Analyse frei. Fehlt das Recht, degradiert das Tool automatisch auf reine IO/TIME-Leistungsmessungen.
* **`VIEW SERVER STATE`**: Server-scoped; ermöglicht das Abfragen der `sys.dm_db_missing_index_*`-DMVs für `sql_suggest_indexes`. Fehlt das Recht, liefert das Tool eine strukturierte Markdown-Notiz mit Hinweis auf die fehlende Berechtigung, statt mit einem Hard-Error (`SQL-AI-0102`) abzubrechen.

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
* **Argumente:** `query` (String, Pflicht), `database` (String, Pflicht), `parameters` (Object, optional — typisierte SQL-Parameter).
* **Zweck:** Prüft eine SQL-Abfrage fachlich und technisch (Syntax-Check über `PARSEONLY` im Kontext der Zieldatenbank), ohne sie auszuführen.

### 4. `sql_search_objects`
* **Argumente:** `search_term` (String, Pflicht), `max_results` (Int, optional), `object_type` (String, optional), `database` (String, Pflicht)
* **Zweck:** Sucht nach Objektnamen (Tabellen, Sichten, Prozeduren, Trigger) in `sys.objects` der Zieldatenbank per `LIKE %search_term%`.
* **`object_type`:** Optionaler Filter auf `type_desc` (z. B. `USER_TABLE`, `VIEW`, `SQL_STORED_PROCEDURE`, `SQL_TRIGGER`, `SQL_SCALAR_FUNCTION`), unterstützt LIKE-Wildcards (z. B. `SQL_%`). Nützlich, um gezielt nach Tabellen statt einer Mischung aus Tabellen, Constraints und Triggern zu suchen.
* **Ranking ohne `object_type`:** Tabellen, Sichten, Prozeduren/Funktionen und Trigger werden vor Constraint-Objekten (`FOREIGN_KEY_CONSTRAINT`, `PRIMARY_KEY_CONSTRAINT`, `DEFAULT_CONSTRAINT`, `CHECK_CONSTRAINT`) einsortiert, da letztere zahlenmäßig meist überwiegen und alphabetisch vor `USER_TABLE` sortieren würden.

### 5. `sql_get_schema`
* **Argumente:** `object_name` (String, Pflicht), `database` (String, Pflicht)
* **Zweck:** Liefert das primäre Schema eines Objekts als Markdown-Dokument, angereichert mit Extended Properties / Metadaten.
* **Inhalt:**
  * **TABLE/VIEW:** Spalten-Tabelle (Typ, Nullable, PK, Identity, **Anonymized** (proaktive Kennzeichnung, siehe Abschnitt 2.F), Custom-Beschreibung) + Trigger-Übersicht (Name, Events, Disabled-Status) + **Discovery-Index** (Zähler für Fremdschlüssel, Indizes, Constraints, sowie die Trigger-Namen selbst zur direkten Verwendung mit `sql_get_trigger_definition`).
  * **PROCEDURE/FUNCTION:** DDL-Definitionstext aus `sys.sql_modules` + **Routine-Parameter-Discovery**.

### 6. `sql_get_schema_foreign_keys`
* **Argumente:** `object_name` (String, Pflicht), `database` (String, Pflicht)
* **Zweck:** Progressive Disclosure: Liefert alle ausgehenden und eingehenden Fremdschlüssel einer Tabelle als Markdown-Tabelle.
* **Typ-Prüfung:** Nur für Tabellen und Sichten zulässig; bei anderen Objekttypen (z. B. Prozeduren) schlägt der Aufruf mit `SQL-AI-0110` fehl, statt fälschlich ein leeres Ergebnis zu liefern.
* **Composite Keys:** Ein Fremdschlüssel über mehrere Spalten erscheint als **eine** Zeile (`Tabelle (Spalte1, Spalte2)`) statt einer Zeile pro Spalte, damit die Zeilenanzahl mit dem Discovery-Index-Zähler übereinstimmt.

### 7. `sql_get_schema_indexes`
* **Argumente:** `object_name` (String, Pflicht), `database` (String, Pflicht)
* **Zweck:** Liefert alle Indizes (PK, Unique, Non-Clustered) inklusive Schlüssel- und `INCLUDE`-Spalten als Markdown.
* **Typ-Prüfung:** Nur für Tabellen und Sichten zulässig; siehe `SQL-AI-0110` oben.

### 8. `sql_get_schema_constraints`
* **Argumente:** `object_name` (String, Pflicht), `database` (String, Pflicht)
* **Zweck:** Liefert alle Default- und Check-Constraints inklusive ihrer Definitionstexte als Markdown.
* **Typ-Prüfung:** Nur für Tabellen und Sichten zulässig; siehe `SQL-AI-0110` oben.

### 9. `sql_get_trigger_definition`
* **Argumente:** `object_name` (Parent, Pflicht), `trigger_name` (Trigger, Pflicht), `database` (String, Pflicht)
* **Zweck:** Liefert die vollständige DDL-Definition (`CREATE TRIGGER ...`) eines DML-Triggers.

### 10. `sql_get_object_references`
* **Argumente:** `object_name` (String, Pflicht), `database` (String, Pflicht)
* **Zweck:** Zeigt statische Referenzen ("Wo wird diese Tabelle/Sicht verwendet?") über die DMV `sys.dm_sql_referencing_entities`.

### 11. `sql_get_routine_parameters`
* **Argumente:** `object_name` (String, Pflicht), `database` (String, Pflicht)
* **Zweck:** Liefert Parameter, Typen und Rückgabestrukturen für Prozeduren und Funktionen.

### 12. `sql_execute_query`
* **Argumente:** `query` (String, Pflicht), `requested_row_limit` (Int, optional), `database` (String, Pflicht), `parameters` (Object, optional — typisierte SQL-Parameter).
* **Zweck:** Führt ein einzelnes SQL-SELECT-Statement aus.
* **Statement-Struktur & DECLARE-Support:** Vorangestellte T-SQL `DECLARE @Variable Typ = Wert;`-Anweisungen am Anfang lesender Abfragen (z. B. in bestehenden Skriptdateien) werden unterstützt, sofern am Ende exakt eine lesende Hauptabfrage steht. Mehrere lesende Hauptabfragen (`SELECT 1; SELECT 2;`) führen weiterhin zu Fehler `SQL-AI-0101`.
* **Datenverarbeitung:** Anwendbare Limits greifen (Default: 100 Zeilen). String-Spalten werden anonymisiert, falls aktiviert und passend zu den Regeln.
* **Token-Auflösung (falls `Anonymizer.Tokenization` aktiv):** Bevor die Abfrage ausgeführt wird, löst der Server jedes erkannte, gültige Anonymisierungs-Token in String-Literalen zum Realwert auf (siehe Abschnitt 2.E). Die KI kann so mit zuvor erhaltenen Tokens filtern/joinen, ohne den Wert je zu kennen.
* **Mehrfach-Content-Rückgabe & Laufzeit-Metadaten:** Das Tool liefert strukturierte Inhaltsblöcke (`Content` im MCP-Protokoll) zurück:
  1. Einen Anonymisierungs-Hinweis (sofern Spalten anonymisiert wurden; siehe Abschnitt 2.F).
  2. Einen `Execution Info`-Header: `Execution Info: X rows returned in Y ms | cpu: Z ms | logical reads: W.`
     `cpu_time_ms`/`logical_reads` werden serverseitig bei jedem Aufruf über `SET STATISTICS IO/TIME`
     gemessen (kein Parameter nötig, kein zusätzlicher Roundtrip). `Y` bleibt die reine
     Client-Laufzeit der Abfrage selbst und ist nicht identisch mit `Z` (`cpu`).
  3. Die eigentlichen JSON-Zeilen der Abfrageergebnisse.
* **Berechtigungsprüfung:** Schlägt fehl mit `SQL-AI-0107`, falls das Access-Level der Datenbank nur `SchemaOnly` oder `None` ist.

### 13. `sql_compare_queries`
* **Argumente:** `database` (String, Pflicht), `query_a` (String, Pflicht), `query_b` (String, Pflicht), `parameters_a` (Object, optional), `parameters_b` (Object, optional), `parameters` (Object, optional — gemeinsame Parameter), `max_diff_rows` (Int, optional — Default: 5).
* **Zweck:** Vergleicht zwei SQL-Abfragen auf der Zieldatenbank auf semantische Ergebnissatz-Gleichheit ohne Übertragung großer Datenmengen.
* **Prüfschritte:**
  1. **Schema-Check:** Vergleicht Spaltenanzahl, Spaltennamen und Datentypen via `CommandBehavior.SchemaOnly`.
  2. **Count-Check:** Vergleicht die exakte Zeilenanzahl via `COUNT_BIG(*)`.
  3. **Set-Differenz (EXCEPT):** Führt DB-seitige Set-Differenzen (`A EXCEPT B` und `B EXCEPT A`) aus und liefert Beispielzeilen für Abweichungen zurück.

### 14. `sql_measure_performance`
* **Argumente:** `database` (String, Pflicht), `query` (String, Pflicht), `parameters` (Object, optional), `warmup_runs` (Int, optional — Default: 1, steuert die Anzahl initialer, ungemessener Aufwärm-Läufe zum Vorwärmen des Plan-Cache), `execution_runs` (Int, optional — Default: 1, steuert die Anzahl gemessener Läufe, deren Werte gemittelt werden), `include_plan_analysis` (Bool, optional — Default: true).
* **Zweck:** Erfasst präzise Server-Metriken (CPU-Zeit, Elapsed Time, Logical Reads, Physical Reads, Read-Ahead Reads) via T-SQL `STATISTICS IO, TIME` und parst den XML-Ausführungsplan (`Missing Indexes`, `CONVERT_IMPLICIT`, `Table Scans`).
* **Graceful Degradation:** Fehlt dem Datenbankbenutzer die `SHOWPLAN`-Berechtigung, degradiert das Tool automatisch auf reine IO/TIME-Messung und gibt einen entsprechenden Hinweis zurück.
* **Rückgabestruktur (`PerformanceMeasurementResult`):** `database`, `runs_evaluated`, `warmup_runs`,
  `metrics`, `warnings[]` (je `type`/`severity`/`message`/`impact` aus dem tatsächlichen
  Ausführungsplan-XML; `MissingIndex`-Warnings enthalten zusätzlich `missing_index_statement`
  (string, nullable) mit dem fertigen `CREATE NONCLUSTERED INDEX`-DDL aus den
  `ColumnGroup`-Spalten — `null`, wenn keine Schlüssel- (`EQUALITY`/`INEQUALITY`)-Spalten
  vorhanden sind und somit kein baubares Index-Statement), `has_showplan_permission`,
  `showplan_note`. `metrics` enthält `cpu_time_ms`/`elapsed_time_ms`/`logical_reads`/
  `physical_reads`/`read_ahead_reads` (Mittelwerte) sowie die nullable
  `min_elapsed_ms`/`max_elapsed_ms`/`min_cpu_ms`/`max_cpu_ms` — diese vier sind nur befüllt,
  wenn `execution_runs > 1` ist (sonst `null`), und existieren nur für `elapsed`/`cpu`,
  nicht für die drei Reads-Felder.

### 15. `sql_benchmark_optimization`
* **Argumente:** `database` (String, Pflicht), `query_a` (Baseline, Pflicht), `query_b` (Kandidat, Pflicht), `parameters_a` (Object, optional), `parameters_b` (Object, optional), `parameters` (Object, optional), `warmup_runs` (Int, optional), `execution_runs` (Int, optional).
* **Zweck:** Kombinierter All-in-One Benchmark zur Evaluierung von SQL-Optimierungen. Führt Äquivalenzvergleich und Performancemessungen für beide Abfragen durch, berechnet prozentuale und absolute Deltas (CPU, IO) und liefert ein klares Urteil (`Recommended`, `NotRecommended`, `Neutral`, `UnsafeDueToDataMismatch`).
* **Rückgabestruktur (`OptimizationBenchmarkResult`):** `database`, `verdict`, `summary`,
  `comparison` (vollständiges `sql_compare_queries`-Ergebnis), `performance_a`/`performance_b` (je ein
  vollständiges `sql_measure_performance`-Ergebnis wie unter Punkt 14 beschrieben) sowie `deltas`
  (`BenchmarkMetricsDelta`) mit `cpu_time`/`elapsed_time`/`logical_reads`/`physical_reads`, je ein
  `MetricDelta`-Objekt mit `baseline_value`/`candidate_value`/`absolute_delta`/`percentage_delta`
  (ein negativer `percentage_delta` bedeutet, dass der Kandidat sich verbessert hat).

### 16. `sql_suggest_indexes`
* **Argumente:** `database` (String, Pflicht), `table_name` (String, optional — `LIKE`-Substring-Filter auf die `statement`-Spalte der DMV, z. B. `Orders` oder `dbo.%`, case-insensitive), `min_score` (Number, optional — Mindest-`improvement_score`; Zeilen darunter werden ausgeschlossen, Default 0 = kein Filter), `top` (Int, optional — Maximalanzahl zurückgegebener Empfehlungen, Default 10).
* **Zweck:** Liefert die serverweit kumulierten, seit dem letzten SQL-Server-Neustart akkumulierten Missing-Index-Empfehlungen aus den DMVs `sys.dm_db_missing_index_group_stats`, `sys.dm_db_missing_index_details` und `sys.dm_db_missing_index_columns`. Pro Empfehlung wird ein `improvement_score` (Formel `avg_total_user_cost × avg_user_impact × (user_seeks + user_scans)`) berechnet und das Ergebnis absteigend nach Score sortiert. Pro Zeile werden Tabelle, Equality-/Inequality-/Include-Spaltenlisten, Seek-/Scan-Counts und der Last-Seek-Zeitstempel ausgegeben.
* **Restart-Hinweis:** Die Ausgabe beginnt mit einem festen Hinweis-Block, dass die DMV-Daten seit dem letzten Server-Neustart akkumuliert werden — auf frisch gestarteten Servern liefert das Tool entsprechend wenig oder nichts, auf lang laufenden Produktionsservern ist es aussagekräftig.
* **Graceful Degradation:** Fehlt dem DB-User die server-scoped `VIEW SERVER STATE`-Berechtigung, gibt das Tool eine strukturierte Markdown-Notiz (inkl. Restart-Hinweis) zurück statt eines harten Fehlers. Die für diese Permission nötige Grant-Anweisung ist in §G dokumentiert.
* **Rückgabeformat:** Markdown — `# Missing Index Recommendations — <database>`, Restart-Hinweis-Block, optional Hinweis „No missing-index recommendations found", sonst Tabelle mit Spalten `Score | Table | Equality Columns | Inequality Columns | Include Columns | Seeks | Scans | Last Seek`. `Score` ist auf eine Ganzzahl gerundet (`improvement_score`), `Last Seek` ist `yyyy-MM-dd` oder `-`, Spaltenlisten sind kommagetrennte Spalten-IDs aus `sys.dm_db_missing_index_columns` (gruppiert nach `column_usage`: EQUALITY / INEQUALITY / INCLUDE).
* **Berechtigungen:** `VIEW SERVER STATE` (server-scoped) — siehe §G.

---

## 5. Fehlercodes (Error-Catalog)

Tritt bei der Ausführung eines Tools ein Fehler auf, wird das Tool-Ergebnis als fehlgeschlagen markiert (`IsSuccess = false`) und einer der folgenden standardisierten Fehlercodes zurückgegeben:

| Fehlercode | Bezeichnung | Bedeutung / Ursache |
| :--- | :--- | :--- |
| **SQL-AI-0001** | Ungültige Parameter | Die an das Tool übergebenen Argumente sind ungültig oder unvollständig. |
| **SQL-AI-0101** | Mehrfach-Statements verboten | Die Ausführung von mehreren SQL-Statements (z. B. getrennt durch `;`) ist nicht erlaubt. |
| **SQL-AI-0102** | Abfragefehler | Der SQL-Server hat einen Fehler bei der Syntax oder Ausführung der Query gemeldet. |
| **SQL-AI-0103** | Objekt nicht gefunden | Das angeforderte Datenbankobjekt (Tabelle, Prozedur etc.) existiert nicht. |
| **SQL-AI-0104** | Safety-Check fehlgeschlagen | Die Zieldatenbank ist in keiner erlaubten Zugriffsstufe konfiguriert (ergibt `AccessLevel.None`) oder wurde durch ein Ausschlussmuster (`ExcludedDatabases`) blockiert. |
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

---

## 7. Automatische Konfigurations-Migration (Smart Auto-Migrator)

Beim Start von `SqlToAi` prüft der `AppSettingsMigrator` die lokale `appsettings.json` gegenüber dem in der Assembly eingebetteten Werkstemplate:
* **Neue Optionen:** Werden automatisch mit den Werkseinstellungen eingefügt.
* **Obsolete Optionen:** Werden aus der Konfigurationsdatei entfernt.
* **Nutzeranpassungen:** Vom Nutzer geänderte Werte (z. B. Connection Strings, Passwörter, Whitelists) bleiben unverändert erhalten.
* **Backup:** Werden Änderungen vorgenommen, legt der Server vor dem Speichern eine Sicherungsdatei `appsettings.json.bak` an und protokolliert die Anpassungen über das Logging-System.
