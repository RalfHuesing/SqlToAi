# Implementation Plan: Database Connection & Security Guardrails (Step 2)

This plan details the implementation of database access, static whitelisting, and dynamic access level checks (including caching with TTL).

## Proposed Changes

### [NEW] Security Domain Components

#### [NEW] [SecurityGuard.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Security/SecurityGuard.cs)
* Implements `ISecurityGuard`.
* Loads whitelist (`Allowed`) and blacklist (`Blocked`) glob patterns from `DatabasesOptions`.
* Converts wildcard patterns (e.g., `Demo_*`, `*`) into regexes.
* Provides `IsDatabaseAllowed(string databaseName)` to statically verify if a database target is permitted.

#### [NEW] [AccessCheckResult.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Domain/AccessCheckResult.cs)
* Cache entry model holding `AccessLevel` and the `ExpirationTime` (based on `CacheTtlSeconds`).

#### [NEW] [AccessLevelProvider.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Security/AccessLevelProvider.cs)
* Retrieves and caches the dynamic `AccessLevel` for a specific database.
* Performs the SQL query defined in `AccessCheckSql` (using Dapper/SqlConnection).
* Parses string/int outputs to map them to the `AccessLevel` enum:
  - If dynamic check fails (SQL error, timeout, or missing column/results), it returns `AccessLevel.None` (`SQL-AI-0104`).
* Caches results using the `CacheTtlSeconds` property. If cache expires or is missing, queries the database again.

---

### [NEW] Database Domain Components

#### [NEW] [SqlConnectionFactory.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Database/SqlConnectionFactory.cs)
* Implements `IDatabaseConnectionFactory`.
* Retrieves the connection string from environment variable `SQLTOAI_CONNECTION_STRING` or falls back to `SqlDatabaseOptions`.
* Builds connection strings using `SqlConnectionStringBuilder`.
* If a specific `databaseName` is requested, dynamically swaps the `Initial Catalog`.
* Automatically appends `ApplicationIntent=ReadOnly` if the database is configured as read-only.

---

### [NEW] Test Project Components

#### [NEW] [SecurityGuardTests.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/tests/SqlToAi.Tests/Security/SecurityGuardTests.cs)
* Tests static whitelist and blacklist checks including wildcards (`*`), exact matches, and edge cases.
* Satisfies test-sentinel for `SecurityGuard`.

#### [NEW] [SqlConnectionFactoryTests.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/tests/SqlToAi.Tests/Database/SqlConnectionFactoryTests.cs)
* Tests connection string building, environment variable overrides, database switching, and ReadOnly intent injection.
* Satisfies test-sentinel for `SqlConnectionFactory`.

#### [NEW] [AccessLevelProviderTests.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/tests/SqlToAi.Tests/Security/AccessLevelProviderTests.cs)
* Tests dynamic access level queries (mocking or using local db if needed; we can mock connection/Dapper or run on localdb if available).
* Tests TTL caching behavior.
* Satisfies test-sentinel for `AccessLevelProvider`.

---

## Verification Plan

### Automated Tests
* Run compilation:
  ```powershell
  dotnet build
  ```
* Run all unit tests including the new security, database, and caching tests:
  ```powershell
  dotnet test
  ```

### Manual Verification
* Run the `AiNetLinter` check to verify zero warning/error conformance.
