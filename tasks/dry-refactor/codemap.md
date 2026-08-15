---
task: dry-refactor
type: codemap
maintained_by: planer, coder, kritiker
last_updated: 2026-08-15T18:20:00+02:00
---

# CodeMap: dry-refactor

- **`src/SqlToAi/Mcp/McpJsonContext.cs`** — System.Text.Json Source Generator Context (fehlt `sealed`).
- **`src/SqlToAi/Mcp/ToolDispatcher.cs`** — Zentraler MCP Tool Dispatcher (7 Konstruktor-Abhängigkeiten).
- **`src/SqlToAi/Database/SqlCharScanner.cs`** — Shared SQL Scanner-Zustandsautomat.
- **`src/SqlToAi/Database/QueryDeconstructor.cs`** — SQL-Statement- und Dekonstruktionshelfer (Duplikate zu `SqlMultiStatementDetector`).
- **`src/SqlToAi/Database/SqlMultiStatementDetector.cs`** — Multi-Statement-Erkennung (Duplikate zu `QueryDeconstructor`).
- **`src/SqlToAi/Database/PerformanceMeasurementService.cs`** — Performance-Messdienst (`ExecuteSetOptionAsync`-Duplikat, Methoden mit >6 Parametern).
- **`src/SqlToAi/Database/QueryExecutionService.cs`** — Query-Ausführungsdienst (`ExecuteSetOptionAsync`-Duplikat).
- **`src/SqlToAi/Database/DatabaseCommandExecutor.cs`** — Shared DB-Kommandoausführung (SET-Optionen).
- **`src/SqlToAi/Database/IDatabaseAnalysisService.cs`** — Geplante Facade zur Bündelung der Analyse-Dienste.
- **`tests/SqlToAi.Tests/TestSupport/FakeDbConnection.cs`** — Fake-Db-Implementierung für Unit-Tests (fehlt `sealed`).
- **`tests/SqlToAi.Tests/AiNetLinter/AiNetLinterTests.cs`** — Linter-Integrationsprüfungen (Baseline-Test entfernen).
- **`tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json`** — Zu entfernende Baseline-Datei.
- **`tests/SqlToAi.Tests/Database/`** — Datenbank-bezogene Unit-Tests mit überbreiten Klassen (`QueryExecutionServiceTests`, `SchemaServiceTests`).
- **`tests/SqlToAi.Tests/Mcp/`** — MCP-bezogene Tests (`ToolDispatcherTests`, `McpTrailWriterTests`).
- **`tests/SqlToAi.Tests/Integration/`** — Integrationstests mit überbreiten Klassen (`SchemaServiceIntegrationTests`).
- **`.agents/rules/SqlToAiRichtlinien.mdc`** — Entwicklungs- und Sicherheitsrichtlinien (Baseline-Doku aktualisieren).
