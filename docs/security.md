# Security behavior and limits

SqlToAi applies application-level access checks and query guards. SQL Server permissions remain the security boundary. Use a dedicated login with only the database and object permissions required for the intended work.

## Reporting security issues

See the root [security policy](../SECURITY.md) for the current reporting route and
its availability. Do not include credentials, connection strings, customer data,
database names, raw logs, or unredacted query/result content in public reports.

## Database access

Access lists use exact, case-insensitive database names. Unlisted databases resolve to `None`. Global `SqlServer.ExcludedDatabases` glob patterns override allow lists. If a name occurs in several lists, the first configured level in this table wins:

| Level | Behavior |
| --- | --- |
| `SchemaOnly` | Schema discovery and query validation; data execution, comparison and performance measurement blocked |
| `ReadOnlyAnonymized` | Read queries with string masking when the anonymizer is enabled |
| `ReadOnly` | Read queries with raw results |
| `ReadWrite` | Query/script execution can persist changes |
| `None` | Access blocked; default for unlisted databases |

The shipped main template has empty access lists, so every database, including `DemoDB`, is denied until an administrator configures a level deliberately. The separate fictional DemoDB example explicitly grants `ReadWrite` for a local test database and is never loaded by the host automatically. Database discovery returns permitted databases only. Index suggestions also accept `SchemaOnly`, since they read metadata.

## Query guards and transactions

`QuerySafetyValidator` checks arguments, rejects `USE` switches, verifies database access, applies access-level restrictions, invokes `ReadOnlyGuard` and enforces the single-statement boundary. The guard uses ScriptDom's `TSql150Parser`, AST fragments and token checks. It rejects mutating statements including DML, DDL, `EXEC` and `SELECT ... INTO`; it is not a regular-expression-only filter.

`sql_execute_query` rolls back in read-only modes and commits in `ReadWrite`. Single-query calls support leading `DECLARE` statements followed by one main query; multiple main statements fail with `SQL-AI-0101`. `ReadWrite` bypasses mutation checks but retains `USE` and single-query restrictions.

`sql_execute_file` supports multiple statements and `GO` batches. Protected read-only modes always roll back. In `ReadWrite`, `use_transaction=true` selects one server-managed transaction; `false` permits per-batch autocommit, so earlier changes can remain after a later failure. Before any batch runs in a server-managed transaction, AST preflight rejects explicit `BEGIN`, `COMMIT`, `ROLLBACK`, `SAVE TRANSACTION` and `SET IMPLICIT_TRANSACTIONS`, including nested statements. Comments and string literals do not trigger rejection. Unparseable batches fail before execution. ReadWrite autocommit mode still permits caller transaction control.

Atomicity requires all executed code to preserve the server-managed transaction. Procedures, dynamic SQL and triggers can contain transaction control that caller-text analysis cannot inspect; these remain supported in ReadWrite, with no atomicity guarantee for such code. The post-execution transaction-count check detects some state changes but cannot undo an earlier commit or guarantee detection when the count is restored. SQL Server permissions remain essential.

Validation uses SQL Server `NOEXEC` and rollback. A separate AST preflight rejects caller `SET NOEXEC`, `SET PARSEONLY` and `SET FMTONLY` (ON or OFF), including nested statements, before opening its validation connection. Unparseable input fails with `SQL-AI-0102`; prohibited controls fail with `SQL-AI-0104`. Ordinary SELECT and access-permitted DML validation remain supported. Comparison and performance services also roll back. Rollback does not prevent expensive queries, locks, information disclosure or every external side effect; it does not replace SQL permissions.

Query and script execution, validation, comparison, performance measurement, and transaction-integrity rejection attempt cleanup with cancellation independent of the caller. Reset commands have a command timeout; rollback cancellation depends on provider support and does not guarantee a wall-clock limit. Cleanup failures are logged without replacing an existing operation error or caller cancellation. A cleanup failure after otherwise successful work remains a failure. See [cleanup timeout behavior](configuration.md#settings-reference).

## String masking and tokenization

Query and script result masking applies to `ReadOnlyAnonymized` with `Anonymizer.Enabled=true`, except columns excluded by a matching central rule. Exclusion matching uses the reader's resolved base schema, table and column; output aliases do not change the source-column decision. If any part of that origin is unavailable, the result stays masked by default. Disabling this switch exposes raw values even at that access level. Empty strings and non-string values remain unchanged.

| Mode | Transformation | Limits |
| --- | --- | --- |
| `ScramblePattern` | Deterministic pseudo-random replacement of ASCII uppercase/lowercase letters and digits; preserves length and other characters | Reveals patterns; non-ASCII letters and punctuation remain; distinct inputs can collide |
| `Hash` | Unsalted SHA-256 as lowercase hexadecimal | Reveals equality; predictable inputs can be guessed by hashing candidates |
| Tokenization | Process-local mapping, e.g. `§§§T1§§§` | Reversible in server memory; reveals equality; mappings disappear after restart |

`Tokenization.Enabled=true` with non-empty delimiters replaces masking for protected strings. It follows the same source-origin exclusion decision, including default masking when the origin is incomplete. The query-execution resolver substitutes known complete tokens inside SQL string literals, escaping quotes. It leaves comments, identifiers and unknown tokens unchanged. Tokens can be reused with `=`, `IN` or `LIKE`; token fragments do not preserve the original value relationship. Unknown tokens are ordinary literal text and can match real stored text. Do not assume substitution for parameter values or every analysis tool.

`sql_get_schema` labels columns `No`, `Yes` or `Yes (searchable)` as guidance. Query results include a notice identifying masked columns where their source is known. Labels and notices do not establish that all outputs are sanitized.

Direct catalog queries through `sql_execute_query` or `sql_execute_file` use the same result-masking rules as other query results; returned catalog strings may be altered. Schema and trigger definition tools return raw schema/DDL text, as described below.

## Central anonymization rules

The [rule table script](../sql-scripts/03_anonymization_rules.sql) defines database, table and column patterns, `Anonymize`, `IsActive` and `Comment`. An optional `SchemaPattern` extends matching to schemas; older tables without it use `%`. Patterns use SQL-LIKE-style `%` and `_`. Only active rules participate. `Anonymize=0` exposes a matching column; `1` keeps it protected. Without a match, strings remain protected. Missing or unavailable rule tables produce an empty rule set, retaining default masking.

Specificity ranks exact text above partial wildcards above `%` in each of the four dimensions. A rule dominates another only if it is at least as specific in every dimension and more specific in one. Among remaining incomparable rules, protective rules (`Anonymize=1`) win over permissive rules; a weighted score breaks remaining ties. Database specificity alone therefore cannot override a conflicting exact-column protection rule. For example, `(%, %, FakeConsultants, %, 0)` opens a table while `(%, %, FakeConsultants, FullName, 1)` keeps that column protected. Rules are cached for `AnonymizationRules.CacheTtlSeconds` using the connection in [configuration.md](configuration.md). A failed load is cached as an empty rule set for the same TTL; it keeps default masking active. Requested cancellation propagates without caching a fallback rule set, so the next request can load the rules again. Metadata and rule-provider connection/TLS failures are logged with the affected configuration section and a safe failure category, without exception payloads, credentials or raw connection strings. Missing metadata stays missing, and a dedicated endpoint failure does not retry through the primary server.

## Primary connection failures

Primary TLS/certificate validation and handshake failures return `SQL-AI-0105` with a safe `SqlServer` diagnostic. This also applies to schema discovery/export and index suggestions. Database discovery returns the connection error for TLS failures and connection timeouts instead of substituting the configured database list. Actual timeouts retain `SQL-AI-0106`; a timeout during the pre-login handshake is still a timeout.

The primary connection-open boundary discards raw provider messages and inner exceptions before service loggers receive the failure. Responses and connection-failure exception payloads therefore contain no credentials or full connection strings taken from those exceptions. Diagnostics direct the administrator to certificate trust, validity, name and transport configuration. There is no retry with encryption or certificate validation disabled. This protection is scoped to connection-open failures; ordinary SQL execution errors, query text and other logs retain the exposure limits below.

## Boundaries and data exposure

- `sql_export_schema` deliberately persists raw schema/DDL documentation on the server filesystem, potentially in many large files. Database policy applies, including `SchemaOnly`; the process also needs filesystem write permission. Fully qualified Windows drive/UNC paths are accepted without an export-root sandbox. The caller needs access to the output filesystem. Paths must be absent/empty and writes use exclusive creation. Failed/cancelled output is retained, including incomplete files; use a new or manually emptied destination unless only an empty root remains. No data export, automatic cleanup or additional sanitization is performed. See [export parameters and outcomes](tools.md#schema-export).
- Numeric identifiers, dates, binary values and other non-string columns are not masked.
- Schema/DDL text is raw. Comparison returns raw difference samples; benchmark results embed them. Plan warnings, SQL literals, errors, logs and metadata can contain sensitive information.
- Deterministic masks and tokens reveal repeated values. Cleartext exposed elsewhere can be correlated with protected values. These mechanisms do not guarantee anonymity or PII removal.
- Access lists check the requested database. They do not sandbox every cross-database reference, synonym, linked server or SQL Server object. Restrict the login accordingly.
- SQL connections require encryption and validate the server certificate by default, including primary and dedicated metadata/rule endpoints. `SqlServer.TrustServerCertificate`, `MetadataProvider.TrustServerCertificate`, and `AnonymizationRules.TrustServerCertificate` default to `false`. Setting one to `true` accepts the presented certificate without validating its identity for that endpoint while keeping encryption required; this is intended only as a deliberate local/self-signed development exception. A primary exception does not apply to dedicated secondary servers. When a secondary server is omitted, its connection uses the primary factory and inherits the primary endpoint's effective policy. Existing settings are not automatically migrated or rewritten, so settings that omit these options now validate certificates. The shipped main and demo profiles keep the exception off.
- General local SQL integration tests explicitly load a separate test-only trust overlay for the existing self-signed instance. That fixture exception is not used by application configuration or the isolated TLS default-policy cases, and passing with it does not verify certificate identity validation.
- Script execution reads local files available to the server process; URLs and UNC paths are rejected, but access is not restricted to one script directory.
- SQL text, parameters and results can reach the client's model provider and local logs. Review both before using sensitive data.
- Application and error diagnostics remain enabled.
- Default diagnostic files reside under `%LOCALAPPDATA%\SqlToAi\log\`; explicit log-path overrides remain supported. A user-owned destination does not add encryption, redaction or access isolation beyond Windows permissions. Treat log files and startup stderr as potentially sensitive. See [logging paths and controls](configuration.md#logs).

## SQL Server permissions

Grant only capabilities needed. For broad read access, these are examples rather than a requirement to grant everything:

```sql
USE [TargetDatabase];
ALTER ROLE [db_datareader] ADD MEMBER [SqlToAiUser];
GRANT VIEW DEFINITION TO [SqlToAiUser];
GRANT SHOWPLAN TO [SqlToAiUser];
```

Object-specific `SELECT` grants can be narrower than `db_datareader`. `VIEW DEFINITION` makes module/trigger definitions visible. `SHOWPLAN` enables XML plan analysis; without it, performance tools can return IO/TIME metrics with a permission note.

Missing-index DMVs require `VIEW SERVER STATE` on earlier SQL Server versions and `VIEW SERVER PERFORMANCE STATE` on SQL Server 2022 or later; Azure SQL requirements differ. The current fallback message recommends `VIEW SERVER STATE`, which can be insufficient on newer instances. Select the grant for the deployed instance using [Microsoft's DMV permission reference](https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-objects/sys-dm-db-missing-index-details-transact-sql?view=sql-server-ver17). Suggestions are filtered to the requested database; statistics accumulate since restart and do not prove that an index should be created.
