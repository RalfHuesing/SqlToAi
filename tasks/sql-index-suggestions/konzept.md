---
title: "SQL Index-Analyse & Vorschläge"
status: draft
last_updated: "2026-08-03"
rules_dir: .agents/rules
project_kind: brownfield
estimated_scope: medium
open_questions:
  - "VIEW SERVER STATE Permission: Soll das in der Dokumentation als optionale erweiterte Permission geführt werden?"
  - "Scope: Nur fehlende Indizes (Reads), oder auch ungenutzte Indizes (Writes ohne Reads)?"
  - "Neues Tool sql_suggest_indexes vs. Erweiterung sql_measure_performance?"
  - "DMV-Daten sind server-global seit letztem Restart — für Prod-DBs mit vielen Queries sehr wertvoll, für frisch gestartete Server sinnlos. Hinweis im Tool?"
---

# SQL Index-Analyse & Vorschläge

## Hintergrund

SSMS zeigt manchmal "Missing Index"-Vorschläge. Diese kommen aus SQL Server-internen
Quellen, die wir über den MCP-Server zugänglich machen könnten — ohne SSMS, direkt
nutzbar vom KI-Agenten.

---

## Ideensammlung (noch nicht priorisiert)

### Idee 1 — Ausführungsplan-Parser erweitern (sql_measure_performance)

**Was:** `PerformanceMeasurementService.ExtractMissingIndexWarnings` gibt heute nur
`Table + Impact%`. Der XML-Plan enthält aber vollständige Spalteninformationen:

```xml
<MissingIndexGroup Impact="85.7">
  <MissingIndex Table="[dbo].[Orders]">
    <ColumnGroup Usage="EQUALITY">
      <Column Name="CustomerId" />
    </ColumnGroup>
    <ColumnGroup Usage="INEQUALITY">
      <Column Name="OrderDate" />
    </ColumnGroup>
    <ColumnGroup Usage="INCLUDE">
      <Column Name="Amount" /><Column Name="Status" />
    </ColumnGroup>
  </MissingIndex>
</MissingIndexGroup>
```

**Ergebnis:** Vollständiges `CREATE NONCLUSTERED INDEX`-Statement direkt im Warning.

**Aufwand:** Gering — nur Parser-Erweiterung in bestehender Datei.  
**Permission:** Keine zusätzliche (SHOWPLAN reicht).

---

### Idee 2 — Neues Tool: `sql_suggest_indexes` (DMV-basiert, kumulativ)

**Was:** Fragt `sys.dm_db_missing_index_details` + `sys.dm_db_missing_index_group_stats`
ab — das sind die gleichen Quellen, aus denen SSMS seine Vorschläge zieht.

**Vorteil:** Kumulativ — nicht nur für eine Query, sondern für alle Queries seit dem
letzten Server-Restart. Priorisiert nach `improvement_score` (Formel: avg_user_cost ×
avg_user_impact × (seeks + scans)).

**Beispiel-Output (Markdown):**

```markdown
## Missing Index Recommendations — MyDB

| Score | Table | Equality Columns | Inequality Columns | Include Columns | Seeks | Scans | Last Seek |
|------:|:------|:-----------------|:-------------------|:----------------|------:|------:|:----------|
| 1247  | dbo.Orders | CustomerId | OrderDate | Amount, Status | 45230 | 12 | 2026-08-03 |
```

**Mögliche Parameter:**
- `database` (Pflicht)
- `table_name` (optional — Filter auf eine Tabelle)
- `min_score` (optional — Mindest-Improvement-Score)
- `top` (optional — Top N Vorschläge, Default 10)

**Benötigte Permission:** `VIEW SERVER STATE` — zusätzlich zu `db_datareader` + `SHOWPLAN`.
Muss in `docs/mcp-specification.md` §H dokumentiert werden.

**Hinweis:** DMV-Daten sind seit letztem Restart akkumuliert. Auf Servern die selten
neu starten (Prod), sehr aussagekräftig. Nach Restart leer — Tool sollte darauf hinweisen.

---

### Idee 3 — Ungenutzte Indizes finden

**Was:** `sys.dm_db_index_usage_stats` zeigt, welche Indizes Reads (seeks/scans) hatten
und welche nur schreibend belastet werden (user_updates) ohne je gelesen zu werden.

**Wert:** Ungenutzte Indizes kosten bei jedem INSERT/UPDATE/DELETE unnötig I/O und
Sperren — Kandidaten zum Löschen.

**Mögliche Parameter:**
- `database` (Pflicht)
- `table_name` (optional)
- Ausgabe: Tabelle, Index, user_seeks, user_scans, user_lookups, user_updates

**Permission:** `VIEW SERVER STATE`

**Hinweis:** Wie Idee 2 — Daten seit Restart. Ein frisch gestarteter Server zeigt
alle Indizes als "ungenutzt".

---

### Idee 4 — Fragmentierungsanalyse

**Was:** `sys.dm_db_index_physical_stats` liefert Fragmentierungsgrad pro Index.
Hohe Fragmentierung (>30%) → `REBUILD`; mittlere (10–30%) → `REORGANIZE`.

**Wert:** Direkte Empfehlung für Wartungsarbeiten.

**Achtung:** `sys.dm_db_index_physical_stats` kann bei großen Tabellen selbst
signifikante I/O verursachen (es liest den Index durch). Parameter `mode`:
- `LIMITED` — sehr schnell, nur grob
- `SAMPLED` — Kompromiss
- `DETAILED` — exakt, aber teuer

**Permission:** `db_datareader` + evtl. `VIEW DATABASE STATE`

---

## Nicht-Ideen (bewusst ausgeschlossen)

| Idee | Grund |
|:--|:--|
| Database Tuning Advisor (DTA) API | Nicht per SQL erreichbar, COM-Objekt, Windows-only |
| `DBCC AUTOPILOT` | Intern, undokumentiert, nicht für Produktionseinsatz |
| Automatisches Index-Erstellen | Schreiboperation, außerhalb ReadOnly-Scope |

---

## Nächste Schritte (wenn dieser Task geöffnet wird)

1. Entscheiden: Welche der 4 Ideen kommen in Scope?
2. `VIEW SERVER STATE`-Permission in `mcp-specification.md` §H ergänzen?
3. Neues Tool `sql_suggest_indexes` vs. Erweiterung bestehender Tools?
4. Tests definieren (DMV-Queries sind schwer zu unit-testen → Integration Tests?)
