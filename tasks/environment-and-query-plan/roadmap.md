# Roadmap: Environment Context and Estimated Query-Plan Diagnosis

## Authority and execution

Implement the [concept](Konzept.md). Its Must items and Verification section define the contract; this roadmap assigns implementation and evidence to ordered slices. Start only after the concept has status `ready`.

Each slice is atomic: code changes, tests, documentation updates, and clean build/test gates (`dotnet build SqlToAi.slnx --warnaserror` and `dotnet test SqlToAi.slnx`). Stage only the slice's files and use Conventional Commits with a German imperative description.

Tool count: the server registers 20 tools today; it registers 21 after M1.2 and 22 after M2.4. Every slice that adds a tool updates all count assertions, the smoke check and the docs listed in the concept (Shared Integration, items 1–5), not only `tools.md` and `security.md`.

---

## Milestone 1: Environment Context (`sql_get_environment`)

- [x] **M1.1 — Implement EnvironmentService and Result Models**
  - **Intention**: Query server and database environment metadata through a dedicated service with typed DTOs.
  - **Scope**:
    - Create the environment result DTO with the allowlisted fields and `unavailable_fields`, registered in the existing source-generated JSON contexts.
    - Implement `EnvironmentService` (one fixed query against `sys.databases` filtered by `DB_ID()` and `SERVERPROPERTY`, no caller input, `QueryExecution.CommandTimeoutSeconds`).
    - Null/unavailable fields are reported through `unavailable_fields`, never as `0`/`false`.
  - **Exclusions**: No DMV queries beyond `sys.databases` and `SERVERPROPERTY`; no login or machine name collection; no tool wiring.
  - **Acceptance criteria**:
    - Unit tests verify metadata mapping, `unavailable_fields`, and serialization.
    - Fixture/integration tests on a real database return valid product version, edition, engine edition, compatibility level, collation, state and read-only flag.
  - **Checklist**:
    - [x] DTOs and serializer context configured
    - [x] `EnvironmentService` implemented
    - [x] Unit and fixture tests pass
    - [x] Atomic commit: `feat: Implementiere EnvironmentService und Metadaten-Modelle`

- [x] **M1.2 — Expose `sql_get_environment` in MCP, CLI, and Documentation**
  - **Intention**: Expose the tool with qualified target resolution and `SchemaOnly` minimum access.
  - **Scope**:
    - `McpConstants`, `ToolRegistry` (schema; CLI is derived), `ToolDispatcher`, `SqlMcpToolRegistrations`, DI in `Program.cs`.
    - Target resolution and access check (`SchemaOnly` allowed; `None` or excluded rejected with `SQL-AI-0104`, malformed with `SQL-AI-0001`).
    - Update tool-count assertions to 21 (tests, `scripts/smoke-package.ps1`) and docs: `docs/tools.md` (contract, T-SQL 150 parser note), `docs/security.md` (access-level table), `docs/development.md`, plus architecture/README where tools are listed.
  - **Exclusions**: No arbitrary custom SQL input; no table data access.
  - **Acceptance criteria**:
    - Dispatcher routes `sql_get_environment`; CLI command exists via the registry.
    - Denied/excluded/malformed targets fail before any connection with the cataloged codes.
    - `SchemaOnly` access permits execution; same-name databases on different aliases do not collide.
  - **Checklist**:
    - [x] Registry, dispatcher, MCP registration and DI wired
    - [x] Tool-count assertions and smoke check updated to 21
    - [x] Documentation updated
    - [x] Integration tests pass
    - [x] Atomic commit: `feat: Binde sql_get_environment in MCP, CLI und Dokumentation ein`

- [x] **M1.3 — Milestone 1 Audit and Verification**
  - **Intention**: Verify Milestone 1 against the concept.
  - **Acceptance criteria**:
    - Build with `--warnaserror` and full tests pass with 0 failures.
    - Output contains no connection strings, host names or credentials.
  - **Checklist**:
    - [x] Audit executed
    - [x] Milestone 1 closed

---

## Milestone 2: Estimated Query Plan (`sql_explain_query`)

- [x] **M2.1 — Statement Guard, Options and Plan Models**
  - **Intention**: Enforce the closed statement allowlist before any connection and define options and DTOs.
  - **Scope**:
    - Allowlist guard: optional leading `DECLARE`, exactly one `SELECT` without `INTO`, nothing after it; unparseable → `SQL-AI-0102`, multiple statements → `SQL-AI-0101`, controls (any `SET`, `SHOWPLAN`/`NOEXEC`/`PARSEONLY`/`FMTONLY`/`STATISTICS`, transaction statements, `USE`) → `SQL-AI-0104`, mutations → `SQL-AI-0107`. The existing `ReadOnlyGuard` is called explicitly for all access levels. Reuse existing guards where they fit; do not rely on the preamble detection, which accepts `SET`/`DECLARE` at any position.
    - Access check: `SchemaOnly` minimum.
    - `ExplainQuery` options class (`MaxShowplanXmlBytes` 2 MiB, `MaxOperators` 100, `MaxDepth` 16, `MaxResponseBytes` 64 KiB), bound through `IOptions<T>`, defaults in `src/SqlToAi/appsettings.json`, documented in `docs/configuration.md`.
    - `ExplainQueryResult`, node and warning DTOs registered in the source-generated JSON contexts.
  - **Exclusions**: No database connection or execution in this slice.
  - **Acceptance criteria**:
    - Unit tests cover accepted shapes (`SELECT`, `DECLARE` + `SELECT`) and every rejection class above across all access levels, including trailing controls after the `SELECT` and mutations at `ReadWrite`/`ReadWriteAnonymized`.
    - Options binding test; DTOs serialize.
  - **Checklist**:
    - [x] Guard rules implemented
    - [x] Options, defaults and configuration docs added
    - [x] DTOs registered
    - [x] Guard and options tests pass
    - [x] Atomic commit: `feat: Erstelle Statement-Guard, Optionen und DTOs für sql_explain_query`

- [x] **M2.2 — Showplan Execution on a Non-Pooled Connection**
  - **Intention**: Obtain showplan XML without executing the query and without any chance of leaking session state into the pool.
  - **Scope**:
    - Extend the connection factory so a caller can request a non-pooled connection (`Pooling=false`); existing callers stay unchanged.
    - Implement `ShowplanExecutionService`: separate batches for `SET SHOWPLAN_XML ON`, the parameterized query and `SET SHOWPLAN_XML OFF`; no transaction.
    - Positive result check: exactly one single-column, single-row showplan XML result; anything else aborts, discards the connection, `SQL-AI-0102`.
    - Best-effort `SET SHOWPLAN_XML OFF` with an independent cancellation token (as `DatabaseCleanup` does), then always dispose.
    - Error mapping: missing `SHOWPLAN`/`SELECT` permission → `SQL-AI-0104` with a fixed permission-class message; timeout → `SQL-AI-0106`; connection failure → `SQL-AI-0105`. No fallback to normal execution; no use of `PerformanceMeasurementService`.
    - Read the XML through a bounded reader that enforces `MaxShowplanXmlBytes` while reading.
  - **Exclusions**: No tree parsing in this slice (the bounded XML text is handed to M2.3).
  - **Acceptance criteria**:
    - Fake-based tests verify batch order, non-pooled connection request, disposal on success, error, cancellation and timeout, independent cleanup token, and the non-showplan-result abort.
    - Permission-error mapping tests; size cap test.
    - Fixture test against a real SQL Server proves the plan is returned and the query does not run (no side effects, no rows).
  - **Checklist**:
    - [x] Factory extension and `ShowplanExecutionService` implemented
    - [x] Lifecycle, error-mapping and bound tests pass
    - [x] Atomic commit: `feat: Implementiere Showplan-Ausführung über nicht gepoolte Verbindung`

- [x] **M2.3 — Secure Showplan XML Parser and Tree Truncation**
  - **Intention**: Parse showplan XML into the bounded result shape defined in the concept without leaking sensitive strings.
  - **Scope**:
    - Secure XML settings (`DtdProcessing.Prohibit`, `XmlResolver = null`).
    - Select the plan of the `SELECT` statement; produce nodes (`node_id`, `parent_id`, `physical_op`, `logical_op`, `estimated_rows`, `subtree_cost`, optional `object`) and the allowlisted warnings (missing index without DDL text; plan-affecting conversions without expression text; capped).
    - Apply `MaxOperators`, `MaxDepth` and `MaxResponseBytes` in breadth-first order with `omitted_children` and `truncated`.
    - Never copy raw XML, `StatementText`, `ParameterCompiledValue`, `ScalarString` or expression text. Do not reuse the swallow-all-exceptions parsing or message/DDL generation of `PerformanceMeasurementService`; share only extraction code that fits the allowlist.
  - **Exclusions**: No connection handling; no raw XML in the result.
  - **Acceptance criteria**:
    - Fixture tests cover joins, scans/seeks with `object`, nested operators, missing-index and conversion warnings, `DECLARE` + `SELECT` plans, operator/depth/byte truncation with visible parents.
    - DTD/entity rejection test; malformed XML maps to a catalog error.
    - Serialized output contains no raw query text, literals, parameter values, expression text or `CREATE INDEX` text.
  - **Checklist**:
    - [x] Parser and tree builder implemented
    - [x] Bound and truncation tests pass
    - [x] Literal exclusion tests pass
    - [x] Atomic commit: `feat: Implementiere sicheren Showplan-XML-Parser mit Baum-Begrenzung`

- [x] **M2.4 — `ExplainQueryService` and Exposure in MCP, CLI, and Documentation**
  - **Intention**: Orchestrate target resolution, access check, guard, showplan execution and parsing, and expose the tool.
  - **Scope**:
    - `ExplainQueryService`: resolve target once, access check, guard (M2.1), execution (M2.2), parser (M2.3), result mapping.
    - `McpConstants`, `ToolRegistry` (schema with typed `parameters`; CLI derived), `ToolDispatcher`, `SqlMcpToolRegistrations`, DI.
    - Update tool-count assertions and smoke check to 22.
    - Docs: `docs/tools.md` (arguments, result shape, error behavior, `DECLARE`/recompile caveat), `docs/security.md` (access levels, statistics/anonymization/token caveat, cross-database names), `docs/configuration.md`, `docs/development.md`, architecture/README where tools are listed.
  - **Exclusions**: No execution of any query.
  - **Acceptance criteria**:
    - Dispatcher routes `sql_explain_query`; CLI command exists via the registry.
    - Service-level tests confirm no connection is opened for rejected targets or statements.
    - Documentation matches implemented arguments, result fields, limits and error codes.
  - **Checklist**:
    - [x] `ExplainQueryService` implemented
    - [x] Registry, dispatcher, MCP registration and DI wired
    - [x] Tool-count assertions and smoke check updated to 22
    - [x] Documentation updated
    - [x] Integration tests pass
    - [x] Atomic commit: `feat: Binde sql_explain_query in MCP, CLI und Dokumentation ein`

- [x] **M2.5 — Milestone 2 Audit and Verification**
  - **Intention**: Verify Milestone 2 against the concept.
  - **Acceptance criteria**:
    - Build with `--warnaserror` and full tests pass with 0 failures.
    - `sql_explain_query` verified against a real SQL Server fixture with several query shapes (joins, scans, missing index, parameters, truncation).
  - **Checklist**:
    - [x] Audit executed
    - [x] Milestone 2 closed

---

## Milestone 3: Integrated Verification and Final Audit

- [ ] **M3.1 — Full System Regression and Final Audit**
  - **Intention**: End-to-end audit of both tools across all access levels, CLI commands and test suites.
  - **Scope**:
    - Run `dotnet test SqlToAi.slnx` and `dotnet build SqlToAi.slnx --warnaserror`.
    - Check that all new DTOs are registered in the source-generated JSON contexts and covered by serialization tests. The full Native AOT publish gate is not part of this check (it remains open as documented in `docs/development.md`).
    - Audit `docs/tools.md`, `docs/security.md`, `docs/configuration.md`, `docs/development.md` and `README.md` for consistency, including the tool count 22.
  - **Acceptance criteria**:
    - 0 build errors, 0 build warnings.
    - All tests pass (except baseline unconfigured TLS integration tests).
    - Clean `git diff --check`.
  - **Checklist**:
    - [ ] Full build and test suite verified
    - [ ] Documentation consistency verified
    - [ ] Final audit recorded
    - [ ] Atomic commit: `docs: Schließe Audit für Diagnose-Tools und Abfragepläne ab`
