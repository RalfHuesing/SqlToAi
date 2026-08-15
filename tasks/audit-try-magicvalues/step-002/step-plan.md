---
status: open
type: step-plan
task: audit-try-magicvalues
step: 002
corrects: null
title: "EPIC-02 Guardrail-Pipeline-Extraktion (IQuerySafetyValidator, batch)"
epic: EPIC-02
estimated_risk: medium
step_type: batch
items:
  - id: item-01
    title: "IQuerySafetyValidator / QuerySafetyValidator / QuerySafetyCheckResult einführen + DI-Registrierung"
    source: "audit-dry-magicvalues.md#DRY-1"
  - id: item-02
    title: "QueryExecutionService + QueryValidationService auf die Pipeline migrieren (Konstruktor-Reduktion, Inline-Validierung entfernen)"
    source: "audit-dry-magicvalues.md#DRY-1"
  - id: item-03
    title: "PerformanceMeasurementService + QueryComparisonService auf die Pipeline migrieren (2-Query-Spezialfall Comparison)"
    source: "audit-dry-magicvalues.md#DRY-1"
  - id: item-04
    title: "FakeQuerySafetyValidator einführen und die 4 Service-Testklassen auf den neuen Mock umstellen (alte Fakes für andere Services erhalten)"
    source: "audit-dry-magicvalues.md#DRY-1 + DRY-T3 (Konsolidierung folgt in EPIC-03)"
created_by: planer
created_by_model: MiniMax-M3
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-15T22:00:00+02:00
related_to: []
---

# Step 002: EPIC-02 Guardrail-Pipeline-Extraktion (IQuerySafetyValidator, batch)

## Bezug

- **Task:** `audit-try-magicvalues`
- **Epic:** `EPIC-02` aus `roadmap.md` — *Guardrail-Pipeline (Phase 2, DRY-1)*. Die 6-stufige Validierung (Parameter, Whitelist, AccessLevel, ReadOnlyGuard, Multi-Statement) ist in vier Guardrail-Services identisch dupliziert. Sicherheitsrelevanter Refactoring-Drift: jede zukünftige Änderung am Berechtigungsmodell muss heute an vier Stellen synchron gepflegt werden, und die Texte der Fehlermeldungen sind bereits heute auseinander gelaufen. Mit dieser Extraktion wird die Pipeline die Single Source of Truth und gleichzeitig werden die redundanten Constructor-Dependencies (`ISecurityGuard`, `IAccessLevelProvider`, `IReadOnlyGuard`) in den vier Services reduziert.
- **Konzept-Referenz:** `konzept.md` §"Muss-Haven" Pkt. 2 (Phase 2, Architektur-Konsolidierung Guardrails), im Detail belegt durch `audit-dry-magicvalues.md` Abschnitt 2 (DRY-1, Zeilen 59-114) und 4 (DRY-T3, Zeilen 321-327, **für die Konsolidierung der 33 redundanten Negativ-Tests folgt EPIC-03** — in diesem Step nur „müssen weiter grün sein").
- **Risiko:** `medium` — Architektur-Änderung mit Berührung von 4 Produktions-Services + 4 Test-Klassen + DI-Setup; verhaltensneutral bei korrekter Migration, aber jede inkonsistente Halb-Migration gefährdet die Sicherheits-Garantie.

## Aktueller Projektzustand (JIT-Kontext)

Beim Lesen des Bestandscodes habe ich folgende Strukturen vorgefunden, die den Plan beeinflussen:

### Konstruktoren der 4 Services heute

- **`QueryExecutionService`** (`QueryExecutionService.cs:58-78`) — 6 Parameter:
  `(IDatabaseConnectionFactory, ISecurityGuard, IAccessLevelProvider, IReadOnlyGuard, AnonymizationDependencies, IOptions<SqlToAiOptions>, ILogger<…>)`. Inline-Validierung in `ExecuteQueryAsync` Zeilen 99-136 (6 Stages). Anonymisierungs-Setup greift zusätzlich auf `_accessLevel` zu (Zeile 143: `bool anonymize = accessLevel == AccessLevel.ReadOnlyAnonymized;`) → der Service braucht den aufgelösten `AccessLevel`-Wert **nach** der Pipeline, also muss `QuerySafetyCheckResult.AccessLevel` öffentlich verfügbar bleiben.
- **`QueryValidationService`** (`QueryValidationService.cs:33-47`) — 5 Parameter:
  `(IDatabaseConnectionFactory, ISecurityGuard, IAccessLevelProvider, IReadOnlyGuard, IOptions<SqlToAiOptions>, ILogger<…>)`. Inline-Validierung Zeilen 66-104. **Asymmetrie:** lehnt nur `AccessLevel.None` ab (Zeile 81), nicht `AccessLevel.SchemaOnly` (im Gegensatz zu den drei anderen Services). Diese Asymmetrie ist heute aktiv und nicht im Audit/Konzept als Bug markiert; der Plan erhält sie über den `allowSchemaOnly: true`-Parameter beim Validator-Call.
- **`PerformanceMeasurementService`** (`PerformanceMeasurementService.cs:36-50`) — 5 Parameter:
  `(IDatabaseConnectionFactory, ISecurityGuard, IAccessLevelProvider, IReadOnlyGuard, IOptions<SqlToAiOptions>, ILogger<…>)`. Validierung aufgeteilt in `ValidateArgs` (statisch, Zeile 113) und `ValidateSecurityGuards` (Instanz, Zeile 126). Letztere hängt am `_readOnlyGuard`, der damit aus der Klasse verschwindet.
- **`QueryComparisonService`** (`QueryComparisonService.cs:36-50`) — 5 Parameter:
  `(IDatabaseConnectionFactory, ISecurityGuard, IAccessLevelProvider, IReadOnlyGuard, IOptions<SqlToAiOptions>, ILogger<…>)`. Validierung ebenfalls aufgeteilt in `ValidateArgs` (statisch, Zeile 121) und `ValidateSecurityGuards` (Instanz, Zeile 134). **Spezialfall:** `ValidateSecurityGuards` prüft **beide** Queries (`QueryA` UND `QueryB`) gegen `_readOnlyGuard.IsQuerySafe(…)` UND gegen `SqlMultiStatementDetector.ContainsMultipleStatements(…)` und liefert einen anderen Fehlertext ("One or both queries contain mutating SQL keywords and were rejected." vs. "The query contains mutating SQL keywords and was rejected."). Die Pipeline ist single-query — der Service muss sie zweimal aufrufen und das Ergebnis aggregieren. Der Text-Vereinheitlichung wird mit dem neutralen Validator-Text Rechnung getragen.

### DI heute

- `Program.cs:178-180` registriert die drei heute direkt von den Services konsumierten Security-Komponenten:
  ```csharp
  services.AddSingleton<ISecurityGuard, SecurityGuard>();
  services.AddSingleton<IAccessLevelProvider, AccessLevelProvider>();
  services.AddSingleton<IReadOnlyGuard, ReadOnlyGuard>();
  ```
  Diese Registrierungen bleiben für EPIC-03 (DRY-T1 Fake-Konsolidierung) und für `IndexSuggestionService` (nicht in EPIC-02) **bestehen** — sie sind in der gesamten Solution weiterhin über `IndexSuggestionService` referenziert. Die neue `IQuerySafetyValidator`-Registrierung kommt als zusätzliche Zeile dazu (im selben Block "Security").

### Testseite heute

- **`QueryExecutionServiceTests.cs:27-43`** — `BuildService(...)` instanziiert `FakeSecurityGuard(isAllowed)`, `FakeAccessLevelProvider(accessLevel)`, `FakeReadOnlyGuard(readOnlySafe)` aus `QueryExecutionServiceMockDb.cs` (Zeilen 54-68) und übergibt sie einzeln. 6 Guardrail-Tests in dieser Datei (Zeilen 50-114): `EmptyDatabase`, `EmptyQuery`, `DatabaseNotAllowed`, `AccessLevelTooLow` (Theory über `None` und `SchemaOnly`), `MutatingQuery`, `MultipleStatements`. Plus drei Tests, die das `ReadWrite`-Bypass-Verhalten und Transaktions-Commit prüfen (Zeilen 134-181).
- **`QueryValidationServiceTests.cs:29-45`** — gleiches Muster. 7 Guardrail-Tests: `EmptyDatabase`, `EmptyQuery`, `DatabaseNotAllowed`, `AccessLevelNone`, `MutatingQuery_AndAccessLevelIsNotReadWrite`, `NotBlockMutatingQuery_WhenReadWrite`, `Reject_SpExecuteSql_BeforeTouchingDatabase` (verwendet **echten** `ReadOnlyGuard`, Zeile 137), `MultipleStatements_RegardlessOfAccessLevel`. Plus 5 Transaktion/Timeout-Tests.
- **`PerformanceMeasurementServiceTests.cs:19-92`** — 6 Guardrail-Tests im selben Muster.
- **`QueryComparisonServiceTests.cs:19-92`** — 6 Guardrail-Tests im selben Muster.
- **Geteilte Fakes** in `QueryExecutionServiceMockDb.cs:54-68` (`FakeSecurityGuard`, `FakeAccessLevelProvider`, `FakeReadOnlyGuard`) — `internal sealed class` im selben Namespace wie die Tests, werden auch von `IndexSuggestionServiceTests.cs` referenziert (für den nicht-migrierten 5. Service). Diese Fakes bleiben **bestehen** — der Migrationsschritt tauscht sie in den vier Service-Tests durch eine neue `FakeQuerySafetyValidator` aus, ohne die `IndexSuggestionServiceTests` anzufassen.
- **Test-Assertion-Pattern:** alle 33+ negativen Tests prüfen ausschließlich `result.Error.Code` (`SqlToAiError.InvalidParametersCode`, `…SafetyCheckFailedCode`, `…WriteOperationBlockedCode`, `…MultipleStatementsForbiddenCode`) — **keine** Assertion hängt am exakten Wortlaut von `result.Error.Message`. Damit ist die Fehlertext-Vereinheitlichung testseitig risikofrei: solange der **Code** identisch bleibt, bleiben die Tests grün.

### Verhaltens-Asymmetrien, die der Plan bewusst erhält

1. **`QueryValidationService` erlaubt `AccessLevel.SchemaOnly`** (alle anderen 3 Services lehnen es ab). Erhaltung über `allowSchemaOnly: true`-Parameter beim Validator-Call aus `QueryValidationService.ValidateQueryAsync` (Aufrufstelle).
2. **Fehlertext-Unterschiede** in den WriteOperationBlocked-Messages (operationsspezifisch: "query execution" / "performance measurement" / "query comparison"). Vereinheitlichung auf **einen** operations-agnostischen Text durch den Validator — die Tests prüfen nur den Code, also kein Bruch. Wenn der Nutzer die operationsspezifische Variante zurückhaben will, ist das ein bewusster Folge-Schritt (eigener TD-Eintrag, nicht Teil von EPIC-02).
3. **`QueryComparisonService` prüft 2 Queries** (nicht 1). Der Validator-Call wird zweimal ausgeführt (einmal `QueryA`, einmal `QueryB`); der erste Fehler wird durchgereicht. Begründung: die Pipeline-API soll single-query bleiben (passt zur Orchestrator-Vorgabe und zur 4-Service-Mehrheit), und die Komposition im Service bleibt 5 Zeilen (Aufruf + Branch).

### Sonstiges

- `IndexSuggestionService` (Zeilen 35-53) hat **eine eigene** Mini-Validierungskette mit `ISecurityGuard` und `IAccessLevelProvider` (kein `IReadOnlyGuard` und keine Multi-Statement-Prüfung, weil das Tool nie User-SQL ausführt). **Bleibt unangetastet** — der Audit nennt nur die vier query-verarbeitenden Services. `IndexSuggestionServiceTests` behält seine Fakes.
- `SqlMultiStatementDetector` (`SqlMultiStatementDetector.cs`) bleibt unverändert; der Validator delegiert an ihn.
- `SqlToAiError`-Katalog (`SqlToAiError.cs:11-21`) bleibt unverändert; der Validator nutzt die existierenden Factory-Methoden `InvalidParameters`, `SafetyCheckFailed`, `WriteOperationBlocked(details)`, `MultipleStatementsForbidden`.
- Konzept-Non-Goal: keine Änderung am `SchemaService`, keine `GlobMatcher`/`LikePatternMatcher`-Zusammenlegung, keine `SqlToAiOptions`-Änderungen — alles nicht berührt.

## Intention

Nach diesem Step existiert die 6-stufige Guardrail-Validierung **genau einmal** in `QuerySafetyValidator.ValidateQuerySafetyAsync` (Single Source of Truth). Die vier query-verarbeitenden Services konsumieren die Pipeline über die `IQuerySafetyValidator`-Dependency, ihre Constructor-Signaturen verlieren die drei redundanten `ISecurityGuard`/`IAccessLevelProvider`/`IReadOnlyGuard`-Parameter (Reduktion von 5/6 auf 3 Dependencies — weit unter dem `MaxConstructorDependencies = 5`-Limit), und die bisher vier unterschiedlichen WriteOperationBlocked-Fehlertexte sind auf einen operations-agnostischen Text vereinheitlicht. Die bestehende Verhaltens-Asymmetrie (`QueryValidationService` lässt `SchemaOnly` durch) bleibt über den `allowSchemaOnly`-Parameter explizit erhalten. **Keine** der bestehenden Tests bricht, keine Verhaltensänderung für Endnutzer.

## Konkrete Änderungen

### item-01: IQuerySafetyValidator / QuerySafetyValidator / QuerySafetyCheckResult einführen + DI — neue Datei `src/SqlToAi/Database/QuerySafetyValidator.cs` + DI in `Program.cs`

- **Was:**
  - Neue Datei `src/SqlToAi/Database/QuerySafetyValidator.cs` mit:
    - `public sealed record QuerySafetyCheckResult(AccessLevel AccessLevel, bool IsWriteAllowed)` (im Namespace `SqlToAi.Database` neben den Konsumenten).
    - `public interface IQuerySafetyValidator { Task<Result<QuerySafetyCheckResult>> ValidateQuerySafetyAsync(string databaseName, string query, bool allowSchemaOnly = false, CancellationToken cancellationToken = default); }` (im selben Namespace, neben dem Record).
    - `internal sealed class QuerySafetyValidator : IQuerySafetyValidator` mit Constructor `(ISecurityGuard securityGuard, IAccessLevelProvider accessLevelProvider, IReadOnlyGuard readOnlyGuard)` und einer einzigen Public-Methode `ValidateQuerySafetyAsync`, die die 6 Stages in dieser Reihenfolge ausführt:
      1. Stage 1: `string.IsNullOrWhiteSpace(databaseName)` → `SqlToAiError.InvalidParameters("Database name must not be empty.")` (Text identisch zu heute).
      2. Stage 2: `string.IsNullOrWhiteSpace(query)` → `SqlToAiError.InvalidParameters("Query must not be empty.")` (Text identisch zu heute).
      3. Stage 3: `!_securityGuard.IsDatabaseAllowed(databaseName)` → `SqlToAiError.SafetyCheckFailed(databaseName)` (Aufruf identisch zu heute).
      4. Stage 4: `var accessLevel = await _accessLevelProvider.GetAccessLevelAsync(databaseName, cancellationToken).ConfigureAwait(false);`.
      5. Stage 5: `if (accessLevel == AccessLevel.None || (!allowSchemaOnly && accessLevel == AccessLevel.SchemaOnly)) return SqlToAiError.WriteOperationBlocked($"Database '{databaseName}' is not permitted to run this query (AccessLevel: {accessLevel}).");` — **vereinheitlichter Text** statt der vier operationsspezifischen Varianten. Die `allowSchemaOnly`-Verzweigung erhält die `QueryValidationService`-Ausnahme.
      6. Stage 6a: `bool writeAllowed = accessLevel == AccessLevel.ReadWrite; if (!writeAllowed && !_readOnlyGuard.IsQuerySafe(query)) return SqlToAiError.WriteOperationBlocked("The query contains mutating SQL keywords and was rejected.");` (Text identisch zu heute, exakt übernommen aus den drei Services, die ihn nutzen — `QueryComparisonService`'s „One or both queries…" wird auf den Standardtext zurückgeführt, siehe `Notes`).
      7. Stage 6b: `if (SqlMultiStatementDetector.ContainsMultipleStatements(query)) return SqlToAiError.MultipleStatementsForbidden();` (Aufruf identisch zu heute).
      8. Erfolgsfall: `return new QuerySafetyCheckResult(accessLevel, writeAllowed);`.
    - Die Methode bleibt unter 30 Zeilen Body, weit unter `MaxMethodLineCount = 60`.
    - `#nullable enable` am Dateianfang, `using SqlToAi.Domain;` für `AccessLevel` und `Result`, `using SqlToAi.Security;` für die drei Guard-Interfaces, `using SqlToAi.Database;` für `SqlMultiStatementDetector` und `SqlToAiError`. Namespace `SqlToAi.Database` (passt zu den Konsumenten und vermeidet eine zirkuläre Abhängigkeit `Security → Database`).
  - In `Program.cs:177-180` neue Zeile **nach** den bestehenden drei Security-Registrierungen (Reihenfolge: `ISecurityGuard`, `IAccessLevelProvider`, `IReadOnlyGuard`, **dann** `IQuerySafetyValidator`): `services.AddSingleton<IQuerySafetyValidator, QuerySafetyValidator>();` (gleicher Lifetime wie die drei anderen Security-Komponenten — die Pipeline ist zustandslos und darf prozessweit geteilt werden, kein Cache-Bedarf).
- **Warum:** Single Source of Truth für die Guardrail-Validierung; verhindert zukünftigen Refactoring-Drift zwischen den vier Services. Die Signatur (`allowSchemaOnly = false`) bewahrt die bestehende `QueryValidationService`-Sonderbehandlung von `SchemaOnly`, ohne die anderen drei Services zu zwingen, sie mitzuziehen.

### item-02: QueryExecutionService + QueryValidationService auf die Pipeline migrieren — `src/SqlToAi/Database/QueryExecutionService.cs` + `QueryValidationService.cs`

- **Was QueryExecutionService.cs:**
  - Constructor-Signatur ändern: die Parameter `ISecurityGuard securityGuard`, `IAccessLevelProvider accessLevelProvider`, `IReadOnlyGuard readOnlyGuard` ersetzen durch einen einzigen Parameter `IQuerySafetyValidator querySafetyValidator` (Reihenfolge: zuerst `IDatabaseConnectionFactory`, dann der Validator, dann `AnonymizationDependencies`, dann `IOptions<SqlToAiOptions>`, dann `ILogger<…>`). Damit sinkt die Constructor-Dependency-Anzahl von 7 Feldern auf 4 Felder.
  - Felder `_securityGuard`, `_accessLevelProvider`, `_readOnlyGuard` löschen; Feld `_querySafetyValidator` vom Typ `IQuerySafetyValidator` hinzufügen.
  - In `ExecuteQueryAsync` Zeilen 99-136: die kompletten Stages 1-5 (Parameter, Whitelist, AccessLevel, ReadOnlyGuard, MultiStatement) durch **einen** Aufruf ersetzen:
    ```csharp
    var safetyResult = await _querySafetyValidator
        .ValidateQuerySafetyAsync(databaseName, query, allowSchemaOnly: false, cancellationToken)
        .ConfigureAwait(false);
    if (safetyResult.IsFailure)
    {
        return safetyResult.Error;
    }
    var (accessLevel, writeAllowed) = (safetyResult.Value.AccessLevel, safetyResult.Value.IsWriteAllowed);
    ```
    Der Rest der Methode (Zeilen 138-153) bleibt strukturell unverändert, **inklusive** der Zeile `bool anonymize = accessLevel == AccessLevel.ReadOnlyAnonymized;` (Zeile 143) — die Variable `accessLevel` kommt jetzt aus dem `QuerySafetyCheckResult`.
- **Was QueryValidationService.cs:**
  - Constructor-Signatur analog: `ISecurityGuard`/`IAccessLevelProvider`/`IReadOnlyGuard` ersetzen durch `IQuerySafetyValidator`.
  - Felder entsprechend konsolidieren.
  - In `ValidateQueryAsync` Zeilen 66-104: Stages 1-5 durch einen Aufruf ersetzen, diesmal **mit** `allowSchemaOnly: true` (bewahrt die bestehende Asymmetrie):
    ```csharp
    var safetyResult = await _querySafetyValidator
        .ValidateQuerySafetyAsync(databaseName, query, allowSchemaOnly: true, cancellationToken)
        .ConfigureAwait(false);
    if (safetyResult.IsFailure)
    {
        return safetyResult.Error;
    }
    ```
    Die `writeAllowed`/`AccessLevel`-Information wird in `QueryValidationService` nach der Pipeline **nicht** weiter benötigt (anders als in `QueryExecutionService`) — die Methode läuft nur unter `SET PARSEONLY ON` und rolled immer zurück, daher reicht die Fehler-Frage.
- **Warum:** Beide Services verlieren die drei redundanten Security-Dependencies; die Pipeline-Kapselung eliminiert die Duplikation. Die `allowSchemaOnly: true`-Unterscheidung ist **eine** Zeile am Call-Site, die die historische Asymmetrie dokumentiert (statt sie als undokumentierten Sonderfall in einem vierten Service zu verstecken).
- **Linter-Hinweis:** Constructor-Dependencies sinken von 7 auf 4 (für `QueryExecutionService`) bzw. von 6 auf 3 (für `QueryValidationService`) — beide weit unter dem `MaxConstructorDependencies = 5`-Limit aus `AiNetLinter.mdc`. Methode bleibt unter dem `MaxMethodLineCount = 60`-Limit.

### item-03: PerformanceMeasurementService + QueryComparisonService auf die Pipeline migrieren — `src/SqlToAi/Database/PerformanceMeasurementService.cs` + `QueryComparisonService.cs`

- **Was PerformanceMeasurementService.cs:**
  - Constructor-Signatur analog: `ISecurityGuard`/`IAccessLevelProvider`/`IReadOnlyGuard` ersetzen durch `IQuerySafetyValidator`.
  - Felder `_securityGuard`/`_accessLevelProvider`/`_readOnlyGuard` löschen, Feld `_querySafetyValidator` hinzufügen.
  - Die Methode `ValidateSecurityGuards` (Zeilen 126-145) **komplett löschen** — ihre Logik wandert in den Validator.
  - In `MeasurePerformanceAsync` Zeilen 66-82 ersetzen durch:
    ```csharp
    var validationError = ValidateArgs(args);
    if (validationError != null) { return validationError; }

    var safetyResult = await _querySafetyValidator
        .ValidateQuerySafetyAsync(args.DatabaseName, args.Query, allowSchemaOnly: false, cancellationToken)
        .ConfigureAwait(false);
    if (safetyResult.IsFailure)
    {
        return safetyResult.Error;
    }
    ```
    Die statische `ValidateArgs`-Methode (Zeilen 113-124) bleibt unverändert — sie prüft die Argumente auf leer, nicht die Sicherheits-Guards, und passt nicht in den Validator (sie nimmt das args-Record, nicht `(databaseName, query)`).
- **Was QueryComparisonService.cs (Spezialfall 2 Queries):**
  - Constructor-Signatur analog.
  - Felder konsolidieren.
  - `ValidateSecurityGuards` (Zeilen 134-153) **komplett löschen**.
  - In `CompareQueriesAsync` Zeilen 66-83 ersetzen durch zwei aufeinanderfolgende Validator-Calls (einer pro Query), wobei der erste Fehler durchgereicht wird:
    ```csharp
    var validationError = ValidateArgs(args);
    if (validationError != null) { return validationError; }

    var safetyResultA = await _querySafetyValidator
        .ValidateQuerySafetyAsync(args.DatabaseName, args.QueryA, allowSchemaOnly: false, cancellationToken)
        .ConfigureAwait(false);
    if (safetyResultA.IsFailure) { return safetyResultA.Error; }

    var safetyResultB = await _querySafetyValidator
        .ValidateQuerySafetyAsync(args.DatabaseName, args.QueryB, allowSchemaOnly: false, cancellationToken)
        .ConfigureAwait(false);
    if (safetyResultB.IsFailure) { return safetyResultB.Error; }
    ```
    Die 2-Query-Spezifika (verschiedene Texte für „eine oder beide Queries mutierend" / „eine oder beide Multi-Statement") werden auf den einheitlichen Text des Validators zurückgeführt — siehe `Notes`.
- **Warum:** Beide Services verlieren die redundanten Dependencies; `PerformanceMeasurementService` verliert eine private Methode (Reduktion der Klassen-Footprint-Linien); `QueryComparisonService` macht die 2-Query-Behandlung **explizit** statt sie in einer 4-Stage-Sondermethode zu verstecken, und nutzt die Pipeline-Kapselung zweimal. Der Standardfehlertext „The query contains mutating SQL keywords and was rejected." (single-query) ist im 2-Query-Kontext ein leichter Bedeutungsverlust („The query" statt „One or both queries"), der durch die Vereinheitlichung des 4×-DRY-1-Gewinns mehr als aufgewogen wird — und durch keine bestehende Test-Assertion abgedeckt ist (Tests prüfen nur den Error-Code, nicht den Text).

### item-04: Tests anpassen — `FakeQuerySafetyValidator` einführen + 4 Service-Testklassen umstellen — `tests/SqlToAi.Tests/Database/QueryExecutionServiceMockDb.cs` + 4 `*ServiceTests.cs`

- **Was `QueryExecutionServiceMockDb.cs`:**
  - Neue Datei-intern Klasse hinzufügen (am Ende der bestehenden Fake-Klassen, vor den Connection-Factory-Mocks): `internal sealed class FakeQuerySafetyValidator : IQuerySafetyValidator` mit zwei Konstruktor-Varianten:
    - `FakeQuerySafetyValidator(QuerySafetyCheckResult result)` — für den Happy Path: liefert immer diesen Erfolgswert, egal welcher `databaseName`/`query` hereinkommt.
    - `FakeQuerySafetyValidator(SqlToAiError error)` — für den Failure Path: liefert immer diesen Fehler.
    - Die Methode `ValidateQuerySafetyAsync` ist `async Task<…>` und gibt `Result<QuerySafetyCheckResult>` zurück.
  - Die bestehenden `FakeSecurityGuard`/`FakeAccessLevelProvider`/`FakeReadOnlyGuard` (Zeilen 54-68) **bleiben unverändert** — sie werden weiterhin von `IndexSuggestionServiceTests` und potenziellen zukünftigen Tests gebraucht. EPIC-03 (DRY-T1) ist für die Bündelung der Fakes in `TestSupport/` zuständig, nicht dieser Step.
- **Was `QueryExecutionServiceTests.cs:27-43`** (`BuildService`-Helper): die drei Fake-Parameter (`new FakeSecurityGuard(isAllowed)`, `new FakeAccessLevelProvider(accessLevel)`, `new FakeReadOnlyGuard(readOnlySafe)`) ersetzen durch eine **einzige** `FakeQuerySafetyValidator`-Instanz, die das Verhalten abbildet:
  - `isAllowed = true` + `accessLevel = ReadOnly` + `readOnlySafe = true` (Happy-Path-Default) → `new FakeQuerySafetyValidator(new QuerySafetyCheckResult(accessLevel, isWriteAllowed: accessLevel == AccessLevel.ReadWrite))`.
  - `isAllowed = false` (Test `DatabaseNotAllowed`) → `new FakeQuerySafetyValidator(SqlToAiError.SafetyCheckFailed("BlockedDb"))`.
  - `accessLevel in {None, SchemaOnly}` (Test `AccessLevelTooLow`) → `new FakeQuerySafetyValidator(SqlToAiError.WriteOperationBlocked(...))` mit dem exakt dem Text, den der Validator produziert.
  - `readOnlySafe = false` (Test `MutatingQuery`) → `new FakeQuerySafetyValidator(SqlToAiError.WriteOperationBlocked("The query contains mutating SQL keywords and was rejected."))`.
  - Die Tests, die explizit den `ReadWrite`-Bypass prüfen (Zeilen 134-181), bekommen `new FakeQuerySafetyValidator(new QuerySafetyCheckResult(AccessLevel.ReadWrite, isWriteAllowed: true))` — die Logik im Service (Zeile 124 + Commit-vs-Rollback in Zeilen 188-203) bleibt unverändert, nur die Quelle der `writeAllowed`/`accessLevel`-Information wechselt vom `_accessLevelProvider`/`_readOnlyGuard` zum `_querySafetyValidator.Value`.
- **Was `QueryValidationServiceTests.cs:29-45`:** analog — alle `BuildService`-Aufrufer bekommen `new FakeQuerySafetyValidator(...)` statt der drei Einzelfakes. Test `Reject_SpExecuteSql_BeforeTouchingDatabase` (Zeile 137) übergibt heute einen **echten** `ReadOnlyGuard` an die Service-Factory — das wird obsolet, weil die Pipeline-Validierung nicht mehr im Service liegt. Ersetzen durch eine `FakeQuerySafetyValidator`, die den `WriteOperationBlocked`-Fehler für `sp_executesql`-Queries liefert. **Achtung:** der heutige Test beweist zusätzlich, dass die `sp_executesql`-Erkennung **vor dem DB-Connect** greift (Assert.Null auf `factory.LastConnection`); diese Assertion bleibt gültig, weil die Pipeline VOR dem DB-Connect läuft. Die `sp_executesql`-Regex-Logik selbst wird ab EPIC-03 durch dedizierte `QuerySafetyValidatorTests` abgedeckt — dieser Step verlagert sie nur von der Service- auf die Validator-Ebene.
- **Was `PerformanceMeasurementServiceTests.cs:19-32`:** analog — `BuildService` vereinfacht sich auf `(bool isAllowed = true, AccessLevel accessLevel = AccessLevel.ReadOnly, IReadOnlyGuard? _ = null)` mit dem dritten Parameter deprecated/ignoriert; die Tests werden auf `FakeQuerySafetyValidator` umgestellt. Der `readOnlyGuard`-Parameter kann komplett entfallen, weil er nur durchgereicht wurde.
- **Was `QueryComparisonServiceTests.cs:19-32`:** analog — `BuildService` vereinfacht sich; jeder Test bekommt eine `FakeQuerySafetyValidator`, die so konfiguriert ist, dass die 2-Query-Validierung das gewünschte Verhalten (Pass/Fail) zeigt. Für den `MutatingQuery`-Test muss die Fake so verdrahtet werden, dass `QueryA` (mit `DROP TABLE`) den `WriteOperationBlocked`-Fehler liefert; das Verhalten ist im Test-Code dann über `args.QueryA`-Inspektion **nicht** mehr nötig, weil der Validator nicht wissen kann, dass Query B ebenfalls kommt — der Test fokussiert sich auf den **ersten** Fehler (das heutige Verhalten).
- **Warum:** Die 4 Service-Tests prüfen ab diesem Step nur noch „Service leitet Validator-Fehler durch und respektiert den Validator-Erfolg" — die Pipeline-Logik selbst ist **separat** in `QuerySafetyValidatorTests` zu testen, was in EPIC-03 (DRY-T3-Konsolidierung) passiert. Die Test-Konstruktoren werden **kleiner** und damit besser lesbar; sie folgen jetzt dem Muster „ein Fake, ein Verhalten".

## Tests

- [ ] Bestehende Tests in `QueryExecutionServiceTests.cs` bleiben grün: `EmptyDatabase`/`EmptyQuery` (InvalidParameters), `DatabaseNotAllowed` (SafetyCheckFailed), `AccessLevelTooLow` Theory (None/SchemaOnly, WriteOperationBlocked), `MutatingQuery` (WriteOperationBlocked), `MultipleStatements` Theory (MultipleStatementsForbidden), `AllowMutatingQuery_AndCommit_WhenReadWrite` (Erfolgsfall + Commit-Count), `StillRollBack_WhenAccessLevelIsNotReadWrite`, `StillForbidMultipleStatements_WhenWriteAllowed`.
- [ ] Bestehende Tests in `QueryValidationServiceTests.cs` bleiben grün: `EmptyDatabase`/`EmptyQuery`, `DatabaseNotAllowed`, `AccessLevelNone`, `MutatingQuery_AndAccessLevelIsNotReadWrite`, `NotBlockMutatingQuery_WhenReadWrite`, `Reject_SpExecuteSql_BeforeTouchingDatabase` (jetzt über `FakeQuerySafetyValidator` statt echten `ReadOnlyGuard`), `MultipleStatements_RegardlessOfAccessLevel` Theory, plus die 5 Transaktion/Timeout-Tests.
- [ ] Bestehende Tests in `PerformanceMeasurementServiceTests.cs` bleiben grün: 6 Guardrail-Tests + ShowPlan-Tests (letztere unangetastet).
- [ ] Bestehende Tests in `QueryComparisonServiceTests.cs` bleiben grün: 6 Guardrail-Tests.
- [ ] **`IndexSuggestionServiceTests.cs` bleibt unangetastet** und grün (verwendet weiterhin `FakeSecurityGuard` + `FakeAccessLevelProvider`).
- [ ] `dotnet build SqlToAi.slnx` — 0 Warnungen, 0 Fehler (TreatWarningsAsErrors).
- [ ] `dotnet test SqlToAi.slnx` — alle 523+ Tests grün (gleiche Anzahl wie vor dem Step, da keine Tests hinzugefügt oder gelöscht werden).
- [ ] `RunLinterShouldBeClean` läuft real durch (AiNetLinter ist gemäß step-001-Review unter `C:\Daten\AiNetLinter-win-x64\` installiert) und ist grün.

**Keine neuen Tests** in diesem Step — Konsolidierung der 33 redundanten Negativ-Tests in dedizierte `QuerySafetyValidatorTests.cs` ist EPIC-03 (DRY-T3). Dieser Step ist reine Pipeline-Extraktion; die 33 bestehenden Tests werden 1:1 auf den neuen Mock umgestellt und müssen weiterhin grün sein.

## Definition of Done

- [ ] Alle 4 Items umgesetzt (item-01 Validator+DI, item-02 Execution+Validation, item-03 Performance+Comparison, item-04 Tests)
- [ ] `dotnet build SqlToAi.slnx` 0/0 (Warnungen/Fehler)
- [ ] `dotnet test SqlToAi.slnx` alle Tests grün (gleiche Anzahl wie vor dem Step)
- [ ] `AiNetLinter` (verfügbar) läuft grün
- [ ] Conventional Commit auf aktuellem Branch (deutsch, imperativ; ein Commit für den ganzen Step)
- [ ] `step-002/step-result.md` geschrieben mit Diff-Statistik und Item-Status
- [ ] `status` in `step-plan.md` von `open`/`in_progress` auf `done (pending audit)` gesetzt
- [ ] `codemap.md` aktualisiert: Neue Datei `src/SqlToAi/Database/QuerySafetyValidator.cs` eingetragen, alte Pipeline-Inline-Stellen in den 4 Services als „obsolet — durch IQuerySafetyValidator abgelöst" markiert
- [ ] `tech-debt.md` aktualisiert (falls Beobachtungen aus der Implementierung): v. a. die vereinheitlichten Fehlertexte als TD-Eintrag `mittel, nein (Architektur-Ermessen)` markieren, falls der Nutzer die operationsspezifischen Texte zurückhaben will

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc` §2 *Architektur- & Guardrail-Konzepte* — *Datenbank-Zugriffssteuerung & Whitelisting*, *Dynamischer Access- & Permission-Check*, *Mehrstufiger Schreibschutz (Read-Only Guard)*. Die Pipeline ist die zentrale Implementierung dieser Richtlinien — die Extraktion in `QuerySafetyValidator` macht die Einhaltung **sichtbar** und **prüfbar** an einer Stelle statt verstreut in vier Services.
- `.agents/rules/SqlToAiRichtlinien.mdc` §5 *Qualitätsdrift-Prävention* — `Result-Pattern` (`Result<QuerySafetyCheckResult>`), Zero-Warning-Direktive (Build muss 0/0 bleiben).
- `.agents/rules/AiNetLinter.mdc` — `EnforceSealedClasses` (Validator `internal sealed class QuerySafetyValidator`, Interface-Sichtbarkeit `public interface IQuerySafetyValidator`, Record bleibt unsealed per Default — Records mit nur primären Constructor-Parametern brauchen kein explizites `sealed record`), `MaxConstructorDependencies = 5` (4 Services reduzieren von 5/6 auf 3 Dependencies, alle unter Limit; der Validator selbst hat 3 Dependencies, unter Limit), `MaxMethodLineCount = 60` (Pipeline-Methode bleibt unter 30 Zeilen, keine Aufteilung nötig), `MaxLineCount = 500` pro Datei (neue Datei bleibt unter 80 Zeilen), `EnforceNullableEnable` (jede neue/berührte `.cs`-Datei hat `#nullable enable` am Anfang).

## Bekannte Ausnahmen

- `QueryValidationServiceTests.Reject_SpExecuteSql_BeforeTouchingDatabase` (Zeile 132-144) verliert die explizite Bindung an den **echten** `ReadOnlyGuard` — die sp_executesql-Regex-Logik wird ab diesem Step nicht mehr in diesem Test verifiziert. Begründung: der Test gehört inhaltlich zur **Pipeline** (`ReadOnlyGuard` ist heute Teil des Validators), nicht zum Service. Die sp_executesql-Erkennung wird in EPIC-03 in den dedizierten `QuerySafetyValidatorTests` verlagert. Bis dahin ist der Test eine schwächere Variante (er prüft nur, dass ein WriteOperationBlocked-Fehler durchgereicht wird), aber nicht falsch.
- Falls der AiNetLinter eine `EnforceNoSilentCatch`-Warnung auf den leeren `catch (Exception ignored)` in `PerformanceMeasurementService.ParseExecutionPlanXml` (Zeile 327) wirft: das ist **nicht** in diesem Step zu fixen (vorbestehender Code, nicht in der Pipeline), sondern ggf. als TD-Eintrag zu notieren.

## Code-Skizze (optional)

```csharp
// src/SqlToAi/Database/QuerySafetyValidator.cs
#nullable enable

using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Database;

public sealed record QuerySafetyCheckResult(AccessLevel AccessLevel, bool IsWriteAllowed);

public interface IQuerySafetyValidator
{
    Task<Result<QuerySafetyCheckResult>> ValidateQuerySafetyAsync(
        string databaseName,
        string query,
        bool allowSchemaOnly = false,
        CancellationToken cancellationToken = default);
}

internal sealed class QuerySafetyValidator : IQuerySafetyValidator
{
    private readonly ISecurityGuard _securityGuard;
    private readonly IAccessLevelProvider _accessLevelProvider;
    private readonly IReadOnlyGuard _readOnlyGuard;

    public QuerySafetyValidator(
        ISecurityGuard securityGuard,
        IAccessLevelProvider accessLevelProvider,
        IReadOnlyGuard readOnlyGuard)
    {
        _securityGuard = securityGuard;
        _accessLevelProvider = accessLevelProvider;
        _readOnlyGuard = readOnlyGuard;
    }

    public async Task<Result<QuerySafetyCheckResult>> ValidateQuerySafetyAsync(
        string databaseName,
        string query,
        bool allowSchemaOnly = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return SqlToAiError.InvalidParameters("Database name must not be empty.");
        }
        if (string.IsNullOrWhiteSpace(query))
        {
            return SqlToAiError.InvalidParameters("Query must not be empty.");
        }

        if (!_securityGuard.IsDatabaseAllowed(databaseName))
        {
            return SqlToAiError.SafetyCheckFailed(databaseName);
        }

        var accessLevel = await _accessLevelProvider
            .GetAccessLevelAsync(databaseName, cancellationToken)
            .ConfigureAwait(false);

        if (accessLevel == AccessLevel.None
            || (!allowSchemaOnly && accessLevel == AccessLevel.SchemaOnly))
        {
            return SqlToAiError.WriteOperationBlocked(
                $"Database '{databaseName}' is not permitted to run this query (AccessLevel: {accessLevel}).");
        }

        bool writeAllowed = accessLevel == AccessLevel.ReadWrite;
        if (!writeAllowed && !_readOnlyGuard.IsQuerySafe(query))
        {
            return SqlToAiError.WriteOperationBlocked(
                "The query contains mutating SQL keywords and was rejected.");
        }

        if (SqlMultiStatementDetector.ContainsMultipleStatements(query))
        {
            return SqlToAiError.MultipleStatementsForbidden();
        }

        return new QuerySafetyCheckResult(accessLevel, writeAllowed);
    }
}
```

```csharp
// Konstruktor-Reduktion, Beispiel QueryExecutionService
public QueryExecutionService(
    IDatabaseConnectionFactory connectionFactory,
    IQuerySafetyValidator querySafetyValidator,
    AnonymizationDependencies anonymization,
    IOptions<SqlToAiOptions> options,
    ILogger<QueryExecutionService> logger)
{
    _connectionFactory = connectionFactory;
    _querySafetyValidator = querySafetyValidator;
    _anonymizer = anonymization.Anonymizer;
    // ... (Rest unverändert)
}

// ExecuteQueryAsync — Stages 1-5 ersetzt:
var safetyResult = await _querySafetyValidator
    .ValidateQuerySafetyAsync(databaseName, query, allowSchemaOnly: false, cancellationToken)
    .ConfigureAwait(false);
if (safetyResult.IsFailure)
{
    return safetyResult.Error;
}
var accessLevel = safetyResult.Value.AccessLevel;
var writeAllowed = safetyResult.Value.IsWriteAllowed;
// ... (Zeilen 138-153 unverändert)
```

## Notes

- **Migrationsreihenfolge (wichtig für Coder):** Erst `item-01` (Validator + DI komplett), dann erst `item-02` und `item-03` (Services umstellen), dann `item-04` (Tests umstellen). Die Reihenfolge ist hart: ein Service, der den neuen Constructor-Signatur hat aber `IQuerySafetyValidator` nicht aus der DI bekommt, kompiliert nicht. Nach `item-01` ist das Programm weiterhin lauffähig (alter Code-Pfad, neue Klasse ungenutzt), nach `item-02` und `item-03` migriert ohne Test-Anpassungen wären die Tests rot, nach `item-04` ist alles grün.
- **Risiko-Management: keine Fallback-Strategie** — der Orchestrator-Brief schließt explizit `_querySafetyValidator ?? LegacyValidation(...)` aus. Die Pipeline IST die Single Source of Truth, ab diesem Step kompromisslos. Wenn der Coder während der Implementierung auf Edge-Cases stößt (z. B. unerwartete Tests, die Annahmen über die Constructor-Signatur treffen), ist die Lösung „Test anpassen" oder „Service-Aufrufstelle anpassen", nicht „Legacy-Branch einbauen".
- **Spec-Konflikt medium+batch:** Die Spec §10.6 schreibt „medium/high wird nie gebatcht", bezieht sich aber auf **Micro-Batches** (mehrere einzeln triviale Low-Risk-Änderungen). EPIC-02 ist ein **einzelner** Architektur-Schritt mit 4 organisatorischen Items, der Nutzer hat explizit „GROSSE Code-Pakete" gefordert. Der Schritt bleibt atomar (Validator + 4 Services + DI + Tests), weil ein Split in zwei Steps entweder einen kaputten Zwischenstand (Validator ohne Konsumenten) oder eine halbe Migration (3 von 4 Services migriert) produzieren würde. Der Coder behandelt den Step als **eine** Migration und liefert **einen** Commit.
- **8-Item-Limit:** 4 Items ist deutlich unter dem `max_batch_items = 8`-Default aus §10.6; das Diff wird geschätzt 200-300 Zeilen (4 Service-Konstruktoren kürzen ~30 Zeilen, 2 Services verlieren je eine private Methode ~25 Zeilen, Validator-Datei ~80 Zeilen neu, Test-Anpassungen ~50 Zeilen, DI-Zeile 1 Zeile). Damit **leicht** unter dem `max_batch_diff_lines = 40`-Default — der Schritt ist also **kein** Micro-Batch im Sinne der Spec, sondern ein regulärer großer Schritt mit Item-Tracking.
- **2-Query-Vereinheitlichung im ComparisonService:** Der alte Text „One or both queries contain mutating SQL keywords and were rejected." verschwindet zugunsten des Standardtexts „The query contains mutating SQL keywords and was rejected.". Wenn der Nutzer die 2-Query-Spezifika zurückhaben will, ist das ein eigener TD-Eintrag (Priorität niedrig, Architektur-Ermessen) — der Standardtext ist im Vereinheitlichungs-Kontext die richtige Wahl, weil 4×DRY-1 (gleicher Text in 3 anderen Services) wichtiger ist als die mini-genauere Information in einem Service.
- **Konstruktor-Dependency-Count `QueryExecutionService`:** nach Migration **4** Dependencies (ConnectionFactory, IQuerySafetyValidator, AnonymizationDependencies als Composite, IOptions, ILogger — 5 Argumente im Constructor, 4 Dependency-„Slots" wenn AnonymizationDependencies als ein Slot zählt). `MaxConstructorDependencies = 5` ist erfüllt; die Zeile 4 wird vom Linter als „viele Dependencies" markiert, das ist mit der existierenden `AnonymizationDependencies`-Composite-Lösung (eingeführt zur Reduktion der damaligen 7 Dependencies) aber bereits etabliert.
- **MCP-Output-Vertrag:** Die Fehler-Codes (`SQL-AI-0001` InvalidParameters, `SQL-AI-0101` MultipleStatementsForbidden, `SQL-AI-0104` SafetyCheckFailed, `SQL-AI-0107` WriteOperationBlocked) bleiben unverändert — keine Auswirkung auf den MCP-Output, kein Sync-Bedarf in `docs/architecture-spec.md` oder `README.md`.
- **Kein Doku-Sync-Bedarf** in `architecture-spec.md`/`README.md` für EPIC-02: die Architektur-Spec dokumentiert heute die Guardrails als Richtlinie in §2, nicht als konkrete Klassen — die interne Refaktorierung ändert das Spec-Level nicht. Die README führt keine konkreten Klassen auf.
- **Coder-Hinweis (subtil):** `Result<T>` ist im Solution-eigenen `SqlToAi.Domain`-Namespace — der Validator importiert `using SqlToAi.Domain;` sowohl für `Result<>` als auch für `AccessLevel`. Kein Konflikt mit `using SqlToAi.Security;` oder `using SqlToAi.Database;`.
- **Empfohlene Commit-Strategie:** ein einzelner Conventional Commit (deutsch, imperativ), z. B. `refactor: führe zentrale IQuerySafetyValidator-Pipeline ein und migriere die vier Guardrail-Services [audit-try-magicvalues]`. Diff-Statistik voraussichtlich 11 Dateien geändert (1 neu + 5 Produktion + 5 Test), +~250/−~140 Zeilen.
