---
status: draft
---

# SQL Server Environment Context and Estimated Query-Plan Diagnosis

## Intention

Provide agents with two safe, bounded diagnostic capabilities that never execute workload queries or return table rows, and that work under `SchemaOnly` database access:

1. **Environment context (`sql_get_environment`)**: Retrieve SQL Server version, edition, engine edition, database compatibility level, collation, state, and read-only status in a single compact call, so agents can select compatible T-SQL syntax and interpret diagnostics without ad hoc catalog querying.
2. **Estimated query plan (`sql_explain_query`)**: Inspect how SQL Server's optimizer plans to execute a read-only `SELECT` query without executing it, returning a compact operator hierarchy, estimated rows, subtree costs, and allowlisted compiler warnings rather than an XML dump or a tuning verdict.

## Settled direction and decisions

The user approved combining both draft diagnostic concepts (`database-environment` and `estimated-query-plan`) into one task and roadmap for orchestrated end-to-end implementation. Both capabilities share:
- Qualified `alias/database` target resolution.
- Eligibility under `SchemaOnly` access (`None` and excluded targets remain denied before any connection).
- Strict information boundary: allowlisted metadata and diagnostics only; no table rows, connection strings, hostnames, credentials, or raw query text.
- Source-generated JSON serialization through the existing `JsonSerializerContext` classes.

Decisions confirmed by the user:
- **Connection hygiene**: `sql_explain_query` uses its own non-pooled connection (`Pooling=false`), so an uncertain `SHOWPLAN_XML` state can never reach a pooled connection. `SqlConnection.ClearPool` is not used (it clears the whole pool for the connection string and cannot be verified with the test fakes).
- **Error code for rejected controls**: Caller execution/plan/transaction controls are rejected with `SQL-AI-0104`, consistent with the existing `SqlExecutionControlGuard` and `docs/tools.md`. `SQL-AI-0107` remains for mutating statements.
- **Access levels for Explain**: Every level except `None` and excluded targets is allowed (including the anonymized levels and `SchemaOnly`). Estimates are derived from table statistics, which is not anonymization and does not resolve tokens; this is documented explicitly instead of restricting the levels.
- **Limits are options**: The Explain limits live in an options class bound through `IOptions<T>` with defaults in `appsettings.json`; command timeouts come from `QueryExecution.CommandTimeoutSeconds`.

---

## Scope

### Must

#### 1. Environment Context (`sql_get_environment`)
1. **Tool contract**: Add `sql_get_environment` with one required argument `database`, resolved once as a qualified `alias/database` target (`SQL-AI-0001` for malformed, unqualified or unknown targets; `SQL-AI-0104` for denied or excluded targets, both before any connection).
2. **Access & safety**: Require `SchemaOnly` minimum access. Use the selected endpoint's `SqlClient` factory, TLS and safe connection-error conventions, and `QueryExecution.CommandTimeoutSeconds` as command timeout.
3. **Metadata query**: Execute one fixed read-only query against `sys.databases` (filtered by the connected database via `DB_ID()`) and `SERVERPROPERTY`. It has no caller input.
4. **Allowlisted fields**:
   - `target`: Canonical qualified `alias/database` target name.
   - `product_version`: SQL Server version string (e.g. `"16.0.4180.1"`).
   - `product_major_version`: Numeric major version (e.g. `16`).
   - `edition`: SQL Server edition string (e.g. `"Developer Edition (64-bit)"`).
   - `engine_edition`: Numeric engine edition (e.g. `3` for Enterprise/Developer, `5` for Azure SQL Database).
   - `compatibility_level`: Target database compatibility level (e.g. `160`).
   - `collation`: Database collation name (e.g. `"Latin1_General_CI_AS"`).
   - `state_desc`: Database state (e.g. `"ONLINE"`). A database that is not online typically fails at connection time; the field is informational.
   - `is_read_only`: Whether the SQL Server database itself is read-only.
   - `unavailable_fields`: Names of allowlisted fields the server did not provide (for example `product_major_version` on SQL Server versions that return `NULL` for `SERVERPROPERTY('ProductMajorVersion')`). Because the JSON contexts omit null values, this list is the only way to tell "not provided" from "not part of the result"; unavailable values are never rendered as `0` or `false`.
5. **Failure handling**: Connection, timeout and permission errors return the matching catalog failure (`SQL-AI-0105`, `SQL-AI-0106`, `SQL-AI-0104`), never an all-null object. A database that is unreachable for authentication reasons and one that is offline may share the generic connection failure.
6. **No data leakage**: No arbitrary SQL, login/user inventories, host/machine names, connection strings, or credentials.
7. **Documentation note**: `docs/tools.md` states that `compatibility_level` and `engine_edition` indicate server capabilities, but that SQL-AI parses submitted SQL with the T-SQL 150 grammar, so newer syntax can be rejected with `SQL-AI-0102` regardless of the reported level.

#### 2. Estimated Query Plan (`sql_explain_query`)
1. **Tool contract**: Add `sql_explain_query` with required arguments `database` (qualified `alias/database`) and `query` (string), and optional typed `parameters` bound like in the other query tools. Target resolution and errors as for the environment tool.
2. **Access & statement allowlist**:
   - Require `SchemaOnly` minimum access (`None` and excluded targets denied with `SQL-AI-0104` before any connection).
   - The accepted statement shape is closed: optional leading `DECLARE` statements followed by exactly one `SELECT` statement without `INTO`. Nothing may follow the `SELECT`.
   - The existing read-only guard runs at **every** access level, including `ReadWrite` and `ReadWriteAnonymized`, in addition to the shape check (the standard validator skips it for write-capable levels). Mutating statements (`INSERT`, `UPDATE`, `DELETE`, `MERGE`, `SELECT INTO`, `EXEC`, DDL) fail with `SQL-AI-0107`.
   - Caller execution, plan, session or transaction controls, anywhere in the batch and also after the `SELECT` (`SET` statements of any kind including `SHOWPLAN_*`, `NOEXEC`, `PARSEONLY`, `FMTONLY`, `STATISTICS`; `BEGIN`/`COMMIT`/`ROLLBACK TRAN`), fail with `SQL-AI-0104`; `USE` is rejected with `SQL-AI-0104` as in the existing validator.
   - Multiple main statements or batches fail with `SQL-AI-0101`. Unparseable SQL fails closed with `SQL-AI-0102`. All rejections happen before any database connection.
3. **Execution mechanism**:
   - Open a dedicated **non-pooled** connection for each call (the connection factory gains a way to request this; no change for other tools). Execute `SET SHOWPLAN_XML ON`, the parameterized query, and `SET SHOWPLAN_XML OFF` as distinct batches. The server compiles the query and returns the plan; it does not run it.
   - No transaction is opened: the statement allowlist plus the positive result check below are the safety layers, and `SHOWPLAN_XML ON` does not execute statements.
   - After `SET SHOWPLAN_XML ON`, the query batch must return exactly one single-column, single-row showplan XML result. Any other result (for example data rows) aborts immediately, discards the connection and fails with `SQL-AI-0102` and a generic message.
   - Never route through `PerformanceMeasurementService` and never fall back to normal query execution on failure, timeout, or cancellation.
   - Cleanup attempts `SET SHOWPLAN_XML OFF` as best effort with its own cancellation token (independent of the request token) and always disposes the connection. Because the connection is not pooled, a failed reset cannot affect later calls.
   - Missing `SHOWPLAN` or object `SELECT` permission returns `SQL-AI-0104` with a fixed message naming only the missing permission class (`SHOWPLAN` or `SELECT`), without echoing the raw SQL Server message and its object names. Timeouts map to `SQL-AI-0106`, connection failures to `SQL-AI-0105`.
   - `DECLARE` assignments are not executed during compilation, so variables are `NULL` while the plan is built. Estimates, especially with `OPTION (RECOMPILE)`, can differ from runtime; `docs/tools.md` states this.
4. **Showplan XML parsing & bounds**:
   - The XML is read with a bounded reader that stops once `MaxShowplanXmlBytes` (default 2 MiB) is exceeded. Exceeding the limit fails with `SQL-AI-0102` and a size-limit message. The query was never executed in any case.
   - Secure XML parsing: `DtdProcessing.Prohibit`, `XmlResolver = null`.
   - Result shape (field names in `snake_case`):
     - `target`, `plan_kind` (`"estimated"`), `truncated` (bool), `nodes`, `warnings`.
     - Each node: `node_id`, `parent_id` (`null` for the root), `physical_op`, `logical_op`, `estimated_rows`, `subtree_cost`, optional `object` (schema, table, index name for seeks/scans of a single object), and `omitted_children` (count, only when children were cut off).
   - Only the plan of the `SELECT` statement is returned. Plans of other statements in the batch are ignored, and `node_id` is the showplan `NodeId` of that statement, which is unique within it.
   - Allowlisted warnings: missing indexes (table identity, key/include column names, estimated impact; no generated `CREATE INDEX` text) and plan-affecting conversions (conversion kind and column identity, no expression text). Scans are not warnings: they are visible through `physical_op` and `object` on nodes. Warnings are capped at a fixed maximum; `truncated` also covers dropped warnings.
   - Output bounds (options, defaults): 100 operators (`MaxOperators`), depth 16 (`MaxDepth`), 64 KiB serialized output (`MaxResponseBytes`). Nodes are kept in breadth-first order; cut-off nodes keep their visible parent and report `omitted_children`. If the serialized result still exceeds the byte limit after the operator and depth limits, further nodes are dropped in reverse breadth-first order until it fits, and `truncated` is set.
5. **Strict plan data protection**:
   - Do NOT return raw XML, `StatementText`, `ParameterCompiledValue`/parameter values, `ScalarString`, or arbitrary expression text.
   - Object identities are limited to names needed for diagnosis (nodes and warnings). The plan can name objects in other databases that the query references; the SQL Server login stays the boundary for cross-database references, as documented in `docs/security.md`.
   - `docs/security.md` states that estimates derive from table statistics; this is not anonymization, and tokens in literals are not resolved.

#### 3. Shared Integration & Infrastructure
1. Add the tool names to `McpConstants`, the tool definitions and schemas to `ToolRegistry` (the CLI commands are derived from it), the dispatch to `ToolDispatcher`, the MCP registration to `SqlMcpToolRegistrations`, and the services to dependency injection in `Program.cs`.
2. Register result DTOs in the existing source-generated JSON contexts (`McpJsonContext` / `McpAnalysisJsonContext`).
3. Add the `ExplainQuery` options class, its defaults in `src/SqlToAi/appsettings.json`, and document it in `docs/configuration.md`.
4. Update the tool count from 20 to 22 everywhere it is asserted: tests (`ToolRegistryTests`, `McpModelsTests`, `ComposedTargetIsolationTests`, `ComposedTargetBindingTests`, `ManagedMcpTargetBindingTests`), `scripts/smoke-package.ps1` and its documented tool list.
5. Update canonical documentation: `docs/tools.md` (contracts, error catalog usage, tool count), `docs/security.md` (access-level table, statistics/anonymization caveat, cross-database names), `docs/configuration.md`, `docs/development.md` (smoke-check tool count), and `docs/architecture.md`/`README.md` only where they list tools or components.

---

### Out of scope

- Automated migration advice or version-based capability assertions (support depends on edition, compatibility level, and configuration).
- Health monitoring, server-wide DMV grants, Query Store, plan cache inspection, or database administration.
- Actual execution measurements (CPU time, logical reads, observed rows, memory grants, or spills) in `sql_explain_query`; runtime diagnosis stays with `sql_measure_performance` and `sql_benchmark_optimization`.
- Automatic SQL rewriting, index creation, or procedure execution.
- Returning raw XML or custom visual plan formats.
- Changing the parser grammar version or the connection pooling behavior of other tools.

---

## Verification

1. **Environment tests**:
   - Permitted database returns version, edition, engine edition, compatibility level, collation, state, and read-only flag.
   - `SchemaOnly` target works without data-read permissions.
   - Same-name databases behind distinct configured server aliases resolve correctly without collision.
   - Malformed/unknown targets (`SQL-AI-0001`) and denied/excluded targets (`SQL-AI-0104`) fail before any connection.
   - Unavailable databases, timeouts and permission errors return catalog failures, not empty or zero metadata.
   - A value the server does not provide is `null`/omitted and listed in `unavailable_fields`, never `0`/`false`.
   - Output contains no credentials, machine names, or connection strings.
2. **Explain guard tests** (all before any connection):
   - `SchemaOnly` and every other non-`None` level are accepted for a valid `SELECT`; `None` and excluded targets fail with `SQL-AI-0104`.
   - `DECLARE` + `SELECT` is accepted.
   - Mutations (`INSERT`, `UPDATE`, `DELETE`, `MERGE`, `SELECT INTO`, `EXEC`, DDL) fail with `SQL-AI-0107`, including at `ReadWrite` and `ReadWriteAnonymized`.
   - Controls fail with `SQL-AI-0104`, before and after the `SELECT`: `SET SHOWPLAN_XML`, `SET NOEXEC`, `SET PARSEONLY`, `SET STATISTICS`, other `SET`, `BEGIN TRAN`, `USE`.
   - Multiple statements/batches fail with `SQL-AI-0101`; unparseable SQL fails with `SQL-AI-0102`.
3. **Explain execution and parser tests**:
   - Valid `SELECT` returns an estimated operator tree, costs, estimated rows, and allowlisted warnings without executing the query or returning table rows.
   - Typed parameters are bound; `ParameterCompiledValue` and parameter values do not appear in the output.
   - Missing `SHOWPLAN`/`SELECT` permission returns `SQL-AI-0104` with the fixed message and no raw SQL Server text; no fallback to normal execution exists.
   - A non-showplan result (data rows) after `SET SHOWPLAN_XML ON` aborts, discards the connection and returns `SQL-AI-0102`.
   - The connection is non-pooled, `SET SHOWPLAN_XML OFF` is attempted with an independent token on error, cancellation and timeout, and the connection is disposed in every case.
   - XML parser rejects external DTDs and enforces the 2 MiB cap while reading.
   - Operator, depth and byte limits truncate in breadth-first order, keep visible parent identity and report `omitted_children`/`truncated`.
   - `DECLARE` + `SELECT` returns the plan of the `SELECT` with root `parent_id` `null`.
   - Sensitive literals, `StatementText`, `ScalarString`, expression text, generated `CREATE INDEX` text and parameter values do not appear in the serialized JSON.
   - The limit options are bound from configuration and present in `appsettings.json`.
4. **Integration and regression**:
   - Both tools are registered in MCP and derived CLI commands; the tool count assertions and the package smoke check expect 22.
   - `dotnet build SqlToAi.slnx --warnaserror` succeeds with 0 warnings and 0 errors.
   - `dotnet test SqlToAi.slnx` passes with no regressions (only baseline unconfigured TLS integration tests skipped).
