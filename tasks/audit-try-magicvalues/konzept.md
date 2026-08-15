---
status: ready
type: konzept
project_kind: brownfield
estimated_scope: medium
rules_dir: .agents/rules
last_updated: "2026-08-15"
open_questions: []
---

# Konzept: DRY-Konsolidierung & Magic-Values-Bereinigung

## Ziel (Was)

Strukturierte Beseitigung aller im 360-Grad-Audit ([audit-dry-magicvalues.md](audit-dry-magicvalues.md)) identifizierten Code-Duplikate und hartkodierten Werte. Kernziele sind die Extraktion einer zentralen Guardrail-Pipeline (`IQuerySafetyValidator`) zur Ablösung redundanter Validierungslogik in vier Services sowie die Einführung typsicherer Konstanten für SQL-Server-Fehlercodes (`SqlServerErrorCode`), Benchmark-Verdicts (`BenchmarkVerdict`), FNV-1a-Hash-Werte und Regex-Timeouts.

## Warum / Kontext

Der Solution-weite 360-Grad-Audit hat signifikanten Refactoring-Drift und Verstöße gegen die Projektrichtlinien ([SqlToAiRichtlinien.mdc](../../.agents/rules/SqlToAiRichtlinien.mdc) §4 *"No Magic Values"* & §5 *"Qualitätsdrift-Prävention"*) aufgedeckt:

1. **Sicherheitsrelevanter Drift (DRY-1):** Vier Query-verarbeitende Services (`QueryExecutionService`, `QueryValidationService`, `PerformanceMeasurementService`, `QueryComparisonService`) führen dieselbe 6-stufige Validierung (Parameter, Whitelist, AccessLevel, ReadOnlyGuard, MultiStatement) redundant durch. Bei Anpassungen des Berechtigungsmodells drohen Sicherheitslücken durch asynchrone Wartung.
2. **Kryptische Magic Numbers (MV-1, MV-3, MV-4):** SQL-Server-Fehlernummern (`262`, `297`, `300`, `-2`, `121`, `258`, `233`, `18456`), Gewichtungsfaktoren (`1000`, `100`, `10`) und FNV-1a-Parameter (`2166136261`, `16777619`) sind als rohe Zahlen im Code verstreut.
3. **Vertragsrelevante Magic Strings (MV-2, MV-6):** Benchmark-Ergebnisse (`"Recommended"`, `"NotRecommended"` etc.) und Anonymisierungs-Modi (`"Hash"`, `"Scramble"`) werden ohne Typsicherheit als String-Literale geführt.

## Scope

### Muss-Haben

1. **Konstanten-Zentralisierung (Phase 1):**
   - Einführung von `SqlServerErrorCode.cs` für alle SQL-Server-Fehlernummern (Permissions, Timeouts, Connection-Resets, Auth-Fehler).
   - Einführung von `BenchmarkVerdict.cs` für alle Urteile von `sql_benchmark_optimization`.
   - Einführung von `AnonymizationMode.cs` für Anonymisierungs-Modi (`Hash`, `Scramble`).
   - Deklaration von `SecurityConstants.DefaultRegexTimeout` (`200 ms`) und Ersetzung verstreuter `TimeSpan.FromMilliseconds(200)` in `ReadOnlyGuard`, `GlobMatcher`, `LikePatternMatcher`.
   - Benannte Konstanten für FNV-1a (`FnvOffsetBasis32`, `FnvPrime32`) in `Anonymizer.cs`.
   - Benannte Gewichtungskonstanten (`DatabaseDimensionWeight` etc.) in `AnonymizationRuleProvider.cs`.
   - Auslagerung von `DdlUnavailableNote` in eine gemeinsame Konstante in `DetailSchemaRenderer.cs`.
   - Entfernung des redundanten Scheinduplikats `OptionalStringParam` in `ToolRegistry.cs`.

2. **Architektur-Konsolidierung Guardrails (Phase 2 — DRY-1):**
   - Entwurf und Implementierung von `IQuerySafetyValidator` / `QuerySafetyValidator` zur Kapselung der 6-stufigen Validierung.
   - Umstellung von `QueryExecutionService`, `QueryValidationService`, `PerformanceMeasurementService` und `QueryComparisonService` auf den neuen Validator.
   - Reduktion redundanter Constructor-Dependencies (`ISecurityGuard`, `IAccessLevelProvider`, `IReadOnlyGuard`) in den betroffenen Services.

3. **Test-Suite-Bereinigung (Phase 3 — DRY-T1 bis T3):**
   - Konsolidierung doppelter Fakes in `TestSupport/` bzw. `ToolDispatcherTestFakes.cs`.
   - Einführung von `ShowPlanTestHelper` zur Beseitigung redundanter XML-Test-Fixtures.
   - Konsolidierung der 33 duplizierten Negativ-Guardrail-Tests in `QuerySafetyValidatorTests.cs`.

### Non-Goals (bewusst NICHT Teil davon)

- **Keine Zusammenlegung von `GlobMatcher` und `LikePatternMatcher`:** Beide Matcher bedienen unterschiedliche Wildcard-Dialekte (Glob `*`/`?` vs. SQL-LIKE `%`/`_`). Nur das Timeout und Exception-Handling werden vereinheitlicht.
- **Keine Änderung der `SqlToAiOptions`-Defaults:** Property-Initialisierer in `*Options`-Klassen sind laut Richtlinie §4 der einzig autorisierte Ort für Konfigurations-Defaults.
- **Kein Auslagern von `"Password"` in `AppSettingsMigrator` in einen Secret-Store:** `"Password"` ist ein JSON-Property-Schlüsselname für den Backup-Maskierer, kein Klartext-Passwort (False Positive des Linters).
- **Keine Änderung der `SchemaService`-Forwarder:** Die Methoden bilden das öffentliche Interface-API ab und delegieren bereits an die gemeinsame Hilfsmethode `ExecuteDetailQueryAsync`.

## Zielplattformen / Technischer Rahmen

- **Sprache & Runtime:** C# 14 / .NET 10
- **Test-Framework:** xUnit v3
- **Linter-Vorgaben:** Null-Toleranz-Politik (0 Fehler, 0 Warnungen) gegen [.agents/rules/AiNetLinter.mdc](../../.agents/rules/AiNetLinter.mdc).
- **Fehlerbehandlung:** Weiterhin strikte Verwendung von `Result<T>` und des standardisierten Fehlerkatalogs (`SqlToAiError`).

## Verworfene Alternativen

- **Reflexionsbasierte Generifizierung der Enum-Parser in `Program.cs`:** Verworfen, da typsichere `switch`-Expressions mit spezifischen Defaults und Aliasen (`"info"`, `"warn"`) performanter, AOT-kompatibel und lesbarer sind.
- **Zusammenlegung von `WriteResultAndCapture` und `WriteErrorAndCapture` in `McpHost.cs`:** Verworfen, da `System.Text.Json` Source Generation (`McpJsonContext.Default`) typspezifische Aufrufe für `JsonRpcResponse` vs. `JsonRpcErrorResponse` erfordert.
- **Merge von `GlobMatcher` und `LikePatternMatcher` in eine universelle Regex-Engine:** Verworfen, da Spezifitäts-Scoring (`SpecificityScore`) eine reine Domäneneigenschaft von SQL-Anonymisierungsregeln ist und nicht in den allgemeinen Glob-Matcher gehört.

## Wo im Projekt

### Produktionscode (`src/SqlToAi`)
- [src/SqlToAi/Database/QueryExecutionService.cs](src/SqlToAi/Database/QueryExecutionService.cs#L100-L136) — Guardrail-Validierung
- [src/SqlToAi/Database/QueryValidationService.cs](src/SqlToAi/Database/QueryValidationService.cs#L66-L104) — Guardrail-Validierung
- [src/SqlToAi/Database/PerformanceMeasurementService.cs](src/SqlToAi/Database/PerformanceMeasurementService.cs#L113-L168) — Guardrails & Error Code 262
- [src/SqlToAi/Database/QueryComparisonService.cs](src/SqlToAi/Database/QueryComparisonService.cs#L121-L153) — Guardrail-Validierung
- [src/SqlToAi/Database/IndexSuggestionService.cs](src/SqlToAi/Database/IndexSuggestionService.cs#L291-L292) — Error Codes 300 & 297
- [src/SqlToAi/Database/SqlToAiErrorMapper.cs](src/SqlToAi/Database/SqlToAiErrorMapper.cs#L48-L78) — Error Codes (-2, 121, 258, 233, etc.)
- [src/SqlToAi/Database/OptimizationBenchmarkService.cs](src/SqlToAi/Database/OptimizationBenchmarkService.cs#L104-L129) — Verdict-Strings
- [src/SqlToAi/Anonymization/Anonymizer.cs](src/SqlToAi/Anonymization/Anonymizer.cs#L88-L133) — FNV-1a Konstanten & Modus-Strings
- [src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs](src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs#L290) — Gewichtungsfaktoren
- [src/SqlToAi/Security/ReadOnlyGuard.cs](src/SqlToAi/Security/ReadOnlyGuard.cs#L24) — Regex-Timeout
- [src/SqlToAi/Mcp/ToolRegistry.cs](src/SqlToAi/Mcp/ToolRegistry.cs#L119-L358) — Tool-Definitionen & `OptionalStringParam`
- [src/SqlToAi/Database/TableSchemaRenderer.cs](src/SqlToAi/Database/TableSchemaRenderer.cs#L13) & [DetailSchemaRenderer.cs](src/SqlToAi/Database/DetailSchemaRenderer.cs#L11) — `DdlUnavailableNote`

### Testcode (`tests/SqlToAi.Tests`)
- [tests/SqlToAi.Tests/Database/PerformanceMeasurementServiceTests.cs](tests/SqlToAi.Tests/Database/PerformanceMeasurementServiceTests.cs#L97-L374) — ShowPlan-XML-Fixtures
- `tests/SqlToAi.Tests/Mcp/ToolDispatcherTestFakes.cs` & `TestSupport/` — Zentrale Fakes
- Service-Testklassen — 33 redundante Negativ-Tests

## Entdeckte Mängel/Redundanzen

Alle Einzelfunde, Code-Snippets und detaillierten Bewertungen sind im begleitenden Audit-Bericht dokumentiert:
👉 [tasks/audit-try-magicvalues/audit-dry-magicvalues.md](audit-dry-magicvalues.md)

1. **Guardrail-Validierung 4x inline nachgebaut (DRY-1):**
   - **Gefunden:** `QueryExecutionService`, `QueryValidationService`, `PerformanceMeasurementService`, `QueryComparisonService`.
   - **Bezug:** [SqlToAiRichtlinien.mdc](../../.agents/rules/SqlToAiRichtlinien.mdc) §2 & §5.
   - **Vorschlag:** Extraktion `IQuerySafetyValidator`.
   - **Entscheidung:** Übernommen ins Scope (Muss-Haben).
2. **Rohe SQL-Server-Fehlercodes (MV-1):**
   - **Gefunden:** `SqlToAiErrorMapper.cs`, `PerformanceMeasurementService.cs`, `IndexSuggestionService.cs`.
   - **Bezug:** [SqlToAiRichtlinien.mdc](../../.agents/rules/SqlToAiRichtlinien.mdc) §4 (*"No Magic Values"*).
   - **Vorschlag:** Einführung `SqlServerErrorCode.cs`.
   - **Entscheidung:** Übernommen ins Scope (Muss-Haben).
3. **Hardcodierte Benchmark-Verdicts (MV-2):**
   - **Gefunden:** `OptimizationBenchmarkService.cs`, `ToolRegistry.cs`, Tests.
   - **Bezug:** [SqlToAiRichtlinien.mdc](../../.agents/rules/SqlToAiRichtlinien.mdc) §4.
   - **Vorschlag:** Einführung `BenchmarkVerdict.cs`.
   - **Entscheidung:** Übernommen ins Scope (Muss-Haben).
4. **Verstreute Regex-Timeouts (MV-5, DRY-5):**
   - **Gefunden:** `ReadOnlyGuard.cs`, `GlobMatcher.cs`, `LikePatternMatcher.cs`.
   - **Bezug:** [SqlToAiRichtlinien.mdc](../../.agents/rules/SqlToAiRichtlinien.mdc) §4.
   - **Vorschlag:** `SecurityConstants.DefaultRegexTimeout`.
   - **Entscheidung:** Übernommen ins Scope (Muss-Haben).
5. **Scheinduplikat `OptionalStringParam` (DRY-3):**
   - **Gefunden:** `ToolRegistry.cs:356`.
   - **Bezug:** [AiNetLinter.mdc](../../.agents/rules/AiNetLinter.mdc) (DuplicateCode).
   - **Vorschlag:** Ersetzen durch `StringParam`.
   - **Entscheidung:** Übernommen ins Scope (Muss-Haben).

## Wie (grober Ansatz)

1. **Phase 1: Konstanten & Bereinigung (Low-Risk, isoliert):**
   - Neue Klassen `SqlServerErrorCode.cs`, `BenchmarkVerdict.cs`, `AnonymizationMode.cs` anlegen.
   - Timeouts, FNV-Konstanten, Gewichte und Ddl-Hinweise umstellen.
   - `OptionalStringParam` entfernen.
   - `dotnet test` & Linter-Check ausführen.
2. **Phase 2: Guardrail-Pipeline-Architektur:**
   - `IQuerySafetyValidator` mit Implementierung `QuerySafetyValidator` erstellen.
   - Die 4 Services schrittweise auf den Validator migrieren.
   - `dotnet test` ausführen und Verhaltensgleichheit absichern.
3. **Phase 3: Test-Konsolidierung:**
   - `ShowPlanTestHelper` anlegen und ShowPlan-Tests refaktorisieren.
   - Fakes bereinigen.
   - Dedizierte `QuerySafetyValidatorTests` anlegen und Redundanzen in Service-Tests abbauen.

## Definition of Done / Erfolgskriterien

1. **Zero Warnings & Build-Erfolg:** `dotnet build` läuft fehler- und warnungsfrei durch (`TreatWarningsAsErrors`).
2. **Grüne Test-Suite:** `dotnet test` besteht zu 100%.
3. **Linter-Konformität:** `AiNetLinter` meldet 0 Fehler und 0 Warnungen auf dem geänderten Produktions- und Testcode.
4. **Keine Magic Numbers / Strings:** Alle in MV-1 bis MV-7 genannten Stellen nutzen benannte Konstanten.
5. **Deduplizierte Guardrails:** Die 6-stufige Validierung existiert nur noch einmalig in `QuerySafetyValidator`.
6. **Dokumentation:** `docs/architecture-spec.md` und `README.md` sind synchronisiert (falls öffentliche Schnittstellen berührt werden).

## Offene Punkte

Keine offenen Punkte. Das Konzept ist vollständig spezifiziert und bereit für die Umsetzung.
