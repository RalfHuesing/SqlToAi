# Implementation Plan: Read-Only Guard & String Anonymization (Step 3)

This plan details the implementation of the SQL safety checks (Read-Only Guard with comment stripping, regex verification, and transaction rollback) and PII string anonymization.

## Proposed Changes

### [NEW] Security Domain Components

#### [NEW] [IReadOnlyGuard.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Security/IReadOnlyGuard.cs)
* Declares the contract to verify if a query string is read-only safe.

#### [NEW] [ReadOnlyGuard.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Security/ReadOnlyGuard.cs)
* Implements `IReadOnlyGuard`.
* Strips SQL comments (both single-line `--` and multi-line `/* ... */`) before scanning.
* Validates queries against a regex containing mutating SQL commands: `INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `TRUNCATE`, `MERGE`, `EXEC`, `EXECUTE`, `CREATE`, etc.
* Provides `IsQuerySafe(string query)` returning true if no mutating commands are detected.

---

### [NEW] Anonymization Domain Components

#### [NEW] [Anonymizer.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Anonymization/Anonymizer.cs)
* Implements `IAnonymizer`.
* Matches column names against configured `ExcludedColumns` and `Rules`.
* Reuses regex wildcard matching to check glob patterns (e.g. `*name*`, `*Id`).
* Implements:
  * `ScramblePattern`: Replaces uppercase letters with `'X'`, lowercase letters with `'x'`, and digits with `'9'`. Preserves all other symbols (e.g. email formats like `@` and `.`).
  * `Hash`: Calculates the SHA-256 hash of the string, converting it to lowercase hex. Preserves logical consistency for joins.

---

### [NEW] Test Project Components

#### [NEW] [ReadOnlyGuardTests.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/tests/SqlToAi.Tests/Security/ReadOnlyGuardTests.cs)
* Tests query parsing.
* Verifies comment stripping (e.g., ignoring comments containing write keywords).
* Verifies blocking of mutating statements (`INSERT`, `UPDATE`, etc.) while allowing safe `SELECT` statements.
* Satisfies test-sentinel for `ReadOnlyGuard`.

#### [NEW] [AnonymizerTests.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/tests/SqlToAi.Tests/Anonymization/AnonymizerTests.cs)
* Tests pattern scramble matching: `Max.Mustermann@mail.de` -> `Xxx.Xxxxxxxxxx@xxxx.xx`.
* Tests SHA-256 consistency hashing.
* Tests column whitelisting/blacklisting.
* Satisfies test-sentinel for `Anonymizer`.

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
* Run `AiNetLinter` check to verify zero warning/error compliance.
