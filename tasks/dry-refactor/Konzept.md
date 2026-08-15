---
status: draft
type: konzept
project_kind: brownfield
estimated_scope: medium
rules_dir: .agents/rules
last_updated: 2026-08-15
open_questions:
  - Soll die Linter-Baseline (SqlToAi-baseline.json) mitsamt RecreateBaseline-Test und Regelwerk-Referenzen restlos entfernt werden, sodass der CI/Test-Lauf strikt Clean-Checks (0 Fehler, 0 Warnings) erzwingt?
  - Wie tief soll die Konsolidierung der SQL-Parsing-Helfer (QueryDeconstructor, SqlMultiStatementDetector, SqlCharScanner) gehen (z. B. einheitliche Shared Scanner/Tokenizer-Komponente)?
  - Sollen die Testklassen-Splits (Aufteilung von z. B. QueryExecutionServiceTests und SchemaServiceTests zur Einhaltung von MaxPublicMembersPerType <= 15) über Partial Classes oder separate thematische Testklassen erfolgen?
  - Gibt es neben den Linter-Befunden spezifische Architektur-Bereiche, die im Refactoring priorisiert adressiert werden sollen?
---

# Konzept: DRY & Code-Qualitäts-Refactoring (Baseline-Eliminierung)

## Ziel (Was)

Beseitigung aller Code-Duplikate (DRY) und bestehenden Linter-Verstöße im Projekt `SqlToAi`. Vollständige Entfernung der Linter-Baseline (`SqlToAi-baseline.json`), sodass die gesamte Codebase (Produktionscode und Tests) ohne Ausnahmen die AiNetLinter- und Compiler-Regeln (Zero-Warning) erfüllt. Zudem Vereinheitlichung aller Kommentare und Dokumentationen auf einen neutralen, sachlichen und nicht-wertenden Ton sowie gezielte Behebung identifizierter architektonischer Schwächen.

## Warum / Kontext

- **Hintergrund:** Die Baseline `SqlToAi-baseline.json` diente bisher als Übergangslösung zur Duldung bestehender Verstöße. Sie erzeugt Pflegeaufwand und verdeckt potenzielle Qualitäts- und Duplikationsprobleme.
- **Motivation:** Eine saubere, duplikatfreie Codebasis verbessert die Wartbarkeit, verringert Fehlerrisiken bei künftigen Erweiterungen und sorgt für klare Orientierung von KI-Assistenten.
- **Constraints:**
  - .NET 10 / C# 14, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
  - Zero-Warning-Direktive & AiNetLinter-Konformität (`.agents/rules/AiNetLinter.mdc`).
  - Alle bestehenden xUnit v3 Tests müssen nach dem Refactoring grün bleiben (keine Verhaltensänderung der öffentlichen API/MCP-Tools).

## Scope

### Muss-Haben

- **Baseline-Eliminierung:** Vollständiges Beheben aller 25 Linter-Verstöße (2 Errors, 23 Warnings in `src/` und `tests/`), Löschen der `SqlToAi-baseline.json` und Anpassung der Testsuite (`AiNetLinterTests.cs`).
- **DRY-Konsolidierung (Produktionscode):**
  - Zusammenführung der exakten Duplikate zwischen `QueryDeconstructor` und `SqlMultiStatementDetector` (Kommentar- und Statement-Scanning).
  - Konsolidierung von `ExecuteSetOptionAsync` zwischen `PerformanceMeasurementService` und `QueryExecutionService`.
- **DRY-Konsolidierung (Testcode):**
  - Zusammenführung redundanter Test-Helper (`BuildTokenizationOptions`, `CreateWriter`, `CreateAnonymizer`).
- **Konstruktor- & Parameter-Refactoring:**
  - Reduktion der Parameterzahl in `PerformanceMeasurementService` (`ExecuteWarmupRunsAsync`, `ExecuteMeasuredRunsAsync`) via Parameter-Records.
  - Reduktion der Konstruktor-Abhängigkeiten in `ToolDispatcher` (max. 5 gem. Regelwerk).
- **Testklassen-Restrukturierung:**
  - Aufteilung/Kapselung zu großer Testklassen (`QueryExecutionServiceTests`, `SchemaServiceTests`, `SchemaServiceIntegrationTests`, `ToolDispatcherTests`), um `MaxPublicMembersPerType <= 15` einzuhalten.
- **Neutralitäts-Audit:**
  - Überprüfung und Bereinigung aller Quellcode-Kommentare und Docs auf neutrale, sachliche Sprache (keine wertenden Formulierungen).
- **Doku- & Regel-Synchronisation:**
  - Aktualisierung von `.agents/rules/SqlToAiRichtlinien.mdc` (Abschnitt zur Baseline entfernen/anpassen).

### Nice-to-Have (Zwischenspeicher — vor `status: ready` aufgelöst)

*Keine offenen Punkte im Zwischenspeicher.*

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

## Wo im Projekt

- [src/SqlToAi/Database/QueryDeconstructor.cs](src/SqlToAi/Database/QueryDeconstructor.cs) — Parsing- & Statement-Zerlegung (Duplikate zu `SqlMultiStatementDetector`).
- [src/SqlToAi/Database/SqlMultiStatementDetector.cs](src/SqlToAi/Database/SqlMultiStatementDetector.cs) — Statement-Erkennung & Kommentar-Skipping (Duplikat-Quelle).
- [src/SqlToAi/Database/PerformanceMeasurementService.cs](src/SqlToAi/Database/PerformanceMeasurementService.cs) — `ExecuteSetOptionAsync`-Duplikat und Methoden mit >6 Parametern.
- [src/SqlToAi/Database/QueryExecutionService.cs](src/SqlToAi/Database/QueryExecutionService.cs) — `ExecuteSetOptionAsync`-Duplikat.
- [src/SqlToAi/Mcp/ToolDispatcher.cs](src/SqlToAi/Mcp/ToolDispatcher.cs) — Konstruktor mit 7 Abhängigkeiten (Limit: 5).
- [src/SqlToAi/Mcp/McpJsonContext.cs](src/SqlToAi/Mcp/McpJsonContext.cs) — `sealed`-Modifikator fehlt, AIContextFootprint.
- [tests/SqlToAi.Tests/TestSupport/FakeDbConnection.cs](tests/SqlToAi.Tests/TestSupport/FakeDbConnection.cs) — `sealed`-Modifikator fehlt.
- [tests/SqlToAi.Tests/AiNetLinter/AiNetLinterTests.cs](tests/SqlToAi.Tests/AiNetLinter/AiNetLinterTests.cs) — Baseline-Testlogik & Clean-Check.
- [tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json](tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json) — Zu entfernende Baseline-Datei.
- [tests/SqlToAi.Tests/Database/](tests/SqlToAi.Tests/Database/) — Testklassen mit >15 Public Membern und duplizierten Test-Helpern.
- [tests/SqlToAi.Tests/Mcp/](tests/SqlToAi.Tests/Mcp/) — Testklassen mit duplizierten Hilfsmethoden (`CreateWriter`, `CreateAnonymizer`).
- [.agents/rules/SqlToAiRichtlinien.mdc](.agents/rules/SqlToAiRichtlinien.mdc) — Projektregeln bzgl. Baseline-Pflege synchronisieren.

## Entdeckte Mängel/Redundanzen

- **SQL Parsing / Tokenizer Duplikation**
  - **Gefunden:** [QueryDeconstructor.cs](src/SqlToAi/Database/QueryDeconstructor.cs#L91-L260) und [SqlMultiStatementDetector.cs](src/SqlToAi/Database/SqlMultiStatementDetector.cs#L48-L150) enthalten 6 identische Hilfsmethoden (`GetSemicolonIndices`, `GetSegmentsFromIndices`, `GetLastNonEmptyIndex`, `StripLeadingCommentsAndWhitespace`, `TrySkipLineComment`, `TrySkipBlockComment`).
  - **Bezug:** Regel `DuplicateCode` (`.agents/rules/AiNetLinter.mdc`).
  - **Vorschlag:** Auslagerung der gemeinsamen Scanner- und Segmentierungslogik in [SqlCharScanner.cs](src/SqlToAi/Database/SqlCharScanner.cs) oder eine dedizierte interne Hilfsklasse.
  - **Entscheidung:** übernommen ins Scope (→ siehe Muss-Haben).

- **ExecuteSetOptionAsync Duplikation**
  - **Gefunden:** [PerformanceMeasurementService.cs](src/SqlToAi/Database/PerformanceMeasurementService.cs#L275) und [QueryExecutionService.cs](src/SqlToAi/Database/QueryExecutionService.cs#L300) haben identische Implementierungen von `ExecuteSetOptionAsync`.
  - **Bezug:** Regel `DuplicateCode` (`.agents/rules/AiNetLinter.mdc`).
  - **Vorschlag:** Zentralisierung in einem internen DB-Execution-Helper.
  - **Entscheidung:** übernommen ins Scope (→ siehe Muss-Haben).

- **Fehlende sealed-Modifikatoren**
  - **Gefunden:** [McpJsonContext.cs](src/SqlToAi/Mcp/McpJsonContext.cs#L11) und [FakeDbConnection.cs](tests/SqlToAi.Tests/TestSupport/FakeDbConnection.cs#L33).
  - **Bezug:** Regel `EnforceSealedClasses` (`.agents/rules/AiNetLinter.mdc`).
  - **Vorschlag:** Klassen als `sealed` deklarieren.
  - **Entscheidung:** übernommen ins Scope (→ siehe Muss-Haben).

- **Hohe Parameterzahl in Methoden & Konstruktoren**
  - **Gefunden:** [PerformanceMeasurementService.cs](src/SqlToAi/Database/PerformanceMeasurementService.cs#L201-L229) (8 Parameter) und [ToolDispatcher.cs](src/SqlToAi/Mcp/ToolDispatcher.cs#L51) (7 Konstruktor-Parameter).
  - **Bezug:** Regeln `MaxMethodParameterCount` und `MaxConstructorDependencies` (`.agents/rules/AiNetLinter.mdc`).
  - **Vorschlag:** Einführung von Parameter-Records / Parameter-Objects und Bündelung von Dispatcher-Services.
  - **Entscheidung:** übernommen ins Scope (→ siehe Muss-Haben).

- **Überbreite Testklassen**
  - **Gefunden:** `QueryExecutionServiceTests` (23 Public Members), `SchemaServiceTests` (19 Public Members), `SchemaServiceIntegrationTests` (17 Public Members), `ToolDispatcherTests` (16 Public Members).
  - **Bezug:** Regel `MaxPublicMembersPerType` (`.agents/rules/AiNetLinter.mdc`).
  - **Vorschlag:** Aufteilung in thematisch fokussierte Test-Dateien/Klassen (z. B. nach Feature/Szenario) oder Reduktion der Sichtbarkeit interner Helper.
  - **Entscheidung:** übernommen ins Scope (→ siehe Muss-Haben).

## Wie (grober Ansatz)

1. **Vorbereitung & Baseline-Entkopplung:** Testlauf auf reinen Clean-Check umstellen, Baseline-Datei entfernen.
2. **Core / Produktionscode Refactoring:**
   - Bereinigung der Linter-Errors (`sealed`).
   - DRY-Konsolidierung der SQL-Parsing-Helfer und Execution-Methoden.
   - Bündelung von Parametern via `record` in `PerformanceMeasurementService` und `ToolDispatcher`.
3. **Test-Refactoring:**
   - Konsolidierung redundanter Test-Helper.
   - Aufteilung überbreiter Testklassen in separate Test-Fixtures.
4. **Qualitäts- und Doku-Prüfung:**
   - Neutrale Sprachprüfung in Kommentaren und Dokumenten.
   - Verifikation über `AiNetLinter` (0 Fehler, 0 Warnungen) und vollständigen Testlauf (`dotnet test`).

## Definition of Done / Erfolgskriterien

- `dotnet build` läuft ohne Warnungen und ohne Fehler durch (`TreatWarningsAsErrors`).
- `dotnet test` läuft vollständig grün durch (alle Unit- und Integrationstests erfolgreich).
- `AiNetLinter` MCP-Tool `safeguard` liefert einen Quality-Score von **10,00/10** (0 Fehler, 0 Warnungen).
- Die Datei `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` ist gelöscht und wird im Build/Test nicht mehr referenziert.
- Kommentare und Dokumentation sind sachlich, neutral und auf Englisch verfasst.

## Offene Punkte

- Details zu den strukturierten Fragen aus Runde 1 klären (siehe Chat).