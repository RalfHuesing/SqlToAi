---
status: draft
---

# Feature-Konzept: Datenexport in lokale Dateien (`sql_export_data`)

## 1. Intention & Motivation

Bislang gibt [`sql_execute_query`](../../docs/tools.md#query-and-script-execution) die Ergebniszeilen immer direkt als Textblock im MCP-Antwort-Payload (NDJSON / JSONL) zurück.

* **Für Programmier- und direkte Arbeits-Tasks** ist dieses Verhalten ideal: Der Agent sieht die Daten unmittelbar im Kontextfenster und kann ad-hoc damit arbeiten. **Dieses Verhalten bleibt zu 100 % unverändert erhalten.**
* **Für Analyse-, Data-Science- und Research-Tasks** hat das direkte Zurückliefern großer Datenmengen im Kontextfenster gravierende Nachteile:
  * Das Token-Budget wird überflutet oder vorzeitig erschöpft („Context Bombing“).
  * Modell-Latenzen und API-Kosten steigen drastisch; die Schärfe der Argumentation sinkt („Lost in the Middle“-Effekt).
  * Nachgelagerte Analysen (z. B. per Python, Pandas, Matplotlib, Seaborn) müssen die Daten erst wieder umständlich aus dem Prompt-Kontext herauskratzen und in lokale Dateien schreiben.
* **Ziel des Features**: Ein kontrollierter Export von Abfrageergebnissen direkt in das lokale Dateisystem (maschinenlesbare Formate wie CSV und JSONL).
  * Der MCP-Response bleibt schlank und transportiert ausschließlich **Metadaten** (Pfad, Zeilenanzahl, Dateigröße, Spaltenliste, Performance-Metriken).
  * Ein Folge-Task (z. B. ein Datenanalyse-Subagent oder ein lokales Python-Skript) kann direkt auf der lokalen Datei aufsetzen und Visualisierungen oder tiefere Auswertungen erstellen.

---

## 2. Festgelegte Leitplanken (Settled Decisions)

Folgende verbindliche Entscheidungen wurden getroffen:

1. **Eigenständiges MCP-Tool `sql_export_data`**:
   * Keine Überfrachtung von `sql_execute_query`.
   * Klare Trennung der Verantwortlichkeiten: `sql_execute_query` bleibt strikt für In-Context-Tasks (mit konservativen Zeilenlimits von 100 bis max. 1.000 Zeilen); `sql_export_data` ist für File-Handoff und größere Datenmengen optimiert.
2. **Zwingend absolute Pfadübergabe (`output_path`)**:
   * Es muss immer ein **vollständiger, absoluter Windows-Pfad** übergeben werden (z. B. `C:\Projekte\Repo\data\kunden.csv`).
   * Relative Pfade sind unzulässig und werden mit `SQL-AI-0001` abgewiesen, da aufrufende Agenten in unterschiedlichen Repositories/Verzeichnissen agieren und nur der jeweilige Agent den exakten Zielkontext kennt.
3. **Atomares Schreiben via temporäre Datei (`.tmp`) mit UUID**:
   * Der Stream schreibt in eine temporäre Datei im selben Zielverzeichnis: `<Verzeichnis>\<Dateiname>.<Guid>.tmp`.
   * Erst nach erfolgreichem Abschluss aller Zeilen wird die temporäre Datei atomar in den Zielpfad umbenannt.
   * Bei Timeout, Fehler oder Abbruch wird die temporäre Datei sofort und restlos gelöscht. Unvollständige oder korrupte Exporte verbleiben niemals auf der Festplatte.
4. **Ausschließlich lesende Queries (Strikter ReadOnly-Zwang)**:
   * Erlaubt sind lesende Abfragen, die ein Result-Set liefern: `SELECT`, lesende CTEs, Tabellenwertfunktionen (TVFs) oder lesende Stored Procedures (`EXEC ...`).
   * Mutationen jeglicher Art (`INSERT`, `UPDATE`, `DELETE`, `MERGE`, DDL) sind strikt verboten und werden durch die Sicherheits-Guardrails blockiert.
5. **Formate & Auto-Detection**:
   * Unterstützt werden `csv` (RFC 4180 mit Komma `,` und UTF-8) und `jsonl` (Newline-Delimited JSON).
   * Fehlt die explizite `format`-Angabe, leitet das Tool das Format aus der Dateiendung von `output_path` ab (Fallback: `csv`).
6. **Limits & Performance**:
   * Echtes Streaming vom `DbDataReader` direkt in den gepufferten `FileStream` (konstanter RAM-Bedarf).
   * Eigene Konfiguration für Exportlimits: Standard 10.000 Zeilen, konfigurierbar bis 50.000 (oder darüber hinaus).

---

## 3. Architektur & Detaildesign

### 3.1 Tool-Signatur: `sql_export_data`

| Argument | Typ | Erforderlich | Beschreibung |
|---|---|---|---|
| `database` | `string` | Ja | Qualifiziertes Ziel (`alias/database`). |
| `query` | `string` | Ja | Auszuführendes lesendes SQL-Statement (SELECT, TVF, Prozedur mit Result-Set). |
| `output_path` | `string` | Ja | **Zwingend absoluter** Dateipfad (z. B. `C:\Repos\Analysis\output.csv`). Relative Pfade werden abgewiesen. |
| `format` | `string` | Nein (Default: Auto) | `"csv"` oder `"jsonl"`. Fehlt die Angabe, erfolgt Auto-Detection via Dateiendung (`.csv` / `.jsonl`). Fallback ist `"csv"`. |
| `overwrite` | `bool` | Nein (Default: `false`) | Schutz vor versehentlichem Überschreiben bestehender Dateien. |
| `requested_row_limit` | `int?` | Nein | Optionales Limit für diesen Export (gedeckelt durch `MaxExportRowLimit`). |
| `parameters` | `object?` | Nein | Bind-Parameter für die SQL-Query (gleiche Syntax/Logik wie in `sql_execute_query`). |

### 3.2 Dateiformate & Serialisierung

#### A. CSV (RFC 4180-konform)
* **Encoding**: UTF-8 (ohne BOM, standardmäßig kompatibel mit Python/Pandas).
* **Delimiter**: Komma `,`.
* **Escaping**: Werte mit Komma, Zeilenumbruch (`\r`, `\n`) oder Anführungszeichen `"` werden in doppelte Anführungszeichen eingeschlossen. Ein Anführungszeichen im Text wird verdoppelt (`""`).
* **NULL-Semantik**:
  * `NULL` wird als leeres Feld (ohne Anführungszeichen) serialisiert: `val1,,val3`.
  * Ein echter Leerstring `""` wird quotiert serialisiert: `val1,"",val3`.
  * *Vorteil*: Pandas liest `NULL` sauber als `NaN` und Leerstrings als String ein.
* **Header**: Die erste Zeile enthält immer die Spaltennamen aus dem SQL-Result-Set.

#### B. JSONL (Newline-Delimited JSON)
* Jede Zeile ein vollständiges JSON-Objekt (`{"ColA": 1, "ColB": "Text"}`).
* Nutzt die bestehende, AOT-kompatible JSON-Serialisierung (`McpJsonContext`).
* Garantiert typgetreue Datentypen (Numbers, Booleans, Nulls, verschachtelte Strings).

#### C. Erweiterbarkeit für künftige Formate (Pluggable Writer-Pattern)
* Die Formatierung wird über ein Interface abstrahiert (z. B. `IDataRowWriter` / `IDataFormatWriter`):
  ```csharp
  public interface IDataRowWriter : IAsyncDisposable
  {
      Task WriteHeaderAsync(IReadOnlyList<ResultColumn> columns, CancellationToken ct);
      Task WriteRowAsync(DbDataReader reader, IReadOnlyList<ResultColumn> columns, AnonymizationContext anonCtx, CancellationToken ct);
  }
  ```
* Eine Factory (`DataWriterFactory`) instanziiert den passenden Writer basierend auf dem `format`-Parameter oder der Dateiendung.
* **Mögliche zukünftige Formate**:
  * `parquet` (Apache Parquet für Big-Data / Polars / PyArrow)
  * `tsv` (Tab-Separated Values)
  * `json` (formatiertes JSON-Array)
  * `xlsx` (Excel-Arbeitsmappen)

### 3.3 Streaming-Pipeline & Atomarer Schreibzyklus

Um auch 50.000 Zeilen speicherschonend und ausfallsicher zu verarbeiten:

```text
1. Validierung: output_path ist absolut, Zielverzeichnis existiert, overwrite-Bedingung erfüllt.
2. Temp-Pfad generieren: targetDir/<fileName>.<Guid:N>.tmp
3. DbCommand -> DbDataReader (SequentialAccess)
    ↳ Row-für-Row
        ↳ Anonymisierungs-Check (falls aktiv)
            ↳ StreamWriter (gepuffert, 64 KB Buffer) -> FileStream (tempPath)
4. Bei Erfolg: File.Move(tempPath, finalPath, overwrite)
5. Bei Fehler/Timeout/Abbruch:
    ↳ FileStream schließen
    ↳ File.Delete(tempPath)
    ↳ Unvollständige Dateien verbleiben NIEMALS auf der Festplatte.
```

### 3.4 Sicherheit, Anonymisierung & Guardrails

* **Strikter ReadOnly-Zwang**:
  * Die Abfrage darf keine Mutationen enthalten. `QuerySafetyValidator` blockiert alle schreibenden DML- oder DDL-Befehle.
  * Auch lesende Stored Procedures oder TVFs dürfen keine versteckten Statusänderungen durchführen.
  * `SchemaOnly`-Zugriffsstufen werden abgewiesen (`SQL-AI-0107`).
* **Anonymisierungspflicht**:
  * Steht die Zieldatenbank auf `ReadOnlyAnonymized` oder `ReadWriteAnonymized`, greift dieselbe Anonymisierungs-Pipeline (`Anonymizer`, `TokenVault`) wie bei `sql_execute_query`. Es werden niemals unmaskierte Rohdaten in die Zieldatei geschrieben.
* **Überschreibschutz (`overwrite`)**:
  * Ist `overwrite == false` und die Zieldatei existiert bereits, schlägt der Aufruf vor Ausführung der Query mit `SQL-AI-0114` fehl.

### 3.5 MCP-Response (Agenten-Ergonomie)

Der Agent erhält im MCP-Tool-Ergebnis **keinen Datenblock**, sondern eine kompakte JSON-Zusammenfassung mit allen für die Weiterverarbeitung relevanten Spalten- und Performance-Metadaten:

```json
{
  "status": "completed",
  "file_path": "C:\\Repos\\Project\\data\\kunden_umsatz.csv",
  "format": "csv",
  "row_count": 15420,
  "file_size_bytes": 1048576,
  "columns": [
    { "name": "CustomerId", "type": "int" },
    { "name": "CompanyName", "type": "string" },
    { "name": "TotalRevenue", "type": "decimal" },
    { "name": "LastOrderDate", "type": "datetime" }
  ],
  "elapsed_ms": 340,
  "cpu_ms": 110,
  "logical_reads": 4200,
  "was_anonymized": false,
  "anonymized_columns": []
}
```

> **Besonderer Mehrwert für Agenten**:
> Weil die Rückgabe die vollständige Liste der Spalten (`columns`) und Datentypen enthält, muss der Folge-Agent die exportierte Datei nicht erst probelesen. Er kann unmittelbar und fehlerfrei ein Python-Skript verfassen:
> ```python
> import pandas as pd
> import matplotlib.pyplot as plt
> df = pd.read_csv(r"C:\Repos\Project\data\kunden_umsatz.csv")
> plt.bar(df['CompanyName'], df['TotalRevenue'])
> plt.savefig(r"C:\Repos\Project\charts\revenue.png")
> ```

---

## 4. Konfiguration (`appsettings.json`)

Erweiterung der Konfigurationsstruktur unter `SqlToAi:QueryExecution`:

```json
{
  "SqlToAi": {
    "QueryExecution": {
      "MaxRowLimit": 1000,
      "Export": {
        "DefaultRowLimit": 10000,
        "MaxRowLimit": 50000,
        "CommandTimeoutSeconds": 120,
        "DefaultFormat": "csv"
      }
    }
  }
}
```

---

## 5. Geplanter Scope

### Must
1. Neues MCP-Tool `sql_export_data` mit den Argumenten `database`, `query`, `output_path`, `format`, `overwrite`, `requested_row_limit`, `parameters`.
2. Validierung: `output_path` muss ein absoluter Windows-Pfad sein; relative Pfade werden abgewiesen.
3. Atomarer Schreibzyklus mit temporärer Datei (`.<guid>.tmp`) und sicherem Cleanup bei Fehlern/Abbrüchen.
4. Dedizierter Streaming-Service (`DataExportService`), der CSV (RFC 4180) und JSONL unterstützt.
5. Volle Unterstützung der bestehenden Sicherheits- und Anonymisierungs-Pipeline (`Anonymizer`, `TokenVault`).
6. Strikter ReadOnly-Zwang (nur lesende Statements/Prozeduren mit Result-Set).
7. Überschreibschutz (`overwrite: false` als Default).
8. Aussagekräftiges Metadaten-Ergebnis inklusive Spaltenliste und Datentypen für optimale Agenten-Weiterverarbeitung.
9. CLI-Support: Aufrufbar via `.\SqlToAi.exe query sql_export_data ...`.
10. Unit- und Integrationstests (CSV-Escaping, NULL-Handling, Overwrite-Schutz, atomarer Abbruch/Cleanup, Streaming größerer Zeilenmengen).

### Out of scope
* Binäre Spezialformate (Parquet, Arrow, `.xlsx`) in Phase 1.
* Direkte Python-Ausführung im Server (dies bleibt die Aufgabe des aufrufenden Agenten/Clients).
* Multi-Statement-Skript-Exporte mit mehreren Ergebnismengen.

---

## 6. Verifikation & Testkriterien

1. **Pfad-Validierung**: Übergabe relativer Pfade führt zu `SQL-AI-0001`.
2. **Atomarer Abbruchtest**: Künstlicher Abbruch (z. B. nach 100 Zeilen via CancellationToken) stellt sicher, dass die temporäre Datei gelöscht wird und keine Teildatei zurückbleibt.
3. **CSV-Formatprüfung**: Export einer Tabelle mit Kommas, Zeilenumbrüchen, Anführungszeichen und `NULL`-Werten. Gegenprüfung mit `pd.read_csv()` in Python.
4. **JSONL-Formatprüfung**: Gegenprüfung mit `pd.read_json(..., lines=True)`.
5. **Anonymisierungstest**: Abfrage auf eine maskierte Tabelle exportiert nur maskierte Werte und listet `anonymized_columns` auf.
6. **Limits & Streaming**: Abfrage mit 25.000 Zeilen überschreitet nicht den Arbeitsspeicher und hält das konfigurierte Limit ein.
7. **ReadOnly-Zwang**: Versuch eines `UPDATE` oder `INSERT` schlägt fehl.
8. **Overwrite-Schutz**: Erneuter Export auf dieselbe Datei ohne `overwrite: true` schlägt mit Fehlermeldung fehl.
