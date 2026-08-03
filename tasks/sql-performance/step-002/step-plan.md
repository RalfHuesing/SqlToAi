---
status: open
type: step-plan
task: sql-performance
step: 002
title: "STATISTICS IO/TIME in sql_execute_query (cpu_time_ms / logical_reads in Execution Info)"
epic: EPIC-02
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: Claude Sonnet 5
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-03T00:00:00+02:00
related_to: []
---

# step-002: STATISTICS IO/TIME in sql_execute_query

## Bezug

- **Task:** `sql-performance`
- **Epic:** `EPIC-02` aus `roadmap.md` — `QueryExecutionService` misst bisher nur den
  Client-Round-Trip (`Stopwatch` → `ElapsedMs`), aber keine server-seitigen Metriken
  (`cpu_time_ms`, `logical_reads`). `sql_measure_performance` hat das bereits (STATISTICS
  IO/TIME), `sql_execute_query` (die alltägliche Abfrage) nicht.
- **Konzept-Referenz:** `konzept.md` Muss-Haben 2 — „`logicalReads`/`cpuMs` in
  `sql_execute_query` Execution Info", inkl. expliziter Entscheidung „Kein Parameter" (STATISTICS
  IO/TIME läuft bei **jedem** `sql_execute_query`-Aufruf mit, kein `include_statistics`-Schalter,
  kein appsettings-Eintrag).

## Aktueller Projektzustand (JIT-Kontext)

**`QueryExecutionService.cs` (aktueller Stand, komplett gelesen):**
- `ExecuteQueryAsync` → `ExecuteQueryInTransactionAsync` (öffnet Connection+Transaction, ruft
  `ExecuteAndSerializeAsync` auf, committet/rollbackt je nach `writeAllowed`/`tranCountChanged`).
- `ExecuteAndSerializeAsync` (L234–270): startet `Stopwatch`, baut `command` aus
  `args.Query`, liest via `reader.ReadAsync` bis `RowLimit`, stoppt `Stopwatch`, baut
  `QueryExecutionResult` an zwei Stellen (Zeile 265 leerer Result, Zeile 268–269 normaler
  Result) — **beide** Stellen müssen die neuen Felder befüllen.
- Kein `InfoMessage`-Handler, kein `SET STATISTICS` irgendwo in dieser Klasse — komplett neu.
- Der `Stopwatch` misst aktuell exakt den Round-Trip der Haupt-Query (Command-Erstellung bis
  Ende des Read-Loops) — dieses Zeitfenster bleibt unverändert; `SET STATISTICS ...`-Befehle
  laufen **davor**, außerhalb des gestoppten Fensters, damit `ElapsedMs` semantisch unverändert
  bleibt (reiner Client-Round-Trip der eigentlichen Query, nicht der Vorbereitung).

**`PerformanceMeasurementService.cs` (Referenzimplementierung, bereits vorhanden, wiederverwenden statt duplizieren):**
- `ExecuteMeasurementAsync` (L146 ff.): `if (connection is SqlConnection sqlConn) { sqlConn.InfoMessage += (_, e) => messages.Add(e.Message); }` — exakt dieses Pattern (Guard auf `SqlConnection`, da Test-Doubles/andere Provider das Event nicht haben) wird 1:1 für `QueryExecutionService` übernommen.
- `ExecuteSetOptionAsync` (L267–273): private, 6-zeiliger Helper, der ein `SET ...`-Statement per `ExecuteNonQueryAsync` auf `connection`/`transaction` ausführt. **Bewusste Entscheidung dieses Plans:** dieser triviale Helper wird lokal in `QueryExecutionService` dupliziert (nicht cross-class wiederverwendet) — die Methode ist zu klein, um eine neue Kopplung zwischen den beiden Service-Klassen zu rechtfertigen; der wertvolle Teil (Regex-Parsing) wird unten stattdessen tatsächlich geteilt.
- `PerformanceMetricsCalculator.cs` (aus step-001, `internal static class`): `ParseRunMessages`
  (aktuell **private** static, L74–98) parst eine `IReadOnlyList<string>` STATISTICS-Messages zu
  `(long Cpu, long Elapsed, long Logical, long Physical, long ReadAhead, bool HasMatch)` via
  `CpuTimeRegex`/`IoReadsRegex`. Dieser Step **wiederverwendet** diese Methode, statt die
  Regex-Logik zu duplizieren — dafür muss sie von `private` auf `internal` angehoben werden
  (gleiches Assembly `SqlToAi`, `InternalsVisibleTo SqlToAi.Tests` bereits vorhanden in
  `SqlToAi.csproj`, also für Tests ohnehin schon direkt aufrufbar). Nur `Cpu`/`Logical` werden
  gebraucht (`Elapsed` liefert bereits der bestehende `Stopwatch`, `Physical`/`ReadAhead` sind
  nicht Teil des Muss-Habens 2 — bewusst nicht mit rausgereicht, um den Scope klein zu halten).

**`QueryExecutionResult.cs` (Domain, komplett gelesen):** reines Daten-Record, **nicht** über
`McpJsonContext` JSON-serialisiert (im Gegensatz zu `PerformanceMetrics`) — `Data` ist bereits ein
fertiger JSON-String, `RowCount`/`ElapsedMs`/etc. werden von `ToolDispatcher` nur zu Text
zusammengebaut. Die zwei neuen Felder brauchen deshalb **keine** `JsonPropertyName`-Attribute.

**`ToolDispatcher.cs` (L146):**
```csharp
string execInfoText = $"Execution Info: {queryResult.RowCount} rows returned in {queryResult.ElapsedMs} ms.";
```
Einzige Stelle, die den Execution-Info-Text baut — hier wird `cpu`/`logical reads` angehängt.

**Sicherheits-Invariante geprüft (wichtig für Review):** Die neuen `SET STATISTICS IO ON`/`SET
STATISTICS TIME ON`-Befehle laufen als eigene `ExecuteNonQueryAsync`-Aufrufe mit fest codiertem
`CommandText`, **nicht** als Teil von `args.Query`/`effectiveQuery` — sie berühren weder
`SqlMultiStatementDetector` noch `IReadOnlyGuard` (beide laufen bereits vorher, ausschließlich auf
dem User-Query-Text) noch `@@TRANCOUNT` (SET-Statements ändern den Tran-Count nicht) — die
bestehende `TransactionIntegrityGuard`-Baseline/Nachher-Prüfung in
`ExecuteQueryInTransactionAsync` bleibt unverändert korrekt.

**Testbarkeit (geprüft an `QueryExecutionServiceMockDb.cs`/`FakeDbCommand.cs`):** Der Test-Double
`FakeDbConnection` ist **keine** `SqlConnection` → der `is SqlConnection`-Guard greift in Tests
nie, `messages` bleibt leer, `CpuTimeMs`/`LogicalReads` bleiben `0` — exakt das gleiche Verhalten
wie bei `PerformanceMeasurementService` (dort existiert aus demselben Grund kein Unit-Test, der
echte STATISTICS-Werte parst; das ist bereits durch `PerformanceMetricsCalculatorTests` isoliert
abgedeckt). `FakeDbCommand.ExecuteNonQuery()` ruft bereits `_handlers.ExecuteNonQuery?.Invoke(this)
?? 0` auf — die neuen `SET STATISTICS ...`-Aufrufe laufen also gegen die Mocks, ohne dass an
`FakeDbCommand` selbst etwas geändert werden muss. Um die neuen `SET`-Aufrufe trotzdem sichtbar zu
machen, wird `MockQueryConnectionFactory`/`FakeDbConnection` um eine kleine Aufzeichnung der
ausgeführten `ExecuteNonQuery`-Commandtexte ergänzt (siehe Tests unten).

## Intention

`QueryExecutionService` liefert nach diesem Step bei **jedem** `sql_execute_query`-Aufruf
zusätzlich `cpu_time_ms` und `logical_reads` (server-seitig via `SET STATISTICS IO/TIME ON`,
gleiche Connection/Transaction, kein Extra-Roundtrip der Query selbst) — sichtbar im
`Execution Info`-Text. Kein neuer Parameter, kein appsettings-Eintrag (Entscheidung aus
`konzept.md` bereits getroffen). Die Regex-Parsing-Logik wird von `PerformanceMetricsCalculator`
wiederverwendet statt dupliziert.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Domain/QueryExecutionResult.cs`

- **Was:** Record um zwei neue Felder erweitern, mit Default `0` (Positionsreihenfolge ans Ende,
  nach `RowCount`, damit bestehende positionale Test-Aufrufe unverändert kompilieren):
  ```csharp
  public sealed record QueryExecutionResult(
      string Data,
      bool WasAnonymized,
      IReadOnlyList<string> AnonymizedColumns,
      string AnonymizationMode,
      IReadOnlyList<string> SearchableTokenColumns = null!,
      long ElapsedMs = 0,
      int RowCount = 0,
      long CpuTimeMs = 0,
      long LogicalReads = 0)
  { ... }
  ```
- **Warum:** Serverseitige Metriken analog zu `PerformanceMetrics.CpuTimeMs`/`LogicalReads`,
  nicht-nullable mit Default `0` (konsistent mit `ElapsedMs`/`RowCount` im selben Record — kein
  Nullable-Bruch nötig, da STATISTICS IO/TIME immer angefordert wird und `0` ein plausibler
  „nichts gemessen"-Default ist, analog zum bereits bestehenden Verhalten bei
  Nicht-`SqlConnection`-Providern).

### Datei 2: `src/SqlToAi/Database/QueryExecutionService.cs`

- **Was:**
  1. Neuer privater static Helper `ExecuteSetOptionAsync(DbConnection, DbTransaction, string, CancellationToken)` (Kopie des Patterns aus `PerformanceMeasurementService.ExecuteSetOptionAsync`, siehe „Aktueller Projektzustand").
  2. In `ExecuteAndSerializeAsync`: **vor** dem `Stopwatch.StartNew()` (damit `ElapsedMs` unverändert bleibt): `InfoMessage`-Handler registrieren (`if (args.Connection is SqlConnection sqlConn) { var messages = new List<string>(); sqlConn.InfoMessage += (_, e) => messages.Add(e.Message); }`), dann `await ExecuteSetOptionAsync(args.Connection, args.Transaction, "SET STATISTICS IO ON", ct)` und `await ExecuteSetOptionAsync(args.Connection, args.Transaction, "SET STATISTICS TIME ON", ct)`.
  3. Nach dem Read-Loop (nach `stopwatch.Stop()`, vor den zwei `return new QueryExecutionResult(...)`-Stellen): `var (cpu, _, logical, _, _, _) = PerformanceMetricsCalculator.ParseRunMessages(messages);` (nur ausgeführt, wenn `messages`-Liste tatsächlich existiert, sonst `cpu`/`logical` bleiben `0`) und beide `QueryExecutionResult`-Konstruktionsstellen (leer + normal) um `CpuTimeMs: cpu, LogicalReads: logical` ergänzen.
  4. **Achtung Linter (`MaxMethodParameterCount` 4 / `MaxBoolParameterCount` 1):** `ExecuteAndSerializeAsync` nimmt bereits `ExecutionArgs args` als Parameter-Object — keine neuen losen Parameter nötig, die neuen lokalen Variablen (`messages`, `cpu`, `logical`) bleiben methodenintern. Falls die Methode dadurch über 60 Zeilen wächst: das Message-Parsing (Schritt 3) in eine private Hilfsmethode `(long Cpu, long Logical) ExtractServerMetrics(List<string> messages)` auslagern.
- **Warum:** Reine Ergänzung, keine bestehende Logik (Security-Guards, Anonymisierung,
  Transaction-Handling) wird angefasst — die neuen Aufrufe sitzen ausschließlich innerhalb von
  `ExecuteAndSerializeAsync`.

### Datei 3: `src/SqlToAi/Database/PerformanceMetricsCalculator.cs`

- **Was:** `ParseRunMessages` von `private static` auf `internal static` anheben (Signatur/Body
  unverändert).
- **Warum:** Wiederverwendung der Regex-Parsing-Logik durch `QueryExecutionService`, statt
  `CpuTimeRegex`/`IoReadsRegex` ein zweites Mal zu definieren (DRY — beide Klassen liegen im
  selben Namespace `SqlToAi.Database`).

### Datei 4: `src/SqlToAi/Mcp/ToolDispatcher.cs` (Zeile 146)

- **Was:**
  ```csharp
  string execInfoText = $"Execution Info: {queryResult.RowCount} rows returned in {queryResult.ElapsedMs} ms | cpu: {queryResult.CpuTimeMs} ms | logical reads: {queryResult.LogicalReads}.";
  ```
- **Warum:** Sichtbarmachung der neuen Metriken für den Agenten, exakt im in `konzept.md`
  skizzierten Format (`X rows returned in Y ms | cpu: Z ms | logical reads: W`).

## Tests

- [ ] `QueryExecutionResultTests` (oder passender bestehender Ort): neue Felder `CpuTimeMs`/`LogicalReads` defaulten auf `0` bei rein positionaler Konstruktion (Rückwärtskompatibilität bestehender Test-Aufrufe wie in `ToolDispatcherTests.cs:356/360` prüfen — diese dürfen ohne Änderung weiter kompilieren).
- [ ] `QueryExecutionServiceTests`: `MockQueryConnectionFactory`/`FakeDbConnection` um eine `List<string> ExecutedNonQueryCommands` (oder gleichwertig) ergänzen, die jeden über `FakeDbCommandHandlers.ExecuteNonQuery` laufenden `CommandText` aufzeichnet; neuer Test `ExecuteQueryAsync_ShouldIssueSetStatisticsCommands_BeforeMainQuery` prüft, dass `"SET STATISTICS IO ON"` und `"SET STATISTICS TIME ON"` ausgeführt wurden (Reihenfolge egal, beide vorhanden) und dass `result.Value.CpuTimeMs == 0`/`LogicalReads == 0` bleibt (da Fake keine `SqlConnection` ist — dokumentiert bewusst die Guard-Grenze, siehe „Aktueller Projektzustand").
- [ ] `ToolDispatcherTests`: bestehenden `Execution Info:`-Test (L217/237) um eine Variante mit `CpuTimeMs`/`LogicalReads` ungleich `0` erweitern (z. B. neuer Test `ExecuteQuery_ShouldIncludeCpuAndLogicalReads_InExecutionInfoText`, `QueryExecutionResult(..., CpuTimeMs: 12, LogicalReads: 34)` → Assert `Contains("cpu: 12 ms | logical reads: 34.")`).
- [ ] `PerformanceMetricsCalculatorTests`: keine Änderung nötig (Verhalten von `ParseRunMessages` unverändert, nur Sichtbarkeit geändert) — optional ein direkter Test `ParseRunMessages_ShouldExtractCpuAndLogicalReads_FromSingleRunMessages`, falls der Kritiker mehr Abdeckung auf der jetzt internal sichtbaren Methode selbst sehen will (nicht zwingend, da `Compute`-Tests dieselbe Methode bereits transitiv abdecken).

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] `dotnet build` — 0 Warnings, 0 Errors
- [ ] `dotnet test` — alle Tests grün (inkl. `AiNetLinterTests.RecreateBaseline`)
- [ ] Commit auf aktuellem Branch (Conventional Commit, `[sql-performance]`-Suffix)
- [ ] `step-002/step-result.md` geschrieben
- [ ] `status` in diesem `step-plan.md` von `open` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/AiNetLinter.mdc` — `MaxMethodParameterCount` (4, bereits durch `ExecutionArgs`-Record abgefangen), `MaxMethodLineCount` (60, ggf. `ExtractServerMetrics`-Extraktion nötig), `MaxBoolParameterCount` (1, hier nicht betroffen — keine neuen bool-Parameter), `EnforceNullableEnable`/`sealed` (unverändert einzuhalten in allen vier Dateien).
- `.agents/rules/SqlToAiRichtlinien.mdc` — §4 „Keine hartkodierten Werte": betrifft hier **nicht** (keine neue Konfigurationsoption, `konzept.md` hat explizit „kein Parameter, kein appsettings-Schalter" entschieden); §5 Zero-Warning + Baseline-Update automatisch über `RecreateBaseline`-Test.

## Bekannte Ausnahmen

- Keine.

## Code-Skizze (optional)

```csharp
// QueryExecutionService.ExecuteAndSerializeAsync — Ausschnitt
var messages = new List<string>();
if (args.Connection is SqlConnection sqlConn)
{
    sqlConn.InfoMessage += (_, e) => messages.Add(e.Message);
}
await ExecuteSetOptionAsync(args.Connection, args.Transaction, "SET STATISTICS IO ON", cancellationToken);
await ExecuteSetOptionAsync(args.Connection, args.Transaction, "SET STATISTICS TIME ON", cancellationToken);

var stopwatch = System.Diagnostics.Stopwatch.StartNew();
// ... bestehender Command-Aufbau + Read-Loop unverändert ...
stopwatch.Stop();

var (cpu, _, logical, _, _, _) = PerformanceMetricsCalculator.ParseRunMessages(messages);

// beide QueryExecutionResult(...)-Konstruktionen um CpuTimeMs: cpu, LogicalReads: logical ergänzen
```

## Notes

- **Kein `SET STATISTICS ... OFF`:** analog zu `PerformanceMeasurementService` (dort wird IO/TIME
  ebenfalls nie explizit ausgeschaltet) — die Connection wird pro Aufruf frisch erstellt und nach
  dem `using` disposed, es gibt kein Connection-Pooling-Leck der Session-Option über diesen aufruf
  hinaus, das hier relevant wäre.
- **Keine SHOWPLAN-artige Graceful-Degradation nötig:** `SET STATISTICS IO/TIME ON` erfordert
  (anders als `SET STATISTICS XML ON`) keine besondere Server-Berechtigung — ein Fehlschlag dort
  würde ohnehin durch das bestehende äußere `try/catch` in `ExecuteQueryInTransactionAsync`
  abgefangen (Rollback + `SqlToAiErrorMapper.MapException`), kein zusätzlicher Sonderfall in
  diesem Step nötig/geplant.
- **Nicht in diesem Step:** EPIC-03 (ToolRegistry Descriptions) und EPIC-04 (Dokumentation) —
  beide folgen als eigene Steps, sobald EPIC-02 abgeschlossen ist. `mcp-specification.md`/`README.md`
  werden hier **nicht** angefasst, obwohl `SqlToAiRichtlinien.mdc` §4 grundsätzlich
  Doku-Synchronisation bei jeder Feature-Änderung verlangt — das ist bewusst EPIC-04 vorbehalten
  (siehe Roadmap-Reihenfolge), nicht vergessen im nächsten Step-Modus-Aufruf.
