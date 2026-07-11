# Implementation Plan: Folder, Namespace, & Test Infrastructure

This is Step 1 of the `SqlToAi` project. We will set up the domain-driven folder and namespace structure, define the core domain abstractions and error handling types (including the `Result<T>` pattern), implement the configuration bindings, and set up corresponding tests.

## Architecture & Namespaces

We will map namespaces 1:1 to directory names as required by the linter (`EnforceNamespaceDirectoryMapping`). To keep coupling low and maintain clean domain boundaries, we partition the server into the following subfolders/namespaces under `src/SqlToAi`:

1. **`SqlToAi.Domain`** (`src/SqlToAi/Domain`)
   * Core domain models, records, enums, and utility structures.
   * `Result<T>`: A generic union-like type for error-handling.
   * `SqlToAiError`: Standardized error representation mapping to the Error-Catalog (`SQL-AI-0001` to `SQL-AI-0109`).
   * `AccessLevel`: Enum for safety levels (`None = 0`, `SchemaOnly = 1`, `ReadOnly = 2`, `ReadWrite = 3`).

2. **`SqlToAi.Configuration`** (`src/SqlToAi/Configuration`)
   * Option classes to strongly type `appsettings.json` (`SqlDatabaseOptions`, `AnonymizerOptions`, `MetadataProviderOptions`).

3. **`SqlToAi.Database`** (`src/SqlToAi/Database`)
   * Interfaces and implementations for SQL Server database connectivity and Dapper query execution.

4. **`SqlToAi.Security`** (`src/SqlToAi/Security`)
   * Safety guardrails (static whitelist check, dynamic access-level probes, read-only guards).

5. **`SqlToAi.Anonymization`** (`src/SqlToAi/Anonymization`)
   * PII protection pipelines, scramblers, and consistency-hashing modules.

6. **`SqlToAi.Metadata`** (`src/SqlToAi/Metadata`)
   * Table/Column documentation enrichment.

7. **`SqlToAi.Mcp`** (`src/SqlToAi/Mcp`)
   * MCP json-rpc protocol message types, stdio transport host, and tool handler registrations.

---

## Proposed Changes

### [NEW] Core Domain Component

#### [NEW] [Result.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Domain/Result.cs)
* Implements a generic `Result<T>` class/struct to encapsulate success state or standard error catalog values.
* Matches linter rule: `sealed` class, nullable enabled, no silent catch, zero warnings.

#### [NEW] [SqlToAiError.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Domain/SqlToAiError.cs)
* Readonly struct or record (`EnforceValueObjectContracts` rule applies to `*ValueObject` but records/structs are best for all immutable objects).
* Represents a standardized error containing a code (e.g. `SQL-AI-0104`) and message.

#### [NEW] [AccessLevel.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Domain/AccessLevel.cs)
* Enum defining database permissions: `None = 0`, `SchemaOnly = 1`, `ReadOnly = 2` / `ReadData = 2`, `ReadWrite = 3`.

---

### [NEW] Configuration Options Component

#### [NEW] [SqlToAiOptions.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Configuration/SqlToAiOptions.cs)
* Root class mapping to `appsettings.json`.
* Contains `SqlDatabaseOptions`, `AnonymizerOptions`, and `MetadataProviderOptions`.

---

### [NEW] Folder Sentinels & Project Files

#### [NEW] Sentinels
* To avoid compiler warnings or linter errors, we will add basic placeholder classes/interfaces in each domain sub-directory to ensure namespaces map perfectly and build correctly.
* Specifically, directories `Database`, `Security`, `Anonymization`, `Metadata`, and `Mcp` will receive basic structural interfaces/classes.

---

### [NEW] Test Project Infrastructure

To satisfy `EnableTestSentinel`, each non-test class must have a corresponding test class (e.g., `ResultTests` covering `Result`).

#### [NEW] [ResultTests.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/tests/SqlToAi.Tests/Domain/ResultTests.cs)
* Unit tests for success and failure flows using `Result<T>`.

#### [NEW] [SqlToAiOptionsTests.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/tests/SqlToAi.Tests/Configuration/SqlToAiOptionsTests.cs)
* Unit tests verifying `appsettings.json` binding logic.

---

## Verification Plan

### Automated Tests
* Run compilation:
  ```powershell
  dotnet build
  ```
* Run all unit tests including the new domain tests and linter verification:
  ```powershell
  dotnet test
  ```

### Manual Verification
* Ensure `AiNetLinterTests` passes and synchronizes rules cleanly with zero warnings/errors in the generated linter output report.
