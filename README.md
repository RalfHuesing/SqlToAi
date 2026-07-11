# SqlToAi

**SqlToAi** is a lightweight, secure, and highly customizable Model Context Protocol (MCP) server for Microsoft SQL Server. It allows AI agents and LLMs (such as Cursor, Windsurf, or Claude Desktop) to interact with databases securely, retrieve schema information, and run read-only queries with on-the-fly string anonymization to protect PII.

Designed specifically for developers analyzing ERP systems and complex database schemas without exposing sensitive customer data.

---

## Key Features

* 🚀 **Stdio-based MCP Host:** Fast, local execution using standard input/output (no HTTP/network setup required).
* 🛡️ **PII Shield (On-the-Fly Anonymization):** Automatically scrambles or hashes all string values in query results (default: ON) to protect customer data while preserving data structure, casing, length, and join logical consistency.
* 🔒 **Schreibschutz (Read-Only Guard):** Configurable read-only transaction execution and regex command checking to prevent the AI from executing modifying queries (`INSERT`, `UPDATE`, `DROP`, etc.).
* 🚦 **Safety/Demo Probe Check:** Run a configurable SQL validation query (e.g. `SELECT 1 WHERE DB_NAME() LIKE '%demo%'`) before accessing any database, blocking access to production databases.
* 📖 **Schema Enrichment (Custom Metadata):** Inject custom business logic or table/column documentation from another database/table via configurable SQL queries directly into the schema results returned to the AI.
* 📋 **Progressive Disclosure Schema Tools:** Exposes optimized tools for schema discovery, triggers, constraints, indexes, routine parameters, and referencing entities (`sys.dm_sql_referencing_entities`), formatted in clean Markdown for the AI.

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
| `Databases` | Static whitelist (`Allowed`/`Blocked`), default database, `AccessCheckSql` for the dynamic permission probe, and `CacheTtlSeconds`. |
| `SqlDatabase` | Connection parameters (`Server`, `DefaultDatabase`, `CommandTimeoutSeconds`, `ReadOnly`). Credentials are intentionally not loaded from here — see below. |
| `Anonymizer` | Enables PII string scrambling, defines per-pattern rules and excluded columns. |
| `MetadataProvider` | Optional custom queries for table/column documentation enrichment. |
| `QueryExecution` | `DefaultRowLimit` and `MaxRowLimit` for `sql_execute_query`. |

### Credentials

The server picks credentials in this order (first match wins):

1. **`SQLTOAI_CONNECTION_STRING` environment variable** — full ADO.NET connection string. **Use this
   for shared and production setups** so credentials never end up in source control or in a committed
   config file.
2. **`SqlDatabase.Server` + `SqlDatabase.UserId` + `SqlDatabase.Password`** in `appsettings.json` —
   convenient for local development against a developer SQL Server. The server still requires at
   least `Server` to be set; if it is empty, startup fails with a clear error.
3. **Windows Integrated Security** if `UserId`/`Password` are empty and the process runs as a
   domain user with access to the SQL Server.

**`SQLTOAI_CONNECTION_STRING` always wins when set** — that lets a developer override the file
without editing it:

```powershell
# Production / shared
$env:SQLTOAI_CONNECTION_STRING = "Data Source=prod-sql;Initial Catalog=Reporting;User ID=svc_sqltoai;Password=...;TrustServerCertificate=False;Encrypt=True"

# Local dev (uses the values from appsettings.json)
dotnet run --project src/SqlToAi
```

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
      "AccessCheckSql": "SELECT CASE WHEN SYSTEM_USER = 'readonly_ai' THEN 'ReadData' ELSE 'None' END AS AccessLevel"
    },
    "SqlDatabase": {
      "Server": "localhost\\MSSQLSERVER",
      "DefaultDatabase": "MyDemoDatabase",
      "ReadOnly": true
    },
    "Anonymizer": {
      "Enabled": true,
      "DefaultMode": "ScramblePattern",
      "Rules": [
        { "Pattern": "*name*", "Mode": "ScramblePattern" },
        { "Pattern": "*mail*", "Mode": "ScramblePattern" }
      ],
      "ExcludedColumns": ["*Id", "Id", "*Code", "*Type", "Status"]
    },
    "MetadataProvider": {
      "Enabled": true,
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

## Architecture & Concept Documentation

For more in-depth details about the concept, database schemas, and tool parameters, please refer to:
* **[mcp-specification.md](docs/mcp-specification.md)** - Details on MCP tools, safety mechanisms, anonymization algorithms, and error mapping.

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
