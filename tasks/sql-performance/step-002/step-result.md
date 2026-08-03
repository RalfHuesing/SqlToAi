---
status: done
type: step-result
task: sql-performance
step: 002
epic: EPIC-02
step_type: single
coded_by: coder
coded_by_model: Claude Sonnet 5
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-03T00:00:00+02:00
code_commit_hash: 3c63f72
status_after: done
blocker_category: n/a
---

# Result Step 002: STATISTICS IO/TIME in sql_execute_query

## Zusammenfassung

`QueryExecutionService.ExecuteAndSerializeAsync` führt jetzt vor jeder Query `SET STATISTICS IO
ON`/`SET STATISTICS TIME ON` aus, fängt `SqlConnection.InfoMessage` ab und parst die Messages über
das (jetzt `internal`) `PerformanceMetricsCalculator.ParseRunMessages`. `QueryExecutionResult` trägt
neu `CpuTimeMs`/`LogicalReads` (Default `0`), `ToolDispatcher` hängt beide Werte an den
`Execution Info`-Text an. Kein neuer Parameter, kein appsettings-Eintrag (Konzept-Entscheidung
umgesetzt wie geplant).

## Geänderte Dateien

- `src/SqlToAi/Domain/QueryExecutionResult.cs` — zwei neue Felder `CpuTimeMs`/`LogicalReads` (Default `0`, ans Ende positioniert).
- `src/SqlToAi/Database/PerformanceMetricsCalculator.cs` — `ParseRunMessages` `private` → `internal` (Signatur/Body unverändert).
- `src/SqlToAi/Database/QueryExecutionService.cs` — `ExecuteAndSerializeAsync` registriert `InfoMessage`-Handler und führt `SET STATISTICS IO/TIME ON` vor dem `Stopwatch.StartNew()` aus; neuer privater Helper `ExecuteSetOptionAsync` (Kopie des Patterns aus `PerformanceMeasurementService`); beide `QueryExecutionResult`-Konstruktionsstellen befüllen `CpuTimeMs`/`LogicalReads`.
- `src/SqlToAi/Database/QueryExecutionService.Anonymization.cs` (neu) — zweite Partial-Class-Datei; enthält die komplette Anonymization-/Row-Serialisierungs-Hälfte (`RowAnonymizationTracker`, `ColumnOrigin`, `AnonymizationContext`, `ResolveAnonymizationContextAsync`, `ResolveCentralExclusionsAsync`, `AppendSerializedRow`, `AnonymizeCell`, `GetColumnOrigins`, `PopulateColumnOrigins`, `ReadOriginValue`, `GetColumnNames`) 1:1 verschoben, keine Verhaltensänderung.
- `src/SqlToAi/Mcp/ToolDispatcher.cs` — `Execution Info`-Text um ` | cpu: {CpuTimeMs} ms | logical reads: {LogicalReads}.` erweitert.
- `tests/SqlToAi.Tests/Database/QueryExecutionServiceMockDb.cs` — `MockQueryConnectionFactory` bekommt `ExecutedNonQueryCommands`-Liste + `ExecuteNonQuery`-Handler, der jeden `CommandText` aufzeichnet.
- `tests/SqlToAi.Tests/Database/QueryExecutionServiceTests.cs` (erweitert) — neuer Test `ExecuteQueryAsync_ShouldIssueSetStatisticsCommands_BeforeMainQuery` prüft beide `SET STATISTICS ...`-Commands und dass `CpuTimeMs`/`LogicalReads` bei der Fake-Connection `0` bleiben.
- `tests/SqlToAi.Tests/Mcp/ToolDispatcherTests.cs` (erweitert) — `FakeQueryExecutionService` nimmt `cpuTimeMs`/`logicalReads` entgegen; neuer Test `ExecuteQuery_ShouldIncludeCpuAndLogicalReads_InExecutionInfoText` prüft den erweiterten Execution-Info-Text.
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` — automatisch von `AiNetLinterTests.RecreateBaseline` neu geschrieben.

## Commit

- **Code-Commit-Hash:** `3c63f72`
- **Message:**
  ```
  feat(performance): STATISTICS IO/TIME in sql_execute_query [sql-performance]

  sql_execute_query liefert nun serverseitige cpu_time_ms/logical_reads
  (SET STATISTICS IO/TIME, kein Parameter, kein Extra-Roundtrip) im
  Execution-Info-Text, analog zu sql_measure_performance. Regex-Parsing
  wird von PerformanceMetricsCalculator wiederverwendet statt dupliziert.
  QueryExecutionService in zwei Partial-Dateien gesplittet (MaxLineCount).

  Refs: tasks/sql-performance/step-002
  ```
- **Branch:** main
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit (Hash steht nicht hier drin — siehe `git log`).

## Build-/Test-Output

```
dotnet build → grün (0 Warnings, 0 Errors)
dotnet test  → grün (486 Tests, 0 Fehler, inkl. AiNetLinterTests.RecreateBaseline)
```

## Abweichungen vom Plan

- **Datei in zwei Partial-Class-Dateien gesplittet — vom Plan nicht vorhergesehen.** Die Ergänzung
  von `SET STATISTICS ...` + `ExecuteSetOptionAsync` + den beiden neuen Feldern brachte
  `QueryExecutionService.cs` von 480 auf 506 Zeilen — über dem `MaxLineCount`-Limit (500) aus
  `AiNetLinter.mdc`. Das war im Step-Plan nicht erwähnt (nur `MaxMethodLineCount` wurde als
  Risiko genannt) und hätte `RunLinterShouldBeCleanOrBaselineMatch` als neue, nicht gebaselinte
  Violation rot laufen lassen. Fix: komplette Anonymization-/Row-Serialisierungs-Logik (11
  private Member, ~210 Zeilen) 1:1 in eine zweite Partial-Class-Datei
  `QueryExecutionService.Anonymization.cs` verschoben — reine mechanische Extraktion ohne
  Verhaltensänderung, `MaxPartialClassFiles`-Limit (2) bleibt eingehalten. Hauptdatei jetzt 297
  Zeilen.

Ansonsten Plan 1:1 umgesetzt (inkl. Test-Ansatz für `ExecutedNonQueryCommands`, wie im Plan unter
„Testbarkeit" beschrieben).

## Beobachtungen

- Der `dotnet test`-Lauf zeigte vor meiner Änderung bereits 4 weitere, vorbestehende
  AiNetLinter-Violations, die aber bereits in der Baseline stehen und den Testlauf nicht rot
  machen (`PerformanceMeasurementService.ExecuteWarmupRunsAsync`/`ExecuteMeasuredRunsAsync`:
  `MaxMethodParameterCount`; `ToolDispatcher`-Konstruktor: `MaxConstructorDependencies`;
  `GlobMatcherTests`: `AvoidExcessiveMiddleMen`) — nicht von diesem Step verursacht, nicht
  angefasst.
- `PerformanceMeasurementService.ExecuteSetOptionAsync` und der neue,
  lokal duplizierte Helfer in `QueryExecutionService` sind inhaltlich identisch (6 Zeilen) — vom
  Plan bewusst so vorgesehen (siehe „Aktueller Projektzustand" im Step-Plan), nicht mein eigener
  Entscheid.

## Bekannte Unschärfen

- Keine über das im Step-Plan unter „Testbarkeit" bereits Dokumentierte hinaus: `CpuTimeMs`/
  `LogicalReads` bleiben in allen Unit-Tests `0`, da `FakeDbConnection` keine `SqlConnection` ist —
  echtes Parsing von STATISTICS-Messages über eine echte `SqlConnection` ist durch dieses
  Test-Setup nicht abgedeckt (analog zu `PerformanceMeasurementService`, dort ebenfalls nicht
  abgedeckt).
