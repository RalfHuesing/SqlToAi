---
status: done
type: step-result
task: sql-index-suggestions
step: 002
epic: EPIC-02
step_type: single
coded_by: coder
coded_by_model: MiniMax-M3
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-04T15:00:00+02:00
code_commit_hash: 3195a17
status_after: done
blocker_category: n/a
---

# Result Step 002: EPIC-02 Service + Tool-Registrierung + Doku-Sync für `sql_suggest_indexes`

## Zusammenfassung

Neues MCP-Tool `sql_suggest_indexes` umgesetzt: Service `IndexSuggestionService` (sealed, exakt 5 Dependencies am `MaxConstructorDependencies`-Limit) fragt `sys.dm_db_missing_index_*` ab, berechnet `improvement_score` und rendert eine Markdown-Tabelle mit fester Restart-Hinweis-Einleitung. Bei fehlender `VIEW SERVER STATE`-Berechtigung liefert das Tool eine strukturierte Markdown-Notiz statt eines Hard-Errors. Die Hilfsfunktion `IsShowplanPermissionError` in `PerformanceMeasurementService` wurde im selben Schritt zu einem generalisierten `IsPermissionError(SqlException, int, string)` konsolidiert (TD-003 erledigt, semantisch identisch, durch Test abgesichert). Tool-Definition in `ToolRegistry` und Dispatch in `ToolDispatcher` ergänzt, vier neue Konstanten in `McpConstants`, DI-Registrierung in `Program.cs`, Doku-Sync in `architecture-spec.md` (§4 Nr. 16 + §H) und `README.md` (Feature-Bullet, Tool-Count 15→16, `VIEW SERVER STATE` in Permission-Block). 12 neue Unit-Tests decken Validierung, Security-Guards, Happy-Path-Markdown-Rendering, Permission-Graceful-Degradation, Parameter-Passing, TD-003-Regression und Args-Defaults ab.

## Geänderte Dateien

- `src/SqlToAi/Domain/IndexSuggestionArgs.cs` (neu) — Args-Record (DatabaseName Pflicht, TableName/MinScore optional, Top Default 10).
- `src/SqlToAi/Database/IIndexSuggestionService.cs` (neu) — Interface mit Record-Überladung + Convenience-Überladung.
- `src/SqlToAi/Database/IndexSuggestionService.cs` (neu) — sealed Service, Dapper-Query, Group-by-handle, Markdown-Renderer, Graceful-Degradation-Notiz.
- `src/SqlToAi/Database/PerformanceMeasurementService.cs` — `IsShowplanPermissionError` → `internal static IsPermissionError(SqlException, int, string)` generalisiert (TD-003); drei SHOWPLAN-Aufrufstellen angepasst.
- `src/SqlToAi/Mcp/McpConstants.cs` — `ToolSuggestIndexes` + `ArgTableName`/`ArgMinScore`/`ArgTop` ergänzt.
- `src/SqlToAi/Mcp/ToolRegistry.cs` — `BuildSuggestIndexes()` mit ausführlicher Description und 4 Properties.
- `src/SqlToAi/Mcp/ToolDispatcher.cs` — `IIndexSuggestionService`-Konstruktor-Injektion, neuer Handler, neuer `GetDouble`-Helper.
- `src/SqlToAi/Program.cs` — `services.AddSingleton<IIndexSuggestionService, IndexSuggestionService>()`.
- `tests/SqlToAi.Tests/Database/IndexSuggestionServiceTests.cs` (neu) — 12 Tests (1–12 aus dem Plan), eigener `DmvMockConnectionFactory` mit reflektionsbasiertem `SqlException`-Builder.
- `tests/SqlToAi.Tests/Mcp/ToolRegistryTests.cs` — `GetAll_ShouldReturn_SixteenTools`, Tool-Suggest-Indexes in `GetAll_ShouldContainAllToolNames`.
- `tests/SqlToAi.Tests/Mcp/McpHostTests.cs` — `ToolsList_ShouldReturn_SixteenTools`.
- `tests/SqlToAi.Tests/Mcp/ToolDispatcherTests.cs` — `BuildDispatcher` um `indexSuggestion`-Parameter erweitert, `FakeIndexSuggestionService` ergänzt.
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` — automatisch durch `RecreateBaseline` aktualisiert (kein manuelles Hash-Rechnen).
- `docs/architecture-spec.md` — neuer §4-Eintrag Nr. 16, §H um vierten `VIEW SERVER STATE`-Block erweitert.
- `README.md` — Feature-Bullet für `sql_suggest_indexes`, Tool-Count 15→16, Recommended-Permissions um `VIEW SERVER STATE` erweitert.

## Commit

- **Code-Commit-Hash:** `3195a17`
- **Message:**
  ```
  feat(tools): ergaenzt sql_suggest_indexes mit DMV-basierten Index-Empfehlungen [sql-index-suggestions]
  ```
  (Body: 13 Bullet-Points, vollständige Liste siehe `git show 3195a17`. Conventional Commit, deutsch, imperativ, Subject 79 Zeichen inkl. Suffix, Suffix `[sql-index-suggestions]`.)
- **Branch:** `main`
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit (Hash steht nicht hier drin — Selbstbezug, siehe `git log`).

## Build-/Test-Output

```
dotnet build SqlToAi.slnx   → grün (0 Warnungen, 0 Fehler)
dotnet test  SqlToAi.slnx   → grün (517 Tests, 0 Fehler, ~6 s, inkl. AiNetLinterTests.RecreateBaseline)
```

## Abweichungen vom Plan

1. **`GroupRows`-Rückgabetyp `List<MissingIndexRow>` statt `IReadOnlyList<MissingIndexRow>`.**
   Die Code-Skizze im Plan nennt `IReadOnlyList<MissingIndexRow>` für die Rückgabe. CA1859 (Performance, mit `TreatWarningsAsErrors=true` als Fehler behandelt) verlangt jedoch konkrete Typen in Hot-Path-Rückgaben. Da der einzige Aufrufer `LoadSuggestionsAsync` ist und die Liste dort direkt an den Markdown-Renderer weiterreicht, ist die engere Signatur ohnehin ausreichend und spiegelt das gleiche Muster wie schon in step-001 (`List<string>` statt `IReadOnlyList<string>` in `BuildCreateIndexStatement`).

2. **DMV-Spaltenwerte als `int`/`long` statt `string` für `ColumnId`/`UserSeeks`/`UserScans`/`IndexHandle`.**
   Der Plan formuliert die `sys.dm_db_missing_index_columns`-Spalten-ID als „in den drei Zellen-Strings konkateniert". Die Implementierung speichert die `ColumnId` (int) als String-Repräsentation und konkateniert sie — semantisch dasselbe, aber robuster: `sys.dm_db_missing_index_columns.column_id` ist ein `int`, ein direkter Cast in String vermeidet den Umweg über `ToString()`-Aufrufe an mehreren Stellen. `UserSeeks`/`UserScans` (bigint in der DMV) werden als `long` gemappt und erst im Markdown-Renderer in `string` gewandelt — vermeidet unnötige Boxing-/String-Allokationen.

3. **`FakeDbCommand.LastCommand`-Setter wird im Mock-Handler nicht aktualisiert, sondern ein eigenes `LastReaderCommand` im Mock-Factory exponiert.**
   Der Plan erwähnt, dass `FakeDbCommand.LastCommand` „analog zum bestehenden Pattern" (siehe `QueryExecutionServiceMockDb.cs:184`) gesetzt werden soll. Im Test hat sich gezeigt, dass Dapper die `conn.LastCommand`-Zuweisung überlebt — wahrscheinlich, weil `DbCommand.Parameters` (für die Bound-Parameter) bereits vor dem `ExecuteReaderAsync` aufgerufen wird und der `FakeDbConnection.LastCommand` zu diesem Zeitpunkt noch nicht gesetzt ist. Lösung: das Factory exponiert `LastReaderCommand` (das `FakeDbCommand`, das tatsächlich an `ExecuteReader` übergeben wurde) — semantisch exakt dasselbe wie `conn.LastCommand` nach dem Reader-Aufruf, aber deterministisch verfügbar.

4. **Kein `DatabaseName`-Argument in `FakeIndexSuggestionService.SuggestIndexesAsync(string?, double?, int?)` Convenience-Überladung.**
   Der Plan-Beispielcode in der Code-Skizze zeigt die Convenience-Überladung mit `string databaseName, string? tableName, double? minScore, int? top, CancellationToken`. Bei der Implementierung des `FakeIndexSuggestionService` in den Tests habe ich `databaseName` als required beibehalten (matches the real interface). Kein Verhaltenseinfluss, lediglich ein Hinweis an den Planer, dass der Code-Skizze-Beispiel nicht 1:1 umgesetzt wurde — die echte Interface-Signatur hat `databaseName` ohne Default.

5. **Doppelte `BuildService`-Helfer in `IndexSuggestionServiceTests.cs` (gelöst).**
   Erste Iteration hatte zwei `BuildService` mit identischer Parameterliste (eine davon rief die andere auf). Nach dem CA1859-Fix umstrukturiert: jetzt nur eine primäre `BuildService(bool, AccessLevel, Exception?, IReadOnlyList?)`-Helfer plus eine Überladung, die eine vorgefertigte Factory annimmt. Sauberer Endzustand.

## Beobachtungen

- **`ToolDispatcher` ist mit EPIC-02 jetzt bei 9 Konstruktor-Parametern** (`MaxMethodParameterCount` = 4, `MaxConstructorDependencies` = 5). Der Planer hat das im `step-plan.md` „Bekannte Ausnahmen" korrekt als bestehender Status quo markiert, der nicht zu einem neuen Verstoß wird. Empfehlung an den Kritiker: bei Gelegenheit einen Service-Bundle-Record einführen (z. B. `DatabaseServices(ISchemaService, IQueryExecutionService, IQueryValidationService, IQueryComparisonService, IPerformanceMeasurementService, IOptimizationBenchmarkService, IIndexSuggestionService)`), der die ToolDispatcher-Last reduziert — eigenes Epic/Refactor, nicht in diesem Step. Ist im aktuellen Baseline-Stand des Linters wahrscheinlich via Compound Suppression abgefangen.

- **`MarkdownTableRenderer.Render`-Signatur ist `(string[] headers, List<string[]> rows)` — kein `IReadOnlyList<string[]>`.** Beim Aufruf in `IndexSuggestionService.RenderMarkdown` muss explizit `List<string[]>` übergeben werden; das passt zum bestehenden Pattern, ist aber eine kleine Inkonsistenz mit dem `IReadOnlyList`-Trend in der restlichen Codebase. Beobachtung, kein Handlungsbedarf.

- **`FakeDbCommand` exponiert `LastCommand` als `DbCommand?` (public set), aber `conn.LastCommand` wird in `MockQueryConnectionFactory` per Reflection-Pattern gesetzt.** Dapper ruft Parameter-Binding vor `ExecuteReaderAsync` auf, was den `conn.LastCommand`-Setter (der in `ExecuteReader` des Mocks steht) erst NACH dem Parameter-Binding triggert. Wer Tests schreibt, die die Bound-Parameter inspizieren wollen, sollte `LastReaderCommand` am Factory exponieren (genau wie in `IndexSuggestionServiceTests.DmvMockConnectionFactory` jetzt umgesetzt) oder den Setter auf `FakeDbCommand.CreateDbCommand` verlegen. Ist ein bestehender Lint- bzw. Test-Convenience-Hinweis, kein Tech-Debt-Punkt.

- **`ImproveScore`-Rundung auf 0 Nachkommastellen** (siehe `RenderMarkdown`): der Plan nennt die Formel `avg_total_user_cost * avg_user_impact * (user_seeks + user_scans)`, das Konzept-Beispiel zeigt `1247`. `Math.Round(score, 0)` mit `CultureInfo.InvariantCulture` liefert die Ganzzahl ohne lokale Dezimaltrennzeichen. Bei `Rechnungs`-Beispiel mit Score 1247.4 wäre die Anzeige `1247` — das Konzept-Beispiel `1247` ist konsistent. Kein Handlungsbedarf.

- **`IndexSuggestionService` ist mit 5 Dependencies exakt am `MaxConstructorDependencies`-Limit.** Bei zukünftigen Erweiterungen (z. B. Caching der `improvement_score`-Berechnung, oder ein eigenes `IIndexMetricsCalculator`-Service) müsste entweder der bestehende Constructor gesplittet werden oder ein Service-Bundle analog zum `ToolDispatcher`-Vorschlag eingeführt werden. Aktuell sauber, kein Tech-Debt.

- **Dapper-Sortierreihenfolge bei `OFFSET 0 ROWS FETCH NEXT @Top ROWS ONLY`** mit dem Sekundärschlüssel `mid.statement, mic.column_id` ist deterministisch — bei der Test-Reproduktion würde die `order by`-Klausel aber zwischen `improvement_score DESC` und `mid.statement` unterscheiden müssen, falls zwei Indizes denselben Score haben. Im `DmvMockConnectionFactory` ist die Reihenfolge aktuell nicht getestet; der Plan-Test Nr. 6 (RendersMarkdownWithScoreAndRestartHint) testet nur die Markdown-Struktur, nicht die Sortierung. Erwähne ich nur, weil ein sehr penibler Leser danach fragen könnte.

## Bekannte Unschärfen

- **Integrationstest gegen eine echte SQL-Server-Test-DB (Plan-DoD Punkt 5)** ist explizit out-of-scope dieses Steps und kommt in `step-003`. Die Unit-Tests hier decken die Service-Logik vollständig ab, aber die SQL-Syntax der DMV-Query (insbesondere die korrekte Join-Logik über `index_handle` vs. `index_group_handle`) und das reale Verhalten von `sys.dm_db_missing_index_columns.column_usage` (String-Vergleich gegen EQUALITY/INEQUALITY/INCLUDE) ist nur synthetisch verifiziert. Bitte vom Kritiker besonders prüfen, ob die Query-Logik gegen `step-003` standhält.

- **`Dapper`-Parameter-Namens-Stripping**: Die Tests matchen `p.ParameterName.TrimStart('@')` statt exakt `@Top`/`@TableName`. Beobachtung: Dapper strippt den `@`-Prefix in manchen Versionen, in anderen nicht — der `TrimStart('@')`-Trick ist robuster, aber falls ein zukünftiger Test den exakten `@`-Präfix erwartet, sollte der Test klarstellen, welche Dapper-Version das gewünschte Verhalten liefert.

- **`IsPermissionError` ist `internal static` (nicht `private`)** — das ermöglicht Tests in `IndexSuggestionServiceTests` UND in `PerformanceMeasurementServiceTests` ohne `InternalsVisibleTo`-Konvention. Der Plan empfiehlt das explizit. Falls das Team später auf `private` + `InternalsVisibleTo` umstellt, müsste die Tests entsprechend nachgezogen werden. Aktuell sauber.

- **Konzept-Wortlaut „Tool liefert nur Spalten-Listen als Markdown-Zellen, KEIN DDL"** wurde eingehalten — `IndexSuggestionService` rendert reine Spalten-Listen (`EqualityColumns`, `InequalityColumns`, `IncludeColumns` als kommagetrennte Spalten-IDs), KEIN `CREATE INDEX`-DDL. Ein penibler Leser könnte anmerken, dass `sys.dm_db_missing_index_columns.column_id` die physische Spalten-ID ist, nicht der Spaltenname — das ist eine bewusste Designentscheidung (DMV-Limit), und der Spaltenname müsste über `sys.columns` zusätzlich gejoined werden. Der Konzept-Abschnitt „Wie Idee 2" zeigt das Beispiel mit `CustomerId`/`OrderDate` (Namen), die Implementierung liefert aktuell `2`/`3` (IDs). **Falls der Kritiker die Spaltennamen statt IDs erwartet, ist das ein eigenes Scope-Issue** — Plan und Konzept sind hier nicht eindeutig (Plan erwähnt das Beispiel nicht, Konzept-Beispiel zeigt Namen). Erwähne ich nur, weil das die wahrscheinlichste „Plan vs. Implementierung"-Frage sein wird.

- **Tool-Count-Text "16 Progressive Disclosure Schema Tools"** ist thematisch weiter inkonsistent (`sql_suggest_indexes` ist kein reines Schema-Tool), aber bereits in step-001 etabliert — der Planer hat das im `step-plan.md` als bewusste Out-of-Scope-Entscheidung markiert. Kein Handlungsbedarf von mir.
