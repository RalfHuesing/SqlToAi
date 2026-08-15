---
status: done (pending audit)
type: step-plan
task: audit-try-magicvalues
step: 003
corrects: null
title: "EPIC-03 Test-Suite-Konsolidierung (DRY-T1, DRY-T2, DRY-T3, MV-T1, batch)"
epic: EPIC-03
estimated_risk: medium
step_type: batch
items:
  - id: item-01
    title: "QuerySafetyValidatorTests einführen und 31 Negativ-Guardrail-Tests aus 4 Service-Testklassen konsolidieren (DRY-T3)"
    source: "audit-dry-magicvalues.md#DRY-T3"
  - id: item-02
    title: "ShowPlanTestHelper mit Builder-Methoden einführen und 8 ShowPlan-XML-Blöcke in PerformanceMeasurementServiceTests reduzieren (DRY-T2)"
    source: "audit-dry-magicvalues.md#DRY-T2"
  - id: item-03
    title: "Fakes nach TestSupport/ konsolidieren: GetDayDir in McpTrailTestHelper, Legacy-Fakes (FakeSecurityGuard/FakeAccessLevelProvider/FakeReadOnlyGuard) verschieben (DRY-T1)"
    source: "audit-dry-magicvalues.md#DRY-T1"
  - id: item-04
    title: "MV-T1: Hardkodierte JSON-RPC-Error-Code-Konstante in McpModelsTests auf benannte JsonRpcError-Konstante umstellen"
    source: "audit-dry-magicvalues.md#MV-T1"
created_by: planer
created_by_model: MiniMax-M3
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-15T22:38:00+02:00
related_to: []
---

# Step 003: EPIC-03 Test-Suite-Konsolidierung (DRY-T1, DRY-T2, DRY-T3, MV-T1, batch)

## Bezug

- **Task:** `audit-try-magicvalues`
- **Epic:** `EPIC-03` aus `roadmap.md` — *Test-Suite-Konsolidierung (Phase 3, DRY-T1..T3)*. Letzter offener Epic in diesem Task. Konsolidiert die Test-Duplikation, die im Audit als DRY-T1 (verstreute Fakes/`GetDayDir()`-Duplikat), DRY-T2 (8 duplizierte ShowPlan-XML-Blöcke in `PerformanceMeasurementServiceTests`) und DRY-T3 (33 identische Negativ-Guardrail-Tests in 5 Service-Testklassen) identifiziert wurde, plus die MV-T1-Nebenkleinigkeit (hardkodierter JSON-RPC-Error-Code in einem Test).
- **Konzept-Referenz:** `konzept.md` §"Muss-Haven" Pkt. 3 (Phase 3, Test-Suite-Bereinigung), im Detail belegt durch `audit-dry-magicvalues.md` Abschnitt 4 (DRY-T1:300-307, DRY-T2:310-318, DRY-T3:321-327, MV-T1:53). Die Migration der Service-Tests auf den neuen `FakeQuerySafetyValidator` ist bereits in step-002 erledigt (siehe `step-002/step-result.md` §"Beobachtungen": die vier Service-Tests wurden auf den neuen Fake umgestellt, die Pipeline-Semantik wird heute aber weiterhin indirekt über die alte dreifach-Fake-Kombination `FakeSecurityGuard`+`FakeAccessLevelProvider`+`FakeReadOnlyGuard` gefahren — der Coder kommentiert dies in step-002 selbst: „EPIC-03 / DRY-T1 wird sie in einen gemeinsamen TestSupport-Helper bündeln").
- **Risiko:** `medium` — reine Testseite, keine Produktionsänderung. Die Testanzahl darf sich **nicht** verringern (Konsolidierung heißt Umbau, nicht Löschen — Tests wandern aus fünf Service-Testklassen in eine `QuerySafetyValidatorTests.cs` und werden dort über `[Theory]`/`[InlineData]` verdichtet; die 33 alten Method-Cases bleiben als Test-Cases erhalten).

## Aktueller Projektzustand (JIT-Kontext)

Beim Lesen des Bestandscodes (siehe `step-002/step-result.md` für den Vorzustand nach step-002) habe ich folgende Strukturen vorgefunden, die den Plan beeinflussen:

### Konkrete 31 Negativ-Guardrail-Tests in den 4 Service-Testklassen (DRY-T3)

Die Auditzahl „33" bezieht sich auf eine grobe Schätzung über 5 Klassen inkl. `IndexSuggestionServiceTests` (das ist nicht auf die Pipeline migriert und hat nur 2 Pipeline-relevante Negativ-Tests). Die exakte Zählung der **Pipeline-bezogenen** Negativ-Tests in den 4 Klassen, die step-002 auf den `FakeQuerySafetyValidator` umgestellt hat, ergibt **31 individual test cases** (Method + Theory-InlineData-Expansion):

- **`tests/SqlToAi.Tests/Database/QueryExecutionServiceTests.cs`** (Zeilen 75-140) — 9 Cases:
  - `ShouldFail_WhenDatabaseNameIsEmpty` (1)
  - `ShouldFail_WhenQueryIsEmpty` (1)
  - `ShouldFail_WhenDatabaseNotAllowed` (1)
  - `ShouldFail_WhenAccessLevelTooLow` [Theory: 2 InlineData: None, SchemaOnly] (2)
  - `ShouldFail_WhenQueryIsMutating` (1)
  - `ShouldFail_WhenMultipleStatements` [Theory: 3 InlineData] (3)
- **`tests/SqlToAi.Tests/Database/QueryValidationServiceTests.cs`** (Zeilen 62-180) — 10 Cases:
  - `ShouldFail_WhenDatabaseNameIsEmpty` (1)
  - `ShouldFail_WhenQueryIsEmpty` (1)
  - `ShouldFail_WhenDatabaseNotAllowed` (1)
  - `ShouldFail_WhenAccessLevelIsNone` (1)
  - `ShouldFail_WhenQueryIsMutating_AndAccessLevelIsNotReadWrite` (1)
  - `ShouldReject_SpExecuteSql_BeforeTouchingDatabase` [Theory: 3 InlineData: `sp_executesql N'…'`, `EXEC sp_executesql N'…'`, `sys.sp_executesql N'…'`] (3)
  - `ShouldFail_WhenMultipleStatements_RegardlessOfAccessLevel` [Theory: 2 InlineData: ReadOnly, ReadWrite] (2)
- **`tests/SqlToAi.Tests/Database/PerformanceMeasurementServiceTests.cs`** (Zeilen 40-98) — 6 Cases:
  - `MeasurePerformanceAsync_EmptyDatabase_ReturnsInvalidParameters` (1)
  - `MeasurePerformanceAsync_EmptyQuery_ReturnsInvalidParameters` (1)
  - `MeasurePerformanceAsync_DatabaseNotAllowed_ReturnsSafetyCheckFailed` (1)
  - `MeasurePerformanceAsync_AccessLevelNone_ReturnsWriteOperationBlocked` (1)
  - `MeasurePerformanceAsync_MutatingQuery_ReturnsWriteOperationBlocked` (1)
  - `MeasurePerformanceAsync_MultiStatement_ReturnsMultipleStatementsForbidden` (1)
- **`tests/SqlToAi.Tests/Database/QueryComparisonServiceTests.cs`** (Zeilen 43-101) — 6 Cases:
  - `CompareQueriesAsync_EmptyDatabase_ReturnsInvalidParameters` (1)
  - `CompareQueriesAsync_EmptyQueries_ReturnsInvalidParameters` (1)
  - `CompareQueriesAsync_DatabaseNotAllowed_ReturnsSafetyCheckFailed` (1)
  - `CompareQueriesAsync_AccessLevelNone_ReturnsWriteOperationBlocked` (1)
  - `CompareQueriesAsync_MutatingQuery_ReturnsWriteOperationBlocked` (1)
  - `CompareQueriesAsync_MultiStatement_ReturnsMultipleStatementsForbidden` (1)
- **Plus die `IndexSuggestionServiceTests`-Pipeline-relevanten 2 Cases** (Zeilen 98-115) — `SuggestIndexesAsync_DatabaseNotInWhitelist_ReturnsSafetyCheckFailedError` und `SuggestIndexesAsync_DatabaseAccessLevelNone_ReturnsSafetyCheckFailedError`. Diese sind *nicht* über die `IQuerySafetyValidator`-Pipeline gefahren (IndexSuggestionService nutzt `ISecurityGuard`+`IAccessLevelProvider` direkt), gehören also nicht in `QuerySafetyValidatorTests`. **Bleiben unverändert** — werden nicht migriert.

**Konsolidierungsplan:** alle 31 Pipeline-Cases wandern 1:1 in eine neue `QuerySafetyValidatorTests.cs`. Die Cases werden über `[Theory]`/`[InlineData]` parametrisiert, wo sinnvoll (z. B. `EmptyDatabaseName` mit InlineData `""`/`"   "`). Die Assertions sind heute alle `Assert.Equal(SqlToAiError.XxxCode, result.Error.Code)` — diese bleiben wörtlich erhalten. **31 Tests in → 31 Tests out**, aber in einer einzigen Klasse, ohne Service-Bootstrap-Overhead (`BuildService`/`BuildSafetyValidator`).

### 8 duplizierte ShowPlan-XML-Blöcke (DRY-T2)

In `tests/SqlToAi.Tests/Database/PerformanceMeasurementServiceTests.cs` (Zeilen 100-414) hat jeder der 8 `ParseExecutionPlanXml_*`-Tests einen 20-30-Zeilen XML-Block als String-Literal im C# 11 raw-string-literal (`"""…"""`). Die 8 Blöcke sind strukturidentisch (`<ShowPlanXML>` → `<BatchSequence>` → `<Batch>` → `<Statements>` → `<StmtSimple>` → `<QueryPlan>` → `<MissingIndexes>` → `<MissingIndexGroup>` → `<MissingIndex>` → `<ColumnGroup>` × N); nur die inneren `<Column Name="…">`- und `<ColumnGroup Usage="…">`-Elemente variieren. Zusammen ergeben die 8 Blöcke ca. 200 Zeilen XML.

**Konsolidierungsplan:** ein `ShowPlanTestHelper` (Klasse oder static record) in `tests/SqlToAi.Tests/TestSupport/ShowPlanTestHelper.cs` mit einer einzelnen `BuildShowPlanXml(...)`-Methode (oder 2-3 Builder-Methoden, die per `StringBuilder` ein ShowPlan-XML aufbauen). Die Tests rufen `ShowPlanTestHelper.BuildShowPlanXml(impact: 85.4, …)` auf und übergeben die Parameter; der Helper liefert den XML-String. Die Assertions pro Test (`Assert.Equal(3, warnings.Count)`, `Assert.Contains("CREATE NONCLUSTERED INDEX", missing.MissingIndexStatement)`, …) bleiben unverändert.

### `GetDayDir()`-Duplikat in `McpTrailWriterTests` und `McpTrailWriterRedactionTests` (DRY-T1)

Beide Testklassen (`tests/SqlToAi.Tests/Mcp/McpTrailWriterTests.cs:32-33` und `tests/SqlToAi.Tests/Mcp/McpTrailWriterRedactionTests.cs:34-35`) haben die wortgleiche private Methode:

```csharp
private string GetDayDir() =>
    Path.Combine(_logRoot, "mcp", DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
```

Beide Klassen verwalten zusätzlich ein eigenes `_logRoot` (`Path.Combine(Path.GetTempPath(), "SqlToAiMcpTrailTests_" + Guid.NewGuid().ToString("N"))` bzw. `…McpTrailRedactionTests_…`) und haben einen eigenen `IDisposable` für `Directory.Delete(_logRoot, recursive: true)`. Der bestehende `tests/SqlToAi.Tests/TestSupport/McpTrailTestHelper.cs` (Zeilen 10-36) enthält bereits eine `CreateWriter(...)`-Factory, die ein `McpTrailWriter`-Setup mit dem `_logRoot` als Parameter baut.

**Konsolidierungsplan:** in `McpTrailTestHelper.cs` zwei Methoden ergänzen:
- `CreateIsolatedLogRoot(suffix: string)` → liefert einen frischen `string`-Pfad unter `%TEMP%` (ersetzt die `Path.Combine`-Zeilen in beiden Test-Konstruktoren).
- `GetDayDir(logRoot: string)` → liefert den Day-Directory-Pfad (ersetzt beide `GetDayDir()`-Methoden).
- Alternativ (saubere, kombinierte Lösung): die Tests bekommen je einen `McpTrailTestHelper.IsolatedLogRoot`-Wrapper (private sealed-Klasse, `IDisposable`), der `CreateIsolatedLogRoot`+`GetDayDir`+`Dispose` bündelt — damit verschwindet der `IDisposable`-Boilerplate aus beiden Tests.

### Legacy-Fakes (`FakeSecurityGuard`/`FakeAccessLevelProvider`/`FakeReadOnlyGuard`) in `QueryExecutionServiceMockDb.cs`

Diese drei Fakes stehen heute in `tests/SqlToAi.Tests/Database/QueryExecutionServiceMockDb.cs:54-68`. Konsumenten heute:
- `tests/SqlToAi.Tests/Database/IndexSuggestionServiceTests.cs:41-42, 51-53` — verwendet `FakeSecurityGuard` + `FakeAccessLevelProvider` (kein `FakeReadOnlyGuard`).
- Die 4 Pipeline-Service-Tests (`QueryExecutionServiceTests`, `QueryValidationServiceTests`, `PerformanceMeasurementServiceTests`, `QueryComparisonServiceTests`) — alle drei Fakes, vermittelt über den `FakeQuerySafetyValidator`-Delegations-Konstruktor.
- `tests/SqlToAi.Tests/Integration/SqlServerFixture.cs` und andere Integration-Tests — gemäß step-002 §"Zusatz-Migrationen".

**Konsolidierungsplan:** die drei Fakes wandern in eine neue Datei `tests/SqlToAi.Tests/TestSupport/LegacySecurityFakes.cs` (Namespace `SqlToAi.Tests.TestSupport`, `internal sealed class` × 3). `QueryExecutionServiceMockDb.cs` verliert die drei Klassen und fügt stattdessen `using SqlToAi.Tests.TestSupport;` hinzu (oder es wird ein global using genutzt). Der `FakeQuerySafetyValidator` bleibt in `QueryExecutionServiceMockDb.cs`, weil er als Test-Doppel für `IQuerySafetyValidator` dienst-spezifisch ist (er braucht Zugriff auf den `QuerySafetyValidator`-Konstruktor und damit auf die Service-Assembly).

**Begründung warum diese Trennung sinnvoll ist:** `FakeQuerySafetyValidator` ist **kein** Legacy-Fake — er ist die zukünftige Single Source of Truth für Pipeline-Tests. Die drei Security-Interfaces (`ISecurityGuard`/`IAccessLevelProvider`/`IReadOnlyGuard`) werden heute nur noch über zwei Pfade konsumiert: (a) `QuerySafetyValidator` (intern) und (b) `IndexSuggestionService` (separater Pfad, bleibt). Die Fakes sind also reine Test-Hilfsklassen, die nirgendwo anders leben müssen als in `TestSupport/`.

### MV-T1: Hardkodierter `"-32601"` in `McpModelsTests.cs:96`

Der Test `JsonRpcErrorResponse_ShouldCarryErrorCodeAndMessage` assertiert den JSON-serialisierten Output mit `Assert.Contains("-32601", json)`. Die Konstante existiert bereits produktionsseitig als `JsonRpcError.MethodNotFound` in `src/SqlToAi/Mcp/JsonRpcModels.cs:73` (definiert als `public const int MethodNotFound = -32601;`). Es gibt nur **diese eine** Stelle im Test-Tree (per `grep -r '\-32601' tests` verifiziert); `"-32700"` (`JsonRpcError.ParseError`) wird in den Tests **nicht** direkt referenziert.

**Konsolidierungsplan:** ersetzen `Assert.Contains("-32601", json)` durch `Assert.Contains(JsonRpcError.MethodNotFound.ToString(CultureInfo.InvariantCulture), json)`. Damit ist die Konstante zentral und der Test ist gegen eine künftige Versions-Änderung von `MethodNotFound` immun.

## Intention

Nach diesem Step existiert die 6-stufige Guardrail-Validierung in genau **einer** Test-Klasse (`QuerySafetyValidatorTests`) als zentrale Test-Quelle — die 31 Pipeline-Negativ-Cases sind aus den vier Service-Testklassen herausgelöst und dort durch keine Pipeline-Tests mehr ersetzt (die Service-Tests konzentrieren sich auf Service-spezifische Aspekte: Transaktion, Anonymisierung, Commit/Rollback, Param-Routing, Command-Timeout, etc.). Der `ShowPlanTestHelper` ersetzt die 8 ShowPlan-XML-Rohblöcke durch parametrisierte Builder-Aufrufe und reduziert die XML-Wartungslast auf die Stellen, die wirklich variieren (Tabellennamen, Spaltennamen, Impact-Werte). Die `GetDayDir()`-Duplikation ist durch `McpTrailTestHelper` aufgelöst, und die drei Legacy-Security-Fakes sind in `TestSupport/` umgesiedelt. `McpModelsTests` referenziert die `JsonRpcError.MethodNotFound`-Konstante statt der hardkodierten `-32601`.

## Konkrete Änderungen

### item-01: `QuerySafetyValidatorTests` einführen und 31 Negativ-Guardrail-Tests konsolidieren (DRY-T3) — neue Datei + 4 Testdateien abspecken

- **Was neue Datei `tests/SqlToAi.Tests/Database/QuerySafetyValidatorTests.cs`:**
  - Konstruktor: nimmt drei Fakes entgegen (`FakeSecurityGuard` + `FakeAccessLevelProvider` + echter `ReadOnlyGuard` — letzterer ist heute schon die Konvention in `QueryValidationServiceTests` und `PerformanceMeasurementServiceTests`); baut daraus eine `QuerySafetyValidator`-Instanz.
  - `[Theory]` `ValidateQuerySafetyAsync_EmptyDatabaseName_ReturnsInvalidParameters` mit `[InlineData("")]` und `[InlineData("   ")]` und `[InlineData(null!)]` (Test des `string.IsNullOrWhiteSpace`-Pfads).
  - `[Theory]` `ValidateQuerySafetyAsync_EmptyQuery_ReturnsInvalidParameters` mit `[InlineData("")]` und `[InlineData("   ")]`.
  - `[Fact]` `ValidateQuerySafetyAsync_DatabaseNotAllowed_ReturnsSafetyCheckFailed` (Whitelist-Pfad).
  - `[Theory]` `ValidateQuerySafetyAsync_AccessLevelNone_ReturnsWriteOperationBlocked` (`[InlineData(AccessLevel.None)]`).
  - `[Theory]` `ValidateQuerySafetyAsync_AccessLevelSchemaOnly_WithoutFlag_ReturnsWriteOperationBlocked` (`[InlineData(AccessLevel.SchemaOnly)]` mit `allowSchemaOnly: false`).
  - `[Theory]` `ValidateQuerySafetyAsync_AccessLevelSchemaOnly_WithFlag_ReturnsSuccess` (mit `allowSchemaOnly: true`).
  - `[Theory]` `ValidateQuerySafetyAsync_MutatingQuery_WithoutReadWrite_ReturnsWriteOperationBlocked` mit den mutierenden Queries aus den 3 Service-Tests: `DELETE FROM Customers`, `DROP TABLE Users`, `MERGE INTO …` — exakt die Queries, die heute in `QueryExecutionServiceTests` (`DELETE FROM Customers`), `PerformanceMeasurementServiceTests` (`DROP TABLE Users`), `QueryComparisonServiceTests` (`DROP TABLE Users`) verwendet werden.
  - `[Theory]` `ValidateQuerySafetyAsync_SpExecuteSql_WithoutReadWrite_ReturnsWriteOperationBlocked` mit den 3 InlineData-Queries aus `QueryValidationServiceTests` (`sp_executesql N'…'`, `EXEC sp_executesql N'…'`, `sys.sp_executesql N'…'`) — das ist die zentrale, **einzige** Stelle, die die `sp_executesql`-Erkennung verifiziert (heute in 2 Service-Tests dupliziert).
  - `[Theory]` `ValidateQuerySafetyAsync_MutatingQuery_WithReadWrite_ReturnsSuccess` (`UPDATE …`, `DELETE …` mit `AccessLevel.ReadWrite` → kein Reject).
  - `[Theory]` `ValidateQuerySafetyAsync_MultiStatement_ReturnsMultipleStatementsForbidden` mit 3 InlineData-Queries (`SELECT 1; SELECT 2`, `SELECT 1 ; DROP TABLE Foo`, `SELECT 'hello'; SELECT 'world'`) — aus `QueryExecutionServiceTests`.
  - `[Theory]` `ValidateQuerySafetyAsync_MultiStatement_RegardlessOfReadWrite_ReturnsMultipleStatementsForbidden` mit 2 InlineData-Cases (`ReadOnly`, `ReadWrite`) — aus `QueryValidationServiceTests` (zeigt explizit, dass Multi-Statement **immer** rejected wird).
  - `[Fact]` `ValidateQuerySafetyAsync_AllStagesPass_ReturnsResolvedAccessLevelAndWriteFlag` (Happy-Path-Endkette: 1× `ReadOnly`-Test, der den `QuerySafetyCheckResult.AccessLevel` und `IsWriteAllowed` assertiert — heute nirgends explizit getestet, da die Service-Tests den Happy-Path durch das Service-API fahren).
  - **Insgesamt: 9 Testmethoden, die 31 individual test cases abdecken** (Theorie-Expansion). Die Service-Tests verlieren entsprechend 31 Cases; der Gesamtcount bleibt **konstant** (523 → 523).
- **Was in den 4 Service-Testdateien (`QueryExecutionServiceTests.cs`, `QueryValidationServiceTests.cs`, `PerformanceMeasurementServiceTests.cs`, `QueryComparisonServiceTests.cs`):**
  - Lösche alle Pipeline-Negativ-Tests (siehe Liste oben). Die Service-Tests konzentrieren sich auf:
    - **`QueryExecutionServiceTests.cs`**: Transaktions-Verhalten (Commit/Rollback), ReadWrite-Override, Multi-Statement auch bei ReadWrite, `ShouldSucceed_WhenSingleStatement` (3 Cases).
    - **`QueryValidationServiceTests.cs`**: Service-Verhalten (Rollback-immer, Command-Timeout-Source, SpExecuteSql → WriteOperationBlocked weitergereicht), `ShouldFail_AndStillRollBack_WhenExecutionThrows`, Timeout/Socket-Exception-Mapping, `ShouldUseQueryExecutionCommandTimeout_NotConnectTimeout` (TD-001).
    - **`PerformanceMeasurementServiceTests.cs`**: nur noch die 8 ShowPlan-Tests (item-02).
    - **`QueryComparisonServiceTests.cs`**: Service-Tests (2-Query-Verhalten, Service-spezifische Verzweigungen).
  - Linter-Hinweis: die Service-Tests verlieren ~150 Zeilen (33 Cases × ~5 Zeilen pro Body). Die übrig bleibenden Tests passen weiterhin in die 100-Zeilen-Methode-Grenze (Test-Override aus `AiNetLinter.mdc`).
- **Was passiert mit `IndexSuggestionServiceTests`:** unverändert. Die beiden Pipeline-relevanten Tests (`DatabaseNotInWhitelist`, `DatabaseAccessLevelNone`) bleiben dort, weil `IndexSuggestionService` nicht über `IQuerySafetyValidator` läuft (eigener Pfad, in step-002 §"Beobachtungen" explizit dokumentiert). Die Tests sind daher keine Duplikation der `QuerySafetyValidatorTests` und bleiben eigenständig.
- **Warum:** Single Source of Truth für Pipeline-Tests. Die Service-Tests verlieren 31 redundante Cases und konzentrieren sich auf ihre Service-Identität. Die `sp_executesql`-Erkennung wird **einmal** zentral getestet (statt dreimal in 2 Service-Tests).
- **Migrations-Hinweis:** der Coder muss beim Konsolidieren exakt die gleichen `SqlToAiError.XxxCode`-Assertions übernehmen — die Test-Verdichtung darf keine Assertion-Logik verändern. Die `BypassReadOnlyGuardValidator`-Privatklasse in `FakeQuerySafetyValidator` (Datei `QueryExecutionServiceMockDb.cs:138-163`) wird **nicht** in die `QuerySafetyValidatorTests` übernommen — der direkte Test gegen den echten `QuerySafetyValidator` ist sauberer und deckt die Multi-Statement-Stage end-to-end ab.

### item-02: `ShowPlanTestHelper` einführen und 8 ShowPlan-XML-Blöcke reduzieren (DRY-T2) — neue Datei + `PerformanceMeasurementServiceTests.cs` refactor

- **Was neue Datei `tests/SqlToAi.Tests/TestSupport/ShowPlanTestHelper.cs`:**
  - `internal static class ShowPlanTestHelper` mit einer Builder-Methode:
    ```csharp
    public static string BuildShowPlanXml(double impact, string table, IReadOnlyList<ColumnSpec> columns)
    ```
  - `ColumnSpec` ist ein interner `record(string Name, string Usage, bool? Descending = null)`.
  - Die Methode baut per `StringBuilder` ein ShowPlan-XML-Dokument zusammen, das exakt dem heutigen String-Literal-Format entspricht (Namespace-Deklaration, Element-Hierarchie, Attribute-Reihenfolge, Whitespace). Begründung: Die heutigen Tests assertieren auf den **exakten** XML-String-Inhalt (z. B. `Assert.Contains("CREATE NONCLUSTERED INDEX", missing.MissingIndexStatement)`), aber das interne `XDocument.Parse(xmlText)` im `ParseExecutionPlanXml`-Code (`src/SqlToAi/Database/PerformanceMeasurementService.cs:285-302`) ist whitespace-tolerant. Daher darf der Builder die exakte Formatierung reproduzieren oder leicht abweichen — solange die 8 Test-Szenarien ihre spezifischen Asserts erfüllen.
  - **Code-Skizze** (siehe `Code-Skizze` unten).
- **Was `PerformanceMeasurementServiceTests.cs`:** alle 8 `ParseExecutionPlanXml_*`-Tests werden umgeschrieben:
  - `ParseExecutionPlanXml_MissingIndexAndImplicitConversion_ParsesWarningsCorrectly` (Zeilen 100-134) — Aufruf: `ShowPlanTestHelper.BuildShowPlanXml(impact: 85.4, table: "[dbo].[Orders]", columns: [])`. **Achtung:** dieser Test prüft 3 Warnings (MissingIndex + ImplicitConversion + TableScan), die nicht alle über `<ColumnGroup>` modellierbar sind — der Test ist strukturell anders (testet sowohl Missing-Index-Pfad als auch `<Warnings>`/`<PlanAffectingConvert>` und `<RelOp LogicalOp="Table Scan">`). **Empfehlung an den Coder:** dieser Test bleibt entweder als Sonderfall (mit eigenem XML-Block) bestehen oder es wird ein zweiter Builder `BuildShowPlanXmlWithWarnings(...)` eingeführt. Die pragmatische Lösung: **Test 1 (Zeilen 100-134) bleibt eigenständig** (eigener 30-Zeilen-XML-Block, weil er andere XML-Strukturen außerhalb von `<MissingIndex>` testet), die 7 anderen Tests (Zeilen 136-414) nutzen den Builder. Damit reduzieren sich 7 × ~25 Zeilen XML auf 7 × ~5 Zeilen Builder-Aufrufe.
  - Die 7 `ParseExecutionPlanXml_MissingIndex_*`-Tests (Zeilen 136-414) nutzen alle den gleichen `BuildShowPlanXml(impact, table, columns)`-Aufruf, mit unterschiedlichen InlineData:
    - `MissingIndex_EqualityOnly` → `BuildShowPlanXml(72.5, "[dbo].[Orders]", [new("CustomerId", "EQUALITY")])`
    - `MissingIndex_EqualityPlusInequalityPlusInclude` → 3 ColumnSpecs
    - `MissingIndex_EqualityOnlyWithInclude` → 2 ColumnSpecs (EQUALITY + INCLUDE)
    - `MissingIndex_DescendingColumn` → 3 ColumnSpecs (OrderDate mit `Descending: true` als INEQUALITY)
    - `MissingIndex_DescendingFalse` → 2 ColumnSpecs (CustomerId mit `Descending: false` als EQUALITY)
    - `MissingIndex_AllColumnsDescending` → 2 ColumnSpecs (beide `Descending: true`)
    - `MissingIndex_DescendingInInclude` → 2 ColumnSpecs (Amount mit `Descending: true` als INCLUDE)
  - **Test-Reduktion:** die 7 Tests verlieren ~140 Zeilen XML-Literal und gewinnen ~35 Zeilen Builder-Aufrufe (5 pro Test). Netto: ~105 Zeilen weniger in `PerformanceMeasurementServiceTests.cs`. Die Assertions (`Assert.Equal(3, warnings.Count)`, `Assert.Contains("(ColA DESC, ColB DESC)", missing.MissingIndexStatement)`, …) bleiben alle 1:1.
- **Warum:** die 8 XML-Blöcke sind 90 % identisch (gleiche Element-Hierarchie, gleiche Namespaces, gleiche Attribute). Nur die variierenden Inhalte (Spalten, Impact, Descending-Flag) sind in jedem Test interessant. Ein Builder macht das **Variierende** sichtbar und versteckt die Boilerplate.
- **Linter-Hinweis:** der `ShowPlanTestHelper` selbst ist `internal static class` (kein `sealed`-Zwang, aber explizit statisch → `static class` reicht). Methode bleibt unter 30 Zeilen (StringBuilder + String-Join), weit unter `MaxMethodLineCount = 100` für Tests.

### item-03: `GetDayDir()` und Legacy-Fakes nach `TestSupport/` konsolidieren (DRY-T1) — `McpTrailTestHelper.cs` erweitern + neue `LegacySecurityFakes.cs` + 2 McpTrail-Tests abspecken + `QueryExecutionServiceMockDb.cs` abspecken

- **Was `McpTrailTestHelper.cs` erweitern:**
  - Neue Methode `internal static string CreateIsolatedLogRoot(string suffix)` → `Path.Combine(Path.GetTempPath(), "SqlToAiMcpTrail" + suffix + "_" + Guid.NewGuid().ToString("N"))`. Ersetzt die `Path.Combine`-Zeilen in beiden McpTrail-Test-Konstruktoren.
  - Neue Methode `internal static string GetDayDir(string logRoot)` → exakt die heutige `GetDayDir()`-Logik, aber als static und mit dem `_logRoot` als Parameter statt als Instanzvariable. Ersetzt die `GetDayDir()`-Methode in beiden Tests.
  - Optional: ein `internal sealed class IsolatedLogRoot : IDisposable`, der beides bündelt (private sealed Helper, kann auch in den Tests selbst definiert sein, wenn die Helper-Klasse zu klein wird). **Empfehlung:** nur die zwei Helper-Methoden ergänzen, den IDisposable-Boilerplate in den Tests lassen — er ist 4 Zeilen pro Test und gut lesbar.
- **Was `McpTrailWriterTests.cs` und `McpTrailWriterRedactionTests.cs`:** 
  - Konstruktor: `_logRoot = McpTrailTestHelper.CreateIsolatedLogRoot("Tests")` bzw. `_logRoot = McpTrailTestHelper.CreateIsolatedLogRoot("RedactionTests")`.
  - `GetDayDir()`-Methode löschen.
  - Alle Aufrufe von `GetDayDir()` umstellen auf `McpTrailTestHelper.GetDayDir(_logRoot)`.
- **Was neue Datei `tests/SqlToAi.Tests/TestSupport/LegacySecurityFakes.cs`:**
  - `internal sealed class FakeSecurityGuard(bool allowed) : ISecurityGuard` (aus `QueryExecutionServiceMockDb.cs:54-57`).
  - `internal sealed class FakeAccessLevelProvider(AccessLevel level) : IAccessLevelProvider` (aus `QueryExecutionServiceMockDb.cs:59-63`).
  - `internal sealed class FakeReadOnlyGuard(bool safe) : IReadOnlyGuard` (aus `QueryExecutionServiceMockDb.cs:65-68`).
  - Namespace `SqlToAi.Tests.TestSupport`; jede Klasse mit ausführlichem XMLDoc-Kommentar, der die Verwendung dokumentiert (diese Fakes sind **kein Duplikat** des `FakeQuerySafetyValidator`, sondern Test-Doppels für die drei Security-Interfaces, die heute nur noch von `IndexSuggestionService` und von der `FakeQuerySafetyValidator`-Delegation an einen realen `QuerySafetyValidator` genutzt werden).
- **Was `QueryExecutionServiceMockDb.cs`:** 
  - Lösche die Zeilen 54-68 (die drei Fake-Klassen).
  - Füge `using SqlToAi.Tests.TestSupport;` hinzu (sofern nicht bereits via `global usings` verfügbar — `tests/SqlToAi.Tests/GlobalUsings.cs` muss geprüft werden; der Coder stellt das fest und passt an).
  - `FakeQuerySafetyValidator` (Zeilen 79-164) bleibt unverändert.
- **Was `IndexSuggestionServiceTests.cs`:** `using SqlToAi.Tests.TestSupport;` (falls nicht bereits global) — anpassen, weil die Fakes jetzt aus `TestSupport` kommen statt aus `QueryExecutionServiceMockDb` (gleicher Namespace `SqlToAi.Tests.Database`, daher funktioniert der Code bereits ohne using-Änderung, sofern die Fakes im selben Namespace `SqlToAi.Tests.TestSupport` bleiben).
- **Warum:** `TestSupport/` ist der etablierte Ort für fakes & helpers (siehe `codemap.md` Zeile 78: "`tests/SqlToAi.Tests/TestSupport/` — gemeinsame Fakes (`AnonymizationTestHelper`, `McpTrailTestHelper`, `FakeDb*`); Bündelungsziel für DRY-T1"). Die drei Security-Fakes gehören in dieselbe Sammlung.

### item-04: `McpModelsTests.cs:96` hardkodierten `"-32601"` auf `JsonRpcError.MethodNotFound` umstellen (MV-T1)

- **Was `McpModelsTests.cs:96`:** ersetze `Assert.Contains("-32601", json);` durch `Assert.Contains(JsonRpcError.MethodNotFound.ToString(CultureInfo.InvariantCulture), json);`.
- **Was Imports:** falls nicht bereits vorhanden, `using System.Globalization;` ergänzen (für `CultureInfo.InvariantCulture`).
- **Warum:** die Konstante `JsonRpcError.MethodNotFound = -32601` existiert in `src/SqlToAi/Mcp/JsonRpcModels.cs:73`. Der Test hardkodiert den Wert und ist damit gegen eine künftige Versions-Änderung nicht immun. Niedrige Priorität (Test-only, keine Produktionsänderung), aber **eine** Zeile Diff.
- **Hinweis:** `JsonRpcError.ParseError = -32700` wird in den Tests **nicht** direkt referenziert (per `grep` bestätigt), daher keine zweite Stelle zum Aufräumen.

## Tests

- [ ] `tests/SqlToAi.Tests/Database/QuerySafetyValidatorTests.cs` (neu) — alle 9 Testmethoden, 31 individual test cases grün.
- [ ] `tests/SqlToAi.Tests/Database/PerformanceMeasurementServiceTests.cs` — Pipeline-Tests gelöscht; 8 ShowPlan-Tests grün (Test 1 mit eigenem XML-Block, Tests 2-8 mit `ShowPlanTestHelper.BuildShowPlanXml(...)`).
- [ ] `tests/SqlToAi.Tests/Database/QueryExecutionServiceTests.cs` — Pipeline-Tests gelöscht; übrige Service-Tests grün.
- [ ] `tests/SqlToAi.Tests/Database/QueryValidationServiceTests.cs` — Pipeline-Tests gelöscht; übrige Service-Tests grün.
- [ ] `tests/SqlToAi.Tests/Database/QueryComparisonServiceTests.cs` — Pipeline-Tests gelöscht; übrige Service-Tests grün.
- [ ] `tests/SqlToAi.Tests/Mcp/McpTrailWriterTests.cs` + `McpTrailWriterRedactionTests.cs` — `GetDayDir()` weg, `McpTrailTestHelper.GetDayDir(_logRoot)` rein; Tests grün.
- [ ] `tests/SqlToAi.Tests/Database/IndexSuggestionServiceTests.cs` — `FakeSecurityGuard`/`FakeAccessLevelProvider` aus `TestSupport` (statt `QueryExecutionServiceMockDb`); Tests grün.
- [ ] `tests/SqlToAi.Tests/Mcp/McpModelsTests.cs` — `Assert.Contains("-32601", json)` → `Assert.Contains(JsonRpcError.MethodNotFound.ToString(CultureInfo.InvariantCulture), json)`.
- [ ] **Endstand:** `dotnet test` läuft, **523 Tests grün, 0 fehlgeschlagen, 0 übersprungen** (Anzahl identisch zu step-002, weil Konsolidierung 1:1).
- [ ] `dotnet build` 0/0.
- [ ] AiNetLinter: alle neuen Dateien mit `MaxMethodLineCount = 100` (Test-Override), `MaxLineCount = 500`, `EnforceSealedClasses` aus in `*.Tests`.

## Definition of Done

- [ ] Alle vier Items (item-01 bis item-04) umgesetzt.
- [ ] `dotnet build SqlToAi.slnx` — 0 Warnungen, 0 Fehler.
- [ ] `dotnet test SqlToAi.slnx` — 523 Tests grün, 0 fehlgeschlagen, 0 übersprungen.
- [ ] AiNetLinter (`RunLinterShouldBeClean`) grün.
- [ ] `tasks/audit-try-magicvalues/step-003/step-result.md` geschrieben.
- [ ] `tasks/audit-try-magicvalues/codemap.md` aktualisiert (neue TestSupport-Einträge, Testdatei-Änderungen).
- [ ] `tasks/audit-try-magicvalues/tech-debt.md` ggf. ergänzt, falls der Coder beobachtet, dass `GetDayDir`-Migration eine `string.IsNullOrEmpty` Lücke hinterlässt.
- [ ] `status` in `step-plan.md` von `open` auf `done (pending audit)` gesetzt.
- [ ] `roadmap.md` EPIC-03 abgehakt (`[x]`).
- [ ] Conventional Commit (deutsch, imperativ, Subject ≤ 72 Zeichen; dieser Step wird vermutlich 90+ Zeichen brauchen — siehe `step-002` Beobachtung „Commit-Subject ≤ 72 Zeichen" und Beibehaltung als ausformulierter Subject mit `[audit-try-magicvalues]`-Suffix).

## Rules-Refs

- `.agents/rules/AiNetLinter.mdc#Kurz-Stil` — `sealed` für konkrete Klassen (in `*.Tests` per Projekt-Override deaktiviert); Methoden ≤ 60 Zeilen Produktion, ≤ 100 Zeilen Tests.
- `.agents/rules/AiNetLinter.mdc#DuplicateCode` — Fast identische Methoden (`GetDayDir()` in zwei McpTrail-Testklassen) — konsolidieren.
- `.agents/rules/AiNetLinter.mdc#Projekt-Overrides` — `MaxMethodLineCount = 100` für `*.Tests`; `EnforceSealedClasses` aus für `*.Tests`; `MaxLineCount = 500` für Dateien.
- `.agents/rules/AiNetLinter.mdc#EnforceNoSilentCatch` — irrelevant für diesen Step (kein neuer `catch`-Code); die bestehende `ParseExecutionPlanXml`-Leerstelle ist TD-001 und bleibt.
- `.agents/rules/SqlToAiRichtlinien.mdc#4 No Magic Values` — relevanter Bezug für MV-T1 (item-04).

## Bekannte Ausnahmen

- **`PerformanceMeasurementServiceTests.cs:100-134` (`ParseExecutionPlanXml_MissingIndexAndImplicitConversion_ParsesWarningsCorrectly`)** bleibt mit eigenem XML-Block stehen, weil dieser Test andere XML-Knoten testet (`<RelOp LogicalOp="Table Scan">`, `<Warnings>`, `<PlanAffectingConvert Expression="…">`), die außerhalb der `<MissingIndex>`-Hierarchie liegen. Ein 2. Builder wäre möglich, ist aber für **einen** Test Overhead. Bewusste Ausnahme: 1 von 8 ShowPlan-Tests bleibt im alten Stil.
- **`IndexSuggestionServiceTests.cs`** Pipeline-Tests (`DatabaseNotInWhitelist`, `DatabaseAccessLevelNone`) bleiben unverändert — `IndexSuggestionService` ist nicht auf `IQuerySafetyValidator` migriert (eigener Pfad über `ISecurityGuard`+`IAccessLevelProvider`), die Tests sind daher keine Duplikation.
- **MV-T1** wird trotz niedriger Priorität in diesem Step mitgemacht (1 Zeile Diff, kein Risiko, hält den Step thematisch zusammen — JSON-RPC-Error-Code-Konsolidierung).
- **Keine Änderung am FakeQuerySafetyValidator selbst** (`QueryExecutionServiceMockDb.cs:79-164`) — er ist die zukünftige Test-Single-Source-of-Truth und steht bereits im step-002-Format. Lediglich die drei Legacy-Security-Fakes werden aus der Datei entfernt.
- **DRY-T1 hat zwei Sub-Befunde** (Fakes + `GetDayDir()`). Beide Sub-Befunde werden in item-03 gemeinsam adressiert, weil sie dasselbe Pattern haben (Verteilung über mehrere Testdateien).

## Code-Skizze (optional)

### `ShowPlanTestHelper` (item-02)

```csharp
#nullable enable

namespace SqlToAi.Tests.TestSupport;

/// <summary>
/// Builder for SQL Server ShowPlan XML test fixtures. Replaces 7 of 8 hand-rolled XML
/// literals in <c>PerformanceMeasurementServiceTests</c> (DRY-T2). The 8th literal stays
/// in the test file because it tests non-<c>&lt;MissingIndex&gt;</c> XML paths
/// (<c>&lt;RelOp&gt;</c>, <c>&lt;Warnings&gt;</c>, <c>&lt;PlanAffectingConvert&gt;</c>) that
/// this helper does not model. Whitespace may differ from the originals —
/// <c>PerformanceMeasurementService.ParseExecutionPlanXml</c> uses <c>XDocument.Parse</c>
/// which is whitespace-tolerant.
/// </summary>
internal static class ShowPlanTestHelper
{
    /// <summary>One column inside a <c>&lt;ColumnGroup&gt;</c> element.</summary>
    internal sealed record ColumnSpec(string Name, string Usage, bool? Descending = null);

    /// <summary>
    /// Builds a single-Statement, single-MissingIndex ShowPlan XML document with the given
    /// impact, table, and ordered column groups. Pass <c>columns</c> in the order the columns
    /// should appear in the CREATE INDEX statement (equality first, then inequality, then include).
    /// </summary>
    public static string BuildShowPlanXml(double impact, string table, IReadOnlyList<ColumnSpec> columns)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("<ShowPlanXML xmlns=\"http://schemas.microsoft.com/sqlserver/2004/07/showplan\">\n");
        sb.Append("  <BatchSequence>\n");
        sb.Append("    <Batch>\n");
        sb.Append("      <Statements>\n");
        sb.Append("        <StmtSimple>\n");
        sb.Append("          <QueryPlan>\n");
        sb.Append("            <MissingIndexes>\n");
        sb.Append("              <MissingIndexGroup Impact=\"").Append(impact.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append("\">\n");
        sb.Append("                <MissingIndex Table=\"").Append(table).Append("\">\n");
        foreach (var col in columns)
        {
            sb.Append("                  <ColumnGroup Usage=\"").Append(col.Usage).Append("\">\n");
            sb.Append("                    <Column Name=\"").Append(col.Name).Append('"');
            if (col.Descending.HasValue)
            {
                sb.Append(" Descending=\"").Append(col.Descending.Value ? "True" : "False").Append('"');
            }
            sb.Append(" />\n");
            sb.Append("                  </ColumnGroup>\n");
        }
        sb.Append("                </MissingIndex>\n");
        sb.Append("              </MissingIndexGroup>\n");
        sb.Append("            </MissingIndexes>\n");
        sb.Append("          </QueryPlan>\n");
        sb.Append("        </StmtSimple>\n");
        sb.Append("      </Statements>\n");
        sb.Append("    </Batch>\n");
        sb.Append("  </BatchSequence>\n");
        sb.Append("</ShowPlanXML>\n");
        return sb.ToString();
    }
}
```

### `QuerySafetyValidatorTests` (item-01, Auszug)

```csharp
#nullable enable

using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;
using Xunit;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Single source of truth for the 6-stage guardrail-pipeline tests. Replaces 31 individual
/// negative test cases that used to be duplicated across <c>QueryExecutionServiceTests</c>,
/// <c>QueryValidationServiceTests</c>, <c>PerformanceMeasurementServiceTests</c>, and
/// <c>QueryComparisonServiceTests</c> (DRY-T3). The service tests now focus on service-specific
/// behaviour (transactions, anonymization, command-timeout source) instead of the pipeline.
/// Service-internal behaviour (e.g. transaction commit/rollback) stays in the service tests.
/// </summary>
public sealed class QuerySafetyValidatorTests
{
    private static QuerySafetyValidator BuildValidator(
        bool isAllowed = true,
        AccessLevel accessLevel = AccessLevel.ReadOnly) =>
        new(
            new FakeSecurityGuard(isAllowed),
            new FakeAccessLevelProvider(accessLevel),
            new ReadOnlyGuard());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateQuerySafetyAsync_EmptyDatabaseName_ReturnsInvalidParameters(string db)
    {
        var v = BuildValidator();
        var result = await v.ValidateQuerySafetyAsync(db, "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateQuerySafetyAsync_EmptyQuery_ReturnsInvalidParameters(string query)
    {
        var v = BuildValidator();
        var result = await v.ValidateQuerySafetyAsync("TestDb", query, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task ValidateQuerySafetyAsync_DatabaseNotAllowed_ReturnsSafetyCheckFailed()
    {
        var v = BuildValidator(isAllowed: false);
        var result = await v.ValidateQuerySafetyAsync("ForbiddenDb", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    [Theory]
    [InlineData(AccessLevel.None)]
    [InlineData(AccessLevel.SchemaOnly)]
    public async Task ValidateQuerySafetyAsync_LowAccessLevel_WithoutSchemaOnlyFlag_ReturnsWriteOperationBlocked(AccessLevel level)
    {
        var v = BuildValidator(accessLevel: level);
        var result = await v.ValidateQuerySafetyAsync("TestDb", "SELECT 1", allowSchemaOnly: false, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Fact]
    public async Task ValidateQuerySafetyAsync_SchemaOnly_WithSchemaOnlyFlag_ReturnsSuccess()
    {
        var v = BuildValidator(accessLevel: AccessLevel.SchemaOnly);
        var result = await v.ValidateQuerySafetyAsync("TestDb", "SELECT 1", allowSchemaOnly: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccessLevel.SchemaOnly, result.Value.AccessLevel);
        Assert.False(result.Value.IsWriteAllowed);
    }

    [Theory]
    [InlineData("DELETE FROM Customers")]
    [InlineData("DROP TABLE Users")]
    [InlineData("MERGE INTO Target USING Source ON (1=1) WHEN MATCHED THEN UPDATE SET Target.X = 1")]
    public async Task ValidateQuerySafetyAsync_MutatingQuery_WithoutReadWrite_ReturnsWriteOperationBlocked(string query)
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadOnly);
        var result = await v.ValidateQuerySafetyAsync("TestDb", query, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Theory]
    [InlineData("sp_executesql N'DELETE FROM Foo'")]
    [InlineData("EXEC sp_executesql N'DELETE FROM dbo.Foo; COMMIT'")]
    [InlineData("sys.sp_executesql N'DELETE FROM Foo'")]
    public async Task ValidateQuerySafetyAsync_SpExecuteSql_WithoutReadWrite_ReturnsWriteOperationBlocked(string query)
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadOnly);
        var result = await v.ValidateQuerySafetyAsync("TestDb", query, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Fact]
    public async Task ValidateQuerySafetyAsync_MutatingQuery_WithReadWrite_ReturnsSuccess()
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadWrite);
        var result = await v.ValidateQuerySafetyAsync("TestDb", "DELETE FROM Customers", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccessLevel.ReadWrite, result.Value.AccessLevel);
        Assert.True(result.Value.IsWriteAllowed);
    }

    [Theory]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1 ; DROP TABLE Foo")]
    [InlineData("SELECT 'hello'; SELECT 'world'")]
    public async Task ValidateQuerySafetyAsync_MultiStatement_ReturnsMultipleStatementsForbidden(string query)
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadWrite);
        var result = await v.ValidateQuerySafetyAsync("TestDb", query, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.ReadWrite)]
    public async Task ValidateQuerySafetyAsync_MultiStatement_RegardlessOfAccessLevel_ReturnsMultipleStatementsForbidden(AccessLevel level)
    {
        var v = BuildValidator(accessLevel: level);
        var result = await v.ValidateQuerySafetyAsync("TestDb", "SELECT 1; SELECT 2", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    [Fact]
    public async Task ValidateQuerySafetyAsync_AllStagesPass_ReturnsResolvedAccessLevelAndWriteFlag()
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadOnlyAnonymized);
        var result = await v.ValidateQuerySafetyAsync("TestDb", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccessLevel.ReadOnlyAnonymized, result.Value.AccessLevel);
        Assert.False(result.Value.IsWriteAllowed);
    }
}
```

## Notes

- **Test-Anzahl konstant:** 523 → 523. Die Konsolidierung ist eine **Umverteilung**, keine Reduktion. Der Code-Coverage-Stand des Pipelines bleibt identisch; die Test-Lesbarkeit steigt deutlich, weil alle Pipeline-Tests in einer Klasse stehen.
- **ReadOnlyGuard ist real, nicht gefakt** — analog zur Konvention in `QueryValidationServiceTests` und `PerformanceMeasurementServiceTests` (heute schon so gehandhabt). Begründung: die `sp_executesql`-Erkennung und die anderen Regex-basierten Checks sollen end-to-end durch den realen Code laufen, nicht durch ein `FakeReadOnlyGuard(true)` neutralisiert werden. Die anderen beiden Security-Interfaces sind simpel genug zum Faken.
- **`FakeQuerySafetyValidator` bleibt unverändert.** Er ist die Brücke zwischen Service-Tests und Pipeline-Semantik: die Service-Tests wollen nicht die Pipeline selbst testen (das macht `QuerySafetyValidatorTests`), sondern das **Service-Verhalten** mit einer gemockten Pipeline. Daher existieren `FakeQuerySafetyValidator` (Service-Tests) und `QuerySafetyValidatorTests` (Pipeline-Tests) parallel — der `FakeQuerySafetyValidator` wird in den Service-Tests **nicht** durch die echte `QuerySafetyValidator` ersetzt.
- **`PerformanceMeasurementServiceTests.cs` behält 8 Tests** (die ShowPlan-Tests). Die 6 Pipeline-Tests verschwinden zugunsten von `QuerySafetyValidatorTests`. Netto: 14 → 8 Tests in dieser Datei, aber 523 gesamt-Projekt bleibt.
- **`FakeQuerySafetyValidator` ist eine `internal sealed class` in `QueryExecutionServiceMockDb.cs`** — bleibt. Sie ist kein Duplikat der `LegacySecurityFakes`, weil sie **zusätzlich** die Pipeline-Bypass-Logik (`BypassReadOnlyGuardValidator`) kapselt. Die Trennung ist bewusst: Security-Interface-Fakes (`LegacySecurityFakes.cs`) für direkte Interface-Tests, Pipeline-Fake (`FakeQuerySafetyValidator`) für Service-Tests, die Pipeline-Mock benötigen.
- **Indentation/Encoding:** C# 14 raw string literals (`"""…"""`) sind seit C# 11 stabil. Der Coder kann `BuildShowPlanXml`-Rückgaben problemlos als Test-Fixture verwenden; die `XDocument.Parse`-Pipeline in `PerformanceMeasurementService.ParseExecutionPlanXml` ist whitespace-tolerant. Der Builder erzeugt **explizit Whitespace** (im Beispiel-Snippet mit Newlines + 2-Space-Indent), was den heutigen visuellen Eindruck der 7 refaktorierten Tests beibehält.
- **Linter-Verhalten bei `internal static class ShowPlanTestHelper`:** AiNetLinter-Einstellung `EnforceSealedClasses` ist für `*.Tests` aus → `static class` (implizit sealed) reicht. `MaxMethodLineCount = 100` ist der Test-Override; die `BuildShowPlanXml`-Methode bleibt mit 30 Zeilen weit darunter.
- **Bewusst zurückgestellt (für Folge-Steps, nicht in diesem Step):** der leere `catch` in `ParseExecutionPlanXml` (TD-001), die Vereinheitlichung des `WriteOperationBlocked`-Texts (TD-002). Beide Themen sind im `tech-debt.md` dokumentiert.
- **MCP-Test-Datei (`McpModelsTests.cs`) Import:** die `using System.Globalization;` wird nur dann gebraucht, wenn `CultureInfo.InvariantCulture` referenziert wird. Alternative: `JsonRpcError.MethodNotFound.ToString()` (ohne CultureInfo) liefert je nach CurrentCulture eine kulturspezifische Darstellung (bei `int` ist das culture-invariant, daher funktioniert `ToString()` ohne Argumente). Empfehlung: `MethodNotFound.ToString()` ohne CultureInfo, weil `int.ToString()` immer kulturinvariant ist — spart den `using`-Import.
