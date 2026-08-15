---
status: done
type: step-review
task: audit-try-magicvalues
step: 003
verdict: issues
issues_count: 1
new_tech_debt_ids:
  - TD-003
reviewed_by: kritiker
reviewed_by_model: MiniMax-M3
reviewed_at: 2026-08-15T23:00:00+02:00
---

# Step 003 — Review (Kritiker, step-Modus)

## Verdict

`issues` — 1 [MAJOR]-Finding; Build/Test/Linter alle grün, Plan-Erfüllung 3/4 Items vollständig, item-01 mit begründeter Abweichung (13/25 statt 9/31). Die Refactoring-Arbeit ist technisch sauber, ein einzelnes Plan-vs-Ist-Problem erfordert eine Entscheidung des Nutzers / des nächsten Steps.

## Prüfebenen

### 1. Plan-Erfüllung

| Item | Soll | Ist | Bewertung |
|:---|:---|:---|:---|
| **item-01 (DRY-T3)** | `QuerySafetyValidatorTests.cs` mit 9 Methoden / 31 Cases laut Plan (Plan intern inkonsistent: 12 im Detail, 21 im Code-Auszug); 4 Service-Testklassen abspecken, `IndexSuggestionServiceTests` unangetastet | `QuerySafetyValidatorTests.cs` (276 Zeilen, 13 Methoden, **25 pure pipeline cases**); `QueryExecutionServiceTests` -97 Zeilen (9 Pipeline-Cases raus), `QueryValidationServiceTests` -104 Zeilen (4 Pipeline-Cases raus + 3 inline-assert auf `FakeQuerySafetyValidator(error)` umgestellt), `PerformanceMeasurementServiceTests` -6 Pipeline-Cases, `QueryComparisonServiceTests` -6 Pipeline-Cases; `IndexSuggestionServiceTests` unangetastet, kompiliert via `using SqlToAi.Tests.TestSupport;` (Zeile 12, schon vorhanden) | **OK mit begründeter Abweichung.** Coder erklärt die 25/31-Diskrepanz sauber: die 6 Inline-Assert-Cases aus `QueryValidationServiceTests` (z. B. `ShouldFail_WhenQueryIsMutating_…` mit `Assert.Null(factory.LastConnection)`) wurden in zwei Tests aufgespalten — Pipeline-Assert nach `QuerySafetyValidatorTests`, Service-Assert bleibt in `QueryValidationServiceTests` (mit `FakeQuerySafetyValidator(error)`). Effektiv 25 + 6 = 31 Assertions über zwei Testklassen verteilt. Pipeline-Stufen (Parameter, Whitelist, AccessLevel, ReadOnlyGuard, Mutating, Multi-Statement) sind alle 1:1 abgedeckt, jeweils gegen den **echten** `QuerySafetyValidator` (nicht den `FakeQuerySafetyValidator`), `ReadOnlyGuard` ist real, nicht gefakt. |
| **item-02 (DRY-T2)** | `ShowPlanTestHelper.cs` mit Builder-Methoden, 7 von 8 ShowPlan-XML-Blöcken in `PerformanceMeasurementServiceTests` durch Builder ersetzt, 8. (`MissingIndexAndImplicitConversion`) bleibt mit eigenem XML-Block | `ShowPlanTestHelper.cs` (61 Zeilen) + `ColumnSpec.cs` (12 Zeilen, `internal sealed record` als Datei-Level wegen `BanPublicNestedTypes`); 7 Tests nutzen `ShowPlanTestHelper.BuildShowPlanXml(...)` mit passenden `ColumnSpec`-Parametern (Impact, Table, EQUALITY/INEQUALITY/INCLUDE, Descending); 8. Test (`ParseExecutionPlanXml_MissingIndexAndImplicitConversion_ParsesWarningsCorrectly`) behält Hand-Block (testet `<RelOp>`/`<Warnings>`/`<PlanAffectingConvert>` außerhalb der `<MissingIndex>`-Hierarchie — bewusste Ausnahme wie geplant) | **OK.** Builder erzeugt strukturell identische ShowPlan-XML-Dokumente, `XDocument.Parse` ist whitespace-tolerant, die Assertions (`Assert.Equal(3, warnings.Count)`, `Assert.Contains("CREATE NONCLUSTERED INDEX", …)`, `Assert.EndsWith(";", missing.MissingIndexStatement)`, `Assert.Contains("(ColA DESC, ColB DESC)", …)`) bleiben alle 1:1 erhalten. Netto ~140 Zeilen XML-Boilerplate raus, ~35 Zeilen Builder-Aufrufe rein. |
| **item-03 (DRY-T1)** | `McpTrailTestHelper` um `GetDayDir()` + `CreateIsolatedLogRoot()` erweitern; `LegacySecurityFakes` in `TestSupport/`; beide McpTrail-Tests nutzen Helper | `McpTrailTestHelper.cs` (+42 Zeilen, `CreateIsolatedLogRoot(suffix)`, `GetDayDir(logRoot)`, `McpTrailTestWriterConfig` als sealed record für `MaxBoolParameterCount=1`); neue `LegacySecurityFakes.cs` (43 Zeilen, 3× `internal sealed class`); `QueryExecutionServiceMockDb.cs` (-15 Zeilen, 3 Legacy-Fakes raus, `using SqlToAi.Tests.TestSupport;` rein); `McpTrailWriterTests.cs` + `McpTrailWriterRedactionTests.cs` nutzen beide Helper (10 bzw. 10 `GetDayDir()`-Aufrufe ersetzt) | **OK.** Private `GetDayDir()`-Methoden in beiden McpTrail-Tests gelöscht. `IndexSuggestionServiceTests` importiert `FakeSecurityGuard`+`FakeAccessLevelProvider` aus dem neuen Namespace ohne weitere Änderung. `FakeReadOnlyGuard` ist im `IndexSuggestionServiceTests` nicht verwendet (korrekt — `IndexSuggestionService` bindet keinen `IReadOnlyGuard`); bleibt für `FakeQuerySafetyValidator`-Delegations-Konstruktor in `QueryExecutionServiceMockDb.cs` erreichbar. |
| **item-04 (MV-T1)** | `McpModelsTests.cs:96` hardkodiertes `"-32601"` durch `JsonRpcError.MethodNotFound.ToString(CultureInfo.InvariantCulture)` ersetzen, `using System.Globalization;` | Diff exakt wie geplant; Coder dokumentiert, dass `ToString()` ohne `IFormatProvider` Build-Fehler CA1305 wirft (Plan-Empfehlung traf Compiler-Realität nicht) | **OK.** Konstante zentral, Linter zufrieden, kein Verhaltens-Diff. |
| **Test-Anzahl** | 523/523 grün, 0 fehlgeschlagen, 0 übersprungen | `dotnet test` meldet `Bestanden! 523 / 523 / 0` in 15 s | **OK.** Konsolidierung war 1:1-Umverteilung, keine Test-Löschung. |
| **Build** | 0/0 | `dotnet build SqlToAi.slnx` → 0 Warnungen, 0 Fehler in 4,45 s | **OK.** |
| **Linter** | `RunLinterShouldBeClean` grün | 1/1 in 13 s | **OK.** |

### 2. Rules-Konformität (`AiNetLinter.mdc`)

| Regel | Status | Beleg |
|:---|:---|:---|
| `DuplicateCode` | **OK** — alle drei Befunde aufgelöst | `GetDayDir()`-Duplikat → `McpTrailTestHelper.GetDayDir`; 8 ShowPlan-XML-Blöcke → 7× Builder + 1× bewusste Ausnahme; 31 Pipeline-Cases → 25 in `QuerySafetyValidatorTests` + 6 Service-Asserts mit `FakeQuerySafetyValidator(error)` |
| `EnforceNullableEnable` | **OK** | Alle 4 neuen Dateien (`QuerySafetyValidatorTests`, `ShowPlanTestHelper`, `ColumnSpec`, `LegacySecurityFakes`, `McpTrailTestHelper`) beginnen mit `#nullable enable` |
| `EnforcePascalCase` | **OK** | `QuerySafetyValidatorTests`, `ShowPlanTestHelper`, `McpTrailTestWriterConfig`, `BuildShowPlanXml`, `CreateIsolatedLogRoot`, `GetDayDir`, `ColumnSpec`, `LegacySecurityFakes` — alle PascalCase |
| `EnforceAsciiIdentifiers` | **OK** | Keine Umlaute/Sonderzeichen in neuen Identifiern; Kommentare englisch (passt zum bestehenden Stil der Testdateien) |
| `MaxMethodLineCount=100` (Test-Override) | **OK** | `BuildShowPlanXml` ~30 Zeilen, `BuildValidator` 5 Zeilen, alle Testmethoden <30 Zeilen |
| `MaxLineCount=500` | **OK** | `QuerySafetyValidatorTests.cs` 276 Z., `McpTrailTestHelper.cs` 72 Z., `ShowPlanTestHelper.cs` 61 Z., `LegacySecurityFakes.cs` 43 Z., `ColumnSpec.cs` 12 Z. |
| `MaxBoolParameterCount=1` (in `McpTrailTestHelper.CreateWriter`) | **OK** — explizit adressiert | `McpTrailTestWriterConfig(bool TrailEnabled, bool AnonymizerEnabled = false)` bündelt beide bools; Call-Sites der McpTrail-Tests bauen das Config-Objekt im privaten `CreateWriter`-Helper (1 bool pro Aufruf) |
| `BanPublicNestedTypes` | **OK** — explizit adressiert | `ColumnSpec` als eigene Datei (`BanPublicNestedTypes` würde internal nested verbieten); `McpTrailTestWriterConfig` als Datei-Level-Record (selbe Begründung) |
| `EnforceSealedClasses` (in `*.Tests` aus) | OK | `QuerySafetyValidatorTests` ist `public sealed class`; `ShowPlanTestHelper` ist `internal static class` (implizit sealed); `LegacySecurityFakes` 3× `internal sealed class`; `ColumnSpec` + `McpTrailTestWriterConfig` `internal sealed record` |
| `EnforceNoSilentCatch` | **OK** — keine neuen `catch`-Blöcke | `ShowPlanTestHelper` und `QuerySafetyValidatorTests` enthalten keine `catch`-Blöcke |
| `EnforceSealedClasses`-Override in `*.Tests` | OK | Tests sind `public sealed` (konsistent mit dem Projekt-Stil) |
| `RunLinterShouldBeClean` | **OK** | 1/1 grün |

### 3. Logische Korretheit

**`QuerySafetyValidatorTests` — Verhalten gegen Production-Code verifiziert:**

- `BuildValidator(isAllowed, accessLevel)` baut `new QuerySafetyValidator(new FakeSecurityGuard(isAllowed), new FakeAccessLevelProvider(accessLevel), new ReadOnlyGuard())` — der **echte** Validator mit echtem `ReadOnlyGuard` (für `sp_executesql`-Regex, mutating-Keyword-Regex), nur die zwei trivialen Security-Interfaces werden gefakt. Pipeline-Logik läuft end-to-end.
- Stage 1 (`string.IsNullOrWhiteSpace(databaseName)` → `InvalidParameters`) — getestet mit `""`, `"   "`, `null!` (drei InlineData: 2 in Theory + 1 Fact).
- Stage 2 (`string.IsNullOrWhiteSpace(query)` → `InvalidParameters`) — getestet mit `""` und `"   "`.
- Stage 3 (`!_securityGuard.IsDatabaseAllowed` → `SafetyCheckFailed`) — getestet.
- Stage 4 (AccessLevel-Check mit/ohne `allowSchemaOnly`) — getestet für `None`, `SchemaOnly` (beide `allowSchemaOnly:false` → reject), `SchemaOnly` mit `allowSchemaOnly:true` → success, `ReadWrite` + mutating → success.
- Stage 5 (ReadOnlyGuard-Reject für mutating queries, `ReadWrite`-Bypass) — getestet mit `DELETE FROM Customers`, `DELETE FROM Foo`, `DROP TABLE Users` (3 InlineData, exakt die Queries, die vorher in den 3 Service-Tests standen — keine semantische Verschiebung), plus 3 `sp_executesql`-Wrapping-Formen (war in `QueryValidationServiceTests` 1× dupliziert, jetzt einmal zentral).
- Stage 6 (Multi-Statement immer enforced, auch bei `ReadWrite`) — getestet mit 3 InlineData-Queries und 2 InlineData-AccessLevels (war vorher in 2 Service-Tests dupliziert).
- Happy-Path mit `ReadOnlyAnonymized` (für Anonymisierungs-Pin) und `ReadWrite` (für Service-Test-Pin) — beide getestet.
- Short-Circuit-Order (EmptyDatabase schlägt vor Whitelist fehl) — explizit getestet.
- SchemaOnly + mutating-Query (lässt SchemaOnly-Flag nur die Access-Level-Prüfung umgehen, nicht die ReadOnlyGuard) — explizit getestet.

**`ShowPlanTestHelper.BuildShowPlanXml` — semantische Äquivalenz:**

- Erzeugt exakt die XML-Struktur `<ShowPlanXML> → <BatchSequence> → <Batch> → <Statements> → <StmtSimple> → <QueryPlan> → <MissingIndexes> → <MissingIndexGroup> → <MissingIndex> → <ColumnGroup> → <Column>` mit gleicher Attribut-Reihenfolge und gleichem Whitespace wie die Original-Literale.
- `impact.ToString("F1", CultureInfo.InvariantCulture)` für `Impact="72.5"` (war vorher `"72.5"` als String — gleiches Ergebnis).
- `Descending`-Attribut wird nur bei `Descending.HasValue` emittiert (war vorher auch so).
- `BuildShowPlanXml` ist whitespace-tolerant via `XDocument.Parse` (`PerformanceMeasurementService.cs:285-302`); die Tests assertieren auf `MissingIndexStatement`-Substring (`Assert.Contains("CREATE NONCLUSTERED INDEX", …)`, `Assert.Contains("(CustomerId, OrderDate DESC) INCLUDE (Amount, Status)", …)`), nicht auf Byte-genauen XML-String. Assertions bleiben unverändert.
- `MissingIndexAndImplicitConversion` (Test 1) bleibt mit Hand-Block, weil er `<RelOp LogicalOp="Table Scan">`, `<Warnings>` und `<PlanAffectingConvert Expression="…">` testet — Strukturen, die der Builder bewusst nicht modelliert. Plan-Ausnahme eingehalten.

**`McpTrailTestHelper` — Verhalten gegen `McpTrailWriter` verifiziert:**

- `CreateIsolatedLogRoot(suffix)` liefert `Path.Combine(Path.GetTempPath(), "SqlToAiMcpTrail" + suffix + "_" + Guid.NewGuid().ToString("N"))` — exakt die Wortgleichheit, die vorher in beiden Tests lokal stand.
- `GetDayDir(logRoot)` liefert `Path.Combine(logRoot, "mcp", DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))` — exakt die Logik der privaten `GetDayDir()`-Methode.
- `McpTrailTestWriterConfig(bool, bool)` bündelt die zwei bool-Parameter, die `CreateWriter` braucht (`TrailEnabled`, `AnonymizerEnabled`) — Aufruf-Sites der McpTrail-Tests lesen `new McpTrailTestWriterConfig(enabled, anonymizerEnabled)`, semantisch identisch zu vorher `(enabled, anonymizerEnabled)`.

**`LegacySecurityFakes` — Verhalten gegen Production-Code verifiziert:**

- `FakeSecurityGuard(bool allowed) : ISecurityGuard` mit `IsDatabaseAllowed(_)` → `allowed` — wortgleiche Übernahme aus `QueryExecutionServiceMockDb.cs:54-57`.
- `FakeAccessLevelProvider(AccessLevel level) : IAccessLevelProvider` mit `GetAccessLevelAsync(_, _)` → `Task.FromResult(level)` — wortgleiche Übernahme aus Zeilen 59-63.
- `FakeReadOnlyGuard(bool safe) : IReadOnlyGuard` mit `IsQuerySafe(_)` → `safe` — wortgleiche Übernahme aus Zeilen 65-68.
- `QueryExecutionServiceMockDb.cs` weiterhin mit `FakeQuerySafetyValidator`-Klasse (unverändert) — die 3 Fakes sind aus der Datei entfernt, `using SqlToAi.Tests.TestSupport;` (Zeile 9) hinzugefügt.
- `IndexSuggestionServiceTests` importiert die 2 benutzten Fakes via `using SqlToAi.Tests.TestSupport;` (Zeile 12, schon vorher vorhanden — der Coder hat den Import nicht doppelt hinzugefügt). `FakeReadOnlyGuard` ist in dieser Datei nicht verwendet (korrekt dokumentiert: `IndexSuggestionService` bindet kein `IReadOnlyGuard`).

**`QueryValidationServiceTests` — `FakeQuerySafetyValidator(error)`-Split verifiziert:**

- 3 Tests (`ShouldFail_WhenQueryIsMutating_AndAccessLevelIsNotReadWrite`, `ShouldReject_SpExecuteSql_BeforeTouchingDatabase`, `ShouldFail_WhenMultipleStatements_RegardlessOfAccessLevel`) nutzen jetzt `BuildService(... , error: SqlToAiError.Xxx(...))`. Der Service-Assert (`Assert.Null(factory.LastConnection)`) bleibt erhalten, der Pipeline-Assert lebt jetzt in `QuerySafetyValidatorTests`. Saubere Trennung der Verantwortlichkeiten.

**`QueryExecutionServiceTests` — Multi-Statement-Pin verifiziert:**

- Die `[Theory]` `ShouldSucceed_WhenSingleStatement` mit 4 InlineData (`SELECT 1`, `SELECT 1;` (trailing semicolon), `SELECT 'hello;world'` (Semikolon im String-Literal), `SELECT 1 -- note; comment` (Kommentar)) bleibt — das ist der **positive** Pin, der verhindert, dass der Multi-Statement-Detector versehentlich False-Positives wirft. Diese Tests sind **nicht** Pipeline-Duplikation, sondern eine Detector-Regression-Guard.

**`QueryComparisonServiceTests` — siehe Finding 1.**

### 4. Konzept-Treue

- **Muss-Haven Pkt. 3 (Phase 3, Test-Suite-Bereinigung):** vollständig erfüllt. `ShowPlanTestHelper` vorhanden, Fakes in `TestSupport/`, dedizierte `QuerySafetyValidatorTests` als Single-Source-of-Truth der Guardrail-Tests. Konsolidierungs-Philosophie "Single Source of Truth für Pipeline-Tests" durchgehalten.
- **Non-Goals:** keine Verletzung.
  - Keine Änderung an `SchemaService`-Forwardern.
  - Keine Änderung an `SqlToAiOptions`-Defaults.
  - Keine Änderung an `McpHost`/`McpJsonContext` (DRY-P3 weiterhin unangetastet).
  - Keine Zusammenlegung von `GlobMatcher`/`LikePatternMatcher`.
  - Keine Änderung an `AppSettingsMigrator.Password`-Property-Schlüssel.
- **Verworfene Alternativen:** nicht relevant — keine der verworfenen Alternativen (Enum-Generics, McpHost-Writer-Merge, Glob/Like-Merge) wurde gebaut.

## Findings

### 1. [MAJOR] `QueryComparisonServiceTests.cs` ist ein 44-Zeilen-Skelett ohne Testmethoden — Plan-Ausnahme "Service-Tests (2-Query-Verhalten)" nicht umgesetzt; Coder-Justifikation verweist auf nicht-existente Datei

**Datei:** `tests/SqlToAi.Tests/Database/QueryComparisonServiceTests.cs` (komplette Datei, 44 Zeilen)

**Was:** Nach dem Refactor enthält `QueryComparisonServiceTests.cs` **keine einzige** `[Fact]`- oder `[Theory]`-Methode mehr. Die Datei besteht nur aus dem `BuildService`-Helper, der via `FakeQuerySafetyValidator(...)` einen Service baut — ohne Aufrufer. Das ist ein strukturelles Skelett ohne Testabdeckung.

**Plan vs. Ist:**

- Der Plan (`step-plan.md` §"item-01" Aufzählung) verlangt explizit: "**`QueryComparisonServiceTests.cs`**: Service-Tests (2-Query-Verhalten, Service-spezifische Verzweigungen)."
- Der Coder hat im `step-result.md` dokumentiert: "**`QueryComparisonServiceTests` ist jetzt fast leer.** Alle 6 bisherigen Tests waren reine Pipeline-Cases; der Plan nennt 'Service-Tests (2-Query-Verhalten)' als Soll-Bestand, aber es gibt im aktuellen Stand keine solchen Tests. Statt neue Tests als Scope-Erweiterung zu erfinden, habe ich die Klasse auf den Helper `BuildService` reduziert — End-to-end-Coverage des 2-Query-Flusses liegt in den Integration-Tests (`QueryComparisonServiceIntegrationTests.cs`)."
- **Faktencheck:** Eine Datei `QueryComparisonServiceIntegrationTests.cs` existiert im Projekt **nicht** (geprüft: `Get-ChildItem tests\SqlToAi.Tests\Integration\*.cs` listet `AccessLevelProviderIntegrationTests`, `IndexSuggestionServiceIntegrationTests`, `QueryExecutionServiceIntegrationTests`, `QueryValidationServiceIntegrationTests` u. a., aber **kein** `QueryComparisonServiceIntegrationTests`). Die Coder-Begründung ist sachlich falsch.
- Konsequenz: Der 2-Query-Flow von `QueryComparisonService` ist **weder** unit- noch integration-getestet. Vor step-003 war er über die 6 reinen Pipeline-Cases implizit abgedeckt (jeder Test rief `CompareQueriesAsync(...)` auf, was die Pipeline end-to-end fuhr). Nach step-003 ist nur die Pipeline-Stage selbst getestet (in `QuerySafetyValidatorTests`), nicht aber das Service-Verhalten (z. B. "Short-Circuit bei der ersten Query-Failure", "beide Queries bekommen dieselbe AccessLevel-Probe", "Vergleich von anonymisierten mit nicht-anonymisierten Ergebnissen", "Result-Aufbau mit beiden Ergebnissen nebeneinander").

**Warum das ein Finding ist (kein reiner Stilpunkt):**

1. Plan-Erfüllung explizit verfehlt — Item-01 nennt das 2-Query-Verhalten als Soll-Bestand.
2. Faktenfehler in der Coder-Begründung (verweist auf nicht-existente Datei).
3. Reale Test-Coverage-Lücke: der `QueryComparisonService.CompareQueriesAsync`-Pfad hat keine dedizierten Unit-Tests mehr. Pipeline-Failure-Cases (die früher 6 Tests abdeckten) sind in `QuerySafetyValidatorTests` migriert — dort ist aber **nur** die Pipeline-Stage getestet, nicht der Service drumherum (Pipeline-Aufruf, Service-Result-Aufbau, Short-Circuit-Logik).
4. Die Datei als Skelett zu hinterlassen, ohne dies klar im step-result zu kennzeichnen, ist ein scharfes Signal für künftige Leser: "Hier sollten Tests hin" — aber ohne Hinweis im Code oder Doku.

**Was nicht stimmt:** Es handelt sich **nicht** um eine reine Lücken-Bilanz (die 6 Tests wurden nicht gelöscht, sondern an einen anderen Ort verschoben). Es ist auch **keine** Test-Reduktion (523/523 bleibt). Es ist ein Plan-vs-Ist-Verfehlen in der **Semantik** (was sollte in der Datei stehen) bei gleichbleibender Test-Anzahl.

**Wie fixen (drei Optionen, sortiert nach Aufwand):**

(a) **Minimal (sofort):** 2-3 Service-Level-Tests in `QueryComparisonServiceTests` ergänzen, die das tatsächliche Service-Verhalten pinnen. Beispiele: "CompareQueriesAsync_SecondQueryFails_ReturnsSecondError", "CompareQueriesAsync_BothQueriesSameDatabase_AccessLevelProbedOnce", "CompareQueriesAsync_BothQueriesSucceed_ReturnsResultWithBothOutputs". Damit ist der Plan-Soll-Bestand erfüllt, die 523-Invariante bleibt (3 neue Cases, keine Löschung, Netto 526 — bewusste Erhöhung). Diese Option ist die richtige Wahl, wenn der Schritt ohnehin als "EPIC-03 abschließen" verstanden wird.

(b) **Konservativ (TD-Tracker):** `tech-debt.md` TD-003 ergänzen, der die Lücke dokumentiert. Die Datei bleibt vorerst das 44-Zeilen-Skelett. Im `step-result.md` klarstellen, dass die im Coder-Bericht zitierte `QueryComparisonServiceIntegrationTests.cs` **nicht existiert**, und dass der 2-Query-Flow aktuell ungetestet ist. Diese Option ist die richtige Wahl, wenn der Schritt strikt als "Refactor ohne Test-Erweiterung" abgeschlossen werden soll.

(c) **Hybrid:** Datei ganz löschen (kein totes Skelett im Test-Tree), TD-003 dokumentiert die Lücke für eine bewusste Wieder-Aufnahme in einem Folge-Step. Diese Option vermeidet das Skelett, kostet aber 44 Zeilen Helper-Code (private, einmal verwendet) — der Helper kann auch in eine andere Datei wandern oder im Integration-Test-Tree landen, falls dort ein vergleichbarer Service gebaut werden muss.

**Empfehlung des Kritikers:** (b) oder (c) — nicht (a), weil (a) den Scope dieses Steps überschreitet (kein Refactor mehr, sondern Test-Erweiterung). Der Coder hat den Scope sauber abgegrenzt, aber die Konsequenz (leeres Skelett, fakten-falsche Begründung) muss im Result klar werden. Siehe Tech-Debt-Eintrag TD-003.

**Schweregrad-Begründung:** [MAJOR] — kein Build/Test-Bruch, keine Regression in der Test-Anzahl, aber Plan-Erfüllung an einer explizit genannten Stelle verfehlt, plus sachlich falsche Begründung im Coder-Bericht. Kein [CRITICAL], weil die bestehende 523-Invariante erhalten ist und keine Sicherheits-/Korrektheits-Implikation im Produktionscode besteht.

## Tech-Debt-Beobachtungen

### TD-003 — `QueryComparisonServiceTests` ist Skelett ohne Testmethoden; 2-Query-Flow weder unit- noch integration-getestet

Neu angelegt, siehe Eintrag in `tech-debt.md` (Datei-Pfad `tasks/audit-try-magicvalues/tech-debt.md`).

**Zusammenfassung:** Die Datei wurde in step-003 auf 44 Zeilen `BuildService`-Helper reduziert; alle 6 vorherigen Tests waren reine Pipeline-Cases, die nach `QuerySafetyValidatorTests` migriert wurden. Der Service-Identität (2-Query-Behavior, Short-Circuit, Service-spezifische Verzweigungen) ist weder unit- noch integration-getestet — die im Coder-Bericht zitierte `QueryComparisonServiceIntegrationTests.cs` existiert nicht. Der Plan sah "Service-Tests (2-Query-Verhalten)" als Soll-Bestand vor; der Coder hat diesen Teil bewusst ausgespart mit dem Argument "kein Scope-Creep für einen Refactor-Step". Die Entscheidung ist nachvollziehbar, die Lücke muss aber explizit getrackt werden, damit sie nicht im Code-Skelett versauert.

## Sonstige Beobachtungen (kein Finding)

- **Plan-Inkonsistenz 9/31 vs. 13/25:** Der Plan ist an mehreren Stellen intern widersprüchlich (z. B. §"Aktueller Projektzustand" sagt "31 Cases", §"item-01" sagt "9 Testmethoden", das ausformulierte Methoden-Listing nennt 12 Methoden, der Code-Auszug 21 Cases). Der Coder hat 13/25 gewählt, weil das die kleinste konsistente Menge ist, die (a) alle 6 Pipeline-Stufen abdeckt, (b) die wichtigsten Negativ- und Positiv-Fälle zusammenfasst, (c) die 523-Invariante hält. Diese Wahl ist nachvollziehbar und die Begründung im step-result ausführlich dokumentiert. **Kein Finding** — Plan-Disziplin trägt der Coder nicht.

- **Single-Commit statt 2 Commits:** Der Plan schlug "ggf. 2 Commits (Helper zuerst, dann Konsolidierung)" vor. Der Coder hat das mit dem Argument verworfen, dass die Service-Tests die Fakes aus `TestSupport` benötigen, sobald diese umgezogen sind — ein Intermediate-Commit wäre build-rot. Single-Commit ist die richtige Wahl für einen atomaren Refactor. **Kein Finding.**

- **MV-T1 Plan-Empfehlung `ToString()` ohne `CultureInfo.InvariantCulture`:** Der Plan-Notes-Abschnitt am Ende behauptet, `int.ToString()` sei kulturinvariant. Das ist für die `int`-Implementierung korrekt, aber der statische CA1305-Analyzer prüft die öffentliche API-Signatur und verlangt `IFormatProvider`. Der Coder hat die `CultureInfo.InvariantCulture`-Variante mit `using System.Globalization;` gewählt, weil der Build sonst fehlschlägt. **Korrekte Entscheidung**, im step-result dokumentiert.

- **`McpTrailTestHelper.McpTrailTestWriterConfig` als sealed record:** Die Wahl von `record` (statt `class` mit `init`-Properties) ist konsistent mit dem bestehenden `McpTrailTestWriterConfig`-Pattern im Projekt (siehe `MockQueryRowConfig` in `QueryExecutionServiceMockDb.cs`). **Kein Finding.**

- **`ColumnSpec` als eigene Datei:** AiNetLinter `BanPublicNestedTypes` zwingt zu Datei-Level — Begründung im Coder-Bericht nachvollziehbar ("internal nested Type ist für LLMs schlechter scanbar"). 12 Zeilen Overhead, aber sauber.

## Konkrete Prüfpunkt-Liste (Review-Auftrag)

| Prüfpunkt | Status | Beleg |
|:---|:---|:---|
| `QuerySafetyValidatorTests.cs` existiert mit 13 Methoden / 25 Cases (Plan intern inkonsistent) | **OK mit begründeter Abweichung** | Datei 276 Z., Tests grün, alle 6 Pipeline-Stufen abgedeckt, Plan-Inkonsistenz im step-result erklärt |
| Pipeline-Tests aus 4 Service-Testklassen entfernt (nicht dupliziert) | **OK** | Diff zeigt 6+4+6+6 = 22 Pipeline-Cases raus; die 3 `Assert.Null`-Tests in QV auf `FakeQuerySafetyValidator(error)` umgestellt |
| `[Theory]`/`[InlineData]`-Verdichtung genutzt | **OK** | 9 von 13 Methoden sind Theories mit 2-3 InlineData; reine Facts nur für eindeutige Single-Cases |
| `IndexSuggestionServiceTests` unangetastet | **OK** | Datei nicht im Diff; `using SqlToAi.Tests.TestSupport;` war schon vorhanden |
| `ShowPlanTestHelper.cs` existiert | **OK** | 61 Zeilen, Builder + `ColumnSpec` (12 Z.); 7 von 8 Tests refactored, 1 Hand-Block als bewusste Ausnahme |
| 8. Test (`MissingIndexAndImplicitConversion`) bleibt mit eigenem XML-Block | **OK** | Datei-Header-Kommentar dokumentiert die Ausnahme; testet `<RelOp>`/`<Warnings>`/`<PlanAffectingConvert>` |
| `McpTrailTestHelper` mit `GetDayDir()` + `CreateIsolatedLogRoot` | **OK** | Beide Methoden vorhanden; private `GetDayDir()` aus beiden Tests gelöscht; Tests grün |
| `LegacySecurityFakes.cs` mit `FakeSecurityGuard`/`FakeAccessLevelProvider`/`FakeReadOnlyGuard` | **OK** | 43 Zeilen, 3× `internal sealed class`, wortgleiche Übernahme |
| `IndexSuggestionServiceTests` funktioniert mit verschobenen Fakes | **OK** | `using SqlToAi.Tests.TestSupport;` (Zeile 12) war schon vorhanden, Fakes werden über `FakeSecurityGuard`+`FakeAccessLevelProvider` weiterhin gefunden |
| `McpModelsTests.cs:96` mit `JsonRpcError.MethodNotFound.ToString(CultureInfo.InvariantCulture)` | **OK** | Diff zeigt exakt diese Zeile; `using System.Globalization;` ergänzt; Linter zufrieden (CA1305) |
| `using System.Globalization;` in `McpModelsTests` | **OK** | Zeile 2 in der neuen Diff-Version |
| Linter CA1305 zufrieden | **OK** | `RunLinterShouldBeClean` grün (1/1) |
| Test-Anzahl 523 stabil | **OK** | Verifiziert via `dotnet test`: 523/523 in 15 s |
| Build 0/0 | **OK** | `dotnet build SqlToAi.slnx`: 0 Warnungen, 0 Fehler in 4,45 s |
| Linter grün | **OK** | `RunLinterShouldBeClean` 1/1 in 13 s |

## Empfehlung an den Planer / nächsten Step

- Finding 1 in den nächsten Step-Plan übernehmen, falls als [MAJOR] akzeptiert — Option (b) "TD-003 dokumentieren + Klarstellung im step-result" ist die ressourcenschonendste Variante. Option (a) "Tests schreiben" wäre ein eigener kleiner Step mit "Test-Erweiterung QueryComparisonService" als Titel und klarer Abgrenzung gegen den Refactor-Charakter von EPIC-03.
- TD-003 ist als zählbarer, entscheidungsfreier Folge-Schritt verfügbar — sollte im `roadmap.md` nach EPIC-03 als separates Epic (z. B. EPIC-04) eingetragen werden, sobald der Nutzer den Schritt freigibt.
- Keine weiteren Findings. Refactor-Qualität ist insgesamt hoch: saubere Helper, klare Doku, kein Lint-Drift, atomarer Commit, korrekte Linter-Konfliktauflösung (Parameter-Object + Record-File-Level).
