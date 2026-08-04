---
task: sql-index-suggestions
type: tech-debt-log
maintained_by: kritiker
last_updated: 2026-08-04T13:30:00+02:00
---

# Tech-Debt-Log: sql-index-suggestions

Append-only. Jeder Eintrag ist eine vom Kritiker während eines
Step-Reviews beobachtete, aber bewusst **nicht** gefixte Auffälligkeit
außerhalb des Scopes des jeweiligen Steps (Architektur, Anti-Pattern,
Duplikation, Konsistenz) — siehe `../spec.md` §8.3/§9.

**Priorität ist reine Sortierhilfe für den Menschen, kein Auslöser.**
Bewusst `hoch`/`mittel`/`niedrig` (deutsch) statt `CRITICAL`/`MAJOR`/
`MINOR`, um jede Verwechslung mit den blockierenden Findings in
`step-review.md` auszuschließen — kein Eintrag hier führt automatisch zu
einem Fix-Step oder einem neuen Epic. Das entscheidet ausschließlich der
Nutzer (manuell, z. B. durch Ergänzen eines Epics in `roadmap.md` mit
Verweis auf die Tech-Debt-ID).

## Index

| ID | Bereich / Datei | Priorität | Kurzfassung |
|---|---|---|---|
| TD-001 | `docs/konzept.md` Zeile 172 vs. `tasks/.../step-001/step-plan.md` Prose-Lesart | mittel | Index-Name-Format `IX_Table_Col_Col2` (Konzept) vs. `IX_Table_Col__Col2` (Plan/Coder) ist nicht harmonisiert — Doku-Bereinigung an Konzept ODER Plan nötig. |
| TD-002 | `src/SqlToAi/Database/PerformanceMeasurementService.cs:373` (`BuildCreateIndexStatement`) | niedrig | `DESC`-Markierung an `Column`-Elementen in `ColumnGroup` wird ignoriert — semantisch nicht deckungsgleich mit SQL-Server-Empfehlung, wenn Spalte absteigend indiziert werden soll. |
| TD-003 | `src/SqlToAi/Database/PerformanceMeasurementService.cs:264` (`IsShowplanPermissionError`) | niedrig | **erledigt in step-002** — generalisiert zu `internal static IsPermissionError(SqlException, int errorNumber, string keyword)`; SHOWPLAN-Aufrufstellen angepasst, durch Test 11 in `IndexSuggestionServiceTests` abgesichert. |

## Einträge

### TD-001 — Konzept- vs. Plan-Prose-Inkonsistenz beim Index-Name-Format [Priorität: mittel]

- **Gefunden in:** step-001 (Kritiker-Review vom 2026-08-04)
- **Ort:**
  - `tasks/sql-index-suggestions/konzept.md:172` (Beispiel: `IX_Orders_CustomerId_OrderDate` — alle einfachen Unterstriche)
  - `tasks/sql-index-suggestions/step-001/step-plan.md` Datei 2 (Prose: `IX_<Table>_<Col>[__<Col2>]` — einfacher `_` zwischen Table und erster Spalte, `__` zwischen Spalten)
  - `src/SqlToAi/Database/PerformanceMeasurementService.cs:399-405` (umgesetzt: Prose-Lesart)
- **Befund:** Das Konzept-Beispiel im §Wie-Idee-1-Abschnitt zeigt den Index-Namen mit durchgehend einfachen Unterstrichen (`IX_Orders_CustomerId_OrderDate`). Der Planer hat im Step-Plan eine abweichende Namenskonvention festgelegt (`__` als Trenner zwischen mehreren Spalten zur besseren Lesbarkeit), die der Coder korrekt umgesetzt hat. Beide Formen sind gültige SQL-Identifier; die Diskrepanz ist eine reine Doku-Inkonsistenz. Konzept ist hier nicht strenger (Pfeil-Form ist ein Beispiel, keine normative Form), Plan hat bewusst abweichend spezifiziert — daher **kein Finding**, sondern eine Doku-Harmonisierungs-Aufgabe.
- **Warum nicht sofort gefixt:** Außerhalb des Scopes von step-001 (Implementierung folgt Plan 1:1, Konzept-Konsistenz ist Planer-/Doku-Aufgabe). Fix würde entweder das Konzept-Beispiel an die Implementierung anpassen oder die Implementierung an das Konzept-Beispiel anpassen — beides berührt mehrere Dateien (Konzept, ggf. architecture-spec, ggf. README), nicht nur diesen Step.
- **Vorschlag:** Bei Gelegenheit Konzept-Beispiel an die implementierte Form angleichen ODER umgekehrt die Code-Skizze aus dem Plan entfernen und Konzept-Beispiel zur verbindlichen Form machen. Entscheidungsträger ist der Planer/Nutzer. Ein vorsichtiger Test-Coverage-Hinweis: Test 1 asserted `IX_Orders_CustomerId` als Substring — das ist mit beiden Formen grün, unterscheidet also nicht scharf.
- **Status:** offen

### TD-002 — `DESC`-Sortierung in `ColumnGroup`-Spalten wird ignoriert [Priorität: niedrig]

- **Gefunden in:** step-001 (Kritiker-Review vom 2026-08-04, auf Basis von Coder-Notiz in `step-result.md` Beobachtungen)
- **Ort:** `src/SqlToAi/Database/PerformanceMeasurementService.cs:373` (`BuildCreateIndexStatement`) — beim Aufbau der `ON`-Klausel wird `Column`-`Name` 1:1 übernommen, das `Descending`-Attribut nicht ausgewertet.
- **Befund:** SQL-Server kann in `<MissingIndex>`-XML-Plans Spalten mit `<Column Name="X" Descending="True" />` markieren. Das gebaute DDL ist für absteigend indizierte Spalten semantisch unvollständig (es fehlt die `DESC`-Direktive in der Schlüsselspaltenliste). Funktional funktioniert der Index weiterhin aufsteigend, ist also nicht falsch, nur nicht exakt deckungsgleich mit der SQL-Server-Empfehlung.
- **Warum nicht sofort gefixt:** Nicht im Scope von step-001 (Plan erwähnt `DESC` nicht; Konzept ebenfalls nicht). Wäre eine 1-2-Zeilen-Erweiterung im Helper, aber berührt Tests und ist eine bewusste Scope-Entscheidung des Planers.
- **Vorschlag:** Bei EPIC-02 (oder einem eigenen kleinen Step) die `Column`-Kinder um `Descending`-Attribut-Auswertung erweitern und `keyClause` entsprechend mit nachgestelltem `DESC` rendern. Planer-/Nutzer-Entscheidung.
- **Status:** offen

### TD-003 — `IsShowplanPermissionError` für EPIC-02-Generalisierung vorsehen [Priorität: niedrig]

- **Gefunden in:** step-001 (Kritiker-Review vom 2026-08-04, auf Basis von Coder-Notiz in `step-result.md` Beobachtungen)
- **Ort:** `src/SqlToAi/Database/PerformanceMeasurementService.cs:264` (`IsShowplanPermissionError`) — verwendet aktuell `string.Contains("SHOWPLAN", …)` als Sekundär-Trigger.
- **Befund:** Für das in EPIC-02 anstehende `sql_suggest_indexes`-Tool muss eine Permission-Erkennung für `VIEW SERVER STATE` etabliert werden. Das bestehende Pattern (`IsShowplanPermissionError`) ist `SHOWPLAN`-spezifisch. Eine Generalisierung zu `IsPermissionError(SqlException, int number, string keyword)` o. ä. würde Code-Duplikation vermeiden. Der Coder hat korrekt davon abgesehen, das in step-001 „mal eben" mitzumachen.
- **Warum nicht sofort gefixt:** Außerhalb des Scopes von step-001 (kein EPIC-01-Auftrag; gehört in EPIC-02-Vorbereitung oder in einen eigenen kleinen Refactoring-Step).
- **Vorschlag:** Bei EPIC-02-Planung die Generalisierung als Vorbereitungs-Schritt einplanen ODER als eigenständigen kleinen Refactor-Step anlegen. Nutzer-Entscheidung.
- **Status:** **erledigt in step-002** — Helper `internal static IsPermissionError(SqlException, int errorNumber, string keyword)` ersetzt die SHOWPLAN-spezifische Variante; die drei SHOWPLAN-Aufrufstellen in `PerformanceMeasurementService` (Zeilen 168, 217, 253) sind auf den neuen Helper umgestellt (`IsPermissionError(ex, 262, "SHOWPLAN")`); semantisch identisch, durch Test 11 in `IndexSuggestionServiceTests` abgesichert.
