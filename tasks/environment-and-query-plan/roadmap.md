# Roadmap: Environment Context and Estimated Query-Plan Diagnosis

## Authority and execution

Implement the approved [concept](Konzept.md) (`status: ready`). Its Must items and Verification section define the contract; this roadmap assigns implementation and evidence to ordered slices.

Each slice must be executed atomically: code changes, unit/integration tests, documentation updates, and clean build/test gates (`dotnet build SqlToAi.slnx --warnaserror` and `dotnet test SqlToAi.slnx`). Stage only the slice's files and use Conventional Commits with a German imperative description.

---

## Milestone 1: Environment Context (`sql_get_environment`)

- [ ] **M1.1 — Implement EnvironmentService and Result Models**
  - **Intention**: Query server and database environment metadata (`SERVERPROPERTY` and `sys.databases`) via a dedicated service and provide typed DTOs.
  - **Scope**:
    - Create `EnvironmentResult` and `EnvironmentMetadata` DTOs.
    - Implement `EnvironmentService` (or method within database service layer) to query `ProductVersion`, `ProductMajorVersion`, `Edition`, `EngineEdition`, `compatibility_level`, `collation_name`, `state_desc`, and `is_read_only`.
    - Handle null/unavailable fields gracefully without fabricating zero/false defaults.
    - Register models in `SqlToAiJsonSerializerContext` for Native AOT support.
  - **Exclusions**: No DMV queries beyond `sys.databases` and `SERVERPROPERTY`; no login or machine name collection.
  - **Acceptance criteria**:
    - Unit tests verify metadata mapping and AOT serialization.
    - Integration tests on fixture database return valid product version, edition, compatibility level, collation, and read-only flag.
  - **Checklist**:
    - [ ] Models and serializer context configured
    - [ ] `EnvironmentService` implemented with safe query
    - [ ] Unit and fixture tests pass
    - [ ] Atomic commit: `feat: Implementiere EnvironmentService und Metadaten-Modelle`

- [ ] **M1.2 — Expose `sql_get_environment` in MCP, CLI, and Documentation**
  - **Intention**: Expose `sql_get_environment` via MCP protocol and CLI, enforcing qualified target resolution and `SchemaOnly` minimum access.
  - **Scope**:
    - Add constant `sql_get_environment` to `McpConstants`.
    - Wire `sql_get_environment` into `ToolDispatcher` with target resolution and access check (`SchemaOnly` allowed; `None` or excluded rejected).
    - Expose CLI command for environment retrieval.
    - Document the tool contract in `docs/tools.md` and security policy in `docs/security.md`.
  - **Exclusions**: No arbitrary custom SQL input; no table data access.
  - **Acceptance criteria**:
    - MCP tool dispatcher routes `sql_get_environment` correctly.
    - Missing or denied targets fail with standard error catalog codes (`SQL-AI-0001`, `SQL-AI-0104`).
    - `SchemaOnly` database access permits tool execution.
  - **Checklist**:
    - [ ] ToolDispatcher and CLI wired
    - [ ] Documentation updated
    - [ ] Integration tests pass
    - [ ] Atomic commit: `feat: Binde sql_get_environment in MCP, CLI und Dokumentation ein`

- [ ] **M1.3 — Milestone 1 Audit and Verification**
  - **Intention**: Verify Milestone 1 implementation against concept requirements.
  - **Acceptance criteria**:
    - `dotnet build SqlToAi.slnx --warnaserror` exits with 0 errors and 0 warnings.
    - `dotnet test SqlToAi.slnx` passes with 0 failures.
    - No sensitive data, connection strings, or unanonymized leaks occur.
  - **Checklist**:
    - [ ] Audit executed
    - [ ] Milestone 1 closed

---

## Milestone 2: Estimated Query Plan (`sql_explain_query`)

- [ ] **M2.1 — AST Guard Validation and Plan Models**
  - **Intention**: Enforce read-only single SELECT validation for `sql_explain_query` and define plan tree DTOs.
  - **Scope**:
    - Define `ExplainQueryResult`, `PlanOperatorNode`, and `PlanWarning` DTOs with AOT serialization.
    - Extend or reuse ScriptDom visitor to enforce single `SELECT` (with leading `DECLARE`), rejecting DML (`INSERT`, `UPDATE`, `DELETE`, `MERGE`), DDL, `EXEC`, `SELECT INTO`, and caller transaction/SHOWPLAN/STATISTICS controls (`SQL-AI-0107`).
    - Enforce `SchemaOnly` minimum access check.
  - **Exclusions**: No database connection or execution in this slice.
  - **Acceptance criteria**:
    - Unit tests reject mutating statements, batches with multiple statements, and control commands across all access levels.
    - DTOs serialize cleanly with Native AOT.
  - **Checklist**:
    - [ ] Plan DTOs and AST guard rules implemented
    - [ ] Guard unit tests pass
    - [ ] Atomic commit: `feat: Erstelle AST-Guard und DTOs für sql_explain_query`

- [ ] **M2.2 — Showplan Connection Management and Lifecycle**
  - **Intention**: Execute `SET SHOWPLAN_XML ON`, query, and `SET SHOWPLAN_XML OFF` safely with strict connection pool hygiene.
  - **Scope**:
    - Implement `ShowplanExecutionService` with dedicated connection lifecycle.
    - Separate batches: `SET SHOWPLAN_XML ON`, target query, `SET SHOWPLAN_XML OFF`.
    - Robust error handling: ensure `SHOWPLAN_XML OFF` executes on error, timeout, or cancellation.
    - If reset is uncertain or fails, dispose and exclude connection from pool (`SqlConnection.ClearPool` or connection discard).
    - Strictly prevent any fallback to normal query execution.
  - **Exclusions**: No XML parsing in this slice (capture raw string for M2.3).
  - **Acceptance criteria**:
    - Tests verify `SHOWPLAN_XML` is enabled, returns XML, and is disabled.
    - Cancellation and timeout tests verify connection is reset or discarded without leaving `SHOWPLAN_XML ON` in pool.
  - **Checklist**:
    - [ ] `ShowplanExecutionService` connection lifecycle implemented
    - [ ] Pool hygiene and error cleanup tests pass
    - [ ] Atomic commit: `feat: Implementiere Showplan-Verbindungsverwaltung und Cleanup`

- [ ] **M2.3 — Secure Showplan XML Parser and Tree Truncation**
  - **Intention**: Safely parse Showplan XML into a bounded, compact operator tree without leaking sensitive strings or raw SQL.
  - **Scope**:
    - Implement XML reader with secure settings (`DtdProcessing.Prohibit`, `XmlResolver = null`, 2 MiB input cap `MaxShowplanXmlBytes`).
    - Extract operator tree: `node_id`, `parent_id`, `physical_op`, `logical_op`, `estimated_rows`, `subtree_cost`.
    - Extract allowlisted warnings: Missing Indexes, Scans, and Plan-Affecting Conversions.
    - Enforce output bounds: max 100 operators, max depth 16, max 64 KiB serialized output. Flag truncated nodes explicitly.
    - Strip raw XML, `StatementText`, parameter values, and `ScalarString` to protect sensitive data.
  - **Exclusions**: Do not return raw XML or execute query.
  - **Acceptance criteria**:
    - XML parser tests verify secure entity handling and 2 MiB boundary enforcement.
    - Fixture tests cover joins, scans, nested operators, missing index warnings, and tree truncation.
    - Serialized output contains no raw query text, literals, or parameter values.
  - **Checklist**:
    - [ ] XML parser and tree builder implemented
    - [ ] Bound and truncation tests pass
    - [ ] Literal exclusion tests pass
    - [ ] Atomic commit: `feat: Implementiere sicheren Showplan-XML-Parser mit Baum-Begrenzung`

- [ ] **M2.4 — Expose `sql_explain_query` in MCP, CLI, and Documentation**
  - **Intention**: Expose `sql_explain_query` via MCP protocol and CLI, wired to `ShowplanExecutionService` and documentation.
  - **Scope**:
    - Add constant `sql_explain_query` to `McpConstants`.
    - Wire `sql_explain_query` into `ToolDispatcher` with parameter binding and qualified target resolution.
    - Expose CLI command for query explanation.
    - Update `docs/tools.md` and `docs/security.md`.
  - **Exclusions**: No execution of mutating queries.
  - **Acceptance criteria**:
    - MCP dispatcher routes `sql_explain_query`.
    - CLI executes query plan explanation.
    - Documentation accurately describes tool arguments, output shape, and safety constraints.
  - **Checklist**:
    - [ ] ToolDispatcher and CLI wired
    - [ ] Documentation updated
    - [ ] Integration tests pass
    - [ ] Atomic commit: `feat: Binde sql_explain_query in MCP, CLI und Dokumentation ein`

- [ ] **M2.5 — Milestone 2 Audit and Verification**
  - **Intention**: Verify Milestone 2 implementation against concept requirements.
  - **Acceptance criteria**:
    - `dotnet build SqlToAi.slnx --warnaserror` exits with 0 errors and 0 warnings.
    - `dotnet test SqlToAi.slnx` passes with 0 failures.
    - `sql_explain_query` verified against real SQL Server fixture with various query shapes.
  - **Checklist**:
    - [ ] Audit executed
    - [ ] Milestone 2 closed

---

## Milestone 3: Integrated Verification and Final Audit

- [ ] **M3.1 — Full System Regression and Final Audit**
  - **Intention**: Execute end-to-end audit of both new diagnostic tools across all access levels, CLI commands, and test suites.
  - **Scope**:
    - Run full test suite: `dotnet test SqlToAi.slnx`.
    - Run warning-as-error build: `dotnet build SqlToAi.slnx --warnaserror`.
    - Check Native AOT compatibility and serializer registration.
    - Audit `docs/tools.md`, `docs/security.md`, and `README.md` for consistency.
  - **Acceptance criteria**:
    - 0 build errors, 0 build warnings.
    - All tests pass (except baseline unconfigured TLS integration tests).
    - Clean `git diff --check`.
  - **Checklist**:
    - [ ] Full build and test suite verified
    - [ ] Documentation consistency verified
    - [ ] Final audit recorded
    - [ ] Atomic commit: `docs: Schließe Audit für Diagnose-Tools und Abfragepläne ab`
