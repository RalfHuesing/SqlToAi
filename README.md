# SqlToAi

**SqlToAi** is a lightweight, secure, and highly customizable Model Context Protocol (MCP) server for Microsoft SQL Server. It allows AI agents and LLMs (such as Cursor, Windsurf, or Claude Desktop) to interact with databases securely, retrieve schema information, run read-only queries with on-the-fly string anonymization to protect PII, execute local multi-batch SQL scripts, measure SQL Server query performance & execution plans, verify query result equivalence, and execute automated optimization benchmarks with typed SQL parameters.

Designed specifically for developers analyzing ERP systems, optimizing SQL queries, and exploring complex database schemas without exposing sensitive customer data.

---

## Key Features

### ⚡ SQL Performance, Equivalence & Benchmarking
* 🏆 **All-in-One Optimization Benchmarking (`sql_benchmark_optimization`):** Compare baseline vs candidate queries in a single step, evaluating result equivalence, resource utilization deltas (CPU time, logical/physical reads), XML execution plan warnings, and returning an automated actionable recommendation verdict (`Recommended`, `NotRecommended`, `Neutral`, `UnsafeDueToDataMismatch`).
* ⚡ **SQL Performance & Execution Plan Engine (`sql_measure_performance`):** Measure server-side CPU time, elapsed time, and logical/physical reads (`STATISTICS IO, TIME`), and parse actual XML execution plans (`STATISTICS XML`) for missing index recommendations (with ready-to-execute CREATE NONCLUSTERED INDEX DDL statements per warning), table scans, and implicit data type conversions (`CONVERT_IMPLICIT`), with graceful degradation if `SHOWPLAN` permissions are missing.
* 🧭 **Server-Wide Missing-Index Suggestions (`sql_suggest_indexes`):** Returns server-wide cumulative missing-index recommendations from SQL Server's `sys.dm_db_missing_index_*` DMVs, prioritized by `improvement_score` (`avg_total_user_cost × avg_user_impact × (user_seeks + user_scans)`), as a Markdown table with the equality/inequality/include column lists, seek/scan counts, and last-seek timestamp. Output always starts with a restart-reset note (DMV data accumulates since the last SQL Server restart). Filters: `table_name` (LIKE on the DMV `statement` column), `min_score`, `top` (default 10). Degrades gracefully (structured permission note instead of a hard error) when the database user lacks `VIEW SERVER STATE`.
* ⚖️ **Database-Side Result Set Equivalence (`sql_compare_queries`):** Perform server-side set difference checks (`EXCEPT` / `UNION ALL`), row count comparisons, and schema validations between baseline (Query A) and candidate (Query B) queries without downloading large datasets to the client.
* 🏷️ **Typed SQL Parameters & Explicit Type Overrides:** Parameterized query support across execution, validation, comparison, and benchmarking tools — supporting automatic primitive/date inference as well as explicit DB type overrides (e.g. `{"value": "123", "dbType": "AnsiString"}`) to eliminate implicit conversion warnings in SQL Server plans.

### 🛡️ Data Protection & PII Safeguards
* 🛡️ **PII Shield (On-the-Fly Anonymization):** Automatically scrambles or hashes all string values in query results (default: ON) to protect customer data while preserving data structure, casing, length, and join logical consistency. *Known limits* — string anonymization applies only to `string`-typed values; numeric IDs, dates, and non-string columns are never anonymized. Schema tools return raw DDL text without anonymization.
* 🔑 **Reversible, Searchable Tokenization (optional):** Replaces masking with deterministic, reversible tokens (`§§§T1§§§`). The server transparently resolves tokens back to real values in `WHERE`, `JOIN`, `LIKE`, and range predicates before execution — allowing cross-table filtering without exposing raw PII to the AI.
* 🌐 **Central & Default Anonymization Rules:** Global default scrambling with optional central database rules (`AnonymizationRules`) supporting `LIKE`-style wildcard patterns and specificity-based rule resolution.

### 🔒 Enterprise Security & Access Control
* 🔒 **Read-Only Guard & Rollback Safety:** AST-based command validation (`Microsoft.SqlServer.TransactSql.ScriptDom`, `TSql150Parser`) blocks modifying queries (`INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `MERGE`, `EXEC`, etc.) inside rollback transactions unless a database is explicitly declared in the `ReadWrite` level list.
* 🚦 **Level-Based Database Access Control:** Granular access lists (`ReadWrite`, `ReadOnly`, `ReadOnlyAnonymized`, `SchemaOnly`) in `appsettings.json` with fail-safe default-deny (`AccessLevel.None`).

### 🔎 Schema Discovery & Developer Experience
* 📋 **17 Progressive Disclosure and Execution Tools:** Exposes 17 optimized tools for schema discovery, triggers, constraints, indexes, routine parameters, referencing entities (`sys.dm_sql_referencing_entities`), local SQL script execution (`sql_execute_file`), result-set equivalence comparison (`sql_compare_queries`), server performance measurement & XML plan warning parsing (`sql_measure_performance`), all-in-one optimization benchmarking (`sql_benchmark_optimization`), and server-wide DMV-driven missing-index suggestions (`sql_suggest_indexes`), formatted in clean Markdown for the AI.
* 🔎 **Proactive Anonymization Awareness:** `sql_get_schema` marks column anonymization status (`No`, `Yes`, `Yes (searchable)`) before queries are written, and `sql_execute_query` reports affected `Table.Column` pairs to guide the AI agent safely.
* 📖 **Schema Enrichment (Custom Metadata):** Inject custom business logic or table/column documentation from another database/table via configurable SQL queries directly into the schema results returned to the AI.
* 🚀 **Stdio MCP Host & CLI Query Runner:** Fast, local stdio execution for AI clients (no network setup), plus direct CLI query execution (`.\SqlToAi.exe query ...`) for manual tool verification.
* 📂 **File-Based Logging + MCP Trail:** Serilog writes rolling app and error logs next to the executable; every MCP request and response is recorded verbatim as JSONL under `log/mcp/YYYY-MM-DD/`, with the same anonymization the LLM saw.

---

## Technical Stack

* **Runtime:** .NET 10 / C# 14
* **Solution Format:** Visual Studio 2026 Solution XML (`.slnx`)
* **ORM:** Dapper
* **Provider:** Microsoft.Data.SqlClient
* **JSON:** System.Text.Json (performance optimized)
* **DI / Logging:** Microsoft.Extensions.DependencyInjection + Microsoft.Extensions.Logging (Console)
* **Testing:** xUnit v3

---

## Configuration (`appsettings.json`)

The server is configured using a standard JSON file. A complete template is provided at
[`src/SqlToAi/appsettings.json`](src/SqlToAi/appsettings.json) and is automatically copied to the build
output. The root section is `SqlToAi`, which contains the following sub-sections:

| Section | Purpose |
| :--- | :--- |
| `Databases` | Level-based database access lists (`ReadWrite`, `ReadOnly`, `ReadOnlyAnonymized`, `SchemaOnly`) and `CacheTtlSeconds`. |
| `SqlServer` | Connection parameters (`Server`, `IntegratedSecurity`, `UserId`, `Password`, `ConnectTimeoutSeconds`, `ExcludedDatabases`). Values support environment variable interpolation (e.g. `%COMPUTERNAME%`). |
| `Anonymizer` | Master switch (`Enabled`), the algorithm (`DefaultMode`: `ScramblePattern` or `Hash`), and the optional `Tokenization` sub-section below. |
| `Anonymizer.Tokenization` | Optional global mode switch (`Enabled`, `Prefix`/`Suffix`) that replaces `DefaultMode` masking with reversible tokens for every anonymized column. See [architecture-spec.md](docs/architecture-spec.md#e-reversible-durchsuchbare-tokenisierung-anonymizertokenization-optional). |
| `AnonymizationRules` | Optional central, cross-database rules (`Enabled`, separate `Server`/`Database`/credentials, `TableName`, `CommandTimeoutSeconds`, `CacheTtlSeconds`). See [architecture-spec.md](docs/architecture-spec.md#d-zentrale-anonymisierungsregeln-anonymizationrules-optional). |
| `MetadataProvider` | Optional custom queries and separate database credentials (`Server`, `Database`, `UserId`, `Password`, `IntegratedSecurity`, `CommandTimeoutSeconds`, etc.) for table/column documentation enrichment. |
| `QueryExecution` | `DefaultRowLimit`, `MaxRowLimit`, and `CommandTimeoutSeconds` for `sql_execute_query`; `MaxScriptFileSizeBytes` limits local SQL script files to 10 MB by default. |
| `Logging` | File-based logging root directory, app/error rolling sinks, and the MCP-trail settings. See [Logging](#logging) below. |

### Automatic Migration (`appsettings.json.bak`)

Upon launch, `SqlToAi` automatically inspects the local `appsettings.json` file against the embedded factory default template:
* **New options** are automatically inserted with factory default values.
* **Obsolete options** (removed from the default template in a new release) are pruned.
* **User modifications** (e.g. customized connection strings, whitelist entries, secrets) are strictly preserved.
* **Backup:** If any changes are applied, a backup copy (`appsettings.json.bak`) is created next to the configuration file before saving.

### Credentials

The server picks credentials in this order (first match wins):

1. **Windows Integrated Security** when `SqlServer.IntegratedSecurity` is configured as `true`.
2. **`SqlServer.Server` + `SqlServer.UserId` + `SqlServer.Password`** in `appsettings.json` (when `IntegratedSecurity` is `false`) —
   convenient for local development against a developer SQL Server. If `IntegratedSecurity` is false, `UserId` and `Password` must be explicitly configured, otherwise connection creation throws an exception. All values support environment variable expansion (e.g. `%COMPUTERNAME%\\MSSQLSERVER2022`).

> **Note for shared repos:** the template [`appsettings.json`](src/SqlToAi/appsettings.json) ships with
> a throwaway `Agent/Agent!` login scoped to a single local demo database (`DemoDB`) — replace it with
> your own credentials (ideally via `SQLTOAI_CONNECTION_STRING` or Integrated Security, not a checked-in
> password) before pointing the server at anything beyond local development. If you do check in
> credentials for a local dev server, make sure the repo is private and the login is limited to
> read-only access on a non-production database.

### Recommended Database Permissions

To give the AI agent optimal read-only analysis capabilities without over-granting administrative rights, configure the SQL Server database user with the following permissions:

```sql
USE [TargetDatabase];

-- 1. Read-only data access (for sql_execute_query, sql_compare_queries)
ALTER ROLE [db_datareader] ADD MEMBER [SqlToAiUser];

-- 2. View object DDL definitions for views, procedures, functions & triggers (for sql_get_schema, sql_get_trigger_definition)
GRANT VIEW DEFINITION TO [SqlToAiUser];

-- 3. Execution plan XML analysis (for sql_measure_performance, sql_benchmark_optimization)
GRANT SHOWPLAN TO [SqlToAiUser];

-- 4. Server-wide cumulative DMV-driven missing-index suggestions (for sql_suggest_indexes)
GRANT VIEW SERVER STATE TO [SqlToAiUser];
```

* **`db_datareader`**: Grants read access to table and view data.
* **`VIEW DEFINITION`**: Allows reading `sys.sql_modules` DDL definitions for views, procedures, functions, and triggers. Without this grant, SQL Server hides definition texts for non-owner logins.
* **`SHOWPLAN`**: Enables actual XML execution plan generation (`STATISTICS XML`) and missing index recommendations. If missing, performance measurement tools degrade gracefully to IO/TIME metrics.
* **`VIEW SERVER STATE`**: Server-scoped grant; enables reading the `sys.dm_db_missing_index_*` DMVs required by `sql_suggest_indexes`. If missing, the tool returns a structured permission note instead of a hard error.

### Example `appsettings.json`

```json
{
  "SqlToAi": {
    "Databases": {
      "CacheTtlSeconds": 300,
      "ReadWrite": ["DemoDB"],
      "ReadOnly": ["ReportingDB"],
      "ReadOnlyAnonymized": [],
      "SchemaOnly": []
    },
    "SqlServer": {
      "Server": "%COMPUTERNAME%\\MSSQLSERVER",
      "IntegratedSecurity": true
    },
    "Anonymizer": {
      "Enabled": true,
      "DefaultMode": "ScramblePattern",
      "Tokenization": {
        "Enabled": false,
        "Prefix": "§§§",
        "Suffix": "§§§"
      }
    },
    "AnonymizationRules": {
      "Enabled": false,
      "Server": "central-sql-server",
      "Database": "SqlToAiConfig",
      "UserId": "config_reader",
      "Password": "...",
      "IntegratedSecurity": false,
      "TableName": "dbo.AnonymizationRules",
      "CommandTimeoutSeconds": 30,
      "CacheTtlSeconds": 300
    },
    "MetadataProvider": {
      "Enabled": true,
      "Server": "%COMPUTERNAME%\\MSSQLSERVER",
      "Database": "MyMetadataDatabase",
      "UserId": "Agent",
      "Password": "Agent!",
      "IntegratedSecurity": false,
      "CommandTimeoutSeconds": 30,
      "TableMetadataQuery": "SELECT Description FROM dbo.TableDocs WHERE TableName = @TableName",
      "ColumnMetadataQuery": "SELECT ColumnName, Description FROM dbo.ColumnDocs WHERE TableName = @TableName"
    },
    "QueryExecution": {
      "DefaultRowLimit": 100,
      "MaxRowLimit": 1000,
      "CommandTimeoutSeconds": 30,
      "MaxScriptFileSizeBytes": 10485760
    },
    "Observability": {
      "Enabled": true,
      "EnableToolCallLogging": true,
      "EnableFeedbackTool": true,
      "LogDirectory": null,
      "EnableResponseLogging": true,
      "MaxResponseLength": 0,
      "FeedbackConfirmationMessage": "Feedback recorded. Thank you."
    },
    "Logging": {
      "Directory": "log",
      "AppLog": {
        "Enabled": true,
        "Level": "Information",
        "RollingInterval": "Day",
        "RetainedFileCount": 30
      },
      "ErrorLog": {
        "Enabled": true,
        "Level": "Warning",
        "RollingInterval": "Day",
        "RetainedFileCount": 90
      }
    }
  }
}
```

---

## Getting Started

### 1. Build the Project
Clone the repository and build using the dotnet CLI:
```powershell
dotnet restore "SqlToAi.slnx"
dotnet build "SqlToAi.slnx" -c Release
```

### 2. Configure in your AI Client
Add the following entry to your `mcp.json` configuration in Cursor, Claude Desktop, Windsurf, or any other MCP-compatible client:

```json
{
  "mcpServers": {
    "sql-to-ai": {
      "command": "C:\\Path\\To\\SqlToAi\\src\\SqlToAi\\bin\\Release\\net10.0\\SqlToAi.exe",
      "args": [],
      "env": {
        "SQLTOAI_CONNECTION_STRING": "Data Source=localhost\\MSSQLSERVER;Initial Catalog=MyDemoDatabase;User ID=DbUser;Password=...;TrustServerCertificate=True;Encrypt=False"
      }
    }
  }
}
```

### Local SQL Script Execution (`sql_execute_file`)

The `sql_execute_file` tool accepts five arguments:

* `file_path` (string, required): local `.sql` file path, absolute or relative to the server working directory.
* `database` (string, required): target database name.
* `use_transaction` (boolean, optional, default `true`): use one atomic transaction for `ReadWrite` databases; set to `false` for provider autocommit per batch. `ReadOnly` and `ReadOnlyAnonymized` always use rollback protection.
* `requested_row_limit` (integer, optional): maximum rows returned per `SELECT` batch, subject to the configured server maximum.
* `parameters` (object, optional): typed SQL parameters shared by all batches.

Only local `.sql` files are accepted. Access-level guardrails remain active: `SchemaOnly` and `None` reject execution, `ReadOnlyAnonymized` anonymizes protected string values, and `ReadWrite` applies the requested transaction mode. The result is a structured Markdown report containing script metadata, transaction mode, execution metrics, batch results, and diagnostics.

### 3. Verify a Tool Manually (without an AI Client)
`SqlToAi.exe` also exposes every MCP tool directly on the command line — useful for manually
verifying behavior (e.g. query results, anonymization, exclusions) without going through an LLM.
Running the exe with no arguments (or with `server`) starts the MCP stdio server as before;
`query <tool>` invokes a single tool once and prints its result to stdout, then exits.

```powershell
# List all available tools and their options
.\SqlToAi.exe query --help
.\SqlToAi.exe query sql_execute_query --help

# Invoke a tool directly
.\SqlToAi.exe query sql_list_databases
.\SqlToAi.exe query sql_execute_query --database MyDemoDatabase --query "SELECT TOP 5 * FROM dbo.Customers"
.\SqlToAi.exe query sql_execute_file --file_path .\scripts\report.sql --database MyDemoDatabase --use_transaction false --requested_row_limit 100 --parameters '{"CustomerId":42}'
```

Tool data is written to stdout (pipeable, e.g. into `jq`); any anonymization notice and error
messages are written to stderr. A failed call exits with code `1`.

---

## Development Workflow

Use [`scripts/deploy.ps1`](scripts/deploy.ps1) to build a fresh release: it stops any running
`SqlToAi.exe` instance (so the publish step isn't blocked by a file lock), runs the full test
suite, and publishes a self-contained single-file executable to `publish/`.

```powershell
.\scripts\deploy.ps1
```

> **Reconnect after redeploying:** the MCP stdio transport is a long-lived process per client
> session. If your AI client (Cursor, Antigravity IDE, Claude Desktop, etc.) already has a session
> open against the previous executable, it keeps talking to the old (now-replaced) process handle
> and new tool calls will fail with a "connection closed" error. This is a client-side limitation,
> not a server bug — after every redeploy, reload the MCP server entry in your client, or restart
> the client, to pick up the new executable.

---

## Logging & MCP Observability

Application and error logs live under `{exe-dir}/log/`:

```
log/
├── app/
│   ├── app-2026-07-12.log            # rolling daily, Information+, 30 days retention
│   └── app-2026-07-11.log
└── error/
    └── error-2026-07-12.log          # rolling daily, Warning+, 90 days retention
```

### MCP Observability & Feedback (`RalfHuesing.Mcp.Observability`)

MCP tool executions and agent feedback are logged using `RalfHuesing.Mcp.Observability`. By default, daily JSONL log files are written to `%LOCALAPPDATA%\RalfHuesing\McpObservability\SqlToAi\{yyyy-MM-dd}\` (or the configured `LogDirectory`).

- **Tool Call Logging:** Records duration, success/error status, tool name, sanitized arguments (passwords/keys redacted automatically), and truncated responses.
- **Feedback Tool (`report_observability_feedback`):** Standard tool allowing AI agents to submit feedback, issues, or compliments directly into the observability log.

Configure under `SqlToAi:Observability` in `appsettings.json`:

```json
{
  "SqlToAi": {
    "Observability": {
      "Enabled": true,
      "EnableToolCallLogging": true,
      "EnableFeedbackTool": true,
      "LogDirectory": null,
      "EnableResponseLogging": true,
      "MaxResponseLength": 0,
      "FeedbackConfirmationMessage": "Feedback recorded. Thank you."
    }
  }
}
```

---

## Architecture & Concept Documentation

For more in-depth details about the concept, database schemas, and tool parameters, please refer to:
* **[architecture-spec.md](docs/architecture-spec.md)** - Details on MCP tools, safety mechanisms, anonymization algorithms, and error mapping.

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
