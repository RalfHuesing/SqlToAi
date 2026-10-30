# SqlToAi

SqlToAi is a local Model Context Protocol (MCP) server for Microsoft SQL Server.
It gives MCP clients tools to inspect schemas, execute SQL, and compare query
results and performance. The server runs over stdio and is built with .NET 10.

## Capabilities

- Inspect tables, views, routines, triggers, keys, indexes, and object references.
- Export database schema as linked Markdown files for offline reading.
- Execute parameterized queries and local `.sql` scripts, including `GO` batches.
- Stream query results directly into local CSV or JSONL files with compact metadata responses.
- Measure CPU time, elapsed time, reads, and execution plan warnings; compare a
  baseline query with a candidate and inspect missing-index suggestions.
- Configure database access levels, from schema-only access to read/write access.
- Mask string query results or replace them with reversible in-memory tokens;
  add table and column descriptions from SQL Server or a metadata database.

See the [tool reference](docs/tools.md) for arguments and behavior.

Export schema without starting the MCP server:

```powershell
.\SqlToAi.exe export-schema --database primary/DemoDB --output C:\Doku\Database
```

The output directory must be absent or empty. The export contains schema details
and SQL definitions, without business records, metadata descriptions or
anonymization indicators. A failure returns a nonzero exit code and leaves any
files already written. See [schema export](docs/development.md#schema-export)
for file layout and retry behavior.

## Getting started

For a downloaded Windows x64 package, follow [manual package setup](SETUP.md)
(included as `README.md` beside the executable). Choose the ZIP to extract and launch
the executable yourself, or the MCPB to install the same executable as a client
launch entry. Both are self-contained and need no separate .NET runtime. Before
either route can start the server, create `%LOCALAPPDATA%\SqlToAi\appsettings.json`
manually from the supplied template; bundle installation does not create it.
Updates preserve the separate user settings and logs, but upgrading from the old
single-server settings is a breaking change: manually move old sections and
provider settings under a named server, qualify database grants, and rename
environment keys before the first new startup. There is no automatic migration;
follow the [upgrade instructions](SETUP.md#upgrading-from-single-server-settings).

You need the .NET 10 SDK, a reachable SQL Server, and a database login with the
permissions required by the tools you use.

### Build

```powershell
dotnet build SqlToAi.slnx -c Release
```

The Windows executable and configuration are written to
`src/SqlToAi/bin/Release/net10.0/`.

### Configure database access

Before starting SqlToAi, create `%LOCALAPPDATA%\SqlToAi\appsettings.json`
manually from the [shipped template](src/SqlToAi/appsettings.json). The main
template has no database grants or working credentials; set the server,
credentials, and access lists deliberately. The separate
[DemoDB example](src/SqlToAi/demo-profile.json) contains fictional
local demo credentials and `ReadWrite` access. Select it manually for a local
test database only; the host does not load it automatically.

For Windows authentication and string masking, configure the `Databases` and
`Servers` sections with the following, substituting your database and server:

```json
"Databases": {
  "CacheTtlSeconds": 300,
  "ReadWrite": [],
  "ReadWriteAnonymized": [],
  "ReadOnly": [],
  "ReadOnlyAnonymized": ["primary/ReportingDB"],
  "SchemaOnly": []
},
"Servers": {
  "primary": {
    "Server": "localhost\\MSSQLSERVER2022",
    "IntegratedSecurity": true,
    "ConnectTimeoutSeconds": 30
  }
}
```

These sections belong inside the existing `SqlToAi` object. Keep
`Anonymizer.Enabled` set to `true`. Databases absent from the access lists are
denied. Use a SQL Server login with restricted permissions as well as the
application access lists.

See [configuration](docs/configuration.md) for manual setup, SQL authentication,
per-field environment overrides, metadata, and logging.

### Connect an MCP client

Add a stdio server entry to your client's MCP configuration, using the absolute
path to your executable. The exact configuration location depends on the client.

```json
{
  "mcpServers": {
    "sql-to-ai": {
      "command": "C:\\Path\\To\\SqlToAi\\src\\SqlToAi\\bin\\Release\\net10.0\\SqlToAi.exe",
      "args": []
    }
  }
}
```

### Check tools from the command line

```powershell
Set-Location src/SqlToAi/bin/Release/net10.0
.\SqlToAi.exe query --help
.\SqlToAi.exe query sql_list_databases --server primary
.\SqlToAi.exe query sql_execute_query --database primary/ReportingDB --query "SELECT TOP 5 name FROM sys.tables"
```

Tool results contain Markdown or JSON. Successful CLI calls write data to stdout;
notices and errors go to stderr. Failed calls exit with code `1`. See
[development and CLI usage](docs/development.md) for export and deployment.

## Protection and limits

Read-only execution uses SQL statement validation and rollback transactions.
`ReadWrite` permits changes with raw string results; `ReadWriteAnonymized` permits
changes while applying the configured string masking or tokenization to query and
script results. These application checks complement SQL Server permissions; they
do not replace them.

String masking applies to query, script, and data export results for `ReadOnlyAnonymized` and
`ReadWriteAnonymized` databases. Numeric values, dates, schema definitions,
query-comparison difference
samples, and execution plan content can remain visible. Tokens are reversible
within the running server. This is not a guarantee that all sensitive data is
removed.

Query comparison checks schema, row counts, and distinct set differences. It
does not establish row ordering or duplicate multiplicity. Benchmark results
describe the measured executions and do not guarantee a production improvement.

Application and error diagnostics remain enabled. Review the [security boundaries](docs/security.md)
and logging settings before use.

## Documentation and development

- [Documentation index](docs/README.md)
- [Configuration and operation](docs/configuration.md)
- [Security and data handling](docs/security.md)
- [Tool reference](docs/tools.md)
- [Architecture](docs/architecture.md)
- [Build, tests, CLI, and deployment](docs/development.md)
- [Support and contribution guidance](docs/development.md#support-and-contributions)
- [Security reporting](SECURITY.md)

Run the xUnit v3 test suite with `dotnet test SqlToAi.slnx`.

## License

[MIT](LICENSE).
