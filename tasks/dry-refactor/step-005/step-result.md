# Step Result: step-005 (EPIC-05 — Test-Infrastruktur & Testklassen-Splits)

## Zusammenfassung der durchgeführten Arbeiten

In diesem Schritt wurden Test-Duplikate konsolidiert, Hilfsmethoden in wiederverwendbare Klassen überführt und überbreite Testklassen in thematisch fokussierte Teilklassen aufgeteilt:

1. **Gemeinsame Test-Helper erstellt:**
   - `AnonymizationTestHelper.cs` mit `BuildTokenizationOptions(bool enabled = true)`
   - `McpTrailTestHelper.cs` mit `CreateWriter` und `CreateAnonymizer`
   - `ToolDispatcherTestFakes.cs` mit `ToolDispatcherTestHelper`, `FakeSchemaService`, `FakeQueryExecutionService`, `FakeQueryValidationService`, etc.

2. **Test-Helper konsolidiert:**
   - `AnonymizerTests.cs` und `QueryExecutionServiceAnonymizationTests.cs` nutzen `AnonymizationTestHelper`.
   - `McpTrailWriterTests.cs` und `McpTrailWriterRedactionTests.cs` nutzen `McpTrailTestHelper`.

3. **Linter-Vorgaben in Tests korrigiert:**
   - `TableSchemaRendererTests.cs`: Redundanten Parameter `maxLength` in `FormatTypeString_Decimal_IncludesPrecisionAndScale` entfernt (`MaxMethodParameterCount <= 4`).
   - `GlobMatcherTests.cs`: Explizite AAA-Zuweisung `bool actual = GlobMatcher.IsMatch(...)` ergänzt (`AvoidExcessiveMiddleMen`).

4. **Überbreite Testklassen aufgeteilt (alle $\le 15$ public Member):**
   - `QueryExecutionServiceTests.cs` aufgeteilt in:
     - `QueryExecutionServiceTests.cs` (Validierung, Sicherheit, Multi-Statements)
     - `QueryExecutionServiceOptionsTests.cs` (Row-Limits, Timeouts, Server-Optionen)
     - `QueryExecutionServiceTransactionTests.cs` (Integrität & sp_executesql)
     - `QueryExecutionServiceAnonymizationTests.cs` (Anonymisierung & Tokenisierung)
   - `SchemaServiceTests.cs` aufgeteilt in:
     - `SchemaServiceTests.cs` (Databases, Search, Basic Schema)
     - `SchemaServiceDetailsTests.cs` (ForeignKeys, Indexes, Constraints, References, RoutineParameters)
   - `SchemaServiceIntegrationTests.cs` aufgeteilt in:
     - `SchemaServiceIntegrationTests.cs` (Databases, Search, Basic Schema)
     - `SchemaServiceDetailsIntegrationTests.cs` (Detailabfragen gegen Live-DB)
   - `ToolDispatcherTests.cs` aufgeteilt in:
     - `ToolDispatcherTests.cs` (Routing & Argument Parsing)
     - `ToolDispatcherExecutionTests.cs` (Execution Info & Anonymization Blocks)

## Verifikation
- `dotnet build`: 0 Fehler, 0 Warnungen.
- `dotnet test`: 486 Tests erfolgreich bestanden (0 Fehler).
- `ainetlinter`: Alle Testklassen-Warnungen eliminiert.
