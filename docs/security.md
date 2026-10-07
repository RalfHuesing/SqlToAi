# Security behavior and limits

SqlToAi applies application-level access checks and query guards. SQL Server permissions remain the security boundary. Use a dedicated login with only the database and object permissions required for the intended work.

## Database access

Access lists use exact, case-insensitive database names. Unlisted databases resolve to `None`. Global `SqlServer.ExcludedDatabases` glob patterns override allow lists. If a name occurs in several lists, the first configured level in this table wins:

| Level | Behavior |
| --- | --- |
| `SchemaOnly` | Schema discovery and query validation; data execution, comparison and performance measurement blocked |
| `ReadOnlyAnonymized` | Read queries with string masking when the anonymizer is enabled |
| `ReadOnly` | Read queries with raw results |
| `ReadWrite` | Query/script execution can persist changes |
| `None` | Access blocked; default for unlisted databases |

The shipped template explicitly puts `DemoDB` in `ReadWrite`. Database discovery returns permitted databases only. Index suggestions also accept `SchemaOnly`, since they read metadata.

## Query guards and transactions

`QuerySafetyValidator` checks arguments, rejects `USE` switches, verifies database access, applies access-level restrictions, invokes `ReadOnlyGuard` and enforces the single-statement boundary. The guard uses ScriptDom's `TSql150Parser`, AST fragments and token checks. It rejects mutating statements including DML, DDL, `EXEC` and `SELECT ... INTO`; it is not a regular-expression-only filter.

`sql_execute_query` rolls back in read-only modes and commits in `ReadWrite`. Single-query calls support leading `DECLARE` statements followed by one main query; multiple main statements fail with `SQL-AI-0101`. `ReadWrite` bypasses mutation checks but retains `USE` and single-query restrictions.

`sql_execute_file` supports multiple statements and `GO` batches. Protected read-only modes always roll back. In `ReadWrite`, `use_transaction=true` selects an atomic transaction; `false` permits per-batch autocommit, so earlier changes can remain after a later failure. Explicit transaction-control statements are restricted by the script transaction-integrity guard.

Validation uses SQL Server `NOEXEC` and rollback. Comparison and performance services also roll back. Rollback does not prevent expensive queries, locks, information disclosure or every external side effect; it does not replace SQL permissions.

## String masking and tokenization

Query and script result masking applies to `ReadOnlyAnonymized` with `Anonymizer.Enabled=true`, except excluded columns. Disabling this switch exposes raw values even at that access level. Empty strings and non-string values remain unchanged.

| Mode | Transformation | Limits |
| --- | --- | --- |
| `ScramblePattern` | Deterministic pseudo-random replacement of ASCII uppercase/lowercase letters and digits; preserves length and other characters | Reveals patterns; non-ASCII letters and punctuation remain; distinct inputs can collide |
| `Hash` | Unsalted SHA-256 as lowercase hexadecimal | Reveals equality; predictable inputs can be guessed by hashing candidates |
| Tokenization | Process-local mapping, e.g. `§§§T1§§§` | Reversible in server memory; reveals equality; mappings disappear after restart |

`Tokenization.Enabled=true` with non-empty delimiters replaces masking for protected strings. The query-execution resolver substitutes known complete tokens inside SQL string literals, escaping quotes. It leaves comments, identifiers and unknown tokens unchanged. Tokens can be reused with `=`, `IN` or `LIKE`; token fragments do not preserve the original value relationship. Unknown tokens are ordinary literal text and can match real stored text. Do not assume substitution for parameter values or every analysis tool.

`sql_get_schema` labels columns `No`, `Yes` or `Yes (searchable)` as guidance. Query results include a notice identifying masked columns where their source is known. Labels and notices do not establish that all outputs are sanitized.

## Central anonymization rules

The [rule table script](../sql-scripts/03_anonymization_rules.sql) defines database, table and column patterns, `Anonymize`, `IsActive` and `Comment`. An optional `SchemaPattern` extends matching to schemas; older tables without it use `%`. Patterns use SQL-LIKE-style `%` and `_`. Only active rules participate. `Anonymize=0` exposes a matching column; `1` keeps it protected. Without a match, strings remain protected. Missing or unavailable rule tables produce an empty rule set, retaining default masking.

Specificity ranks exact text above partial wildcards above `%` in each of the four dimensions. A rule dominates another only if it is at least as specific in every dimension and more specific in one. Among remaining incomparable rules, protective rules (`Anonymize=1`) win over permissive rules; a weighted score breaks remaining ties. Database specificity alone therefore cannot override a conflicting exact-column protection rule. For example, `(%, %, FakeConsultants, %, 0)` opens a table while `(%, %, FakeConsultants, FullName, 1)` keeps that column protected. Rules are cached for `AnonymizationRules.CacheTtlSeconds` using the connection in [configuration.md](configuration.md).

## Boundaries and data exposure

- Numeric identifiers, dates, binary values and other non-string columns are not masked.
- Schema/DDL text is raw. Comparison returns raw difference samples; benchmark results embed them. Plan warnings, SQL literals, errors, logs and metadata can contain sensitive information.
- Deterministic masks and tokens reveal repeated values. Cleartext exposed elsewhere can be correlated with protected values. These mechanisms do not guarantee anonymity or PII removal.
- Access lists check the requested database. They do not sandbox every cross-database reference, synonym, linked server or SQL Server object. Restrict the login accordingly.
- The primary connection factory sets `TrustServerCertificate=true`, disabling certificate validation through that setting.
- Script execution reads local files available to the server process; URLs and UNC paths are rejected, but access is not restricted to one script directory.
- SQL text, parameters and results can reach the client's model provider and local logs. Review both before using sensitive data.

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
