# SqlToAi

**SqlToAi** is a lightweight, secure, and highly customizable Model Context Protocol (MCP) server for Microsoft SQL Server. It allows AI agents and LLMs (such as Cursor, Windsurf, or Claude Desktop) to interact with databases securely, retrieve schema information, and run read-only queries with on-the-fly string anonymization to protect PII.

Designed specifically for developers analyzing ERP systems and complex database schemas without exposing sensitive customer data.

---

## Key Features

* 🚀 **Stdio-based MCP Host:** Fast, local execution using standard input/output (no HTTP/network setup required).
* 🛡️ **PII Shield (On-the-Fly Anonymization):** Automatically scrambles or hashes all string values in query results (default: ON) to protect customer data while preserving data structure, casing, length, and join logical consistency.
* 🔒 **Schreibschutz (Read-Only Guard):** Regex-based command checking rejects modifying queries (`INSERT`, `UPDATE`, `DROP`, `EXEC`, etc.) inside a rollback transaction. The guard only steps aside for a database whose `AccessCheckSql` explicitly returns `ReadWrite` — every other access level stays read-only, always.
* 🚦 **Safety/Demo Probe Check:** Run a configurable SQL validation query (e.g. `SELECT 1 WHERE DB_NAME() LIKE '%demo%'`) before accessing any database, blocking access to production databases. The probe also controls per-database anonymization (return `ReadOnly` for clear-text, `ReadOnlyAnonymized` for protected access) and, if returned, full write access (`ReadWrite`).
* 🛡️ **Default Anonymization:** Every string column is automatically scrambled with the configured default algorithm unless its name matches an `ExcludedColumns` pattern — no per-column rule maintenance required.
* 📖 **Schema Enrichment (Custom Metadata):** Inject custom business logic or table/column documentation from another database/table via configurable SQL queries directly into the schema results returned to the AI.
* 📋 **Progressive Disclosure Schema Tools:** Exposes optimized tools for schema discovery, triggers, constraints, indexes, routine parameters, and referencing entities (`sys.dm_sql_referencing_entities`), formatted in clean Markdown for the AI.
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
| `Databases` | Static whitelist (`Allowed`/`Blocked`), `AccessCheckSql` for the dynamic permission probe, and `CacheTtlSeconds`. |
| `SqlServer` | Connection parameters (`Server`, `IntegratedSecurity`, `UserId`, `Password`, `CommandTimeoutSeconds`). Values support environment variable interpolation (e.g. `%COMPUTERNAME%`). |
| `Anonymizer` | Master switch (`Enabled`), the algorithm (`DefaultMode`: `ScramblePattern` or `Hash`), and the list of column-name patterns that must NOT be anonymized (`ExcludedColumns`). |
| `MetadataProvider` | Optional custom queries and separate database credentials (`Server`, `UserId`, `Password`, `IntegratedSecurity`, etc.) for table/column documentation enrichment. |
| `QueryExecution` | `DefaultRowLimit` and `MaxRowLimit` for `sql_execute_query`. |
| `Logging` | File-based logging root directory, app/error rolling sinks, and the MCP-trail settings. See [Logging](#logging) below. |

### Credentials

The server picks credentials in this order (first match wins):

1. **Windows Integrated Security** when `SqlServer.IntegratedSecurity` is configured as `true`.
2. **`SqlServer.Server` + `SqlServer.UserId` + `SqlServer.Password`** in `appsettings.json` (when `IntegratedSecurity` is `false`) —
   convenient for local development against a developer SQL Server. If `IntegratedSecurity` is false, `UserId` and `Password` must be explicitly configured, otherwise connection creation throws an exception. All values support environment variable expansion (e.g. `%COMPUTERNAME%\\MSSQLSERVER2022`).

> **Note for shared repos:** the template `appsettings.json` ships without credentials, so a fresh
> clone won't leak anything. If you check in credentials for your local dev server (e.g. a
> throwaway `Agent/Agent!` test login), make sure the repo is private and the credentials are
> limited to read-only access on a non-production database.

### Example `appsettings.json`

```json
{
  "SqlToAi": {
    "Databases": {
      "Default": "MyDemoDatabase",
      "Allowed": ["Demo_*", "TestDb", "Reporting_ReadOnly"],
      "Blocked": ["master", "msdb", "tempdb", "model"],
      "AccessCheckSql": "SELECT CASE WHEN SYSTEM_USER = 'readonly_ai' THEN 'ReadOnly' ELSE 'None' END AS AccessLevel"
    },
    "SqlServer": {
      "Server": "%COMPUTERNAME%\\MSSQLSERVER",
      "IntegratedSecurity": true
    },
    "Anonymizer": {
      "Enabled": true,
      "DefaultMode": "ScramblePattern",
      "ExcludedColumns": ["*Id", "Id", "*Code", "*Type", "Status"]
    },
    "MetadataProvider": {
      "Enabled": true,
      "Server": "%COMPUTERNAME%\\MSSQLSERVER",
      "UserId": "Agent",
      "Password": "Agent!",
      "IntegratedSecurity": false,
      "TableMetadataQuery": "SELECT Description FROM dbo.TableDocs WHERE TableName = @TableName",
      "ColumnMetadataQuery": "SELECT ColumnName, Description FROM dbo.ColumnDocs WHERE TableName = @TableName"
    },
    "QueryExecution": {
      "DefaultRowLimit": 100,
      "MaxRowLimit": 1000
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

## Logging

All log files live next to the executable, under `{exe-dir}/log/`. The layout is:

```
log/
├── app/
│   ├── app-2026-07-12.log            # rolling daily, Information+, 30 days retention
│   └── app-2026-07-11.log
├── error/
│   └── error-2026-07-12.log          # rolling daily, Warning+, 90 days retention
└── mcp/
    └── 2026-07-12/                   # one directory per UTC day
        ├── 14-23-45-1-call.jsonl      # one JSONL file per MCP call
        ├── 14-23-46-2-call.jsonl
        └── ...
```

The MCP trail records every method that crosses the host boundary — `initialize`, `tools/list`,
`tools/call` (and the corresponding responses), plus notifications like `notifications/initialized`.
Each line contains the JSON-RPC `id` (or a generated UUID when the request had none), the method,
the tool name (for `tools/call`), the raw arguments as sent by the LLM, the exact response that was
sent back, the wall-clock duration, and a success flag. **The recorded response is byte-for-byte
what the LLM saw**, including any on-the-fly anonymization — so the trail is a faithful reproduction
of the conversation, not a summary.

Tune the sinks in `appsettings.json` under `SqlToAi:Logging`:

```json
{
  "SqlToAi": {
    "Logging": {
      "Directory": "log",
      "AppLog":   { "Enabled": true, "Level": "Information", "RollingInterval": "Day", "RetainedFileCount": 30 },
      "ErrorLog": { "Enabled": true, "Level": "Warning",    "RollingInterval": "Day", "RetainedFileCount": 90 },
      "McpTrail": { "Enabled": true, "Directory": "mcp", "RetainedDays": 14 }
    }
  }
}
```

The MCP trail retention is enforced at server startup: day directories older than
`McpTrail.RetainedDays` are deleted in full. The app and error logs use Serilog's built-in
`retainedFileCountLimit`, so they are pruned continuously as new files roll in.

To inspect the trail quickly:

```powershell
# Latest 5 calls
Get-ChildItem log\mcp\ -Recurse -File | Sort-Object LastWriteTime -Desc | Select-Object -First 5 | Get-Content

# All calls for a specific tool
Select-String -Path log\mcp\**\*.jsonl -Pattern '"tool":"sql_execute_query"'

# Failed calls only
Select-String -Path log\mcp\**\*.jsonl -Pattern '"success":false'
```

---

## Architecture & Concept Documentation

For more in-depth details about the concept, database schemas, and tool parameters, please refer to:
* **[mcp-specification.md](docs/mcp-specification.md)** - Details on MCP tools, safety mechanisms, anonymization algorithms, and error mapping.

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
