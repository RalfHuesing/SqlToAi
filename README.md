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
* **Logging:** Serilog
* **Testing:** xUnit v3

---

## Configuration (`appsettings.json`)

The server is configured using a standard JSON file. Below is an example:

```json
{
  "SqlDatabase": {
    "Server": "localhost\\MSSQLSERVER",
    "UserId": "DbUser",
    "Password": "SecretPassword",
    "DefaultDatabase": "MyDemoDatabase",
    "ExcludedDatabases": ["master", "tempdb", "model", "msdb"],
    "CommandTimeoutSeconds": 30,
    "EnforceSafetyCheck": true,
    "SafetyCheckSql": "SELECT 1 WHERE DB_NAME() LIKE '%demo%' OR DB_NAME() LIKE '%test%'",
    "ReadOnly": true
  },
  "Anonymizer": {
    "Enabled": true,
    "Mode": "ScramblePattern",
    "ExcludedColumns": ["Id", "Code", "Type"]
  },
  "MetadataProvider": {
    "Enabled": true,
    "ConnectionString": "",
    "TableMetadataQuery": "SELECT Description FROM dbo.TableDocs WHERE TableName = @TableName",
    "ColumnMetadataQuery": "SELECT ColumnName, Description FROM dbo.ColumnDocs WHERE TableName = @TableName"
  }
}
```

---

## Getting Started

### 1. Build the Project
Clone the repository and build using the dotnet CLI:
```bash
dotnet restore "SqlToAi.slnx"
dotnet build "SqlToAi.slnx" -c Release
```

### 2. Configure in your AI Client
Add the following entry to your `mcpControllers` or `mcp.json` configuration in Cursor or Claude Desktop:

```json
{
  "mcpServers": {
    "sql-to-ai": {
      "command": "C:\\Path\\To\\SqlToAi\\bin\\Release\\net10.0\\SqlToAi.exe",
      "args": [],
      "env": {}
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
