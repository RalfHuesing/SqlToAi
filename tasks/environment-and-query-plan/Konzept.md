---
status: ready
---

# SQL Server Environment Context and Estimated Query-Plan Diagnosis

## Intention

Provide agents with two safe, bounded diagnostic capabilities that operate without executing workload queries or accessing user application data, functioning even under `SchemaOnly` database access:

1. **Environment context (`sql_get_environment`)**: Retrieve SQL Server version, edition, engine edition, database compatibility level, collation, state, and read-only status in a single compact call, enabling agents to select compatible T-SQL syntax and interpret diagnostics without ad hoc catalog querying.
2. **Estimated query plan (`sql_explain_query`)**: Inspect how SQL Server's optimizer plans to execute a read-only `SELECT` query without actually executing it, returning a compact operator hierarchy, estimated rows, subtree costs, and allowlisted compiler warnings rather than an XML dump or premature tuning verdict.

## Settled direction

The user approved combining both draft diagnostic concepts (`database-environment` and `estimated-query-plan`) into a single unified task and roadmap for orchestrated end-to-end implementation. Both capabilities share:
- Qualified `alias/database` target resolution.
- Eligibility under `SchemaOnly` access (no table-data access required; `None` or excluded targets remain denied).
- Strict information boundary: return allowlisted metadata/diagnostics only; no sensitive user data, connection strings, hostnames, credentials, or raw query text leakage.
- Native AOT-compatible JSON serialization.

---

## Scope

### Must

#### 1. Environment Context (`sql_get_environment`)
1. **Tool contract**: Add `sql_get_environment` with one required argument: `database` (resolved as a qualified `alias/database` target).
2. **Access & safety**: Require `SchemaOnly` minimum access. `None` and excluded targets are rejected before connection. Use the selected endpoint's `SqlClient` factory, timeout, TLS, and safe connection error conventions.
3. **Metadata query**: Execute one fixed, parameterized read-only query against `sys.databases` and `SERVERPROPERTY` in the target database.
4. **Allowlisted fields**:
   - `target`: Canonical qualified `alias/database` target name.
   - `product_version`: SQL Server version string (e.g. `"16.0.4180.1"`).
   - `product_major_version`: Numeric major version (e.g. `16`).
   - `edition`: SQL Server edition string (e.g. `"Developer Edition (64-bit)"`).
   - `engine_edition`: Numeric engine edition (e.g. `3` for Enterprise/Developer).
   - `compatibility_level`: Target database compatibility level (e.g. `160`).
   - `collation`: Database collation name (e.g. `"Latin1_General_CI_AS"`).
   - `state_desc`: Database state (e.g. `"ONLINE"`).
   - `is_read_only`: Boolean indicating whether the SQL Server database itself is read-only.
5. **Robust field handling**: Unavailable or not-visible fields remain `null` with an availability note; do not equate unavailable values with zero or false. Connection or permission errors must return actionable catalog failures, not all-null objects.
6. **No data leakage**: No arbitrary SQL, login/user inventories, host/machine names, connection strings, or credentials.

#### 2. Estimated Query Plan (`sql_explain_query`)
1. **Tool contract**: Add `sql_explain_query` with required arguments `database` (qualified `alias/database`) and `query` (string), and optional typed `parameters`.
2. **Access & safety**:
   - Require `SchemaOnly` minimum access (`None` and excluded targets denied).
   - Enforce read-only AST guard across all access levels (including `ReadWrite`). Permit only a single `SELECT` statement with supported leading declarations (e.g. `DECLARE`).
   - Reject any mutating statement (`INSERT`, `UPDATE`, `DELETE`, `MERGE`, `SELECT INTO`, `EXEC`, DDL) with `SQL-AI-0107`.
   - Reject caller execution, transaction, `SHOWPLAN`, or `STATISTICS` controls (`SET SHOWPLAN_XML`, `SET NOEXEC`, `BEGIN TRAN`, etc.) before database connection.
3. **Execution mechanism**:
   - Dedicated connection lifecycle: execute `SET SHOWPLAN_XML ON`, the parameterized query, and `SET SHOWPLAN_XML OFF` as distinct batches.
   - Never route through `PerformanceMeasurementService` or fallback to normal query execution on failure, timeout, or cancellation.
   - Connection pool safety: reset `SHOWPLAN_XML OFF` reliably in cleanup. If reset fails or is uncertain, dispose and exclude the affected connection from reuse (`ClearPool` or connection discard).
4. **Showplan XML parsing & bounds**:
   - Bound XML input size to a 2 MiB limit (`MaxShowplanXmlBytes`). XML exceeding this limit fails explicitly without query execution.
   - Use secure XML parsing: prohibit external DTDs (`DtdProcessing.Prohibit`) and set `XmlResolver = null`.
   - Extract a compact operator tree:
     - `plan_kind`: `"estimated"`
     - Node attributes: stable `node_id`, `parent_id`, `physical_op`, `logical_op`, `estimated_rows`, `subtree_cost`.
     - Allowlisted compiler warnings: Missing Indexes, Scans, and Plan-Affecting Conversions (implicit type conversions).
   - Output bounds: limit operator tree to 100 operators, depth 16, and serialized output to 64 KiB. Truncated nodes indicate omitted children explicitly while preserving visible parent identity.
5. **Strict plan data protection**:
   - Do NOT return raw XML, `StatementText`, parameter values, `ScalarString`, or arbitrary expression text.
   - Restrict object identities to metadata needed for diagnosis (e.g. table/index names in warnings or scans).

#### 3. Shared Integration & Infrastructure
1. Register both tools in `McpConstants`, `ToolDispatcher`, and CLI command definitions.
2. Register result DTOs in `SqlToAiJsonSerializerContext` for native AOT compilation.
3. Update canonical documentation in `docs/tools.md` and `docs/security.md`.

---

### Out of scope

- Automated migration advice or version-based capability assertions (support depends on edition, compatibility level, and configuration).
- Health monitoring, server-wide DMV grants, Query Store, plan cache inspection, or database administration.
- Actual execution measurements (CPU time, logical reads, observed rows, memory grants, or spills) in `sql_explain_query` — actual runtime diagnosis remains the responsibility of `sql_measure_performance` and `sql_benchmark_optimization`.
- Automatic SQL rewriting, index creation, or procedure execution.
- Returning raw XML or custom visual plan formats.

---

## Verification

1. **Environment tests**:
   - Permitted database returns version, edition, compatibility level, collation, state, and read-only flag.
   - `SchemaOnly` target works without data-read permissions.
   - Same-name databases behind distinct configured server aliases resolve correctly without collision.
   - Denied/excluded targets fail before metadata SQL.
   - Unavailable databases return safe connection failures, not empty/zero metadata.
   - Output contains no credentials, machine names, or connection strings.
2. **Explain tests**:
   - Valid `SELECT` returns estimated operator tree, costs, estimated rows, and allowlisted warnings without executing the query or returning table rows.
   - Mutating statements (`INSERT`, `UPDATE`, `DELETE`, `MERGE`, `SELECT INTO`, `EXEC`, DDL) and control statements (`SET`, `TRAN`) fail AST validation with `SQL-AI-0107`.
   - Missing `SELECT` or `SHOWPLAN` permissions return actionable errors; no fallback to normal execution exists.
   - XML parser rejects external DTDs and enforces the 2 MiB input cap.
   - Plan tree truncation preserves visible parent structure and flags omitted children.
   - Sensitive literals, parameter values, and raw SQL text do not appear in the serialized JSON output.
   - On error, cancellation, or timeout, the connection is reset or discarded without leaving `SHOWPLAN_XML` active in the pool.
3. **Build & regression**:
   - `dotnet build SqlToAi.slnx --warnaserror` succeeds with 0 warnings and 0 errors.
   - `dotnet test SqlToAi.slnx` passes with no regressions (only baseline unconfigured TLS integration tests skipped).
