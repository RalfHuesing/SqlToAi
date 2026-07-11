# Implementation Plan: Metadata Enrichment (Step 5)

This plan details the implementation of the Schema Metadata Enrichment subsystem, which fetches documentation for tables and columns to enrich the schema markdown sent to the AI client.

## Proposed Changes

### [MODIFY] Metadata Domain Interfaces

#### [MODIFY] [IMetadataProvider.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Metadata/IMetadataProvider.cs)
* Update interface methods to accept the target `databaseName`.
* Add `GetColumnDescriptionsAsync` to fetch descriptions for all columns of a table:
```csharp
namespace SqlToAi.Metadata;

public interface IMetadataProvider
{
    Task<string?> GetTableDescriptionAsync(string databaseName, string tableName, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetColumnDescriptionsAsync(string databaseName, string tableName, CancellationToken cancellationToken = default);
}
```

---

### [NEW] Metadata Domain Components

#### [NEW] [MetadataProvider.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Metadata/MetadataProvider.cs)
* Implements `IMetadataProvider`.
* Reads configuration options from `MetadataProviderOptions`.
* If disabled (`Enabled == false`), returns empty results.
* Checks if custom SQL queries are configured in `TableMetadataQuery` and `ColumnMetadataQuery`:
  - **Custom Queries Flow:** Runs the configured custom SQL (passing `@TableName` parameter) using Dapper.
  - **Native Extended Properties Flow (Default):** Runs optimized SQL queries against `sys.extended_properties` for the target database:
    - Table description query using `sys.extended_properties` matching `class = 1` and `minor_id = 0`.
    - Column descriptions query matching columns with `MS_Description`.
* Uses `IDatabaseConnectionFactory` to open connections. If a separate `ConnectionString` is configured in `MetadataProviderOptions`, opens a connection to that connection string; otherwise, connects to the target database.

---

### [NEW] Test Project Components

#### [NEW] [MetadataProviderTests.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/tests/SqlToAi.Tests/Metadata/MetadataProviderTests.cs)
* Tests that the provider correctly returns empty results when disabled.
* Tests the native `MS_Description` query generation flow.
* Tests custom query metadata retrieval using mocked database connections.
* Satisfies test-sentinel for `MetadataProvider`.

---

## Verification Plan

### Automated Tests
* Run compilation:
  ```powershell
  dotnet build
  ```
* Run all unit tests:
  ```powershell
  dotnet test
  ```

### Manual Verification
* Run the `AiNetLinter` check to verify zero warning/error compliance.
