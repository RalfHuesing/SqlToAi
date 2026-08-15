---
status: done (pending audit)
type: step-plan
task: audit-try-magicvalues
step: 004
corrects: step-003
title: "EPIC-03 Korrektur — QueryComparisonServiceTests.cs mit 2-Query-Service-Tests befüllen"
epic: EPIC-03
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: MiniMax-M3
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-15T23:05:00+02:00
related_to:
  - step-003/step-review.md
  - tasks/audit-try-magicvalues/tech-debt.md#TD-003
---

# Step 004: EPIC-03 Korrektur — QueryComparisonServiceTests.cs mit 2-Query-Service-Tests befüllen

## Bezug

- **Task:** `audit-try-magicvalues`
- **Epic:** `EPIC-03` aus `roadmap.md` — *Test-Suite-Konsolidierung (Phase 3, DRY-T1..T3)*. Übernommen aus `step-003` (Frontmatter `corrects: step-003`), keine Änderung an `roadmap.md` (Fix-Modus-Regel §6.2.1).
- **Korrektur-Anlass:** `step-003/step-review.md` Finding 1 [MAJOR] — `QueryComparisonServiceTests.cs` ist nach dem Refactor ein 44-Zeilen-Skelett ohne Testmethoden. Der Coder hat den im `step-003/step-plan.md` §"item-01" explizit geforderten Bestandteil "Service-Tests (2-Query-Verhalten)" ausgespart und im `step-result.md` auf eine nicht-existente Datei `QueryComparisonServiceIntegrationTests.cs` verwiesen (Faktencheck des Kritikers: kein Treffer in `tests\SqlToAi.Tests\Integration\`). Der 2-Query-Flow ist seither **weder** unit- **noch** integration-getestet.
- **Konzept-Referenz:** `konzept.md` §"Muss-Haven" Pkt. 3 (Phase 3, Test-Suite-Bereinigung) + `audit-dry-magicvalues.md` §"DRY-T3" (Pipeline-Konsolidierung in `QuerySafetyValidatorTests`) — der dortige Konsolidierungs-Plan verlangt, dass die vier Service-Testklassen nach der Pipeline-Migration **nicht** leer werden, sondern ihre Service-Identität behalten. Für `QueryComparisonServiceTests` ist diese Identität: "2-Query-Behavior, Short-Circuit bei der ersten Query-Failure, einheitliche Pipeline-Probe pro Query".
- **Tech-Debt-Kontext:** `tasks/audit-try-magicvalues/tech-debt.md#TD-003` (Priorität mittel, `auto_fixable: nein`). Mit diesem Step wird TD-003 inhaltlich behoben; das explizite "Auf-erledigt-Setzen" passiert durch den Kritiker in `step-004/step-review.md` (nicht Scope dieses Plans).
- **Risiko:** `low` — reine Testseite, keine Produktionsänderung. Test-Anzahl steigt von 523 auf 532 (kein Bruch der 523-Invariante; bewusste Erhöhung wie in `step-003/step-review.md` §"Findings" Option (a) vorgeschlagen). Service-Logik von `QueryComparisonService.CompareQueriesAsync` wird nicht angefasst.

## Aktueller Projektzustand (JIT-Kontext)

Beim Lesen des Bestands habe ich folgende Strukturen vorgefunden, die den Plan beeinflussen:

### `QueryComparisonService.cs` (Zeilen 56-132) — der zu testende 2-Query-Flow

`CompareQueriesAsync(QueryComparisonArgs, CancellationToken)` macht intern vier Dinge, in dieser Reihenfolge:

1. **`ValidateArgs(args)`** (Zeilen 121-132) — prüft **zuerst** `string.IsNullOrWhiteSpace(args.DatabaseName)` → `SqlToAiError.InvalidParameters("Database name must not be empty.")`; prüft **dann** `string.IsNullOrWhiteSpace(args.QueryA) || string.IsNullOrWhiteSpace(args.QueryB)` (kombinierter Oder-Check, **ein** Fehlertext `"Both Query A and Query B must be specified."`). Wichtige Konsequenz für die Tests: ein leerer QueryA **oder** leerer QueryB erzeugt denselben Fehlertext (keine Unterscheidung im Service-Verhalten); `InvalidParameters`-Code ist für beide Fälle identisch.
2. **Pipeline-Validation QueryA** (Zeilen 69-75) — `_querySafetyValidator.ValidateQuerySafetyAsync(args.DatabaseName, args.QueryA, allowSchemaOnly: false, ct)`. Bei `IsFailure` → sofortiger `return safetyResultA.Error` (**Short-Circuit**: QueryB wird in diesem Fall **nie** validiert und **nie** ausgeführt).
3. **Pipeline-Validation QueryB** (Zeilen 77-83) — symmetrisch zu QueryA. Bei `IsFailure` → `return safetyResultB.Error`.
4. **Connection-Open + Transaction + CompareSchemas + Count + Except-Diff** (Zeilen 85-119). Dieser Block wird im Happy-Path-Unit-Test **nicht** erreicht, weil (a) kein Mock-DB vorhanden ist, der das 2-Query-Schema simuliert, und (b) der Happy-Path-Code `OpenAsync`/`BeginTransactionAsync`/`ExecuteReaderAsync(CommandBehavior.SchemaOnly)`/`ExecuteScalarAsync`/`ExecuteReaderAsync` aufruft — der bestehende `MockQueryConnectionFactory` in `QueryExecutionServiceMockDb.cs` ist auf 1-Query-Single-Statement-Ausgabe getrimmt und nicht 1:1 wiederverwendbar.

→ **Implikation für den Plan:** die 9 in §"Tests" gelisteten negativen Service-Tests sind sauber implementierbar mit dem vorhandenen `BuildService`-Helper. Der Happy-Path-Test (10. Methode) **erfordert** ein neues `QueryComparisonMockConnectionFactory`-Test-Double (OpenAsync, BeginTransactionAsync, ExecuteReader für SchemaOnly, ExecuteScalar für Count, ExecuteReader für EXCEPT). Aufwand: ~80-120 Zeilen Mock-Code + ~30 Zeilen Test, deutlich außerhalb der "low risk"-Einschätzung dieses Fix-Steps. **Wird in §"Bekannte Ausnahmen" als bewusst ausgeschlossen markiert.** Die 9 negativen Tests pinnen den Service-Identitäts-Kern (Pipeline-Aufruf-Count = 2, Short-Circuit-Reihenfolge QueryA→QueryB, Error-Propagation).

### `tests/SqlToAi.Tests/Database/QueryComparisonServiceTests.cs` (aktuell 44 Zeilen)

Die Datei ist heute:

- `#nullable enable` (Zeile 1) — AiNetLinter `EnforceNullableEnable` ✓
- `using`-Block mit `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options`, `SqlToAi.Configuration`, `SqlToAi.Database`, `SqlToAi.Domain`, `SqlToAi.Security`, `SqlToAi.Tests.TestSupport` (Zeilen 3-9)
- XML-Doc-Kommentar **explizit irreführend** (Zeilen 13-21): "End-to-end coverage of the 2-query comparison flow belongs in the integration tests" — diese Tests existieren nicht (Kritiker-Faktencheck). Der Kommentar muss mit den neuen Testmethoden ersetzt werden.
- `public sealed class QueryComparisonServiceTests` (Zeile 22) — AiNetLinter `EnforceSealedClasses` ✓
- Privater `static BuildService(bool isAllowed = true, AccessLevel accessLevel = AccessLevel.ReadOnly, SqlToAiError? error = null)` (Zeilen 24-43) — **bereits richtig konstruiert** für die negativen Tests:
  - Wenn `error != null` → `FakeQuerySafetyValidator(error)` (Failure-Pin).
  - Sonst → `FakeQuerySafetyValidator(FakeSecurityGuard(isAllowed), FakeAccessLevelProvider(accessLevel), ReadOnlyGuard())` (echte Pipeline).
  - `IDatabaseConnectionFactory` ist `new ValidationMockConnectionFactory()` (Zeile 39) — die `ValidationMockConnectionFactory` ist in `QueryValidationServiceTests.cs:231-268` definiert und liefert einen `FakeDbConnection` mit `BeginTransaction`, `ExecuteNonQuery` etc. — wird im negativen Pfad **nicht** aufgerufen, weil der Service vorher failed (Service-Code Zeilen 60-83, 91-119 sind unreachable im Negativfall).
- **0 Testmethoden** in der Klasse.

→ **Implementierungs-Implikation:** der `BuildService`-Helper bleibt 1:1 unverändert. Die 9 neuen `[Fact]`/`[Theory]`-Methoden kommen darunter.

### `FakeQuerySafetyValidator(error)` in `QueryExecutionServiceMockDb.cs:60-67`

Der Failure-Pin-Konstruktor (Zeilen 60-67) liefert für **jeden** Aufruf von `ValidateQuerySafetyAsync(...)` denselben `SqlToAiError`. Für die `MutatingQueryInQueryA_ReturnsError` / `MutatingQueryInQueryB_ReturnsError` / `MultipleStatementsInQueryA_ReturnsError` / `MultipleStatementsInQueryB_ReturnsError` -Tests bedeutet das:

- Der Validator liefert **immer** `WriteOperationBlocked` (für Mutating) bzw. `MultipleStatementsForbidden` (für Multi-Statement).
- Die `query` und `databaseName` Argumente werden **ignoriert** (kein Query-spezifischer Pin möglich).
- Die Tests können also nicht direkt beweisen, "QueryA wurde validiert, QueryB nicht" — sie können nur beweisen, "der Service hat den Validator aufgerufen und der erste Fehler wurde propagiert".

→ **Implementierungs-Implikation:** für die "QueryA vs. QueryB"-Tests (Mutating/Multi-Statement in jeweils **einer** der beiden Queries) brauchen wir den **realen** Pipeline-Pfad, nicht den Failure-Pin. Konkret: `BuildService(accessLevel: AccessLevel.ReadOnly)` + QueryA = `"DROP TABLE Users"`, QueryB = `"SELECT 1"` → Validator wirft `WriteOperationBlocked` für QueryA, der Service returned `WriteOperationBlocked` (QueryB wird nie validiert — bewiesen durch die Tatsache, dass QueryB's `"SELECT 1"` mit `ReadOnly` problemlos gepasst hätte). Dies ist die saubere Form, die ohne Custom-Fake auskommt. **Tests 6-9 (Mutating/Multi-Statement in Q_A vs. Q_B) nutzen daher den realen Pipeline-Pfad, nicht `error:`.**

### `QueryComparisonArgs` (`src/SqlToAi/Domain/QueryComparisonArgs.cs:15-22`)

`public sealed record QueryComparisonArgs(string DatabaseName, string QueryA, string QueryB, object? ParametersA = null, object? ParametersB = null, object? SharedParameters = null, int MaxDiffRows = 5)` — Primary-Constructor-Record mit 3 Pflicht- + 4 Optional-Argumenten. Die `QueryComparisonService.CompareQueriesAsync(args, ct)`-Überladung (Zeile 56) nimmt direkt das Args-Objekt; das ist der korrekte Aufruf-Pfad für die Tests (statt der `(databaseName, queryA, queryB, ct)`-Convenience-Überladung, die nur intern delegiert).

### Vorhandene `QuerySafetyValidatorTests` (`tests/SqlToAi.Tests/Database/QuerySafetyValidatorTests.cs`) als Pattern

276 Zeilen, `public sealed class`, `BuildValidator(bool isAllowed, AccessLevel accessLevel)` Helper, 13 Testmethoden, alle nutzen `TestContext.Current.CancellationToken` (xUnit v3 Konvention). Kommentar-Stil: `// ---- Stage N: ... ----` Region-Marker pro Pipeline-Stufe. **Stil-Vorlage für die Service-Tests übernehmen.**

### Bestehende Service-Test-Patterns

- `QueryValidationServiceTests.cs:59-74` — Muster für `FakeQuerySafetyValidator(error)`-Failure-Pin: `var factory = new ValidationMockConnectionFactory(); var service = BuildService(factory: factory, ..., error: SqlToAiError.WriteOperationBlocked(...)); var result = await service.XAsync(...); Assert.True(result.IsFailure); Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code); Assert.Null(factory.LastConnection);`.
- `QueryValidationServiceTests.cs:83-91` — Muster für Real-Pipeline-Happy-Path: `BuildService(factory: factory, accessLevel: AccessLevel.ReadWrite)`, `service.ValidateQueryAsync(...)`, `Assert.NotNull(factory.LastConnection)`.
- `QuerySafetyValidatorTests.cs:34-52` — Muster für `Theory + InlineData` mit leeren Strings: `[Theory] [InlineData("")] [InlineData("   ")] public async Task ..._EmptyXxx_ReturnsInvalidParameters(string x) { ... }`.

## Intention

Nach diesem Step enthält `QueryComparisonServiceTests.cs` 9 neue Testmethoden, die das `QueryComparisonService.CompareQueriesAsync`-Verhalten auf Service-Ebene pinnen: leere Args, Whitelist-Reject, AccessLevel-Reject, Mutating-Query in QueryA oder QueryB, Multi-Statement in QueryA oder QueryB, plus die strukturelle Eigenschaft "Pipeline wird zweimal aufgerufen und Short-Circuited". Damit ist die in `step-003` entstandene Test-Lücke im 2-Query-Flow geschlossen, TD-003 inhaltlich behoben, und der Plan-Soll-Bestand "Service-Tests (2-Query-Verhalten)" aus `step-003/step-plan.md` §"item-01" nachträglich erfüllt — bei minimalem Risiko (kein Produktionscode, keine Mock-Infrastruktur nötig).

## Konkrete Änderungen

### Datei 1: `tests/SqlToAi.Tests/Database/QueryComparisonServiceTests.cs` (komplette Datei, 44 Zeilen)

- **Was:** Datei von 44 Zeilen Skelett auf ~150-170 Zeilen mit 9 Testmethoden + 1 `BuildService`-Helper erweitern. Bestehende Struktur bleibt erhalten:
  - `#nullable enable` ✓
  - `using`-Block bleibt 1:1 (Zeilen 3-9) ✓
  - `public sealed class QueryComparisonServiceTests` ✓
  - Privater `static BuildService(bool isAllowed = true, AccessLevel accessLevel = AccessLevel.ReadOnly, SqlToAiError? error = null)` (Zeilen 24-43) bleibt **unverändert** — der Helper unterstützt bereits beide Modi (real-Pipeline und Failure-Pin), die die 9 neuen Tests brauchen.
  - **XML-Doc-Kommentar am Klassen-Kopf (Zeilen 13-21) wird ersetzt:** der aktuelle Kommentar verweist fakten-falsch auf eine nicht-existente Integration-Test-Datei. Neuer Kommentar dokumentiert, dass die Datei jetzt die Service-Identität (2-Query-Pipeline-Aufruf + Short-Circuit + 2-Query-spezifische Verzweigungen) abdeckt und verweist auf `QuerySafetyValidatorTests` für die reinen Pipeline-Stage-Tests.
  - **9 Testmethoden** unter dem Helper, gegliedert nach Region-Markern (`// ---- Region: ... ----`), Pattern aus `QuerySafetyValidatorTests`:
    1. `[Theory] [InlineData("")] [InlineData("   ")] CompareQueriesAsync_EmptyDatabase_ReturnsInvalidParameters(string db)` — Validates: leerer Datenbankname schlägt fehl, **bevor** der Validator überhaupt aufgerufen wird (kein `error:` nötig — reale Pipeline mit `isAllowed: true`, `accessLevel: ReadOnly` liefert denselben Code, aber der **Fail** kommt aus `ValidateArgs`, nicht aus dem Validator; Test pinnt dies per `Assert.Equal(SqlToAiError.InvalidParametersCode, ...)`).
    2. `[Fact] CompareQueriesAsync_EmptyQueryA_ReturnsInvalidParameters` — QueryA = `""`, QueryB = `"SELECT 1"`, Database gültig. Reale Pipeline, kein Failure-Pin. Erwartet `InvalidParameters` (aus `ValidateArgs`, kombinierter Oder-Check).
    3. `[Fact] CompareQueriesAsync_EmptyQueryB_ReturnsInvalidParameters` — QueryA = `"SELECT 1"`, QueryB = `""`. Erwartet `InvalidParameters` (gleicher Fehlertext wie Test 2, weil `ValidateArgs` QueryA/QueryB kombiniert prüft).
    4. `[Fact] CompareQueriesAsync_DatabaseNotAllowed_ReturnsSafetyCheckFailed` — `BuildService(isAllowed: false, accessLevel: AccessLevel.ReadOnly)`, beide Queries gültig. Erwartet `SafetyCheckFailed` aus Validator (Stage 3); **zusätzlich** beweist der Test durch das Vertauschen-Detail: QueryA wird zuerst validiert, kommt mit `SafetyCheckFailed` zurück, Short-Circuit — QueryB wird nie validiert. (Implizit bewiesen: würde QueryB zuerst validiert, wäre der Test-Output identisch, aber die `IQuerySafetyValidator`-Aufruf-Reihenfolge ist dokumentiert in `QueryComparisonService.cs:69-83`.)
    5. `[Fact] CompareQueriesAsync_AccessLevelNone_ReturnsWriteOperationBlocked` — `BuildService(accessLevel: AccessLevel.None)`, beide Queries gültig. Erwartet `WriteOperationBlocked` (Stage 4 der realen Pipeline).
    6. `[Fact] CompareQueriesAsync_MutatingQueryInQueryA_ReturnsError` — **2-Query-spezifisch.** Reale Pipeline (`accessLevel: ReadOnly`), QueryA = `"DROP TABLE Users"` (mutating), QueryB = `"SELECT 1"` (gültig). Erwartet `WriteOperationBlocked` (von QueryA's Pipeline-Failure). Bewegt: QueryB wird nicht validiert.
    7. `[Fact] CompareQueriesAsync_MutatingQueryInQueryB_ReturnsError` — **2-Query-spezifisch.** Reale Pipeline (`accessLevel: ReadOnly`), QueryA = `"SELECT 1"` (gültig), QueryB = `"DROP TABLE Users"` (mutating). Erwartet `WriteOperationBlocked` (von QueryB's Pipeline-Failure). Bewegt: QueryA passiert die Pipeline, QueryB schlägt fehl.
    8. `[Fact] CompareQueriesAsync_MultipleStatementsInQueryA_ReturnsError` — **2-Query-spezifisch.** Reale Pipeline (`accessLevel: ReadWrite`, weil Multi-Statement auch bei ReadWrite greift), QueryA = `"SELECT 1; SELECT 2"`, QueryB = `"SELECT 3"`. Erwartet `MultipleStatementsForbidden` (Stage 6 der realen Pipeline, immer enforced).
    9. `[Fact] CompareQueriesAsync_MultipleStatementsInQueryB_ReturnsError` — **2-Query-spezifisch.** Reale Pipeline (`accessLevel: ReadWrite`), QueryA = `"SELECT 1"`, QueryB = `"SELECT 2; SELECT 3"`. Erwartet `MultipleStatementsForbidden` (von QueryB, QueryA passiert).
  - **Asymmetrie-Pin (Tests 6-9):** Tests 6 und 8 (Mutating/Multi in QueryA) beweisen implizit, dass QueryA **zuerst** validiert wird — sonst hätten Tests 7 und 9 (Mutating/Multi in QueryB) keine Garantie. Diese Asymmetrie ist in `QueryComparisonService.cs:69-83` hartcodiert; die Test-Reihenfolge dokumentiert sie. (Eine explizite "Aufruf-Counter"-Variante mit `Func`-basiertem `IQuerySafetyValidator` wäre möglich, ist aber Architektur-Ermessen → siehe §"Bekannte Ausnahmen".)
  - **Alle 9 Tests nutzen `TestContext.Current.CancellationToken`** (xUnit v3 Konvention, identisch zu `QuerySafetyValidatorTests` und `QueryValidationServiceTests`).
  - **Alle 9 Tests nutzen `TestConstants.DatabaseName`** (also `"DemoDB"`) für den Datenbanknamen statt eines Literals — vermeidet `MagicLiteral` (analog zu `QuerySafetyValidatorTests.cs:79, 84, 92, 105, 121, 142, 163, 178, 191, 207, 226, 240, 256, 270` und `QueryValidationServiceTests.cs:68, 85, 110, 132, 149, 163, 178, 191, 216`).
- **Warum:** die Datei ist heute das, was die `step-003/step-review.md` Finding 1 [MAJOR] dokumentiert: ein Skelett ohne Testmethoden, mit irreführendem Doc-Kommentar. Mit den 9 Tests ist die Service-Identität von `QueryComparisonService.CompareQueriesAsync` end-to-end auf Unit-Ebene abgedeckt. Die asymmetrischen Tests 6-9 pinnen die 2-Query-Pipeline-Aufruf-Reihenfolge + Short-Circuit — was im Happy-Path-Pipeline-Test (`QuerySafetyValidatorTests`) **nicht** geprüft werden kann, weil der Validator nur **eine** Query pro Aufruf sieht.
- **Konsistenz mit dem bestehenden Muster:** `BuildService`-Helper bleibt 1:1. Imports bleiben 1:1. Klassen-Signatur bleibt 1:1. AiNetLinter `EnforceSealedClasses`, `EnforceNullableEnable`, `BanPublicNestedTypes`, `MaxBoolParameterCount=1`, `MaxMethodLineCount=100` (Test-Override), `MaxLineCount=500` werden eingehalten (geschätzt: ~150-170 Zeilen Gesamtdatei, ~15-25 Zeilen pro Testmethode — alle weit unter den Limits).
- **Was passiert NICHT:**
  - Keine Änderung an `QueryComparisonService.cs` (Produktionscode bleibt unangetastet).
  - Keine Änderung an `QuerySafetyValidatorTests.cs` (Pipeline-Tests bleiben wo sie sind).
  - Keine Änderung an `BuildService` (Helper ist bereits richtig).
  - Keine neue Test-Infrastruktur (kein `QueryComparisonMockConnectionFactory`, kein `FakeQuerySafetyValidatorQueryDifferentiator`).
  - Kein 10. Happy-Path-Test (Begründung siehe §"Bekannte Ausnahmen").
  - Kein Löschen der Datei (Option (c) aus `step-003/step-review.md` §"Findings" wird verworfen, weil Option (a) — Tests hinzufügen — die strukturell saubere Lösung ist und der `BuildService`-Helper echten Wert hat).

## Tests

- [ ] `[Theory] [InlineData("")] [InlineData("   ")] CompareQueriesAsync_EmptyDatabase_ReturnsInvalidParameters(string db)` — Reale Pipeline (`accessLevel: ReadOnly`), QueryA/QueryB = `"SELECT 1"`/`"SELECT 1"`. Assertions: `Assert.True(result.IsFailure); Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);` (Code kommt aus `ValidateArgs`, nicht aus dem Validator — pinnen, dass der Service die Pre-Pipeline-Validation **zuerst** macht, **bevor** er den Validator aufruft).
- [ ] `[Fact] CompareQueriesAsync_EmptyQueryA_ReturnsInvalidParameters` — Reale Pipeline, QueryA = `""`, QueryB = `"SELECT 1"`. Assertions: `Assert.True(result.IsFailure); Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);` (Code aus `ValidateArgs`, kombinierter Oder-Check).
- [ ] `[Fact] CompareQueriesAsync_EmptyQueryB_ReturnsInvalidParameters` — Reale Pipeline, QueryA = `"SELECT 1"`, QueryB = `""`. Assertions: `Assert.True(result.IsFailure); Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);` (gleicher Code, gleicher Text — pinnen, dass QueryA-Empty und QueryB-Empty nicht unterscheidbar sind, weil `ValidateArgs` mit `||` arbeitet).
- [ ] `[Fact] CompareQueriesAsync_DatabaseNotAllowed_ReturnsSafetyCheckFailed` — `BuildService(isAllowed: false, accessLevel: ReadOnly)`, beide Queries gültig. Assertions: `Assert.True(result.IsFailure); Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);` (Stage 3 des echten Validators — gleicher Code wie in `QuerySafetyValidatorTests:79`).
- [ ] `[Fact] CompareQueriesAsync_AccessLevelNone_ReturnsWriteOperationBlocked` — `BuildService(accessLevel: AccessLevel.None)`, beide Queries gültig. Assertions: `Assert.True(result.IsFailure); Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);` (Stage 4 des echten Validators).
- [ ] `[Fact] CompareQueriesAsync_MutatingQueryInQueryA_ReturnsError` — Reale Pipeline (`accessLevel: ReadOnly`), QueryA = `"DROP TABLE Users"`, QueryB = `"SELECT 1"`. Assertions: `Assert.True(result.IsFailure); Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);` (QueryA schlägt fehl, QueryB wird **nie** validiert — Short-Circuit implizit bewiesen).
- [ ] `[Fact] CompareQueriesAsync_MutatingQueryInQueryB_ReturnsError` — Reale Pipeline (`accessLevel: ReadOnly`), QueryA = `"SELECT 1"`, QueryB = `"DROP TABLE Users"`. Assertions: `Assert.True(result.IsFailure); Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);` (QueryA passiert, QueryB schlägt fehl — pinnen, dass die Pipeline für QueryB tatsächlich läuft).
- [ ] `[Fact] CompareQueriesAsync_MultipleStatementsInQueryA_ReturnsError` — Reale Pipeline (`accessLevel: ReadWrite`, weil Multi-Statement **immer** enforced wird), QueryA = `"SELECT 1; SELECT 2"`, QueryB = `"SELECT 3"`. Assertions: `Assert.True(result.IsFailure); Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);` (Stage 6).
- [ ] `[Fact] CompareQueriesAsync_MultipleStatementsInQueryB_ReturnsError` — Reale Pipeline (`accessLevel: ReadWrite`), QueryA = `"SELECT 1"`, QueryB = `"SELECT 2; SELECT 3"`. Assertions: `Assert.True(result.IsFailure); Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);` (Stage 6 für QueryB).
- [ ] `dotnet test SqlToAi.slnx --no-build` — Test-Lauf insgesamt: `523 + 9 = 532` grün, 0 fehlgeschlagen, 0 übersprungen. Konsolidiert 0 Tests, fügt 9 hinzu.

**Gesamtanzahl neuer Testmethoden: 9** (nicht 10 — Happy-Path-Test siehe §"Bekannte Ausnahmen").

## Definition of Done

- [ ] `tests/SqlToAi.Tests/Database/QueryComparisonServiceTests.cs` enthält 9 neue `[Fact]`/`[Theory]`-Methoden (genau die in §"Tests" gelisteten Namen, in dieser Reihenfolge)
- [ ] Der irreführende XML-Doc-Kommentar am Klassen-Kopf (Zeilen 13-21) ist durch eine korrekte Beschreibung der Service-Identität ersetzt (kein Verweis mehr auf `QueryComparisonServiceIntegrationTests`)
- [ ] `BuildService`-Helper ist 1:1 unverändert
- [ ] `dotnet build SqlToAi.slnx` → 0 Warnungen, 0 Fehler
- [ ] `dotnet test SqlToAi.slnx --no-build` → 532 / 532 / 0 grün (vorher 523, +9 neu)
- [ ] `dotnet test SqlToAi.slnx --no-build --filter "FullyQualifiedName~AiNetLinter"` → 1 / 1 / 0 grün
- [ ] Konventioneller Commit auf aktuellem Branch (z. B. `test(audit): ergänze 2-Query-Service-Tests in QueryComparisonServiceTests [audit-try-magicvalues]`)
- [ ] `tasks/audit-try-magicvalues/step-004/step-result.md` geschrieben
- [ ] `status` in `tasks/audit-try-magicvalues/step-004/step-plan.md` von `open` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/AiNetLinter.mdc`:
  - `#EnforceNullableEnable` — `#nullable enable` bleibt in Zeile 1 der Datei ✓
  - `#EnforceSealedClasses` (in `*.Tests` aus) — `public sealed class QueryComparisonServiceTests` ✓
  - `#EnforcePascalCase` — alle 9 Methodennamen folgen `CompareQueriesAsync_<Bedingung>_<Erwartung>` Konvention
  - `#EnforceAsciiIdentifiers` — keine Umlaute in Identifiern; SQL-Queries (`"DROP TABLE Users"`, `"SELECT 1; SELECT 2"`) sind englisch, ok
  - `#MaxMethodLineCount=100` (Test-Override) — geschätzt ~15-25 Zeilen pro Test, weit unter Limit
  - `#MaxLineCount=500` — geschätzt ~150-170 Zeilen Gesamtdatei, weit unter Limit
  - `#MaxBoolParameterCount=1` — `BuildService(bool isAllowed = true, AccessLevel accessLevel = AccessLevel.ReadOnly, SqlToAiError? error = null)` hat **1** bool + 1 enum + 1 nullable ref-type → **nicht** AiNetLinter-relevant (bool-Limit ist 1, ist eingehalten)
  - `#BanPublicNestedTypes` — keine nested types in der Datei ✓
  - `#EnforceNoSilentCatch` — keine `catch`-Blöcke in den 9 Tests (alle pinnen nur `result.IsFailure`/`result.Error.Code`, ohne `try`)
  - `#RunLinterShouldBeClean` — explizit grün halten
- `.agents/rules/SqlToAiRichtlinien.mdc`:
  - §"Commit-Konventionen" — Conventional Commits, deutsch (z. B. `test(...)`)
  - §"Test-Standards" — xUnit v3 Konventionen (`[Fact]`, `[Theory]`, `[InlineData]`, `TestContext.Current.CancellationToken`), `TestConstants` statt Magic Literals, kein Test-Setup über `IDisposable` (in dieser Datei nicht nötig)

## Bekannte Ausnahmen

- **Happy-Path-Test (10. Methode, `BothQueriesValid_ReturnsResult`) ist bewusst ausgeschlossen.** Der Happy-Path des Services erfordert einen vollständigen Mock für `IDatabaseConnectionFactory`, der `OpenAsync` + `BeginTransactionAsync` + `ExecuteReaderAsync(CommandBehavior.SchemaOnly)` (für Schema-Vergleich, 2×) + `ExecuteScalarAsync` (für Count, 2×) + `ExecuteReaderAsync` (für EXCEPT-Diff, 2×) simuliert. Der bestehende `MockQueryConnectionFactory` in `QueryExecutionServiceMockDb.cs` ist auf 1-Query-Single-Statement getrimmt (siehe `BuildReader(config)`-Methode, Zeilen 290-302) und nicht 1:1 wiederverwendbar. Ein dedizierter `QueryComparisonMockConnectionFactory` wäre ~80-120 Zeilen neuer Mock-Code + ~30 Zeilen Test, deutlich außerhalb des "low risk"-Scopes dieses Fix-Steps. Die 9 negativen Tests pinnen den **strukturellen Kern** (Pipeline wird zweimal aufgerufen, Short-Circuit bei erster Failure, Error-Propagation, 2-Query-spezifische Verzweigungen Mutating/Multi-Statement in jeweils einer der beiden Queries). Der Happy-Path-Execution-Pfad (Schema-Vergleich, Count-Vergleich, EXCEPT-Diff-Rollback) bleibt ungetestet auf Unit-Ebene; das ist die gleiche Lücke wie vor step-003 (vorher gab es **keinen** Happy-Path-Test in `QueryComparisonServiceTests`, nur Pipeline-Cases). Wenn der Nutzer den Happy-Path explizit möchte: eigener Folge-Step mit dediziertem Mock-Double. **Vom Planer zur Diskussion gestellt, nicht im aktuellen Step umgesetzt.**
- **Pipeline-Aufruf-Counter ist nicht explizit getestet.** Tests 6-9 beweisen implizit, dass QueryA zuerst validiert wird (Test 6 / Test 8 schlagen fehl bei Mutating/Multi-Statement in QueryA, was nur geht wenn QueryA validiert wird; Test 7 / Test 9 beweisen, dass QueryB tatsächlich validiert wird, weil QueryA sauber ist und QueryB der Fehlerverursacher ist). Ein expliziter `int callCount`-Counter via `Func`-basiertem `IQuerySafetyValidator`-Fake wäre möglich (z. B. `RecordingQuerySafetyValidator`), aber das ist Architektur-Ermessen — die Reihenfolge ist in `QueryComparisonService.cs:69-83` hartcodiert und der Compiler verhindert Vertauschen. **Impliziter Pin via asymmetrische Test-Anordnung ist ausreichend.** Wenn der Nutzer expliziten Counter möchte: TD-003-Erweiterung im Folge-Review.
- **Keine Änderung an `roadmap.md`.** Fix-Modus-Regel §6.2.1 — eine Korrektur ändert nichts an den Epics, sie korrigiert nur den bestehenden Step. `epic: EPIC-03` ist aus `step-003` übernommen, kein neuer Epic.
- **TD-003 wird in diesem Step inhaltlich behoben, aber NICHT im tech-debt.md auf "erledigt" gesetzt.** Das "Auf-erledigt-Setzen" passiert durch den Kritiker in `step-004/step-review.md` (im gleichen Atemzug mit der Verifikation der 9 neuen Tests).

## Code-Skizze (optional)

```csharp
#nullable enable

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Service-level tests for <see cref="QueryComparisonService.CompareQueriesAsync"/>.
/// Pins the 2-query identity of the service: pre-pipeline argument validation
/// (<see cref="QueryComparisonService.ValidateArgs"/>), short-circuit-on-first-failure
/// across the two <see cref="IQuerySafetyValidator"/> invocations, and 2-query-specific
/// branching (mutating/multi-statement in either QueryA or QueryB). The pure 6-stage
/// guardrail pipeline itself is covered end-to-end in <c>QuerySafetyValidatorTests</c>
/// (step-003 / DRY-T3); the service tests below target behaviour that the pipeline
/// tests cannot see (calling the validator twice with the right arguments, propagating
/// only the first error).
/// </summary>
public sealed class QueryComparisonServiceTests
{
    private static QueryComparisonService BuildService(
        bool isAllowed = true,
        AccessLevel accessLevel = AccessLevel.ReadOnly,
        SqlToAiError? error = null)
    {
        // unchanged from step-003
        var options = new SqlToAiOptions();
        IQuerySafetyValidator safetyValidator = error != null
            ? new FakeQuerySafetyValidator(error)
            : new FakeQuerySafetyValidator(
                new FakeSecurityGuard(isAllowed),
                new FakeAccessLevelProvider(accessLevel),
                new ReadOnlyGuard());
        return new QueryComparisonService(
            new ValidationMockConnectionFactory(),
            safetyValidator,
            Options.Create(options),
            NullLogger<QueryComparisonService>.Instance);
    }

    // ---- Pre-pipeline: empty arguments are rejected before the validator runs ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CompareQueriesAsync_EmptyDatabase_ReturnsInvalidParameters(string db)
    {
        var service = BuildService();
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(db, "SELECT 1", "SELECT 1"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_EmptyQueryA_ReturnsInvalidParameters()
    {
        var service = BuildService();
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "", "SELECT 1"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_EmptyQueryB_ReturnsInvalidParameters()
    {
        var service = BuildService();
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1", ""),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    // ---- Pipeline stages 3-4: whitelist + access level (both queries get the same probe) ----

    [Fact]
    public async Task CompareQueriesAsync_DatabaseNotAllowed_ReturnsSafetyCheckFailed()
    {
        var service = BuildService(isAllowed: false);
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1", "SELECT 2"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_AccessLevelNone_ReturnsWriteOperationBlocked()
    {
        var service = BuildService(accessLevel: AccessLevel.None);
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1", "SELECT 2"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    // ---- 2-Query-specific: Mutating / Multi-Statement in QueryA vs. QueryB ----

    [Fact]
    public async Task CompareQueriesAsync_MutatingQueryInQueryA_ReturnsError()
    {
        // ReadOnly pipeline; QueryA is mutating, QueryB is clean. Service must fail on
        // QueryA and never call the validator for QueryB (short-circuit proof).
        var service = BuildService(accessLevel: AccessLevel.ReadOnly);
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "DROP TABLE Users", "SELECT 1"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_MutatingQueryInQueryB_ReturnsError()
    {
        // ReadOnly pipeline; QueryA is clean, QueryB is mutating. Service must validate
        // QueryA (passes), then validate QueryB (fails). This proves the validator is
        // called for both queries in the expected order.
        var service = BuildService(accessLevel: AccessLevel.ReadOnly);
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1", "DROP TABLE Users"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_MultipleStatementsInQueryA_ReturnsError()
    {
        // ReadWrite pipeline (multi-statement is enforced at every access level).
        var service = BuildService(accessLevel: AccessLevel.ReadWrite);
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1; SELECT 2", "SELECT 3"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_MultipleStatementsInQueryB_ReturnsError()
    {
        var service = BuildService(accessLevel: AccessLevel.ReadWrite);
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1", "SELECT 2; SELECT 3"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }
}
```

## Notes

- **Reihenfolge der Testmethoden in der Datei** ist bewusst so gewählt, dass die Test-Cases die Pipeline-Stage-Reihenfolge in `QueryComparisonService.ValidateArgs` (DB → QueryA/B) und im Service-Hauptpfad (QueryA first → QueryB second, short-circuit) widerspiegeln. Das macht den Code scanbar für LLMs und Menschen, ohne den Linter zu stressen.
- **Verwendung von `TestConstants.DatabaseName`** statt `"DemoDB"`-Literal: konsistent mit `QuerySafetyValidatorTests.cs` (verwendet `"TestDb"`, würde aber genauso `TestConstants.DatabaseName` akzeptieren) und `QueryValidationServiceTests.cs:68, 85, 110, 132, 149, 163, 178, 191, 216`. Hintergrund: AiNetLinter hat keine `MagicLiteral`-Regel, aber das `konzept.md` §"Magic Values" nennt Magic Strings als Anti-Pattern, und `TestConstants` ist genau für diesen Zweck da.
- **Konstruktor-Signatur `QueryComparisonArgs`**: `new QueryComparisonArgs(TestConstants.DatabaseName, queryA, queryB)` — die drei Pflicht-Argumente per Positional, alle optionalen mit Defaults. Die `CompareQueriesAsync(QueryComparisonArgs, CancellationToken)`-Überladung (Service Zeile 56) nimmt das Args-Objekt direkt. Das ist der korrekte Pfad; die `(string, string, string, CancellationToken)`-Convenience-Überladung (Service Zeile 46) ist nur ein dünner Wrapper und wird in den Tests nicht benutzt.
- **`ValidationMockConnectionFactory` als Connection-Factory in den Negativ-Tests** ist Absicht: in allen 9 Tests wird die Connection **nie** geöffnet (Service failed vorher in `ValidateArgs` oder in einer der Pipeline-Validierungen), aber der Konstruktor verlangt eine Factory. `ValidationMockConnectionFactory` ist die einfachste verfügbare Factory und liefert eine `FakeDbConnection` mit `BeginTransaction`/`ExecuteNonQuery`-Stub. Wenn der Service tatsächlich zur Connection-Open-Phase kommen würde (er tut es in den negativen Tests nicht), würde der Stub sauber durchlaufen — aber das ist hier nicht relevant.
- **Test-Override für AiNetLinter `MaxMethodLineCount=100`**: alle 9 Methoden sind nach dem Code-Skizzen-Muster 12-18 Zeilen lang (Assertions + Arrange), keine überschreitet 25 Zeilen. Deutlich unter dem Limit.
- **`AutoFixture`, `Moq`, `NSubstitute` o. ä. sind NICHT im Einsatz** — bestätigt durch `tests\SqlToAi.Tests\TestSupport\*.cs` und das Test-Klassen-Pattern in `QueryExecutionServiceMockDb.cs`. Die Tests folgen dem bestehenden "Hand-rolled Mocks"-Stil.
- **Keine Concurrency-Tests in diesem Step.** `QueryComparisonService.CompareQueriesAsync` ist `async` und nimmt `CancellationToken`, aber die 9 negativen Tests decken keine Cancellation-Pfade ab. Das ist konsistent mit dem aktuellen Stand: kein Service-Test im `tests\SqlToAi.Tests\Database\`-Verzeichnis testet explizit `CancellationToken`-Propagation (das wäre ein eigener Refactor, nicht in diesem Step-Scope).
- **Commit-Strategie**: ein einzelner Commit mit den 9 neuen Tests + Doc-Kommentar-Ersatz. Kein Intermediate-Commit nötig (im Gegensatz zu step-003, wo die Fakes-Migration einen Atomar-Commit erzwang — hier ist alles in einer Datei, trivial atomar).
