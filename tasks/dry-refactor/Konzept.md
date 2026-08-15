---
status: draft
type: konzept
project_kind: brownfield
estimated_scope: medium
rules_dir: .agents/rules
last_updated: 2026-08-15
open_questions: []
---

# Konzept: DRY & Code-Qualitäts-Refactoring (Baseline-Eliminierung)

## Ziel (Was)

Beseitigung aller Code-Duplikate (DRY) und bestehenden Linter-Verstöße im Projekt `SqlToAi`. Vollständige Entfernung der Linter-Baseline (`SqlToAi-baseline.json`), sodass die gesamte Codebase (Produktionscode und Tests) ohne Ausnahmen die AiNetLinter- und Compiler-Regeln (Zero-Warning) erfüllt. Zudem Vereinheitlichung aller Kommentare und Dokumentationen auf einen neutralen, sachlichen und nicht-wertenden Ton, Entlastung des `ToolDispatcher` durch Service-Bündelung sowie nachhaltige Verbesserung der Test-Infrastruktur.

## Warum / Kontext

- **Hintergrund:** Die Baseline `SqlToAi-baseline.json` diente bisher als Übergangslösung zur Duldung bestehender Verstöße. Sie erzeugt Pflegeaufwand und verdeckt potenzielle Qualitäts- und Duplikationsprobleme.
- **Motivation:** Eine saubere, duplikatfreie Codebasis verbessert die Wartbarkeit, verringert Fehlerrisiken bei künftigen Erweiterungen und sorgt für klare Orientierung von KI-Assistenten.
- **Constraints:**
  - .NET 10 / C# 14, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
  - Zero-Warning-Direktive & AiNetLinter-Konformität (`.agents/rules/AiNetLinter.mdc`).
  - Alle bestehenden xUnit v3 Tests müssen nach dem Refactoring grün bleiben (keine Verhaltensänderung der öffentlichen API/MCP-Tools).

## Scope

### Muss-Haben

- **Baseline-Eliminierung & Regel-Synchronisation:**
  - Vollständiges Löschen von `SqlToAi-baseline.json`.
  - Entfernung des Tests `RecreateBaseline` in `AiNetLinterTests.cs`; Umstellung auf strikte Clean-Prüfung (0 Fehler, 0 Warnungen).
  - Aktualisierung von [.agents/rules/SqlToAiRichtlinien.mdc](.agents/rules/SqlToAiRichtlinien.mdc) (Entfernen der Baseline-Pflegeanweisungen).
- **Linter-Fehlerbehebung (Errors):**
  - Deklaration von `McpJsonContext` als `sealed partial class`.
  - Deklaration von `FakeDbConnection` als `sealed class`.
- **DRY-Konsolidierung (Produktionscode):**
  - **SQL-Parsing & Scanner:** Beseitigung der 6 exakten Duplikate zwischen [QueryDeconstructor.cs](src/SqlToAi/Database/QueryDeconstructor.cs) und [SqlMultiStatementDetector.cs](src/SqlToAi/Database/SqlMultiStatementDetector.cs) durch Migration auf die gemeinsame [SqlCharScanner.cs](src/SqlToAi/Database/SqlCharScanner.cs)-Infrastruktur.
  - **DB-Options-Ausführung:** Zusammenführung der duplizierten Methode `ExecuteSetOptionAsync` aus `PerformanceMeasurementService` und `QueryExecutionService` in einer internen Helper-Klasse oder Extension-Method.
- **Architektur & Dispatcher-Entlastung:**
  - Zusammenfassung der 4 Analyse- und Optimierungsdienste ([IPerformanceMeasurementService](src/SqlToAi/Database/IPerformanceMeasurementService.cs), [IQueryComparisonService](src/SqlToAi/Database/IQueryComparisonService.cs), [IOptimizationBenchmarkService](src/SqlToAi/Database/IOptimizationBenchmarkService.cs), [IIndexSuggestionService](src/SqlToAi/Database/IIndexSuggestionService.cs)) in ein Analyse-Aggregat bzw. eine Facade (`IDatabaseAnalysisService` / `DatabaseAnalysisServices`), um die Konstruktor-Abhängigkeiten von [ToolDispatcher.cs](src/SqlToAi/Mcp/ToolDispatcher.cs) von 7 auf maximal 4 zu senken.
  - Einführung von Parameter-Records in [PerformanceMeasurementService.cs](src/SqlToAi/Database/PerformanceMeasurementService.cs) für `ExecuteWarmupRunsAsync` und `ExecuteMeasuredRunsAsync` zur Einhaltung von `MaxMethodParameterCount <= 6`.
- **Verbesserte Test-Infrastruktur & Testklassen-Splits:**
  - **Zentrale Test-Helfer:** Zusammenführung duplizierter Helper-Methoden (`CreateWriter`, `CreateAnonymizer`, `BuildTokenizationOptions`) in zentralen Hilfsklassen unter [tests/SqlToAi.Tests/TestSupport/](tests/SqlToAi.Tests/TestSupport/).
  - **Aufteilung überbreiter Testklassen:** Aufteilung von Testklassen mit mehr als 15 öffentlichen Methoden in thematisch fokussierte Teilklassen (`QueryExecutionServiceTests`, `SchemaServiceTests`, `SchemaServiceIntegrationTests`, `ToolDispatcherTests`), um `MaxPublicMembersPerType <= 15` einzuhalten.
  - Auflösung der Middle-Man-Warnung in `GlobMatcherTests`.
- **Neutralitäts- und Sprach-Audit:**
  - Prüfung und Bereinigung aller Quellcode-Kommentare und Docs auf sachliche, neutrale und nicht-wertende englische Sprache.

### Nice-to-Have (Zwischenspeicher — vor `status: ready` aufgelöst)

*Keine Einträge (vollständig nach Muss-Haben überführt).*

### Non-Goals (bewusst NICHT Teil davon)

- Keine funktionalen Änderungen an den MCP-Tool-Schnittstellen oder dem JSON-RPC-Protokoll.
- Keine Einführung neuer externer Bibliotheken oder Frameworks.
- Keine Umstellung der Kern-Architektur (Dapper, Stdio-MCP-Server bleiben wie gehabt).

## Zielplattformen / Technischer Rahmen

- **Runtime & Sprache:** .NET 10 / C# 14.
- **Linter & Analyse:** Roslyn-basierter AiNetLinter (MCP-Server & CLI) gem. `.agents/rules/AiNetLinter.mdc`.
- **Test-Framework:** xUnit v3 (`dotnet test`).

## Verworfene Alternativen

- **Beibehalten einer reduzierten Baseline:** verworfen, da das explizite Ziel eine baseline-freie, vollständig konforme Codebasis ist.
- **Regelwerk lockern statt Code anpassen:** verworfen, da die Qualitätsregeln bewusst als Standard für hohe Codequalität und Agenten-Orientierung definiert sind.
- **ToolDispatcher-Abhängigkeiten über IServiceProvider auflösen:** verworfen zugunsten expliziter Service-Bündelung / Facade (verhindert versteckte Abhängigkeiten und Service-Locator-Antipatterns).

## Wo im Projekt

- [src/SqlToAi/Database/SqlCharScanner.cs](src/SqlToAi/Database/SqlCharScanner.cs) — Gemeinsame Scanner- und Tokenizer-Infrastruktur für SQL-Parsing.
- [src/SqlToAi/Database/QueryDeconstructor.cs](src/SqlToAi/Database/QueryDeconstructor.cs) — Parsing- & Statement-Zerlegung (wird auf `SqlCharScanner` migriert).
- [src/SqlToAi/Database/SqlMultiStatementDetector.cs](src/SqlToAi/Database/SqlMultiStatementDetector.cs) — Statement-Erkennung (wird auf `SqlCharScanner` migriert).
- [src/SqlToAi/Database/PerformanceMeasurementService.cs](src/SqlToAi/Database/PerformanceMeasurementService.cs) — `ExecuteSetOptionAsync`-Duplikat und Methoden mit >6 Parametern (Parameter-Records).
- [src/SqlToAi/Database/QueryExecutionService.cs](src/SqlToAi/Database/QueryExecutionService.cs) — `ExecuteSetOptionAsync`-Duplikat (wird zentralisiert).
- [src/SqlToAi/Database/IDatabaseAnalysisService.cs](src/SqlToAi/Database/IDatabaseAnalysisService.cs) — Neue Facade / Aggregat zur Bündelung von Mess-, Benchmark- und Index-Diensten.
- [src/SqlToAi/Mcp/ToolDispatcher.cs](src/SqlToAi/Mcp/ToolDispatcher.cs) — Konstruktor-Refactoring zur Reduktion der Abhängigkeiten.
- [src/SqlToAi/Mcp/McpJsonContext.cs](src/SqlToAi/Mcp/McpJsonContext.cs) — `sealed`-Modifikator ergänzen.
- [tests/SqlToAi.Tests/TestSupport/FakeDbConnection.cs](tests/SqlToAi.Tests/TestSupport/FakeDbConnection.cs) — `sealed`-Modifikator ergänzen.
- [tests/SqlToAi.Tests/TestSupport/](tests/SqlToAi.Tests/TestSupport/) — Zentrale Test-Fixtures und Builder-Helper.
- [tests/SqlToAi.Tests/AiNetLinter/AiNetLinterTests.cs](tests/SqlToAi.Tests/AiNetLinter/AiNetLinterTests.cs) — Baseline-Testlogik entfernen, Clean-Check aktivieren.
- [tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json](tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json) — Zu löschende Baseline-Datei.
- [tests/SqlToAi.Tests/Database/](tests/SqlToAi.Tests/Database/) — Testklassen-Splits für `QueryExecutionServiceTests` und `SchemaServiceTests`.
- [tests/SqlToAi.Tests/Mcp/](tests/SqlToAi.Tests/Mcp/) — Testklassen-Splits für `ToolDispatcherTests` und Vereinheitlichung der MCP-Test-Helper.
- [tests/SqlToAi.Tests/Integration/](tests/SqlToAi.Tests/Integration/) — Testklassen-Splits für `SchemaServiceIntegrationTests`.
- [.agents/rules/SqlToAiRichtlinien.mdc](.agents/rules/SqlToAiRichtlinien.mdc) — Projektregeln bzgl. Baseline-Pflege synchronisieren.

## Entdeckte Mängel/Redundanzen

- **SQL Parsing / Tokenizer Duplikation**
  - **Gefunden:** [QueryDeconstructor.cs](src/SqlToAi/Database/QueryDeconstructor.cs#L91-L260) und [SqlMultiStatementDetector.cs](src/SqlToAi/Database/SqlMultiStatementDetector.cs#L48-L150) enthalten 6 identische Hilfsmethoden (`GetSemicolonIndices`, `GetSegmentsFromIndices`, `GetLastNonEmptyIndex`, `StripLeadingCommentsAndWhitespace`, `TrySkipLineComment`, `TrySkipBlockComment`).
  - **Bezug:** Regel `DuplicateCode` (`.agents/rules/AiNetLinter.mdc`).
  - **Vorschlag:** Auslagerung und Vereinheitlichung auf Basis von [SqlCharScanner.cs](src/SqlToAi/Database/SqlCharScanner.cs).
  - **Entscheidung:** übernommen ins Scope (→ siehe Muss-Haben „DRY-Konsolidierung (Produktionscode)").

- **ExecuteSetOptionAsync Duplikation**
  - **Gefunden:** [PerformanceMeasurementService.cs](src/SqlToAi/Database/PerformanceMeasurementService.cs#L275) und [QueryExecutionService.cs](src/SqlToAi/Database/QueryExecutionService.cs#L300) haben identische Implementierungen von `ExecuteSetOptionAsync`.
  - **Bezug:** Regel `DuplicateCode` (`.agents/rules/AiNetLinter.mdc`).
  - **Vorschlag:** Zentralisierung in einem internen DB-Execution-Helper.
  - **Entscheidung:** übernommen ins Scope (→ siehe Muss-Haben „DRY-Konsolidierung (Produktionscode)").

- **Fehlende sealed-Modifikatoren**
  - **Gefunden:** [McpJsonContext.cs](src/SqlToAi/Mcp/McpJsonContext.cs#L11) und [FakeDbConnection.cs](tests/SqlToAi.Tests/TestSupport/FakeDbConnection.cs#L33).
  - **Bezug:** Regel `EnforceSealedClasses` (`.agents/rules/AiNetLinter.mdc`).
  - **Vorschlag:** Klassen als `sealed` deklarieren.
  - **Entscheidung:** übernommen ins Scope (→ siehe Muss-Haben „Linter-Fehlerbehebung").

- **Hohe Parameterzahl in Methoden & Konstruktoren**
  - **Gefunden:** [PerformanceMeasurementService.cs](src/SqlToAi/Database/PerformanceMeasurementService.cs#L201-L229) (8 Parameter) und [ToolDispatcher.cs](src/SqlToAi/Mcp/ToolDispatcher.cs#L51) (7 Konstruktor-Parameter).
  - **Bezug:** Regeln `MaxMethodParameterCount` und `MaxConstructorDependencies` (`.agents/rules/AiNetLinter.mdc`).
  - **Vorschlag:** Einführung von Parameter-Records und Bündelung von Analyse-Diensten in eine Facade / Aggregat.
  - **Entscheidung:** übernommen ins Scope (→ siehe Muss-Haben „Architektur & Dispatcher-Entlastung").

- **Überbreite Testklassen**
  - **Gefunden:** `QueryExecutionServiceTests` (23 Public Members), `SchemaServiceTests` (19 Public Members), `SchemaServiceIntegrationTests` (17 Public Members), `ToolDispatcherTests` (16 Public Members).
  - **Bezug:** Regel `MaxPublicMembersPerType` (`.agents/rules/AiNetLinter.mdc`).
  - **Vorschlag:** Echte Aufteilung in separate, fokussierte Testklassen nach Test-Szenarien und Zentralisierung der Test-Helper.
  - **Entscheidung:** übernommen ins Scope (→ siehe Muss-Haben „Verbesserte Test-Infrastruktur").

- **Duplizierte Test-Hilfsmethoden**
  - **Gefunden:** Redundante `BuildTokenizationOptions`, `CreateWriter`, `CreateAnonymizer` in Testdateien.
  - **Bezug:** Regel `DuplicateCode` (`.agents/rules/AiNetLinter.mdc`).
  - **Vorschlag:** Zentralisierung in `tests/SqlToAi.Tests/TestSupport/`.
  - **Entscheidung:** übernommen ins Scope (→ siehe Muss-Haben „Verbesserte Test-Infrastruktur").

## Wie (grober Ansatz)

1. **Phase 1: Baseline-Entkopplung & Clean-Gate:**
   - `SqlToAi-baseline.json` löschen.
   - `AiNetLinterTests.cs` anpassen (Recreate-Test entfernen, Clean-Check schärfen).
   - `.agents/rules/SqlToAiRichtlinien.mdc` aktualisieren.
2. **Phase 2: Core-Produktionscode Bereinigung:**
   - `sealed` ergänzen (`McpJsonContext`).
   - SQL-Scanner & Deconstructor auf `SqlCharScanner` konsolidieren.
   - `ExecuteSetOptionAsync` in Shared-DB-Helper extrahieren.
   - Facade `IDatabaseAnalysisService` einführen und `ToolDispatcher` entlasten.
   - Parameter-Records in `PerformanceMeasurementService` einführen.
3. **Phase 3: Test-Infrastruktur & Testklassen-Splits:**
   - `FakeDbConnection` als `sealed` deklarieren.
   - Gemeinsame Test-Helper in `TestSupport` extrahieren.
   - Überbreite Testklassen aufteilen (`QueryExecutionServiceTests`, `SchemaServiceTests`, `SchemaServiceIntegrationTests`, `ToolDispatcherTests`).
   - Middle-Man Warnung in `GlobMatcherTests` bereinigen.
4. **Phase 4: Sprach-Audit & Finale Verifikation:**
   - Kommentare und Markdown-Dokumente auf sachliche Neutralität prüfen.
   - `safeguard` MCP-Check ausführen (Ziel: Score 10.00 / 10).
   - Vollständigen Test-Lauf (`dotnet test`) durchführen.

## Definition of Done / Erfolgskriterien

- `dotnet build` läuft ohne Warnungen und ohne Fehler durch (`TreatWarningsAsErrors`).
- `dotnet test` läuft vollständig grün durch (alle Unit- und Integrationstests erfolgreich).
- `AiNetLinter` MCP-Tool `safeguard` liefert einen Quality-Score von **10,00/10** (0 Fehler, 0 Warnungen).
- Die Datei `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` ist gelöscht und wird im Build/Test nicht mehr referenziert.
- Kommentare und Dokumentation sind sachlich, neutral und auf Englisch verfasst.

## Offene Punkte

*Keine — alle Kernentscheidungen wurden in Runde 1 geklärt.*