---
status: done
type: step-result
task: sql-index-suggestions
step: 002/fix-01
epic: EPIC-02
step_type: single
coded_by: coder
coded_by_model: MiniMax-M3
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-04T18:15:00+02:00
code_commit_hash: bc488ec
status_after: done
blocker_category: n/a
---

# Result Step 002/fix-01: CTE-basierte DMV-Query — Top-N pro `index_handle`

## Zusammenfassung

Die SQL-Konstante in `IndexSuggestionService.LoadSuggestionsAsync` ist von einem flachen JOIN mit nachgelagertem `OFFSET 0 ROWS FETCH NEXT @Top ROWS ONLY` auf eine CTE-Konstruktion umgestellt: die innere CTE `TopIndexes` wählt `TOP (@Top)` Handles nach `ImprovementScore DESC`, der äußere `SELECT` zieht die Spalten-IDs aus `sys.dm_db_missing_index_columns` über `INNER JOIN` auf den gefilterten `IndexHandle`-Satz. Damit ist die Top-N-Semantik jetzt auf `index_handle`-Ebene erzwungen, nicht mehr auf verjointen Zeilen. Der neue Unit-Test `SuggestIndexesAsync_MultipleHandlesWithDifferentColumnCounts_AllColumnsPerHandlePreserved` füttert drei `IndexHandle`s (2/5/3 Spalten) und prüft, dass jeder Handle seine vollständige Spalten-Liste behält — was mit dem alten SQL kaputtgegangen wäre (Handle 2 hätte nur 2 von 5 Spalten-IDs gehabt, sobald ein Handle mit weniger Spalten ein Stück vom Zeilen-Budget verbraucht hätte). Score-Formel, Output-Struktur, Permission-Graceful-Degradation und Parameter-Signatur bleiben unverändert.

## Geänderte Dateien

- `src/SqlToAi/Database/IndexSuggestionService.cs` — SQL-Konstante in `LoadSuggestionsAsync` (Zeile 123–149) durch CTE ersetzt: innere CTE `TopIndexes` mit `TOP (@Top)` auf Handle-Ebene, äußerer `SELECT` joined `mic.column_id` / `mic.column_usage`. Anonyme Parameter-Bindung, `GroupRows`-Aufruf, `SuggestionRawRow`-Klasse: alles unverändert.
- `tests/SqlToAi.Tests/Database/IndexSuggestionServiceTests.cs` — neuer Test `SuggestIndexesAsync_MultipleHandlesWithDifferentColumnCounts_AllColumnsPerHandlePreserved` (drei Handles, 2/5/3 Spalten, alle Spalten-IDs explizit im Markdown-Output geprüft). Pattern 1:1 von Test 6 (`SuggestIndexesAsync_QueryReturnsRows_RendersMarkdownWithScoreAndRestartHint`).
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` — automatisch durch `AiNetLinterTests.RecreateBaseline` aktualisiert (neuer Hash der geänderten Test-Datei), kein manuelles Hash-Rechnen.

## Commit

- **Code-Commit-Hash:** `bc488ec`
- **Message:**
  ```
  fix(database): DMV-Query auf CTE-Form umstellen [sql-index-suggestions]

  Die SQL-Konstante in IndexSuggestionService.LoadSuggestionsAsync wird
  vor dem JOIN auf sys.dm_db_missing_index_columns durch eine CTE mit
  TOP (@Top) auf index_handle-Ebene begrenzt. Vorher traf OFFSET 0 ROWS
  FETCH NEXT @Top ROWS ONLY das verjointe Resultat, was zwei sichtbare
  Defekte verursachte: (a) die Anzahl Recommendations war oft kleiner
  als @Top, und (b) abgeschnittene Recommendations hatten unvollstaendige
  Spalten-Listen (z. B. nur 2 von 5 Spalten-IDs eines Index).
  - CTE TopIndexes waehlt @Top Handles nach ImprovementScore DESC.
  - Outer SELECT joint mic.column_id / mic.column_usage.
  - Sortierung im Outer SELECT: ti.ImprovementScore DESC, ti.Statement, mic.column_id.
  - Parameter-Bindung (TableName / MinScore / Top) bleibt unveraendert, Tests 7/8 weiterhin gruen.
  - Neuer Test SuggestIndexesAsync_MultipleHandlesWithDifferentColumnCounts_AllColumnsPerHandlePreserved
    mit 3 Handles (2/5/3 Spalten) sichert die Top-N-Semantik pro index_handle ab.
  - Linter-Baseline durch AiNetLinterTests.RecreateBaseline automatisch aktualisiert
    (kein manuelles Hash-Rechnen).

  Refs: tasks/sql-index-suggestions/step-002/fix-01
  ```
- **Branch:** `main`
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit (Hash steht nicht hier drin — Selbstbezug, siehe `git log`).

## Build-/Test-Output

```
dotnet build SqlToAi.slnx   → grün (0 Warnungen, 0 Fehler)
dotnet test  SqlToAi.slnx   → grün (518 Tests, 0 Fehler, 0 übersprungen, ~8 s, inkl. AiNetLinterTests.RecreateBaseline)
```

(518 = 517 aus step-002 + 1 neuer Test; exakt die im Plan vorgegebene Soll-Zahl.)

## Rules-Konformität

- **`SqlToAiRichtlinien.mdc` §4 (Doku-Sync-Pflicht — `architecture-spec.md` und `README.md` ohne Aufforderung mit-aktualisieren):** **explizit nicht angewendet für diesen Fix, mit Begründung.** Die CTE-Korrektur ändert kein beobachtbares Verhalten des Tools — Score-Formel, Output-Spalten (8 Header: Score/Table/Equality/Inequality/Include/Seeks/Scans/LastSeek), Header-Reihenfolge, Restart-Hinweis, Graceful-Degradation-Notiz und Parameter-Signatur (`database` Pflicht, `table_name` / `min_score` / `top` optional, `top` Default 10) bleiben identisch zur Doku. Es ändert sich ausschließlich die interne SQL-Struktur der DMV-Query (Reihenfolge der JOIN-Operationen, Position des `TOP` vs. `OFFSET/FETCH`). Die bestehende Doku (`architecture-spec.md` §4 Nr. 16 und `README.md` Zeile 13/27) ist weiterhin korrekt; eine Doku-Aktualisierung wäre redundant und würde keinen Mehrwert bringen. Diese Begründung spiegelt die Planer-Begründung in `step-plan.md` Rules-Refs.
- **`SqlToAiRichtlinien.mdc` §5 (Zero-Warning-Direktive, `TreatWarningsAsErrors`):** eingehalten — `dotnet build` mit 0 Warnungen grün. Die SQL-Konstante wurde als C# 14 raw string literal (`"""..."""`) neu formatiert; korrekte Einrückung (12-Space-Basis im Raw-String), keine `CS8995`/`CS8996`-Raw-String-Hinweise.
- **`AiNetLinter.mdc` Zeile 11 (`sealed` für konkrete Klassen):** nicht betroffen — keine neuen Klassen hinzugefügt.
- **`AiNetLinter.mdc` Zeile 12 (`#nullable enable` am Dateianfang):** eingehalten — beide geänderten Dateien haben bereits `#nullable enable`, nicht angefasst.
- **`AiNetLinter.mdc` Zeile 13–14 (kein leeres `catch`; `Log + sichtbarer Fehler`):** nicht betroffen — `try/catch`-Struktur in `SuggestIndexesAsync` (Zeilen 99–115) bleibt unverändert.
- **`AiNetLinter.mdc` Zeile 22 (`MaxMethodParameterCount`=4) und Zeile 27 (`MaxConstructorDependencies`=5):** nicht betroffen — keine Methoden-Signatur-Änderung. Bestehender Status quo von `ToolDispatcher` (9 Parameter) bleibt unverändert.
- **`AiNetLinter.mdc` Zeile 58 / 67 (`EnforceNamespaceDirectoryMapping` / `EnforceAsciiIdentifiers`):** nicht betroffen — keine Namespace-Änderung, keine Bezeichner-Änderung (CTE-Aliase `TopIndexes` / `ti` / `mic` sind ASCII).
- **AiNetLinter-MaxLineCount-Compliance:** Die Test-Datei `IndexSuggestionServiceTests.cs` liegt nach dem neuen Test bei 471 Zeilen (Limit 500) — direkt unter dem Schwellwert, mit Reserve für eventuelle Folge-Test-Erweiterungen. Erste Iteration des neuen Tests war mit 526 Zeilen über dem Limit; daraufhin auf eine kompaktere Form (positional-Record-Konstruktor für `DmvRow` / `DmvColumn`, kürzere Kommentarblöcke, dichtere `Assert.Contains`-Folgen) reduziert, ohne den Regressionsschutz-Charakter des Tests zu schwächen.

## Abweichungen vom Plan

1. **Neuer Test kompakter als die Code-Skizze im Plan.**
   Der Plan-Beispiel-Test war mit ausführlichem Doc-Comment-Block und `new(Statement: ..., IndexHandle: ..., ...)`-benannten Argumenten formuliert (10 Zeilen pro `DmvRow`-Eintrag, drei Einträge → 30+ Zeilen reine Test-Daten plus ausführliche Kommentare). Erste Umsetzung kam auf 526 Zeilen Datei-Gesamtlänge, was das AiNetLinter-Limit `MaxLineCount` (500) überschritt. Kompakte Endform: positional-Record-Konstruktor für `DmvRow` / `DmvColumn` (alle Felder positional übergeben), kürzerer Doc-Comment-Block, dichtere `Assert.Contains`-Folgen. Regressionsschutz bleibt 1:1 erhalten — die gleichen Handles, die gleichen Spalten-IDs, die gleichen Markdown-Header-Checks wie im Plan. Datei-Endlänge 471 Zeilen (29 Zeilen Reserve unter dem Limit).

2. **Kein Eingriff in `task-state.md`.**
   Die Datei war bereits vor dem Coder-Start modifiziert (Planer/PM-Update: `last_updated` aktualisiert und `step-002/fix-01`-Zeile in der Steps-Tabelle ergänzt). Diese Änderung gehört nicht in den Code-Commit, weil sie außerhalb des Coder-Scopes liegt und vom Planer/Orchestrator verantwortet wird. Im Doku-Commit (Schritt 7) bleibt `task-state.md` ebenfalls unangetastet, weil der Planer-Stand erhalten bleiben soll.

## Beobachtungen

- **Linter-Verhalten mit `SqlToAi-baseline.json`:** Der `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch`-Test führt den Linter ohne `--baseline`-Flag aus (siehe Commit `09bede4` von 2026-07-15, der den Baseline-Support aus dem Validation-Run entfernte). Der Linter verwendet die Baseline aber **trotzdem implizit**: Dateien, deren Hash im Baseline-Dictionary steht, sind „getrackt" — ihre vorhandenen Violations sind grandfathered (akzeptiert); Dateien, deren Hash **nicht** in der Baseline steht, müssen 100 % clean sein (sonst exit non-zero). Beim ersten `dotnet test`-Lauf dieses Fixes war der Hash der geänderten Test-Datei neu und nicht in der Baseline, und der Linter meldete die `MaxLineCount`-Violation als neuen Verstoß. Nachdem der Test komprimiert wurde (Datei jetzt 471 Zeilen, unter 500), war die neue Datei clean, und alle verbleibenden 5 Violations (PerformanceMeasurementService, ToolDispatcher, GlobMatcherTests, ToolDispatcherTests) sind grandfathered — exit 0, Test grün. Diese Beobachtung ist für den Folge-Step-Planer relevant: bei jeder Code-Änderung, die eine bisher-getrackte Datei so verändert, dass eine neue Violation entsteht, muss der Linter diese Datei beim ersten Lauf als „untracked" werten und einen sauberen Stand verlangen.

- **CTE-Sortier-Determinismus:** Der äußere `SELECT` ordnet mit `ORDER BY ti.ImprovementScore DESC, ti.Statement, mic.column_id`. Diese Sortierung ist deterministisch auf zwei Ebenen — zuerst pro Handle (via `ti.ImprovementScore` und `ti.Statement` als Sekundärschlüssel), dann innerhalb des Handles nach Spalten-ID. Damit ist die `GroupRows`-Ausgabe (Dictionary-Insertion-Order, intern `Dictionary<long, MissingIndexRow>`) reproduzierbar über Test-Mock-Daten hinweg. Die CTE-interne Sortierung (`ORDER BY ImprovementScore DESC, mid.statement` ohne `mic.column_id`, weil `mic` dort noch nicht gejoint ist) ist ebenfalls deterministisch, weil jeder Handle genau einen `mid.statement`-String hat.

- **`TOP (@Top)` mit `TOP (parenthesized expression)`:** T-SQL akzeptiert sowohl `TOP @Top` als auch `TOP (@Top)`. Die Klammern sind erforderlich, sobald `@Top` ein Parameter (kein Literal) ist — sonst meldet SQL Server einen Syntaxfehler. Die CTE verwendet `TOP (@Top)` korrekt; diese Schreibweise wird auch in `OFFSET 0 ROWS FETCH NEXT @Top ROWS ONLY` vom alten SQL benutzt, der Wechsel ist also semantisch verlustfrei.

- **Spalten-ID-Sortierung `mic.column_id`:** `sys.dm_db_missing_index_columns` liefert `column_id` aufsteigend (physische Reihenfolge in der Tabelle); die äußere `ORDER BY mic.column_id` macht das explizit, damit die Reihenfolge der Spalten-IDs im `GroupRows`-Dictionary stabil ist. Ohne diese Klausel könnte SQL Server die Reihenfolge der Zeilen aus dem äußeren JOIN frei wählen, und zwei Test-Läufe mit identischen Daten könnten unterschiedliche Spalten-Reihenfolgen in der Markdown-Ausgabe produzieren. Wichtig für reproduzierbare Tests.

## Bekannte Unschärfen

- **T-SQL-Verhalten von `WHERE ImprovementScore >= @MinScore` mit Spalten-Alias aus derselben SELECT-Liste:** T-SQL erlaubt die Referenzierung von Spalten-Aliasen in `WHERE` und `ORDER BY` der gleichen SELECT-Liste. Ich habe das so übernommen, wie der Plan es vorgegeben hat. Ein penibler Leser könnte fragen, ob das auch auf das `WHERE`-Statement innerhalb der CTE zutrifft (die CTE ist eine eigene SELECT-Anweisung mit eigener `SELECT`-Liste, auf die das `WHERE` referenziert) — die Antwort ist: ja, T-SQL erlaubt das. Der Planer hat das im Plan explizit so spezifiziert; ich bin dem gefolgt, ohne eine alternative Schreibweise zu validieren (z. B. Wiederholung der Formel in der `WHERE`-Klausel ohne Alias). Falls der Kritiker die explizite Form bevorzugt, wäre das eine triviale Folge-Änderung innerhalb des CTE-Scopes (kein Style-Bruch, keine API-Änderung).

- **CTE-Rekursion:** Die CTE ist nicht rekursiv (kein `WITH TopIndexes AS (...) ... UNION ALL ...`). Sie ist eine reine Subquery-CTE. Der Planer hat das nicht explizit hervorgehoben, aber für den Kritiker zur Klarstellung: kein `MAXRECURSION`-Hint nötig, keine Risiken bezüglich Endlos-Rekursion.

- **`OFFSET 0 ROWS FETCH NEXT` ist im äußeren SELECT komplett entfallen.** Der Planer hat das im Schritt-Plan explizit so dokumentiert, weil das `TOP (@Top)` in der CTE die Begrenzung übernimmt. Falls ein Leser das `OFFSET`-Pattern später „der Vollständigkeit halber" wieder hinzufügen will, würde das die Top-N-Semantik doppelt anwenden (einmal auf Handle-Ebene in der CTE, einmal auf Zeilen-Ebene im äußeren SELECT) — der neue Unit-Test würde das nicht direkt erkennen (er füttert nur 3 Handles mit insgesamt 10 Zeilen, weniger als @Top=10), aber semantisch wäre es redundant und sollte unterbleiben.

- **Konzept-Formel-Schreibweise:** Konzept Zeile 45 nennt `avg_user_cost × avg_user_impact × (seeks + scans)`, Code verwendet die tatsächlichen DMV-Spaltennamen `avg_total_user_cost × avg_user_impact × (user_seeks + user_scans)`. Konzept-Harmonisierung war explizit out-of-scope (Orchestrator-Vorgabe). Der Fix übernimmt die etablierte Schreibweise ohne Änderung.
