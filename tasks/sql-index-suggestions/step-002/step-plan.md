---
status: done (fix-01 pending)
type: step-plan
task: sql-index-suggestions
step: 002
title: "EPIC-02 Service + Tool-Registrierung + Doku-Sync für sql_suggest_indexes"
epic: EPIC-02
estimated_risk: medium
step_type: single
items: []  # nur bei step_type: batch
created_by: planer
created_by_model: MiniMax-M3
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-04T14:00:00+02:00
related_to:
  - step-001/step-review.md  # EPIC-01-Vorgänger; verweist auf TD-003 zur Permission-Generalisierung
---

# Step 002: EPIC-02 Service + Tool-Registrierung + Doku-Sync für `sql_suggest_indexes`

## Bezug

- **Task:** `sql-index-suggestions`
- **Epic:** `EPIC-02` aus `roadmap.md` — serverweit kumulierte DMV-Index-Empfehlungen mit Graceful Degradation (Idee 2 aus `konzept.md`).
- **Konzept-Referenz:** `konzept.md` §Muss-Haven Idee 2, §Permission-Handling, §Wie Idee 2, §DoD; `architecture-spec.md` §4 (Tool-Spezifikationen, neuer Eintrag Nr. 16) und §H (Empfohlene SQL-Server-Berechtigungen); `README.md` Zeile 13 + 27.

## Aktueller Projektzustand (JIT-Kontext)

Beim Lesen des aktuellen Code- und Doku-Stands (Stand nach `step-001`, Commit `86c0e48`) habe ich folgende Strukturen vorgefunden, die für EPIC-02 direkt wiederverwendet werden können — sie prägen diesen Plan:

- **Pattern-Vorlage DMV-Tool mit Markdown-Output:** `SchemaService.GetObjectReferencesAsync` (Zeile 240–243) delegiert an `DetailSchemaRenderer.GetObjectReferencesAsync` (Zeile 240–280). Letzterer ist `internal static`, nutzt `connection.QueryAsync<ReferenceRow>(...)` (Dapper, kein expliziter `CommandType.StoredProcedure`-Aufruf), rendert über `MarkdownTableRenderer.Render(headers, rows)` und liefert `# Heading\n\n` + Tabelle. **`DetailSchemaRenderer` ist `internal static` und nicht für Dapper-basierte Service-Klassen vorgesehen, die ihre eigenen Dependencies brauchen** — `IndexSuggestionService` wird daher eine eigene `public sealed`-Service-Klasse mit Interface, kein weiterer Eintrag in `DetailSchemaRenderer`. Architekturmuster statt 1:1-Kopie.
- **Pattern-Vorlage Service-Konstruktion:** `PerformanceMeasurementService` (Zeile 20–50) hat das für EPIC-02 relevante Konstruktionsmuster: `sealed class` mit `IDatabaseConnectionFactory` + `ISecurityGuard` + `IAccessLevelProvider` + `IReadOnlyGuard` + `IOptions<SqlToAiOptions>` + `ILogger<T>`. Für `IndexSuggestionService` ist `IReadOnlyGuard` nicht relevant (DMVs sind rein lesend, keine User-SQL-Statements), ansonsten gleiche Signatur. **`MaxConstructorDependencies`-Limit 5** (siehe `AiNetLinter.mdc` Zeile 27) wird mit den fünf Dependencies exakt erreicht.
- **Pattern-Vorlage Tool-Definition:** `ToolRegistry.BuildMeasurePerformance` (Zeile 250–279) ist die umfangreichste `BuildXxx`-Methode — sie zeigt, wie `McpConstants.ArgXxx` referenziert, `StringParam(...)` für Pflicht-Args und Inline `new() { Type = "..." }` für optionale typisierte Args verwendet werden. Pflichtbestandteil: ausführlicher `Description`-String (typischerweise mehrere Sätze), der die Rückgabestruktur und das Verhalten erläutert.
- **Pattern-Vorlage Dispatch:** `ToolDispatcher._handlers` enthält einen Eintrag pro Tool (Zeile 71–202), im einfachsten Fall ein `CallAsync(() => _service.XxxAsync(...), res => JsonSerializer.Serialize(res, ...))`. Da `IndexSuggestionService` `Result<string>` (Markdown) liefert, ist **kein** Custom-Serializer nötig — der Default in `CallAsync` (`result.Value?.ToString() ?? string.Empty`) reicht, wie bei `SchemaService.GetObjectReferencesAsync`.
- **Pattern-Vorlage Permission-Graceful-Degradation:** `PerformanceMeasurementService.IsShowplanPermissionError` (Zeile 265–266):
  ```csharp
  private static bool IsShowplanPermissionError(SqlException ex) =>
      ex.Number == 262 || ex.Message.Contains("SHOWPLAN", StringComparison.OrdinalIgnoreCase);
  ```
  `PerformanceMeasurementService` fängt den Fehler via `catch (SqlException ex) when (IsShowplanPermissionError(ex))` ab, setzt ein `hasShowplanPermission = false` und liefert eine strukturierte Notiz statt Hard-Error. **TD-003 aus `step-001`** hat explizit angeregt, für EPIC-02 eine generalisierte `IsPermissionError(SqlException, int, string)`-Helper bereitzustellen. Dieser Schritt **nimmt diese Generalisierung in den Scope mit auf** — der bestehende `IsShowplanPermissionError` wird in einen kleinen privaten Helper `IsPermissionError(ex, errorNumber, keyword)` überführt, der dann sowohl für SHOWPLAN- (Number 262, Keyword "SHOWPLAN") als auch für VIEW-SERVER-STATE-Erkennung (Number 300 oder 297, Keyword "VIEW SERVER STATE") genutzt wird. So bleibt TD-003 im Rahmen des natürlichen EPIC-02-Scopes (gleiche Datei, gleiche Methode, gleiche Tests-Pattern) und entkoppelt sich nicht zu einem separaten Refactoring-Step. **Kein API-Bruch:** die existierende SHOWPLAN-Erkennung verhält sich identisch (Error-Numbers und Keywords unverändert), nur die Implementierung zieht in den Helper um.
- **McpConstants:** Die Tool-Namen werden in `McpConstants.cs` (Zeile 56–70) als `internal const string` deklariert. Die Arg-Namen analog (Zeile 76–92). Neue Konstanten: `ToolSuggestIndexes`, `ArgTableName`, `ArgMinScore`, `ArgTop`.
- **MarkdownTableRenderer:** `internal static class MarkdownTableRenderer.Render(string[] headers, List<string[]> rows)` (Zeile 31–41) ist die zentrale Markdown-Tabellen-Rendering-Engine. Wird direkt wiederverwendet, keine Duplikation.
- **DI-Registrierung:** `Program.cs` Zeile 162–214. Neuer Service wird analog zu den bestehenden Services als `services.AddSingleton<IIndexSuggestionService, IndexSuggestionService>();` registriert.
- **Konzept vs. Code-Inkonsistenz (TD-001):** Konzept-Beispiel Zeile 172 zeigt `IX_Orders_CustomerId_OrderDate`, die Implementierung in `PerformanceMeasurementService.BuildCreateIndexStatement` (Zeile 399–405) verwendet `IX_Table_Col__Col2`. **EPIC-02 baut KEINE `CREATE INDEX`-DDLs**, sondern rekonstruiert aus `sys.dm_db_missing_index_columns` (EQUALITY/INEQUALITY/INCLUDE) die Spalten-Listen und rendert sie als Markdown-Zellen, kein DDL. Damit ist TD-001 für EPIC-02 nicht relevant — der Konflikt bleibt zwischen Konzept und `PerformanceMeasurementService` (EPIC-01) bestehen und ist **kein** Scope dieses Steps. Der bestehende Doku-Status in `tech-debt.md` (TD-001, offen) bleibt unverändert.
- **Doku-Sync-Pflicht:** `SqlToAiRichtlinien.mdc` §4 (Zeile 61) verlangt, dass `docs/architecture-spec.md` und `README.md` bei jeder Code-Änderung ohne Aufforderung mit-aktualisiert werden. Doku-Sync ist daher zwingender Teil dieses Steps und nicht eigenständiger Folge-Step.
- **Tool-Count:** `README.md` Zeile 27 nennt "15 Progressive Disclosure Schema Tools" — der neue `sql_suggest_indexes` ist kein reines Schema-Tool, sondern ein Performance/Optimization-Tool (passt thematisch zu `sql_measure_performance` und `sql_benchmark_optimization`). Die existierende Tool-Count-Aussage ist **themenbezogen falsch** (zählt auch Performance/Optimization-Tools mit) und wird auf 16 korrigiert. Diese Beobachtung ist keine Verallgemeinerung des EPIC-02-Scopes, sondern eine mit der Code-Änderung ohnehin fällige Konsistenz-Korrektur.

## Intention

Nach diesem Step existiert das neue MCP-Tool `sql_suggest_indexes` vollständig: Ein neuer Service `IIndexSuggestionService` liefert für eine gegebene Datenbank die fehlenden Index-Empfehlungen aus `sys.dm_db_missing_index_details` + `sys.dm_db_missing_index_group_stats` + `sys.dm_db_missing_index_columns`, priorisiert nach `improvement_score`, optional gefiltert nach Tabellennamen oder Mindest-Score, inklusive dem geforderten Restart-Hinweis. Bei fehlender `VIEW SERVER STATE`-Berechtigung degradiert das Tool analog zum SHOWPLAN-Pattern strukturiert. Das Tool ist in `ToolRegistry` registriert, in `ToolDispatcher` dispatched, in `McpConstants` deklariert, in `Program.cs` DI-verdrahtet. Doku (`architecture-spec.md` §4 Nr. 16 + §H, `README.md` Zeile 13 + 27) ist synchron. **Was dieser Step bewusst nicht abdeckt:** ein Integrationstest gegen eine echte Test-DB — der folgt in `step-003`, weil er eine eigene Voraussetzung (laufende SQL-Server-Test-Instanz) hat, die in der regulären `dotnet test`-Pipeline nicht garantiert ist.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Domain/IndexSuggestionArgs.cs` (NEU)

- **Was:** Neuer `public sealed record IndexSuggestionArgs(string DatabaseName, string? TableName = null, double? MinScore = null, int Top = 10)`. Property `DatabaseName` als Pflicht ohne Default, die übrigen mit Defaults. XML-Doc-Kommentar pro Property (Pattern wie `QueryPerformanceArgs`).
- **Warum:** `MaxConstructorDependencies` (AiNetLinter) und der Records-Pattern für `*Options`-Style-Args verlangen ein Parameter-Objekt ab 5 Feldern. Mit nur 4 Feldern wäre ein `record` stilistisch konsequent (auch `QueryPerformanceArgs` ist ein `record` mit 6 Feldern). `Top` Default 10 entspricht der Konzept-Vorgabe.

### Datei 2: `src/SqlToAi/Database/IIndexSuggestionService.cs` (NEU)

- **Was:** Interface `IIndexSuggestionService` mit zwei Überladungen (analog `IPerformanceMeasurementService`):
  - `Task<Result<string>> SuggestIndexesAsync(IndexSuggestionArgs args, CancellationToken ct = default);`
  - `Task<Result<string>> SuggestIndexesAsync(string databaseName, string? tableName = null, double? minScore = null, int? top = null, CancellationToken ct = default);` (Convenience-Überladung, leitet an die Record-Überladung weiter)
- **Warum:** Pattern-Konsistenz mit `IPerformanceMeasurementService` und `IQueryExecutionService` (zwei Überladungen, eine davon nimmt ein Args-Record). Rückgabetyp `Result<string>` (Markdown), nicht `Result<SomeStruct>` — passt zum etablierten Schema-/Doku-Tool-Pattern (`GetSchemaAsync`, `GetObjectReferencesAsync` etc.).

### Datei 3: `src/SqlToAi/Database/IndexSuggestionService.cs` (NEU)

- **Was:** `public sealed class IndexSuggestionService : IIndexSuggestionService` mit folgenden Members:
  - Konstruktor: `(IDatabaseConnectionFactory, ISecurityGuard, IAccessLevelProvider, IOptions<SqlToAiOptions>, ILogger<IndexSuggestionService>)` — exakt fünf Dependencies, am `MaxConstructorDependencies`-Limit 5. (`IReadOnlyGuard` ist NICHT dabei, da die DMV-Queries kein User-SQL sind und kein Read-Only-Guard greift.)
  - `LoggerMessage.Define`-Delegates (analog `PerformanceMeasurementService.LogMeasurementFailed` Zeile 22–26) für die zwei Fehlerquellen: Permission-Denied und generischer Query-Fehler.
  - Private `static bool IsPermissionError(SqlException ex, int errorNumber, string keyword) => ex.Number == errorNumber || ex.Message.Contains(keyword, StringComparison.OrdinalIgnoreCase);` — **ersetzt** den bestehenden `IsShowplanPermissionError` in `PerformanceMeasurementService.cs:265-266`. Der Aufruf dort wird zu `IsPermissionError(ex, 262, "SHOWPLAN")`. Drei Aufrufstellen in `PerformanceMeasurementService` (Zeilen 168, 217, 253) — alle anpassen.
  - Public `SuggestIndexesAsync(IndexSuggestionArgs, CancellationToken)`: Validierung (`databaseName` nicht leer, `top > 0`, `minScore >= 0` falls gesetzt) → `ISecurityGuard.IsDatabaseAllowed` → `IAccessLevelProvider.GetAccessLevelAsync` (blockt `None`) → in `try`-Block `IDatabaseConnectionFactory.CreateConnection(databaseName)` öffnen (nicht in Transaction — DMVs lesen aus dem Speicher, keine Snapshot-Transaction nötig) → DMV-Query ausführen → Markdown rendern. Im `catch (SqlException ex) when (IsPermissionError(ex, 300, "VIEW SERVER STATE") || IsPermissionError(ex, 297, "VIEW SERVER STATE"))` (oder als kleiner, vorab geprüfter Helper `IsViewServerStatePermissionError(ex)`, der auf `IsPermissionError` aufbaut) → strukturierte Permission-Notiz (Markdown-Text, nicht `Result.Failure`) zurückgeben, damit das Tool dem LLM eine sinnvolle Meldung liefert, statt mit `SQL-AI-0102` (Query-Error) abzubrechen.
  - Private DMV-Query: `SELECT` über `sys.dm_db_missing_index_group_stats` JOIN `sys.dm_db_missing_index_groups` JOIN `sys.dm_db_missing_index_details` JOIN `sys.dm_db_missing_index_columns`, gefiltert auf `mid.database_id = DB_ID()` und ggf. `mid.statement LIKE '%' + @TableName + '%'` und `ImprovementScore >= @MinScore`, `ORDER BY ImprovementScore DESC OFFSET 0 ROWS FETCH NEXT @Top ROWS ONLY`. Dapper-Mapping auf einen `private sealed class MissingIndexRow` mit den Spalten `TableName | EqualityColumns | InequalityColumns | IncludeColumns | Seeks | Scans | LastSeek | ImprovementScore` — pro `index_group_handle` werden die Zeilen aus `sys.dm_db_missing_index_columns` nach `column_usage` gruppiert (EQUALITY/INEQUALITY/INCLUDE), in den drei Zellen-Strings konkateniert.
  - Private `static string RenderMarkdown(IReadOnlyList<MissingIndexRow> rows, string databaseName)`: erzeugt `# Missing Index Recommendations — {databaseName}\n\n` + einleitender Restart-Hinweis (siehe nächster Punkt) + `MarkdownTableRenderer.Render(headers, rows)` mit Headers `["Score", "Table", "Equality Columns", "Inequality Columns", "Include Columns", "Seeks", "Scans", "Last Seek"]`. **Der Restart-Hinweis ist fester Bestandteil der Ausgabe** (Konzept §Muss-Haven Idee 2 letzter Spiegelstrich + §DoD), nicht optional: ein einleitender Absatz vor der Tabelle, der erklärt, dass die DMV-Daten seit dem letzten Server-Neustart akkumulieren und auf frisch gestarteten Servern wenig/nichts liefern.
  - `LastSeek` als `DateTime?` über Dapper-Mapping; im Renderer als `dt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-"` (Konzept-Beispiel zeigt `2026-08-03`).
  - `ImprovementScore` als `double`; im Renderer als `Math.Round(score, 0).ToString(CultureInfo.InvariantCulture)` (Konzept-Beispiel zeigt ganzzahligen Score `1247`).
- **Warum:** Das ist der Kern des EPIC-02. Drei Validierungsschritte + eine DMV-Query + Markdown-Rendering. Architektur und Pattern (1:1 von `PerformanceMeasurementService` und `SchemaService` abgeleitet). Der `IsPermissionError`-Generalisierungs-Helper ist eine TD-003-Erfüllung im natürlichen Scope dieses Steps — würde er weggelassen, entstünden später zwei sehr ähnliche Methoden, die explizit als Tech-Debt dokumentiert wären (das `AllowTryPatternOutParameters`-Pattern, der AiNetLinter-Compound-Suppression-Hinweis, und der explizite Coder-Hinweis aus `step-001/step-result.md` Beobachtungen raten dazu, die Generalisierung im selben Schritt mitzunehmen).

### Datei 4: `src/SqlToAi/Mcp/McpConstants.cs` (Änderung, Zeile 56–92)

- **Was:** Vier neue Konstanten in den bestehenden Blöcken:
  - In `// Tool names`: `internal const string ToolSuggestIndexes = "sql_suggest_indexes";` (Position direkt nach `ToolBenchmarkOptimization`, alphabetisch bzw. thematisch am Ende der Liste)
  - In `// Tool argument keys`: drei neue Konstanten — `ArgTableName = "table_name"`, `ArgMinScore = "min_score"`, `ArgTop = "top"`. Position alphabetisch einsortiert nach existierender Konvention (Arg... lexikographisch).
- **Warum:** Pattern-Konsistenz (alle Tool-/Argument-Namen sind in `McpConstants` zentralisiert, siehe `ToolRegistry.cs` Header-Kommentar Zeile 7–9). `ArgTableName` ist neu — `ArgObjectName` (Zeile 79) ist semantisch verschieden (es referenziert ein DB-Objekt wie Tabelle/View/Prozedur, hier geht es um einen LIKE-Filter auf `statement`-Spalten, also ein Such-String, nicht eine aufgelöste Objekt-Identität — Naming `ArgTableName` ist sauberer).

### Datei 5: `src/SqlToAi/Mcp/ToolRegistry.cs` (Änderung, Zeile 28–45 + NEUE Methode)

- **Was:** Zwei Änderungen:
  1. In `BuildTools()`-Liste (Zeile 28–45): `BuildSuggestIndexes()` als neuen Eintrag **nach** `BuildBenchmarkOptimization()` einfügen (thematische Reihenfolge: Performance-Tools am Ende).
  2. Neue private statische Methode `BuildSuggestIndexes()` (zwischen `BuildBenchmarkOptimization` Zeile 281–310 und dem Kommentar-Block ab Zeile 312). Schema:
     - `Name = McpConstants.ToolSuggestIndexes`
     - `Description`: ein ausführlicher englischer String, der erklärt (a) was das Tool liefert (serverweit kumulierte fehlende Index-Empfehlungen aus `sys.dm_db_missing_index_*`-DMVs), (b) die Score-Berechnung (`avg_total_user_cost × avg_user_impact × (user_seeks + user_scans)`), (c) die Filter-Parameter, (d) den Pflicht-Restart-Hinweis, (e) die Graceful-Degradation bei fehlender `VIEW SERVER STATE`-Berechtigung.
     - `InputSchema.Properties`: vier Einträge — `ArgDatabase` (Pflicht, `StringParam("Target database name. Required.")`), `ArgTableName` (optional, `OptionalStringParam("Optional LIKE filter on the table name from the DMV statement column (e.g. 'Orders' or 'dbo.%'). Case-insensitive substring match.")`), `ArgMinScore` (optional, `new() { Type = "number", Description = "Optional minimum improvement_score threshold; rows with score below this are excluded. Default 0 (no threshold)." }`), `ArgTop` (optional, `new() { Type = "integer", Description = "Maximum number of recommendations to return. Default 10." }`).
     - `InputSchema.Required`: `[McpConstants.ArgDatabase]`.
- **Warum:** Pattern 1:1 von `BuildMeasurePerformance` (Zeile 250–279) — der umfangreichste vorhandene `BuildXxx`-Block, der mehrere optionale typisierte Args (integer, boolean) demonstriert. Beschreibung muss im Stil der existierenden Tools sein: ein einziger langer String mit englischen Sätzen, der dem LLM Verhalten, Score-Formel, Filter und Graceful Degradation erklärt.

### Datei 6: `src/SqlToAi/Mcp/ToolDispatcher.cs` (Änderung, Konstruktor + `_handlers`)

- **Was:** Zwei Änderungen:
  1. Konstruktor (Zeile 50–58): Ein zusätzlicher Parameter `IIndexSuggestionService indexSuggestionService` vor `IOptions<SqlToAiOptions> options` (thematische Nähe, gleiche Reihenfolge wie Interface-Declaration). Felder-Block (Zeile 39–46): `private readonly IIndexSuggestionService _indexSuggestionService;` analog zu den existierenden Feldern. **Achtung:** Der bestehende Konstruktor hat bereits 6 Parameter (`ISchemaService, IQueryExecutionService, IQueryValidationService, IQueryComparisonService, IPerformanceMeasurementService, IOptimizationBenchmarkService, IOptions<SqlToAiOptions>, ILogger<ToolDispatcher>`) = 8 Parameter. Mit dem neuen Service sind es 9 — **`MaxConstructorParameterCount` ist 4** (AiNetLinter Zeile 22), aber `ToolDispatcher` ist bereits weit über diesem Limit, also kein neuer Verstoß. **Allerdings:** `MaxConstructorDependencies` (Zeile 27) ist auf 5 limitiert, und `ToolDispatcher` ist ebenfalls schon drüber. Der existierende Tech-Debt ist nicht EPIC-02-relevant und im Linter-Baseline wahrscheinlich akzeptiert (z.B. via Compound Suppression). Der Planer stellt fest: **bestehender Status quo**, kein neuer Konflikt durch EPIC-02.
  2. `_handlers`-Dictionary (Zeile 69–202): Neuer Eintrag **nach** `ToolBenchmarkOptimization` (Zeile 189–201):
     ```csharp
     [McpConstants.ToolSuggestIndexes] = (paramsObj, ct) =>
         CallAsync(() => _indexSuggestionService.SuggestIndexesAsync(
             new IndexSuggestionArgs(
                 GetDb(paramsObj),
                 GetString(paramsObj, McpConstants.ArgTableName),
                 GetDouble(paramsObj, McpConstants.ArgMinScore),
                 GetInt(paramsObj, McpConstants.ArgTop) ?? 10),
             ct)),
     ```
     Der `CallAsync<T>`-Default-Serializer (`result.Value?.ToString() ?? string.Empty`) reicht — `IndexSuggestionService` liefert den fertigen Markdown-String.
  3. **Neuer Helper** `private static double? GetDouble(ToolCallParams p, string key)` (analog `GetInt` Zeile 315–325): parst `JsonElement` NumberKind → `double?`, returnt `null` wenn key fehlt oder kein Number.
- **Warum:** Pattern 1:1 von `ToolBenchmarkOptimization`-Handler. `GetDouble` ist neu, weil das Projekt bisher nur int-Args hatte; sauberer Helper statt Inline-`JsonElement`-Parsing in der Lambda.

### Datei 7: `src/SqlToAi/Program.cs` (Änderung, Zeile 186–191)

- **Was:** Eine neue Zeile in der `// Database`-Service-Registrierungs-Gruppe:
  ```csharp
  services.AddSingleton<IIndexSuggestionService, IndexSuggestionService>();
  ```
  Position: nach `IOptimizationBenchmarkService` (Zeile 190), thematische Reihenfolge (Performance/Optimization-Tools am Ende).
- **Warum:** Pattern-Konsistenz mit den existierenden Service-Registrierungen. Reihenfolge passt zu Registry/Dispatcher (ToolSuggestIndexes steht thematisch am Ende).

### Datei 8: `tests/SqlToAi.Tests/Database/IndexSuggestionServiceTests.cs` (NEU)

- **Was:** Unit-Tests mit Mocks (analog zu `tests/SqlToAi.Tests/Database/PerformanceMeasurementServiceTests.cs`). `IDatabaseConnectionFactory` wird via Test-Doubles gemockt (siehe Pattern in `QueryExecutionServiceTests.cs`, das die Connection nicht direkt mockt, sondern das Verhalten an einer Test-Connection testet — für `IndexSuggestionService` ist es einfacher, da die DMV-Queries selbst die `DbConnection.QueryAsync<T>`-Mechanik nutzen). Konkret:
  - Test 1: `SuggestIndexesAsync_DatabaseNameEmpty_ReturnsInvalidParametersError`
  - Test 2: `SuggestIndexesAsync_TopZero_ReturnsInvalidParametersError`
  - Test 3: `SuggestIndexesAsync_MinScoreNegative_ReturnsInvalidParametersError`
  - Test 4: `SuggestIndexesAsync_DatabaseNotInWhitelist_ReturnsSafetyCheckFailedError`
  - Test 5: `SuggestIndexesAsync_DatabaseAccessLevelNone_ReturnsSafetyCheckFailedError`
  - Test 6: `SuggestIndexesAsync_QueryReturnsRows_RendersMarkdownWithScoreAndRestartHint` (verwendet eine In-Memory-`DbConnection`-Implementierung, die die `QueryAsync<T>`-Aufrufe gegen vorgefertigte Rows bedient — oder, einfacher, ein `FakeDatabaseConnectionFactory` mit einem Stub-`DbCommand`)
  - Test 7: `SuggestIndexesAsync_TableNameFilter_PassedAsLikeParameter` (verifiziert, dass der Parameter an das SQL-Statement durchgereicht wird, via Capturing-Wrapper)
  - Test 8: `SuggestIndexesAsync_TopFilter_PassedAsFetchNextParameter`
  - Test 9: `SuggestIndexesAsync_PermissionDeniedSqlException_ReturnsGracefulDegradationNote` (simuliert eine `SqlException` mit `Number = 300` und prüft, dass das Ergebnis KEIN `Result.Failure` ist, sondern ein Markdown-String mit dem Permission-Hinweis)
  - Test 10: `SuggestIndexesAsync_GenericSqlException_ReturnsQueryError` (verifiziert die Fehler-Mapping-Konsistenz mit dem `SqlToAiErrorMapper`-Pattern)
  - Test 11: `IsPermissionError_ShowplanNumber_ReturnsTrue` (Test der TD-003-Generalisierung in `PerformanceMeasurementService` — stellt sicher, dass das Refactoring von `IsShowplanPermissionError` zu `IsPermissionError` mit dem Aufruf `IsPermissionError(ex, 262, "SHOWPLAN")` semantisch identisch bleibt). Dieser Test könnte in `PerformanceMeasurementServiceTests.cs` mit-aufgenommen werden, falls dort bereits die SHOWPLAN-Permission-Tests liegen (siehe `step-001`-Beobachtungen).
- **Warum:** Konzept §DoD verlangt Unit-Tests. Mocks sind nötig, weil DMV-Verhalten nicht sinnvoll gemockt werden kann — aber die **Service-Logik** (Parameter-Validierung, Permission-Handling, Markdown-Rendering) ist vollständig mockbar. Integrationstest (echte Test-DB) ist **separater** `step-003` (siehe Out-of-Scope).

### Datei 9: `docs/architecture-spec.md` (Änderung, §4 nach Nr. 15, §H vorletzter Block)

- **Was:** Zwei Doku-Sync-Punkte:
  1. **§4 nach Nr. 15** (`sql_benchmark_optimization`): Neuer Eintrag `### 16. sql_suggest_indexes` mit:
     - Aufzählung der Argumente (`database` Pflicht; `table_name`, `min_score`, `top` optional, mit englischen Beschreibungen analog Nr. 14/15)
     - Beschreibung des Zwecks (serverweit kumulierte DMV-Index-Empfehlungen, Score-Formel, Filter, Markdown-Tabelle, Restart-Hinweis)
     - Beschreibung der Graceful Degradation bei fehlender `VIEW SERVER STATE`
     - Hinweis auf `SqlServer-Berechtigungen` (Verweis auf §H)
  2. **§H** (Zeile 152–171, Block "Empfohlene SQL-Server-Berechtigungen"): Neuer vierter Block (nach dem `SHOWPLAN`-Block, vor dem Ende) mit `GRANT VIEW SERVER STATE TO [SqlToAiUser];` und Erklärungstext, dass diese server-scoped Permission für `sql_suggest_indexes` benötigt wird, um `sys.dm_db_missing_index_*` abzufragen. Stilistisch passend zum bestehenden 1./2./3.-Aufzählungsschema.
- **Warum:** SqlToAiRichtlinien §4 (Doku-Sync-Pflicht, Zeile 61) — ohne Aufforderung, mit jeder Code-Änderung. Konzept §DoD listet diese drei Doku-Sync-Punkte explizit auf.

### Datei 10: `README.md` (Änderung, Zeile 11–14 + Zeile 27)

- **Was:** Zwei Stellen:
  1. **Zeile 11–14** (`### ⚡ SQL Performance, Equivalence & Benchmarking`): Neues Bullet analog zum `sql_measure_performance`-Bullet (Zeile 13), mit dem `sql_suggest_indexes`-Tool, einer Zeile Beschreibung (serverweit kumulierte DMV-Index-Empfehlungen, priorisiert nach `improvement_score`, Markdown-Tabelle, Restart-Hinweis, Graceful Degradation bei fehlender `VIEW SERVER STATE`).
  2. **Zeile 27** (`📋 15 Progressive Disclosure Schema Tools`): Tool-Count `15` → `16`. **Stilistische Beobachtung:** Der Bullet-Text spricht weiterhin von "Schema Tools" thematisch, obwohl `sql_suggest_indexes` kein reines Schema-Tool ist. Da der bestehende Bullet bereits `sql_compare_queries`, `sql_measure_performance` und `sql_benchmark_optimization` mit-auflistet (was strenggenommen auch keine Schema-Tools sind), ist die Themen-Inkonsistenz im Bullet-Text bereits etabliert — der Planer **erweitert die Liste nur konsistent** und ändert die Tool-Count-Zahl 15 → 16, ohne die Bullet-Überschrift oder die umgebende Beschreibung umzuformulieren. (Eine semantische Korrektur der Überschrift wäre ein eigener Diskussionspunkt, nicht EPIC-02-Scope.)
- **Warum:** SqlToAiRichtlinien §4 + Konzept §DoD.

## Tests

- [ ] Test 1: `SuggestIndexesAsync_DatabaseNameEmpty_ReturnsInvalidParametersError`
- [ ] Test 2: `SuggestIndexesAsync_TopZero_ReturnsInvalidParametersError`
- [ ] Test 3: `SuggestIndexesAsync_MinScoreNegative_ReturnsInvalidParametersError`
- [ ] Test 4: `SuggestIndexesAsync_DatabaseNotInWhitelist_ReturnsSafetyCheckFailedError`
- [ ] Test 5: `SuggestIndexesAsync_DatabaseAccessLevelNone_ReturnsSafetyCheckFailedError`
- [ ] Test 6: `SuggestIndexesAsync_QueryReturnsRows_RendersMarkdownWithScoreAndRestartHint` — verifiziert Header `# Missing Index Recommendations — <Db>`, Restart-Hinweis-Block vorhanden, Tabelle mit Score/Table/Equality/Inequality/Include/Seeks/Scans/LastSeek-Spalten, Score als gerundete Ganzzahl, `LastSeek` als ISO-Datum oder `-`
- [ ] Test 7: `SuggestIndexesAsync_TableNameFilter_PassedAsLikeParameter`
- [ ] Test 8: `SuggestIndexesAsync_TopFilter_PassedAsFetchNextParameter`
- [ ] Test 9: `SuggestIndexesAsync_PermissionDeniedSqlException_ReturnsGracefulDegradationNote` — verifiziert, dass `IsSuccess = true` und `Value` ein Markdown-String mit dem Permission-Hinweis ist (kein `Result.Failure`, da Graceful Degradation)
- [ ] Test 10: `SuggestIndexesAsync_GenericSqlException_ReturnsQueryError` — verifiziert `SQL-AI-0102` Mapping
- [ ] Test 11: `PerformanceMeasurementService_IsPermissionError_RefactoredToHelper_StillRecognizesShowplanError` — Tests, dass `IsPermissionError(ex, 262, "SHOWPLAN")` weiterhin `true` für `Number=262` und für Message-Containing-`SHOWPLAN` zurückgibt (TD-003-Erfüllung, verhindert Regressions am SHOWPLAN-Pfad durch die Refactoring-Konsolidierung)
- [ ] Test 12: `SuggestIndexesArgs_DefaultsAreCorrect` — minimaler Konstruktor-Test: `new IndexSuggestionArgs("FooDb")` hat `TableName = null`, `MinScore = null`, `Top = 10`

Hinweis: **Integrationstest gegen eine echte Test-DB** (Konzept §DoD, letzter Punkt) ist **explizit out of scope** dieses Steps — er kommt in `step-003`. Begründung: DMVs sind ohne echte SQL-Server-Instanz nicht testbar; eine Test-DB-Suite in `dotnet test` würde eine optional konfigurierbare Connection voraussetzen, die der bestehende `SqlServerFixture` für andere Integration-Tests bereits hat, deren Verfügbarkeit aber nicht in jeder Build-Pipeline garantiert ist. Die Unit-Tests in dieser Liste decken die Service-Logik vollständig ab; der Integrationstest validiert primär die SQL-Syntax und das reale DMV-Verhalten gegen einen laufenden Server.

## Definition of Done

- [ ] Alle zehn "Konkrete Änderungen" umgesetzt
- [ ] `dotnet build` aus `roadmap.md` Tech-Stack-Notiz grün (keine neuen Compiler-Warnungen, `TreatWarningsAsErrors=true`)
- [ ] `dotnet test` grün — bestehende Tests bleiben grün, neue Unit-Tests (Tests 1–12) grün
- [ ] `AiNetLinterTests.RecreateBaseline` läuft mit (automatisch, siehe `SqlToAiRichtlinien.mdc` §5) — `SqlToAi-baseline.json` automatisch aktualisiert, kein manueller Eingriff
- [ ] Commit auf Branch `main` (lokal, kein Push), Conventional-Commit-Format, deutsch, imperativ, Subject ≤ 72 Zeichen, Suffix `[sql-index-suggestions]`
- [ ] `step-002/step-result.md` geschrieben mit Geänderte-Dateien-Liste, Commit-Hash, Build/Test-Output, etwaigen Abweichungen vom Plan
- [ ] `status` in `step-plan.md` von `open` auf `done (pending audit)` gesetzt nach Abschluss der Coder-/Kritiker-Schleife

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc` §4 (Zeile 61) — Doku-Sync-Pflicht: `architecture-spec.md` und `README.md` ohne Aufforderung mit-aktualisieren.
- `.agents/rules/SqlToAiRichtlinien.mdc` §5 (Zeile 71–83) — Zero-Warning-Direktive, `TreatWarningsAsErrors`, AiNetLinter-Hinweis (kein manuelles Hash-Rechnen, `RecreateBaseline` läuft automatisch).
- `.agents/rules/AiNetLinter.mdc` Zeile 22 — `MaxMethodParameterCount` = 4, **aber `ToolDispatcher` ist bereits über diesem Limit** (bestehender Status quo, kein EPIC-02-spezifischer Konflikt; der Planer stellt fest, dass eine zukünftige Konsolidierung via `*Args`-Record sinnvoll wäre, das aber außerhalb dieses Steps liegt).
- `.agents/rules/AiNetLinter.mdc` Zeile 27 — `MaxConstructorDependencies` = 5; `IndexSuggestionService` exakt am Limit (5 Dependencies), `ToolDispatcher` bereits darüber.
- `.agents/rules/AiNetLinter.mdc` Zeile 11 — `sealed` für konkrete Klassen; `IndexSuggestionService` muss `sealed` sein (analog aller existierenden Services).
- `.agents/rules/AiNetLinter.mdc` Zeile 12 — `#nullable enable` am Dateianfang jeder neuen `.cs`-Datei.
- `.agents/rules/AiNetLinter.mdc` Zeile 13–14 — kein leeres `catch`; `Log + sichtbarer Fehler` (über `LoggerMessage.Define` + `Result.Failure`/`SqlToAiError.QueryError`).
- `.agents/rules/AiNetLinter.mdc` Zeile 53–55 (`agent-resilience`) — `EnforceNoSilentCatch`, `BanAsyncVoid`, `BanBlockingTaskAccess`.
- `.agents/rules/AiNetLinter.mdc` Zeile 67 — `EnforceAsciiIdentifiers` — keine Umlaute in Bezeichnern (Konvention `DatabaseName`, `MinScore`).
- `.agents/rules/AiNetLinter.mdc` Zeile 58 — `EnforceNamespaceDirectoryMapping` — neue Dateien in `src/SqlToAi/Database/` müssen `namespace SqlToAi.Database;` haben; `Domain/` → `SqlToAi.Domain`.

## Bekannte Ausnahmen

- **ToolDispatcher überschreitet `MaxConstructorParameterCount` (Limit 4, aktuell 8, mit EPIC-02 = 9) und `MaxConstructorDependencies` (Limit 5, mit EPIC-02 weiter überschritten).** Dies ist ein bestehender Status quo, kein EPIC-02-spezifischer Konflikt. Eine zukünftige Konsolidierung (z.B. Services in ein `IDispatcher`-Bundle gruppieren) wäre sinnvoll, aber out of scope. Der Coder soll die existierende Struktur 1:1 erweitern, keine Refactoring-Spielerei.
- **`tool_count` README-Bullet bleibt thematisch "Schema Tools", obwohl `sql_suggest_indexes` kein reines Schema-Tool ist.** Bereits bestehende Inkonsistenz (Bullet zählt auch Performance/Optimization-Tools). Reine Tool-Count-Korrektur 15 → 16 ist im Scope; eine semantische Umbennenung der Bullet-Überschrift ist out of scope.
- **TD-001 (Index-Name-Format-Konflikt) bleibt unberührt** — `sql_suggest_indexes` rendert keine `CREATE INDEX`-DDLs, sondern nur Spalten-Listen als Markdown-Zellen. Der Konflikt zwischen Konzept und `PerformanceMeasurementService.BuildCreateIndexStatement` ist eine separate Doku-Harmonisierungs-Aufgabe.
- **TD-002 (`DESC`-Sortierung in `ColumnGroup`) bleibt unberührt** — `sql_suggest_indexes` greift nicht auf `ColumnGroup` zu; es nutzt `sys.dm_db_missing_index_columns` mit `column_usage`-Werten EQUALITY/INEQUALITY/INCLUDE, die keine Sortierrichtung tragen. Falls TD-002 angegangen wird, ist es ein separater Step im EPIC-01-Kontext.
- **TD-003 (Generalisierung `IsShowplanPermissionError`) wird in diesem Step mit-erledigt** — der generalisierte `IsPermissionError(ex, number, keyword)`-Helper ersetzt die bestehende SHOWPLAN-spezifische Methode. Dies ist nicht nur Refactoring-Aktion, sondern direkter EPIC-02-Scope, weil der `sql_suggest_indexes`-Service eine entsprechende Permission-Erkennung braucht und eine Duplikation vermieden werden soll. Der semantische Test (Test 11) sichert ab, dass die SHOWPLAN-Erkennung identisch bleibt.

## Code-Skizze (optional)

```csharp
// Domain/IndexSuggestionArgs.cs
public sealed record IndexSuggestionArgs(
    string DatabaseName,
    string? TableName = null,
    double? MinScore = null,
    int Top = 10);

// Database/IIndexSuggestionService.cs
public interface IIndexSuggestionService
{
    Task<Result<string>> SuggestIndexesAsync(IndexSuggestionArgs args, CancellationToken ct = default);
    Task<Result<string>> SuggestIndexesAsync(string databaseName, string? tableName = null, double? minScore = null, int? top = null, CancellationToken ct = default);
}

// Database/IndexSuggestionService.cs (Auszug — Kernlogik)
public sealed class IndexSuggestionService : IIndexSuggestionService
{
    private static readonly Action<ILogger, string, Exception?> LogSuggestFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(1, "SuggestFailed"),
            "Failed to load missing-index suggestions for database {Database}.");

    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly ISecurityGuard _securityGuard;
    private readonly IAccessLevelProvider _accessLevelProvider;
    private readonly ILogger<IndexSuggestionService> _logger;

    public IndexSuggestionService(
        IDatabaseConnectionFactory connectionFactory,
        ISecurityGuard securityGuard,
        IAccessLevelProvider accessLevelProvider,
        IOptions<SqlToAiOptions> options,         // reserved for future per-tool options
        ILogger<IndexSuggestionService> logger)
    { /* assignment */ }

    public async Task<Result<string>> SuggestIndexesAsync(IndexSuggestionArgs args, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(args.DatabaseName))
            return SqlToAiError.InvalidParameters("Database name must not be empty.");
        if (args.Top <= 0)
            return SqlToAiError.InvalidParameters("top must be > 0.");
        if (args.MinScore is < 0)
            return SqlToAiError.InvalidParameters("min_score must be >= 0.");

        if (!_securityGuard.IsDatabaseAllowed(args.DatabaseName))
            return SqlToAiError.SafetyCheckFailed($"Database '{args.DatabaseName}' is blocked by security policies (static whitelist).");

        var accessLevel = await _accessLevelProvider.GetAccessLevelAsync(args.DatabaseName, ct);
        if (accessLevel == AccessLevel.None)
            return SqlToAiError.SafetyCheckFailed($"Database '{args.DatabaseName}' access was denied (AccessLevel: None).");

        try
        {
            using var connection = _connectionFactory.CreateConnection(args.DatabaseName);
            await connection.OpenAsync(ct);
            var rows = await LoadSuggestionsAsync(connection, args, ct);
            return RenderMarkdown(rows, args.DatabaseName);
        }
        catch (SqlException ex) when (IsPermissionError(ex, 300, "VIEW SERVER STATE")
                                   || IsPermissionError(ex, 297, "VIEW SERVER STATE"))
        {
            return RenderPermissionNote(args.DatabaseName);
        }
        catch (Exception ex)
        {
            LogSuggestFailed(_logger, args.DatabaseName, ex);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    internal static bool IsPermissionError(SqlException ex, int errorNumber, string keyword) =>
        ex.Number == errorNumber || ex.Message.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    // ... LoadSuggestionsAsync, RenderMarkdown, RenderPermissionNote
}
```

```csharp
// Mcp/McpConstants.cs (Ergänzungen)
internal const string ToolSuggestIndexes   = "sql_suggest_indexes";
internal const string ArgTableName         = "table_name";
internal const string ArgMinScore          = "min_score";
internal const string ArgTop               = "top";

// Mcp/ToolRegistry.cs (Ergänzungen)
private static ToolDefinition BuildSuggestIndexes() => new()
{
    Name = McpConstants.ToolSuggestIndexes,
    Description = "Returns server-wide cumulative missing-index recommendations from sys.dm_db_missing_index_*. "
        + "Each row reports an improvement_score (avg_total_user_cost * avg_user_impact * (user_seeks + user_scans)), "
        + "the table, the equality/inequality/include columns, the seek/scan counts, and the last-seek timestamp. "
        + "Output is a Markdown table prefixed with a restart-reset note (DMV data accumulates since the last server restart). "
        + "Filters: table_name (LIKE substring on the statement column, optional), min_score (optional), top (default 10). "
        + "Degrades gracefully (returns a structured permission note instead of a hard error) if the login lacks VIEW SERVER STATE.",
    InputSchema = new ToolInputSchema
    {
        Properties = new Dictionary<string, ToolParameterDefinition>
        {
            [McpConstants.ArgDatabase]   = StringParam("Target database name. Required."),
            [McpConstants.ArgTableName]  = OptionalStringParam("Optional LIKE filter on the DMV 'statement' column (e.g. 'Orders' or 'dbo.%'). Case-insensitive substring match."),
            [McpConstants.ArgMinScore]   = new() { Type = "number", Description = "Optional minimum improvement_score threshold; rows below it are excluded. Default 0 (no threshold)." },
            [McpConstants.ArgTop]        = new() { Type = "integer", Description = "Maximum number of recommendations to return. Default 10." }
        },
        Required = [McpConstants.ArgDatabase]
    }
};
```

## Notes

- **Warum kein eigener Step für die Tool-Registrierung:** Die Tool-Registrierung in `ToolRegistry` + `ToolDispatcher` + `McpConstants` + `Program.cs` ist rein mechanisch (~15–20 Zeilen zusätzlich, Pattern-konform). Ein eigenständiger Schritt wäre Overhead ohne Review-Mehrwert — die Datei-Änderungen sind trivial, der Service ist die eigentliche Logik. Der Reviewer prüft die Tool-Definition im selben Schritt wie die Service-Logik, da beide aufeinander aufbauen (Input-Shape, Output-Format).
- **Warum Doku-Sync in step-002, nicht step-003:** SqlToAiRichtlinien §4 (Zeile 61) verlangt Doku-Sync ohne Aufforderung mit jeder Code-Änderung. Wenn Doku-Sync auf step-003 verschoben würde, wäre step-002 zwischen code-merge und Doku-Sync kurzzeitig inkonsistent. Der Doku-Sync ist hier rein mechanisch und kostet keine zusätzliche Review-Runde.
- **Warum step-003 = Integrationstest (separat):** Der Integrationstest in `tests/SqlToAi.Tests/Integration/` braucht eine reale SQL-Server-Test-Instanz, die in `SqlServerFixture.cs` bereits vorgesehen ist. Die bestehenden Integration-Tests (z.B. `QueryExecutionServiceIntegrationTests.cs`) zeigen, dass sie als xUnit-Collection mit `DisableParallelization = true` laufen — das verträgt sich nur, wenn eine Test-DB tatsächlich verfügbar ist. Da der Service in step-002 isoliert mit Mocks vollständig prüfbar ist, ist es sicherer, den Integrationstest als eigenen Step zu planen, der bei nicht verfügbarer Test-DB via `Assert.Skip` (analog zu `AiNetLinterTests.RecreateBaseline`-Pattern) übersprungen werden kann, ohne den Service-Code zu gefährden.
- **Out-of-Scope-Bestätigung TD-002:** Falls ein sehr penibler Leser in `step-003` fragt, warum `sys.dm_db_missing_index_columns` keine `DESC`-Richtung trägt: das ist eine Design-Entscheidung von Microsoft — diese DMV hat keine Sortierrichtungs-Information. TD-002 bezieht sich nur auf `PerformanceMeasurementService.BuildCreateIndexStatement` (EPIC-01-Kontext). Kein Konflikt.
- **`IsPermissionError` als `internal static`:** Sichtbarkeit `internal` (nicht `private`) ermöglicht Tests in `IndexSuggestionServiceTests.cs` UND `PerformanceMeasurementServiceTests.cs` (für die TD-3-Regression-Absicherung) ohne Reflection-Hacks. `private` würde Tests via InternalsVisibleTo erfordern, was im Projekt nicht etabliert ist.
- **Coder-Selbstprüfung vor Commit:** Nach Service-Implementation, vor Commit, mindestens manuell `dotnet build && dotnet test` durchführen. AiNetLinter-Baseline passt sich automatisch an; keine zusätzliche Aktion nötig.
