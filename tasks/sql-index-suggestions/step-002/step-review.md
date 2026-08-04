---
status: done
type: step-review
task: sql-index-suggestions
step: 002
epic: EPIC-02
step_type: single
reviewed_by: kritiker
reviewed_by_model: MiniMax-M3
reviewed_by_model_knowledge_cutoff: 2026-01
reviewed_at: 2026-08-04T16:00:00+02:00
verdict: issues
tech_debt_ids: []
---

# Review Step 002: EPIC-02 Service + Tool-Registrierung + Doku-Sync für `sql_suggest_indexes`

## Verdict

- [ ] **approved** — alle vier Prüfebenen ok
- [x] **issues** — Fix-Step `step-002/fix-XX` angelegt mit Fix-Plan
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

Alle zehn im Plan genannten Datei-Änderungen umgesetzt: `IndexSuggestionArgs.cs` (NEU, Record mit Defaults `Top=10`), `IIndexSuggestionService.cs` (NEU, 2 Überladungen), `IndexSuggestionService.cs` (NEU, sealed, 5 Dependencies, Dapper-JOIN, Markdown-Renderer, Graceful-Degradation), `McpConstants.cs` (4 neue Konstanten), `ToolRegistry.cs` (`BuildSuggestIndexes()` mit ausführlicher Description), `ToolDispatcher.cs` (Konstruktor-Injektion + Handler + `GetDouble`-Helper), `Program.cs` (DI-Singleton), `IndexSuggestionServiceTests.cs` (NEU, 12 Tests 1–12 exakt wie im Plan spezifiziert), `PerformanceMeasurementService.cs` (TD-003-Generalisierung, drei SHOWPLAN-Aufrufstellen umgestellt, `internal static` Helper), `architecture-spec.md` (§4 Nr. 16 + §H um 4. Permission-Block) und `README.md` (Feature-Bullet, Tool-Count 15→16, Recommended-Permissions um `VIEW SERVER STATE` erweitert). Drei bestehende Test-Dateien (`ToolRegistryTests`, `McpHostTests`, `ToolDispatcherTests`) wie vorgeschrieben angepasst (Tool-Count 15→16, neues Service-Mock). Code-Commit `3195a17` auf `main` (lokal, kein Push) mit Conventional-Commit-Format, deutsch, imperativ, Subject 78 Zeichen inkl. Suffix `[sql-index-suggestions]`; Doku-Commit `50437e2` (Result + step-plan.md `status`-Frontmatter auf `done (pending audit)`). `AiNetLinterTests.RecreateBaseline` hat `SqlToAi-baseline.json` automatisch mit-aktualisiert (kein manuelles Hash-Rechnen).

### Rules-Konformität

`SqlToAiRichtlinien.mdc` §4 (Doku-Sync-Pflicht — `architecture-spec.md` und `README.md` ohne Aufforderung mit-aktualisiert): eingehalten. §5 (Zero-Warning-Direktive, `TreatWarningsAsErrors`, AiNetLinter-Hinweis „kein manuelles Hash-Rechnen"): eingehalten — Build grün mit 0 Warnungen, `SqlToAi-baseline.json` automatisch aktualisiert. `AiNetLinter.mdc` Zeile 11 (`sealed` für konkrete Klassen): eingehalten — `IndexSuggestionService`, `MissingIndexRow`, `SuggestionRawRow`, `DmvMockConnectionFactory`, `FakeIndexSuggestionService` durchgehend `sealed`. Zeile 12 (`#nullable enable` am Dateianfang): eingehalten für alle vier neuen `.cs`-Dateien. Zeile 13–14 (kein leeres `catch`; `Log + sichtbarer Fehler`): eingehalten — `LogSuggestFailed` via `LoggerMessage.Define` + `SqlToAiError.QueryError` im generischen Catch; im Permission-Catch wird `RenderPermissionNote` zurückgegeben. Zeile 22 (`MaxMethodParameterCount`=4) und Zeile 27 (`MaxConstructorDependencies`=5): `IndexSuggestionService` exakt am Limit (5 Dependencies, 2 Methoden-Parameter im Record-Überladung), `ToolDispatcher` ist mit 9 Parametern und 8 Dependencies bereits über beiden Limits — **bestehender Status quo**, vom Planer explizit als "Bekannte Ausnahme" markiert, kein EPIC-02-spezifischer Verstoß. Zeile 53–55 (`EnforceNoSilentCatch`, `BanAsyncVoid`, `BanBlockingTaskAccess`): eingehalten — kein `async void`, kein `.Wait()/.Result`. Zeile 58 (`EnforceNamespaceDirectoryMapping`): eingehalten — `src/SqlToAi/Database/` → `namespace SqlToAi.Database;`, `src/SqlToAi/Domain/` → `namespace SqlToAi.Domain;`. Zeile 67 (`EnforceAsciiIdentifiers`): eingehalten — keine Umlaute in Bezeichnern (`DatabaseName`, `MinScore`, `ViewServerStatePermissionError`, …).

### Logische Korrektheit

**Validierungs-Pfad sauber:** `IsNullOrWhiteSpace(DatabaseName)` → `InvalidParameters`; `Top <= 0` → `InvalidParameters`; `MinScore < 0` → `InvalidParameters`; Whitelist- und `AccessLevel.None`-Checks vor Connection-Open. **Dapper-Mapping** via `QueryAsync<SuggestionRawRow>` mit `CommandDefinition(sql, parameters, cancellationToken: ct)` — `CancellationToken` korrekt durchgereicht. **Gruppierungs-Logik** in `GroupRows` aggregiert pro `IndexHandle` und sortiert die Spalten-IDs nach `column_usage` (EQUALITY/INEQUALITY/INCLUDE) — semantisch korrekt für die `sys.dm_db_missing_index_columns`-Semantik. **Permission-Erkennung:** `IsViewServerStatePermissionError` ruft den generalisierten Helper zweimal auf (Number 300 + Number 297, Keyword „VIEW SERVER STATE") — semantisch identisch zum alten `IsShowplanPermissionError` an den drei SHOWPLAN-Aufrufstellen. **TD-003-Erfüllung sauber:** Helper ist `internal static`, ermöglicht Test-Zugriff aus `IndexSuggestionServiceTests` (Test 11); die drei Aufrufstellen `catch (SqlException ex) when (IsPermissionError(ex, 262, "SHOWPLAN"))` ersetzen 1:1 die alte Form. **Konzept-Score-Formel-Detail:** Code verwendet `avg_total_user_cost × avg_user_impact × (user_seeks + user_scans)`, Konzept Zeile 45 schreibt verkürzend `avg_user_cost × avg_user_impact × (seeks + scans)` — die DMV-Spalten heißen tatsächlich `avg_total_user_cost`/`user_seeks`/`user_scans` (siehe Microsoft-Doku für `sys.dm_db_missing_index_group_stats`), Konzept ist hier ungenau, Code ist die korrekte Interpretation. **Ein echter Logikfehler siehe Findings [CRITICAL].**

**Tests-Validität:** Tests 1–3 (Validierung), 4–5 (Security), 6 (Happy-Path), 7/8 (Parameter-Passing), 9 (Permission-Graceful), 10 (Generic-Fail), 11 (TD-003-Regression), 12 (Args-Defaults) sind inhaltlich aussagekräftig — die Reflexion-basierte `CreateSqlException`-Helper-Konstruktion (`SqlError`/`SqlErrorCollection` intern, `SqlException` via internem `Guid`-Constructor) ist das etablierte ADO.NET-Escape-Hatch für Permission-Tests. Tests 7/8 prüfen explizit die Dapper-Parameter-Bindung gegen `LastReaderCommand` (statt `LastCommand`, wegen des Coder-Beobachtung dokumentierten Race-Issues zwischen `Parameters`-Binding und `ExecuteReader`).

### Konzept-Treue (Ebene 4)

**Muss-Haven Idee 2 vollständig adressiert:** Tool `sql_suggest_indexes` existiert mit den vier Parametern (`database` Pflicht, `table_name`/`min_score`/`top` optional, `top` Default 10), `improvement_score` wird berechnet, Markdown-Format mit den acht Spalten Score/Table/Equality/Inequality/Include/Seeks/Scans/LastSeek wird gerendert, **Restart-Hinweis ist fester Bestandteil der Ausgabe** (Konzept §Muss-Haven letzter Spiegelstrich: erfüllt — `RenderMarkdown` und `RenderPermissionNote` beide mit `RestartHint`), Graceful Degradation bei fehlender `VIEW SERVER STATE` (Permission-Fehler abgefangen, strukturierte Notiz statt `SQL-AI-0102` Hard-Error, analog zum `SHOWPLAN`-Pattern) — Konzept §Permission-Handling erfüllt. **Non-Goals nicht verletzt:** kein `CREATE INDEX`-DDL generiert (Tool liefert nur Spalten-Listen, `BuildCreateIndexStatement` aus `PerformanceMeasurementService` bleibt unangetastet — TD-001-Bezug erhalten), keine DTA-Anbindung, keine `DBCC AUTOPILOT`, keine Schreiboperation. **EPIC-01-Bestandteile korrekt nicht mit-umgesetzt** — der `sql_measure_performance`-Pfad ist im `git show 3195a17` nicht modifiziert (nur `IsShowplanPermissionError` → `IsPermissionError` in der gleichen Datei, was die Generalisierung ist). **Eine Einschränkung siehe Findings [CRITICAL].**

### Build-/Test-Status

```
dotnet build SqlToAi.slnx  → grün (0 Warnungen, 0 Fehler)
dotnet test  SqlToAi.slnx  → grün (517 Tests, 0 Fehler, 0 übersprungen, ~5 s, inkl. AiNetLinterTests.RecreateBaseline)
```

(Coder-Report 517 vs. step-001-Report 505: +12 neue Tests in `IndexSuggestionServiceTests` decken die 12 spezifizierten Fälle ab.)

## Findings

1. `src/SqlToAi/Database/IndexSuggestionService.cs:123-149` — **[CRITICAL] [Logische Korrektheit]** Die DMV-Query wendet `OFFSET 0 ROWS FETCH NEXT @Top ROWS ONLY` auf das **verjointe** Resultat an, nicht auf das gruppierte Resultat pro `index_handle`. Konkret: `sys.dm_db_missing_index_columns` liefert eine Zeile pro Spalte — d. h. ein Index mit 5 Spalten produziert 5 verjointe Zeilen, ein Index mit 3 Spalten produziert 3 verjointe Zeilen. Bei `@Top=10` und drei Indizes mit 5/3/7 Spalten können die ersten 10 verjointen Zeilen z. B. aus 2 Indizes (alle 5 + erste 3) bestehen, der dritte Index fehlt komplett. **Zwei sichtbare Defekte:** (a) die zurückgegebene Anzahl Recommendations ist `<= @Top` (oft kleiner als gewünscht), (b) abgeschnittene Recommendations können unvollständige Spalten-Listen haben (z. B. `[dbo].[Orders]` mit nur 2 von 7 Spalten) — die LLM würde daraus ein falsches `CREATE INDEX` ableiten. Der Plan selbst hat diese Query-Struktur so vorgeschrieben; die `step-result.md` Beobachtungen weisen explizit auf synthetische Verifikation und Bitte um SQL-Review durch den Kritiker hin. **Fix:** CTE-basierte Query, die `TOP (@Top)` auf den `index_handle` / `mid.statement` anwendet **bevor** `sys.dm_db_missing_index_columns` gejoint wird; anschließend die Detail-JOINs für die `Top N`-Handles. Beispiel-Skizze (im Fix-Step zu verfeinern):
   ```sql
   WITH TopIndexes AS (
       SELECT TOP (@Top) mid.statement AS Statement, mig.index_handle AS IndexHandle,
              migs.user_seeks, migs.user_scans, migs.last_user_seek,
              migs.avg_total_user_cost, migs.avg_user_impact,
              (migs.avg_total_user_cost * migs.avg_user_impact * (migs.user_seeks + migs.user_scans)) AS ImprovementScore
       FROM sys.dm_db_missing_index_group_stats AS migs
       INNER JOIN sys.dm_db_missing_index_groups AS mig ON migs.index_group_handle = mig.index_group_handle
       INNER JOIN sys.dm_db_missing_index_details AS mid ON mig.index_handle = mid.index_handle
       WHERE mid.database_id = DB_ID()
         AND (@TableName IS NULL OR mid.statement LIKE '%' + @TableName + '%')
         AND (@MinScore IS NULL OR ImprovementScore >= @MinScore)
       ORDER BY ImprovementScore DESC, mid.statement
   )
   SELECT ti.Statement, ti.IndexHandle, ti.user_seeks AS UserSeeks, ti.user_scans AS UserScans,
          ti.last_user_seek AS LastUserSeek, ti.AvgTotalUserCost, ti.AvgUserImpact,
          mic.column_id AS ColumnId, mic.column_usage AS ColumnUsage
   FROM TopIndexes ti
   INNER JOIN sys.dm_db_missing_index_columns AS mic ON ti.IndexHandle = mic.index_handle
   ORDER BY ti.ImprovementScore DESC, ti.Statement, mic.column_id;
   ```
   Erforderlich ist zusätzlich ein **neuer Unit-Test**, der mehrere `IndexHandle`s mit unterschiedlichen Spaltenzahlen füttert und die Top-N-Semantik absichert (z. B. 3 Handles à 2/5/3 Spalten, `@Top=4` → genau 2 Handles mit vollständigen Spalten-Listen, der dritte Handle gar nicht). Der `Integrationstest in step-003` gegen eine echte Test-DB ist ebenfalls betroffen — der Planer muss die Test-Fixture-Daten so wählen, dass dieser Boundary-Fall abgedeckt ist.

## Sonstige Beobachtungen / MINOR / NITPICK

- **Konzept-Spalten-IDs vs Spalten-Namen:** Konzept §Wie-Idee-2 Zeile 188 zeigt in der Beispiel-Tabelle `CustomerId`/`OrderDate` (Namen); `IndexSuggestionService` liefert aktuell `2`/`3` (Spalten-IDs aus `sys.dm_db_missing_index_columns.column_id`). Der Plan ist hierzu still; die Coder-„Bekannte Unschärfe" in `step-result.md` dokumentiert dies transparent. Verbesserung wäre ein zusätzlicher JOIN auf `sys.columns` (Spalten-Name aus `sys.columns.name WHERE object_id = mid.object_id AND column_id = mic.column_id`). **Kein Finding** — Plan-konform, LLM-Agent kann die IDs über `sql_get_schema` nach-auflösen, Konzept-Harmonisierung wäre Doku-Aufgabe.

- **Konzept vs Code-Formel-Schreibweise:** Konzept Zeile 45 nennt `avg_user_cost × avg_user_impact × (seeks + scans)`; Code/README/architecture-spec verwenden die tatsächlichen DMV-Spaltennamen `avg_total_user_cost × avg_user_impact × (user_seeks + user_scans)`. **Kein Finding** — Code ist die korrekte Interpretation der DMV-Semantik, Konzept ist ungenau. Konzept-Harmonisierung wäre Doku-Aufgabe (analog zu TD-001, aber Doku-Source ist Konzept, nicht Plan/Coder).

- **`ToolDispatcher` mit 9 Konstruktor-Parametern / 8 Dependencies:** überschreitet `MaxMethodParameterCount` (4) und `MaxConstructorDependencies` (5) deutlich. **Kein Finding** — bestehender Status quo, vom Planer explizit als "Bekannte Ausnahme" markiert; Service-Bundle-Record (analog `DatabaseServices(...)`-Vorschlag in `step-result.md` Beobachtungen) wäre eine zukünftige Refactoring-Aufgabe, nicht in EPIC-02-Scope.

- **README-Bullet "16 Progressive Disclosure Schema Tools" thematisch inkonsistent** (`sql_suggest_indexes` ist kein reines Schema-Tool). **Kein Finding** — bestehende Inkonsistenz (Bullet zählt bereits `sql_compare_queries`/`sql_measure_performance`/`sql_benchmark_optimization` mit), vom Planer explizit als "Bewusste Out-of-Scope-Entscheidung" markiert; nur die Count-Korrektur 15→16 ist im Scope.

- **Konzept-Count-Text 16 stimmt jetzt:** `ToolRegistryTests.GetAll_ShouldReturn_SixteenTools`, `McpHostTests.ToolsList_ShouldReturn_SixteenTools`, `architecture-spec.md` §4 hat 16 durchnummerierte Einträge, `README.md` Zeile 28 zeigt „16 Progressive Disclosure Schema Tools". Konsistent.

## Tech-Debt-Einträge aus diesem Review

- `TD-003` (siehe `tech-debt.md`) — **mit diesem Step erledigt**: Eintrag in `tech-debt.md` auf Status „erledigt in step-002" gesetzt. Helper `internal static IsPermissionError(SqlException, int errorNumber, string keyword)` ersetzt `IsShowplanPermissionError`; die drei SHOWPLAN-Aufrufstellen in `PerformanceMeasurementService` (Zeilen 168, 217, 253) sind auf `IsPermissionError(ex, 262, "SHOWPLAN")` umgestellt; semantisch identisch, durch Test 11 in `IndexSuggestionServiceTests` (`PerformanceMeasurementService_IsPermissionError_RefactoredToHelper_StillRecognizesShowplanError`) abgesichert.

## Kontext-Hinweis für den Fix-Step

Der Fix-Step `step-002/fix-01/` (vom Orchestrator anzulegen) sollte **ausschließlich** die SQL-Query in `IndexSuggestionService.LoadSuggestionsAsync` umstellen (CTE-basiert) und einen passenden Unit-Test ergänzen. Keine Scope-Erweiterung: kein JOIN auf `sys.columns` (IDs vs Namen — bewusst out-of-scope, siehe „Sonstige Beobachtungen"), keine Änderung an `PerformanceMeasurementService`/`IsPermissionError` (TD-003 ist abgeschlossen), keine Änderung an Doku (architecture-spec/README sind konsistent zum jetzigen Code, eine Konzept-/Doku-Update folgt nur falls die Fix-Query die Score-Formel oder Output-Spalten-Anzahl verändert).
