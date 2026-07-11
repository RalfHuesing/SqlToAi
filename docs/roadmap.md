# SqlToAi Project Roadmap

This document outlines the development roadmap, epics, and steps for the **SqlToAi** MCP Server.

---

## Epic 1: Foundations & Core Services (Completed)
- [x] **Step 1: Project Setup & Test Infrastructure**
  - Folder structure and namespace definitions.
  - Core domain models (`Result<T>`, `SqlToAiError`, `AccessLevel`).
  - Configuration mapping classes (`SqlToAiOptions`).
  - Unit tests for core types.
- [x] **Step 2: Database Connection & Security Whitelisting**
  - Database connection factory (`SqlConnectionFactory`) with credentials and read-only connection intent.
  - Whitelist security guard (`SecurityGuard`) using wildcard/glob matches.
  - Dynamic access check provider (`AccessLevelProvider`) with TTL caching.
- [x] **Step 3: Read-Only Guard & String Anonymization**
  - Query parser (`ReadOnlyGuard`) to block mutating commands and strip SQL comments.
  - Custom column anonymization logic.
- [x] **Step 4: Anonymizer Overhaul & Privilege Mapping**
  - Shift default access level fallback to `ReadOnlyAnonymized`.
  - Overhaul anonymizer to apply general, column-independent string scrambling.
  - Implement consistent random scrambling using stable FNV-1a hashing and PRNG seeding.
- [x] **Step 5: Schema Documentation Enrichment**
  - Metadata provider (`MetadataProvider`) to read native `MS_Description` extended properties.
  - Support for custom database table metadata queries.

---

## Epic 2: Database Schema & Query Engines
- [ ] **Step 6: Schema Exploration Service**
  - Create services to query tables, views, column types, primary keys, foreign keys, indexes, triggers, and parameters using SQL Server catalog views (`sys.objects`, `sys.columns`, etc.).
  - Render schemas to clean Markdown.
  - Integrate metadata descriptions from `IMetadataProvider`.
- [ ] **Step 7: Safe Query Execution Service**
  - Execute queries inside an explicit database transaction.
  - Enforce transaction rollback at completion to guarantee no data modifications.
  - Fetch results, apply PII anonymization to string columns, and format output as JSON lines.

---

## Epic 3: MCP Protocol & Transport
- [ ] **Step 8: JSON-RPC Message Models**
  - Implement strongly typed C# models matching the MCP specification (Request, Response, Notification, ToolDefinition, etc.).
- [ ] **Step 9: Stdio Transport Host**
  - Create Stdio Server Host listening on standard input and writing to standard output.
  - Create a Tool Registry to route incoming tool requests to their respective handlers.

---

## Epic 4: MCP Tools Implementation
- [ ] **Step 10: Metadata and Discovery Tools**
  - Implement handlers for:
    - `sql_list_databases`
    - `sql_search_databases`
    - `sql_search_objects`
    - `sql_get_schema`
    - `sql_get_schema_foreign_keys`
    - `sql_get_schema_indexes`
    - `sql_get_schema_constraints`
    - `sql_get_trigger_definition`
    - `sql_get_object_references`
    - `sql_get_routine_parameters`
- [ ] **Step 11: Validation and Execution Tools**
  - Implement handler for `sql_validate_query` (syntax checking via T-SQL `PARSEONLY`).
  - Implement handler for `sql_execute_query` (wrapping Safe Query Execution Service).

---

## Epic 5: Integration & Packaging
- [ ] **Step 12: Bootstrapping & Configuration**
  - Wire up dependency injection in `Program.cs`.
  - Bind `appsettings.json` and environmental overrides.
  - Run Stdio host loop.
  - Update `README.md` and perform a final developer-end validation.
