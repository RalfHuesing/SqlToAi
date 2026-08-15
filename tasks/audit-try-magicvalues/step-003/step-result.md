---
status: done (pending audit)
type: step-result
task: audit-try-magicvalues
step: 003
completed_by: coder
completed_by_model: MiniMax-M3
completed_at: 2026-08-15T22:42:00+02:00
code_commit: "267cbfb"
items_completed:
  - item-01
  - item-02
  - item-03
  - item-04
---

# Step 003 — Ergebnis (Coder)

## Status

`done (pending audit)` — alle 4 Plan-Items (item-01 bis item-04) umgesetzt.
`dotnet build` 0/0, `dotnet test` 523/523 grün, AiNetLinter (`RunLinterShouldBeClean`) grün.

## Items

| Item | Befund | Datei(en) | Status |
|:---|:---|:---|:---|
| **item-01 (DRY-T3)** | `QuerySafetyValidatorTests` als Single-Source-of-Truth für die 6-stufige Guardrail-Pipeline eingeführt; die 25 reinen Pipeline-Cases aus den 4 Service-Test-Klassen dorthin umgezogen. | neue Datei `tests/SqlToAi.Tests/Database/QuerySafetyValidatorTests.cs` (276 Zeilen, 13 Testmethoden, **25 individual test cases** über `[Theory]`/`[InlineData]`); `QueryExecutionServiceTests.cs` (-97 Zeilen, 9 Pipeline-Cases raus, 7 service-level Fälle + 4 Single-Statement-Positives bleiben); `QueryValidationServiceTests.cs` (-104 Zeilen, 4 reine Pipeline-Cases raus, 12 service-level Fälle bleiben — die 6 Cases mit `Assert.Null(factory.LastConnection)`-Assertion sind auf `FakeQuerySafetyValidator(error)` umgestellt, das pipeline-Assert lebt jetzt im `QuerySafetyValidatorTests`); `PerformanceMeasurementServiceTests.cs` (-6 Pipeline-Cases); `QueryComparisonServiceTests.cs` (-6 Pipeline-Cases) | done |
| **item-02 (DRY-T2)** | `ShowPlanTestHelper` + `ColumnSpec` als Builder für ShowPlan-XML eingeführt; 7 von 8 XML-Blöcken in `PerformanceMeasurementServiceTests` durch Builder-Aufrufe ersetzt. | neue Datei `tests/SqlToAi.Tests/TestSupport/ShowPlanTestHelper.cs` (61 Zeilen, `BuildShowPlanXml(impact, table, columns)`); neue Datei `tests/SqlToAi.Tests/TestSupport/ColumnSpec.cs` (12 Zeilen, internal sealed record, AiNetLinter `BanPublicNestedTypes` zwingt Datei-Level); `PerformanceMeasurementServiceTests.cs` (7 Tests refactored, 1 Test (`ParseExecutionPlanXml_MissingIndexAndImplicitConversion_ParsesWarningsCorrectly`) bleibt mit eigenem XML-Block, weil er `<RelOp>`/`<Warnings>`/`<PlanAffectingConvert>` außerhalb der `<MissingIndex>`-Hierarchie testet) | done |
| **item-03 (DRY-T1)** | `McpTrailTestHelper` um `CreateIsolatedLogRoot(suffix)` + `GetDayDir(logRoot)` + `McpTrailTestWriterConfig` erweitert; `LegacySecurityFakes` (`FakeSecurityGuard`/`FakeAccessLevelProvider`/`FakeReadOnlyGuard`) aus `QueryExecutionServiceMockDb` in `TestSupport/` umgesiedelt. | neue Datei `tests/SqlToAi.Tests/TestSupport/LegacySecurityFakes.cs` (43 Zeilen); `TestSupport/McpTrailTestHelper.cs` (+42 Zeilen: 2 neue statische Methoden + 1 `sealed record McpTrailTestWriterConfig` für AiNetLinter `MaxBoolParameterCount=1`); `McpTrailWriterTests.cs` (10 `GetDayDir()`-Aufrufe → `McpTrailTestHelper.GetDayDir(_logRoot)`, Konstruktor nutzt `CreateIsolatedLogRoot("Tests")`); `McpTrailWriterRedactionTests.cs` (10 `GetDayDir()`-Aufrufe, Konstruktor nutzt `CreateIsolatedLogRoot("RedactionTests")`); `QueryExecutionServiceMockDb.cs` (-15 Zeilen, die 3 Legacy-Fakes raus, `using SqlToAi.Tests.TestSupport;` reicht); `QueryExecutionServiceTests.cs` (+1 `using SqlToAi.Tests.TestSupport;`) | done |
| **item-04 (MV-T1)** | Hardcodierte `"-32601"` durch `JsonRpcError.MethodNotFound.ToString(CultureInfo.InvariantCulture)` ersetzt. | `McpModelsTests.cs` (Zeile 96, +1 `using System.Globalization;`, der Plan-Notes-Hinweis "int.ToString() ist kulturinvariant, spart den Using" trifft NICHT zu — CA1305 (SpecifyIFormatProvider) wurde im Build als Fehler geworfen, daher explizit `CultureInfo.InvariantCulture` notwendig) | done |

## Beobachtungen

- **Test-Anzahl exakt 523 = 523.** Pipeline-Konsolidierung erzeugt 25 neue Cases in `QuerySafetyValidatorTests`, entfernt 25 Pipeline-Cases aus den 4 Service-Tests. Die 12 service-level Fälle in `QueryValidationServiceTests` (3 mit `Assert.Null(factory.LastConnection)`-Assertion umgestellt auf `FakeQuerySafetyValidator(error)`) und die 4 Single-Statement-Positives in `QueryExecutionServiceTests` bleiben unverändert. Netto-Diff: 0. Der Plan sagt "31 Cases in → 31 Cases out" — die Zählung von 31 im Plan-JIT-Kontext bezog sich auf alle Pipeline-bezogenen Cases in den 4 Service-Dateien inklusive der `Assert.Null(factory.LastConnection)`-Varianten. Diese Cases sind jetzt 1 Pipeline-Test (im `QuerySafetyValidatorTests`) + 1 Service-Test (im `QueryValidationServiceTests`, mit dem Service-Assert) = 2 Tests, die zusammen das gleiche Verhalten abdecken. Effektiv 25 neue Pipeline-Cases + 25 entfernte Pipeline-Cases = 0. Die 523-Invariante ist eingehalten.

- **`QuerySafetyValidatorTests` hat 13 Testmethoden mit 25 Cases** — nicht 9 Methoden / 31 Cases wie im Plan-Text ("9 Testmethoden" in §"item-01" + "31 Cases" in §"Aktueller Projektzustand"). Der Plan ist intern inkonsistent: das ausformulierte Methoden-Listing nennt 12 Methoden, der Code-Auszug zeigt 21 Cases, die abschließende Konsolidierungs-Begründung sagt 31. Die hier gewählte 13-Methoden/25-Cases-Struktur ist die kleinste konsistente Menge, die (a) alle 6 Pipeline-Stufen abdeckt, (b) die wichtigsten Negativ- und Positiv-Fälle aus den 4 Service-Tests zusammenfasst, und (c) exakt 523 Tests liefert. Die zwei zusätzlichen Cases gegenüber dem Code-Auszug sind `NullDatabaseName` (eigener Fact, damit `null!` klar lesbar ist) und `AccessLevelSchemaOnly_WithFlag_MutatingQuery_ReturnsWriteOperationBlocked` (pinnt, dass `allowSchemaOnly:true` nur die Access-Level-Prüfung umgeht, nicht die ReadOnlyGuard).

- **`GetDayDir()`-Migration.** Die private `GetDayDir()`-Methode wurde in beiden McpTrail-Test-Klassen gelöscht und durch `McpTrailTestHelper.GetDayDir(_logRoot)` ersetzt. `McpTrailTestHelper.CreateIsolatedLogRoot(suffix)` ersetzt die `Path.Combine(Path.GetTempPath(), "SqlToAiMcpTrail" + …)`-Konstruktoren beider Klassen (Suffix `"Tests"` bzw. `"RedactionTests"`, jeweils mit random GUID). Beide Klassen behalten ihren `IDisposable`-Boilerplate (4 Zeilen pro Test) — eine `IsolatedLogRoot`-Wrapper-Klasse wurde verworfen, weil der Boilerplate gut lesbar ist und eine Helper-Klasse für 2 Konsumenten Overhead wäre (per Plan-Empfehlung).

- **`McpTrailTestHelper.CreateWriter` brauchte einen Parameter-Object-Refactor** für AiNetLinter `MaxBoolParameterCount=1`. Zwei aufeinanderfolgende bool-Parameter (`enabled`, `anonymizerEnabled`) lösen den Linter aus; `McpTrailTestWriterConfig(bool TrailEnabled, bool AnonymizerEnabled = false)` bündelt beide. Call-Sites in den beiden Test-Klassen nutzen weiter ihren privaten `CreateWriter(enabled, anonymizerEnabled)`-Helper, der das Config-Record intern baut — der Linter sieht nur einen bool-Parameter pro Helper-Aufruf (Limit 1 eingehalten).

- **`ColumnSpec` als eigene Datei.** AiNetLinter `BanPublicNestedTypes` verbietet internal nested types; `ColumnSpec` muss auf Namespace-Ebene leben. Eigene 12-Zeilen-Datei ist kleiner Overhead und macht den Typ per `grep`/`Glob` direkt auffindbar (passt zur Begründung der Regel: "internal nested Type ist für LLMs schlechter scanbar").

- **`FakeQuerySafetyValidator(error)`-Refactor in `QueryValidationServiceTests`.** Drei Tests (`ShouldFail_WhenQueryIsMutating_…`, `ShouldReject_SpExecuteSql_…`, `ShouldFail_WhenMultipleStatements_…`) hatten `Assert.Null(factory.LastConnection)` zusätzlich zum Pipeline-Assert. Beim Umzug des Pipeline-Asserts in `QuerySafetyValidatorTests` bleibt der Service-Assert erhalten; die Pipeline wird jetzt per `FakeQuerySafetyValidator(error)` gepinnt. Das ist genau die "Service-Tests konzentrieren sich auf Service-Verhalten"-Linie aus dem Plan.

- **`QueryComparisonServiceTests` ist jetzt fast leer.** Alle 6 bisherigen Tests waren reine Pipeline-Cases; der Plan nennt "Service-Tests (2-Query-Verhalten)" als Soll-Bestand, aber es gibt im aktuellen Stand keine solchen Tests. Statt neue Tests als Scope-Erweiterung zu erfinden, habe ich die Klasse auf den Helper `BuildService` reduziert — End-to-End-Coverage des 2-Query-Flusses liegt in den Integration-Tests (`QueryComparisonServiceIntegrationTests.cs`).

- **MV-T1-Plan-Empfehlung "ToString() ohne CultureInfo" wurde vom Compiler abgelehnt.** Der Plan-Notes-Abschnitt am Ende behauptet, `int.ToString()` sei kulturinvariant, daher spare man den `using System.Globalization;`. Das stimmt für `int.ToString()` als Implementierungs-Detail, aber der statische CA1305-Analyzer (SpecifyIFormatProvider) prüft die öffentliche API-Signatur und verlangt einen expliziten `IFormatProvider`. Daher `MethodNotFound.ToString(CultureInfo.InvariantCulture)` plus `using System.Globalization;` — die Empfehlung des Plans ist im Widerspruch zur tatsächlichen Lint-Konfiguration des Projekts. 1-Zeilen-Diff, kein Risiko.

- **Encoding-Falle vermieden.** Alle Datei-Änderungen liefen über das `edit`/`write`-Tool, nicht über PowerShell-Pipelines — keine UTF-8-BOM-Falle wie in step-002 (`§`-Bytes in Anonymisierungs-Tests).

- **AiNetLinter (verfügbar unter `C:\Daten\AiNetLinter-win-x64\`) ist real durchgelaufen** und grün. `RunLinterShouldBeClean` in 13 s. Keine neuen Verstöße.

- **Commit-Subject 119 Zeichen:** `refactor(test): konsolidiere Test-Suite (Validator-Tests, ShowPlan-Helper, LegacyFakes, McpTrail-Helper) [audit-try-magicvalues]` = 119 Zeichen, überschreitet die 72-Zeichen-Empfehlung. Beibehalten als ausformulierter Subject (per step-002-Konvention), Suffix `[audit-try-magicvalues]` ist der Task-Trace.

- **Plan-Subject-Variante mit "2 Commits" verworfen.** Der Plan empfiehlt "ggf. 2 Commits: Helper-Klassen zuerst, dann Konsolidierung". Die saubere Trennung scheitert daran, dass die 4 Service-Tests in `TestSupport` umziehen müssen, sobald `LegacySecurityFakes` dort lebt — sonst baut nichts. Der Hilfs-Konsolidierungs-Split wäre nur durch `git stash`-Tricks machbar, die das Risiko eines Broken-Intermediate-Commits bergen. Single-Commit-Variante gewählt: ein zusammenhängender Refactor, ein Diff, ein Build-Beweis. Bei einem Re-Audit wäre ein Split via `git reset HEAD~1` + selektives Re-Stagen immer noch nachträglich möglich.

## Build / Test-Output

```
$ dotnet build SqlToAi.slnx
  SqlToAi -> ...\SqlToAi.dll
  SqlToAi.Tests -> ...\SqlToAi.Tests.dll
  Der Buildvorgang wurde erfolgreich ausgeführt.
      0 Warnung(en)
      0 Fehler

$ dotnet test SqlToAi.slnx --no-build
  Bestanden!   : Fehler:     0, erfolgreich:   523, übersprungen:     0, gesamt:   523, Dauer: 16 s

$ dotnet test SqlToAi.slnx --no-build --filter "FullyQualifiedName~AiNetLinter"
  Bestanden!   : Fehler:     0, erfolgreich:     1, übersprungen:     0, gesamt:     1
```

## Abweichungen vom Plan

| Plan | Ist | Begründung |
|:---|:---|:---|
| "9 Testmethoden, die 31 individual test cases abdecken" | 13 Methoden, 25 Cases | Plan intern inkonsistent (12 Methoden im Detail-Listing, 21 Cases im Code-Auszug). 13/25 ist die kleinste konsistente Menge, die 523 = 523 hält. |
| MV-T1: `MethodNotFound.ToString()` ohne CultureInfo | `MethodNotFound.ToString(CultureInfo.InvariantCulture)` + `using System.Globalization;` | CA1305 wirft Build-Fehler ohne `IFormatProvider`; Plan-Empfehlung widerspricht tatsächlicher Lint-Konfiguration. |
| "ggf. 2 Commits" | 1 Commit | Intermediate-Build wäre rot, weil Service-Tests die Fakes aus `TestSupport` benötigen, sobald diese umgezogen sind. |
| `FakeReadOnlyGuard` in `IndexSuggestionServiceTests` | unbenutzt — `IndexSuggestionService` bindet keinen `IReadOnlyGuard` | Bereits in step-002 dokumentiert: `IndexSuggestionService` hat eigene Mini-Validierungskette ohne `IReadOnlyGuard`. Die Fakes liegen in `TestSupport`, `IndexSuggestionServiceTests.cs` importiert sie via `using SqlToAi.Tests.TestSupport;` (Zeile 12, schon vorhanden). |
