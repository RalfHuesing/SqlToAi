---
status: done
type: step-review
task: sql-index-suggestions
step: 002/fix-01
epic: EPIC-02
step_type: single
reviewed_by: kritiker
reviewed_by_model: MiniMax-M3
reviewed_by_model_knowledge_cutoff: 2026-01
reviewed_at: 2026-08-04T18:30:00+02:00
verdict: approved
tech_debt_ids: []
---

# Review Step 002/fix-01: CTE-basierte DMV-Query — Top-N pro `index_handle`

## Verdict

- [x] **approved** — alle vier Prüfebenen ok
- [ ] **issues** — Fix-Step `step-002/fix-XX` angelegt mit Fix-Plan
- [ ] **blocked** — Nutzer-Entscheidung nötig (siehe Frage unten)

## Geprüft

- [x] Plan-Erfüllung: alle im `step-plan.md` genannten Änderungen erfolgt
- [x] Rules-Konformität: `<rules_dir>/**` eingehalten
- [x] Logische Korrektheit: Code macht was er soll, nicht nur „grün"
- [x] Konzept-Treue: passt die Umsetzung zu `konzept.md` (Scope, Non-Goals, Muss-Haben)
- [x] Build: selbst nachgeprüft, grün
- [x] Tests: selbst nachgeprüft, grün (oder Baselines beachtet)

## Befund

### Plan-Erfüllung

Alle neun DoD-Punkte erfüllt: SQL-Konstante in `IndexSuggestionService.cs:123-158` auf CTE umgestellt, `OFFSET/FETCH NEXT` entfernt, neuer Test `SuggestIndexesAsync_MultipleHandlesWithDifferentColumnCounts_AllColumnsPerHandlePreserved` mit 3 Handles à 2/5/3 Spalten in `IndexSuggestionServiceTests.cs:175-215` hinzugefügt, `SqlToAi-baseline.json` automatisch durch `AiNetLinterTests.RecreateBaseline` mit-aktualisiert (Hashes für `IndexSuggestionService.cs` und `IndexSuggestionServiceTests.cs` neu — kein anderes File), Plan-Frontmatter auf `done (pending audit)` gesetzt, beide Commits (`bc488ec` Code, `1a412cb` Result-Doku) konventionell mit deutschem imperativem Subject und Suffix `[sql-index-suggestions]`. Beide dokumentierten Abweichungen (kompaktere Test-Form via positional-Record-Konstruktor zur Einhaltung von `MaxLineCount`=500; kein Eingriff in `task-state.md`) sind begründet und semantisch verlustfrei.

### Rules-Konformität

`SqlToAiRichtlinien.mdc` §4-Entkräftung trägt: nur die interne SQL-Struktur der DMV-Query ändert sich, Score-Formel, Output-Spalten, Header-Reihenfolge, Restart-Hinweis, Graceful-Degradation-Notiz, Parameter-Signatur (`database`/`table_name`/`min_score`/`top`) bleiben identisch — `RenderMarkdown`/`GroupRows`/`RenderPermissionNote`/`IsViewServerStatePermissionError` und die anonyme Parameter-Bindung sind unverändert, `architecture-spec.md` §4 Nr. 16 und `README.md` Zeile 13/27 bleiben korrekt. `SqlToAiRichtlinien.mdc` §5 (Zero-Warning-Direktive): Build grün mit 0 Warnungen. `AiNetLinter.mdc` Zeile 11/12/13-14/22/27/58/67: keine neuen Klassen, beide Dateien mit `#nullable enable`, `try/catch` unverändert, keine Methoden-Signatur-Änderung, CTE-Aliase `TopIndexes`/`ti`/`mic` ASCII.

### Logische Korrektheit

CTE-Top-N-Semantik korrekt: `TOP (@Top)` INNERHALB der CTE auf `index_handle`/`mid.statement` angewendet BEVOR `sys.dm_db_missing_index_columns` gejoint wird, `OFFSET/FETCH NEXT` im äußeren SELECT komplett entfallen. Die 9 Outer-SELECT-Spalten matchen 1:1 `SuggestionRawRow.Statement`/`IndexHandle`/`UserSeeks`/`UserScans`/`LastUserSeek`/`AvgTotalUserCost`/`AvgUserImpact`/`ColumnId`/`ColumnUsage` (PascalCase → PascalCase) — Dapper-Mapping ist konsistent. CTE-interne Sortierung `ImprovementScore DESC, mid.statement` und Outer-Sortierung `ti.ImprovementScore DESC, ti.Statement, mic.column_id` sind deterministisch (zwei Schlüsselebenen), womit `GroupRows`-Dictionary-Insertion-Order reproduzierbar ist; die `WHERE`-Referenz auf den berechneten CTE-Alias `ImprovementScore` ist T-SQL-konform. Parameter-Bindungs-Tests 7/8 weiterhin grün, weil die anonyme Objekt-Signatur `new { TableName, MinScore, Top }` in `LoadSuggestionsAsync:160-165` unverändert ist und Dapper Property-Namen an `@`-Namen unabhängig von der SQL-Stelle bindet. Der neue Test ist aussagekräftig: 3 Handles mit unterschiedlichen Spaltenzahlen (2/5/3) zwingen die CTE, alle 5 Spalten-IDs von Handle 2 (`10, 11` EQUALITY, `12, 13, 14` INCLUDE) durchzureichen — was mit dem alten `OFFSET/FETCH`-Pattern auf verjointen Zeilen unmöglich war, da nach 2+2+2 verjointen Zeilen für Handle 1+2-Anfang der dritte Handle 2-INCLUDE abgeschnitten worden wäre.

### Konzept-Treue (Ebene 4)

Kein Scope-Drift, kein Non-Goal verletzt: Konzept §Muss-Haven Idee 2 bleibt erfüllt (DMV-Abfrage, `improvement_score`-Berechnung, 8-Spalten-Markdown, Restart-Hinweis als Pflichtbestandteil, Graceful Degradation bei fehlender `VIEW SERVER STATE`, Parameter `database` Pflicht + `table_name`/`min_score`/`top` optional, `top` Default 10); Konzept §DoD für Idee 2 unverändert adressiert. Konzept-Formel-Schreibweise `avg_user_cost` (Konzept Zeile 45) vs. `avg_total_user_cost` (Code) bleibt bewusst uneinheitlich (Konzept-Harmonisierung war explizit out-of-scope, vom Planer in `step-plan.md` Notes dokumentiert und vom Coder im `step-result.md` Beobachtung 4 gespiegelt) — Konzept ist hier ungenau, Code ist die korrekte DMV-Interpretation. Integrationstest gegen eine echte Test-DB in `step-003` ist nicht Teil dieses Fixes (Orchestrator-Vorgabe).

### Build-/Test-Status

```
dotnet build SqlToAi.slnx → grün (0 Warnungen, 0 Fehler, TreatWarningsAsErrors=true)
dotnet test  SqlToAi.slnx → grün (518 Tests, 0 Fehler, 0 übersprungen, ~5 s)
```

Selbst nachgeprüft: gezielte Filter auf den neuen Test (1/1 grün) und auf `SuggestIndexesAsync`-Testgruppe (11/11 grün) sowie auf Tests 7+8 Parameter-Bindung (2/2 grün) — alle bestätigen das Test-Result-Reporting des Coders.
