---
status: completed
type: step-plan
task: dry-refactor
step: step-005
corrects: null
title: "Test-Infrastruktur & Testklassen-Splits"
epic: EPIC-05
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: Gemini 3.7 Flash (High)
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-15T18:31:00+02:00
related_to: [step-004]
---

# Step 005: Test-Infrastruktur & Testklassen-Splits

## Bezug

- **Task:** `dry-refactor`
- **Epic:** `EPIC-05` aus `roadmap.md`
- **Konzept-Referenz:** [Konzept.md](tasks/dry-refactor/Konzept.md) Abschnitt „Scope > Muss-Haben"

## Aktueller Projektzustand (JIT-Kontext)

In der Testsuite existieren Duplikate bei Hilfsmethoden (`BuildTokenizationOptions`, `CreateWriter`, `CreateAnonymizer`). Einige Testklassen überschreiten das Limit von maximal 15 öffentlichen Methoden (`MaxPublicMembersPerType`): `QueryExecutionServiceTests`, `SchemaServiceTests`, `SchemaServiceIntegrationTests`, `ToolDispatcherTests`. `TableSchemaRendererTests` hat eine Methode mit 5 Parametern (`MaxMethodParameterCount`). `GlobMatcherTests` meldet `AvoidExcessiveMiddleMen`.

## Intention

Zentralisierung der Test-Helper unter `tests/SqlToAi.Tests/TestSupport/`, saubere Aufteilung überbreiter Testklassen in thematisch fokussierte Teilklassen (alle <= 15 Public Members) und Behebung der verbleibenden Methoden-/Heuristik-Warnungen in Tests.

## Konkrete Änderungen

### Datei 1: `tests/SqlToAi.Tests/TestSupport/AnonymizationTestHelper.cs` (neu)
- **Was:** Shared `BuildTokenizationOptions(bool enabled = true)`.
- **Warum:** Beseitigung von `DuplicateCode` zwischen `AnonymizerTests.cs` und `QueryExecutionServiceAnonymizationTests.cs`.

### Datei 2: `tests/SqlToAi.Tests/TestSupport/McpTrailTestHelper.cs` (neu)
- **Was:** Shared `CreateWriter` und `CreateAnonymizer`.
- **Warum:** Beseitigung von `DuplicateCode` zwischen `McpTrailWriterRedactionTests.cs` und `McpTrailWriterTests.cs`.

### Datei 3: `tests/SqlToAi.Tests/Database/TableSchemaRendererTests.cs`
- **Was:** Entfernen des ungenutzten Parameters `maxLength` aus `FormatTypeString_Decimal_IncludesPrecisionAndScale`.
- **Warum:** Einhaltung von `MaxMethodParameterCount <= 4`.

### Datei 4: `tests/SqlToAi.Tests/Domain/GlobMatcherTests.cs`
- **Was:** Explizite Arrange-Act-Assert Zuweisung in Testmethoden.
- **Warum:** Auflösung der `AvoidExcessiveMiddleMen`-Warnung.

### Datei 5: `tests/SqlToAi.Tests/Database/QueryExecutionServiceTests.cs`
- **Was:** Trennung der `partial class QueryExecutionServiceTests` in eigenständige Klassen `QueryExecutionServiceTests`, `QueryExecutionServiceAnonymizationTests`, `QueryExecutionServiceSchemaScopeTests`.
- **Warum:** Einhaltung von `MaxPublicMembersPerType <= 15`.

### Datei 6: `tests/SqlToAi.Tests/Database/SchemaServiceTests.cs`
- **Was:** Aufteilung in `SchemaServiceTests` und `SchemaServiceDetailsTests.cs` (neu).
- **Warum:** Einhaltung von `MaxPublicMembersPerType <= 15`.

### Datei 7: `tests/SqlToAi.Tests/Integration/SchemaServiceIntegrationTests.cs`
- **Was:** Aufteilung in `SchemaServiceIntegrationTests` und `SchemaServiceDetailsIntegrationTests.cs` (neu).
- **Warum:** Einhaltung von `MaxPublicMembersPerType <= 15`.

### Datei 8: `tests/SqlToAi.Tests/Mcp/ToolDispatcherTests.cs`
- **Was:** Aufteilung in `ToolDispatcherTests` und `ToolDispatcherExecutionTests.cs` (neu).
- **Warum:** Einhaltung von `MaxPublicMembersPerType <= 15`.

## Tests

- [ ] `dotnet build` läuft ohne Fehler/Warnungen.
- [ ] `dotnet test` läuft vollständig grün durch.
- [ ] AiNetLinter meldet alle Test-Warnungen als behoben.

## Definition of Done

- [ ] Alle Änderungen umgesetzt
- [ ] Build & Test grün
- [ ] Commit erfolgt
- [ ] `step-result.md` geschrieben
