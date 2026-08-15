---
status: done (pending audit)
type: step-result
task: audit-try-magicvalues
step: 001
completed_by: coder
completed_by_model: MiniMax-M3
completed_at: 2026-08-15T21:55:00+02:00
code_commit: "0f6f99a"
items_completed:
  - item-01
  - item-02
  - item-03
  - item-04
  - item-05
  - item-06
  - item-07
  - item-08
  - item-09
  - item-10
  - "zusa tzfund-QueryTokenResolver"
  - "zusa tzfund-DetailSchemaRenderer-251"
  - "zusa tzfund-ToolDispatcherTestFakes-185"
---

# Step 001 — Ergebnis (Coder)

## Status

`done (pending audit)` — alle 10 Plan-Items + die drei im JIT-Kontext dokumentierten Zusatzfunde
umgesetzt, `dotnet build` und `dotnet test` grün, Conventional Commit auf `main` abgelegt.

## Items

| Item | Befund | Datei(en) | Status |
|:---|:---|:---|:---|
| **item-01** | MV-1 — SQL-Server-Fehlercodes | neue Datei `src/SqlToAi/Database/SqlServerErrorCode.cs` (+13 Konstanten, inkl. der fünf vom Planer empfohlenen Codes `20, 40, 53, 10060, 10061`); Aufrufer in `SqlToAiErrorMapper.cs` (Timeout-OR + Switch-Arm), `PerformanceMeasurementService.cs` (3×), `IndexSuggestionService.cs` (2×) | done |
| **item-02** | MV-2 — Benchmark-Verdicts | neue Datei `src/SqlToAi/Database/BenchmarkVerdict.cs`; Ersetzung in `OptimizationBenchmarkService.cs` (4×), Interpolations-String in `ToolRegistry.cs` Description, Testseite `OptimizationBenchmarkServiceTests.cs` (3×), Test-Fake `ToolDispatcherTestFakes.cs:185` (Zusatzfund) | done |
| **item-03** | MV-3 — Gewichtungsfaktoren | `private const int DatabaseDimensionWeight/SchemaDimensionWeight/TableDimensionWeight` in `AnonymizationRuleProvider.cs`; `WeightedScore` umgeschrieben | done |
| **item-04** | MV-4 — FNV-1a | `private const uint FnvOffsetBasis32/FnvPrime32` lokal in `Anonymizer.GetStableHashCode`; Literale ersetzt | done |
| **item-05** | MV-5 — Regex-Timeout | neue Datei `src/SqlToAi/Security/SecurityConstants.cs` (`public static readonly TimeSpan DefaultRegexTimeout`); Ersetzung in `ReadOnlyGuard.cs`, `GlobMatcher.cs`, `LikePatternMatcher.cs` und **Zusatzfund `QueryTokenResolver.cs:77`** | done |
| **item-06** | MV-6 — Anonymisierungs-Modi | neue Datei `src/SqlToAi/Anonymization/AnonymizationMode.cs` (`Hash`, `Scramble`); `string.Equals(mode, "Hash", …)` in `Anonymizer.cs` umgestellt | done |
| **item-07** | MV-7 — SQL-Server-Objekttypen | neue Datei `src/SqlToAi/Database/SqlServerObjectType.cs` (`UserTable = "U"`, `View = "V"`); Ersetzung in `DetailSchemaRenderer.cs` Zeile 30 (ValidateTableOrViewAsync) und **Zusatzfund Zeile 251** (GetObjectReferencesAsync) | done |
| **item-08** | DRY-2 — DdlUnavailableNote | Sichtbarkeit in `DetailSchemaRenderer.cs:11` von `private` auf `internal` geändert; lokale Kopie in `TableSchemaRenderer.cs:13-14` entfernt; beide Verwendungen referenzieren `DetailSchemaRenderer.DdlUnavailableNote`. `InternalsVisibleTo Include="SqlToAi.Tests"` war bereits vorhanden (csproj-Zeile 28-30), keine Änderung nötig. | done |
| **item-09** | DRY-3 — OptionalStringParam | Methode in `ToolRegistry.cs` gelöscht; beide Aufrufer (`ArgObjectType`, `ArgTableName`) auf `StringParam` umgestellt | done |
| **item-10** | DRY-4 — BuildObjectDetailTool | Helper `BuildObjectDetailTool(string name, string description)` in `ToolRegistry.cs` ergänzt; fünf Builder (`BuildGetSchemaForeignKeys`, `BuildGetSchemaIndexes`, `BuildGetSchemaConstraints`, `BuildGetObjectReferences`, `BuildGetRoutineParameters`) auf je einen einzeiligen Aufruf reduziert. `BuildGetTriggerDefinition` bleibt eigenständig (drei Pflichtfelder) | done |

## Zusatzfunde (aus JIT-Kontext im Plan)

- **`src/SqlToAi/Database/QueryTokenResolver.cs:77`** — vierter ReDoS-Timeout ersetzt.
- **`src/SqlToAi/Database/DetailSchemaRenderer.cs:251`** — zweites `"U"`/`"V"`-Literal in `GetObjectReferencesAsync` ersetzt.
- **`tests/SqlToAi.Tests/Mcp/ToolDispatcherTestFakes.cs:185`** — zweites `"Recommended"`-Literal im Test-Fake auf `BenchmarkVerdict.Recommended` umgestellt.

## Empfehlung des Planers (bewusst umgesetzt)

Die fünf zusätzlichen SQL-Fehlercodes (`20, 40, 53, 10060, 10061`) wurden **im selben Schritt** als
Konstanten in `SqlServerErrorCode.cs` ergänzt (`InstanceNotFound`, `StatementTooComplex`,
`ServerNotFound`, `ConnectionTimedOut`, `ConnectionRefused`). Damit läuft der
`IsInfrastructureException`-Switch in `SqlToAiErrorMapper.cs` vollständig auf benannten Konstanten —
keine halbe Umstellung.

## Build & Test

- `dotnet build SqlToAi.slnx` — **0 Warnungen, 0 Fehler** (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` ist gesetzt, s. csproj:4).
- `dotnet test SqlToAi.slnx` — **523 Tests grün, 0 fehlgeschlagen, 0 übersprungen**, Dauer ~17 s.
- AiNetLinter: kein lokales `AiNetLinter.exe` vorhanden (s. Roadmap) — `RunLinterShouldBeClean` ist im Projekt bereits korrekt mit `Assert.Skip` versehen, kein Eingriff nötig.

## Geänderte Dateien (20)

```
src/SqlToAi/Anonymization/AnonymizationMode.cs              (neu)
src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs      (item-03)
src/SqlToAi/Anonymization/Anonymizer.cs                     (item-04, item-06)
src/SqlToAi/Anonymization/LikePatternMatcher.cs             (item-05)
src/SqlToAi/Database/BenchmarkVerdict.cs                    (neu, item-02)
src/SqlToAi/Database/DetailSchemaRenderer.cs                (item-07, item-08)
src/SqlToAi/Database/IndexSuggestionService.cs              (item-01)
src/SqlToAi/Database/OptimizationBenchmarkService.cs        (item-02)
src/SqlToAi/Database/PerformanceMeasurementService.cs       (item-01)
src/SqlToAi/Database/QueryTokenResolver.cs                  (item-05 Zusatzfund)
src/SqlToAi/Database/SqlServerErrorCode.cs                  (neu, item-01)
src/SqlToAi/Database/SqlServerObjectType.cs                 (neu, item-07)
src/SqlToAi/Database/SqlToAiErrorMapper.cs                  (item-01)
src/SqlToAi/Database/TableSchemaRenderer.cs                 (item-08)
src/SqlToAi/Domain/GlobMatcher.cs                           (item-05)
src/SqlToAi/Mcp/ToolRegistry.cs                             (item-02, item-09, item-10)
src/SqlToAi/Security/ReadOnlyGuard.cs                       (item-05)
src/SqlToAi/Security/SecurityConstants.cs                   (neu, item-05)
tests/SqlToAi.Tests/Database/OptimizationBenchmarkServiceTests.cs  (item-02)
tests/SqlToAi.Tests/Mcp/ToolDispatcherTestFakes.cs          (item-02 Zusatzfund)
```

## Commits

- `0f6f99a` — `refactor: zentralisiere MV-1..7 Konstanten und entferne Boilerplate-Duplikate [audit-try-magicvalues]` (Code-Commit, 20 Dateien, +177/−109)
- Doku-Commit erfolgt im Anschluss an diesen Schritt (separater Commit, kleiner, nur Step-Doku + CodeMap).

## Beobachtungen

- **Sichtbarkeits-/DRY-2-Hinweis:** `DetailSchemaRenderer` ist `internal static class` und
  `DdlUnavailableNote` jetzt `internal const string` — der Linter-Akzeptanztest
  `EnforceSealedClasses` (in den Linter-Regeln) verlangt `sealed` für konkrete Klassen.
  `internal static class` ist bereits `sealed` per Definition (statisch → nicht weiter ableitbar),
  der Linter sollte das nicht anmeckern. Falls doch, ist die Anpassung in einem Folge-Step zu
  prüfen — kein Eingriff in diesem Schritt, da außerhalb des Plans.
- **Tool-Beschreibung im Helper:** Beim Zusammenfassen der fünf Object-Detail-Builder auf
  `BuildObjectDetailTool` geht die differenzierte `object_name`-Parameter-Beschreibung verloren
  (war vorher "target table" / "target table or view" / "stored procedure or function"; jetzt
  einheitlich "target object"). Die `Tool.Description` der jeweiligen Builder macht den
  Unterschied weiterhin sichtbar. Der Plan hat diese Vereinfachung explizit so vorgesehen
  ("die fünf strukturgleichen Detail-Tool-Builder sind tatsächlich strukturidentisch").
- **Doku-Sync:** Die Konstanten-Erweiterung in `SqlToAiErrorMapper` und der Timeout-Helfer in
  `SecurityConstants` sind interne Refactorings — keine API- oder Verhaltensänderung am
  MCP-Output, daher kein Eingriff in `docs/architecture-spec.md` oder `README.md` nötig.
- **Kein 11. Item:** Bewusst keine über den Plan hinausgehenden Änderungen vorgenommen.
