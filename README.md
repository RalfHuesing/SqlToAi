# SqlToAi

SqlToAi is a local Model Context Protocol (MCP) server for Microsoft SQL Server.
It gives MCP clients tools to inspect schemas, execute SQL, and compare query
results and performance. The server runs over stdio and is built with .NET 10.

## Capabilities

- Inspect tables, views, routines, triggers, keys, indexes, and object references.
- Execute parameterized queries and local `.sql` scripts, including `GO` batches.
- Measure CPU time, elapsed time, reads, and execution plan warnings; compare a
  baseline query with a candidate and inspect missing-index suggestions.
- Configure database access levels, from schema-only access to read/write access.
- Mask string query results or replace them with reversible in-memory tokens;
  add table and column descriptions from SQL Server or a metadata database.

See the [tool reference](docs/tools.md) for arguments and behavior.

## Getting started

You need the .NET 10 SDK, a reachable SQL Server, and a database login with the
permissions required by the tools you use.

### Build

```powershell
dotnet build SqlToAi.slnx -c Release
```

The Windows executable and configuration are written to
`src/SqlToAi/bin/Release/net10.0/`.

### Configure database access

Edit `appsettings.json` beside the executable before starting it. The
[shipped template](src/SqlToAi/appsettings.json) contains a local demo login
(`Agent` / `Agent!`) and grants `ReadWrite` access to `DemoDB`. Replace those
settings with your server and access lists.

For Windows authentication and string masking, replace the `Databases` and
`SqlServer` sections with the following, substituting your database and server:

```json
"Databases": {
  "CacheTtlSeconds": 300,
  "ReadWrite": [],
  "ReadOnly": [],
  "ReadOnlyAnonymized": ["ReportingDB"],
  "SchemaOnly": []
},
"SqlServer": {
  "Server": "localhost\\MSSQLSERVER2022",
  "IntegratedSecurity": true,
  "ConnectTimeoutSeconds": 30
}
```

These sections belong inside the existing `SqlToAi` object. Keep
`Anonymizer.Enabled` set to `true`. Databases absent from the access lists are
denied. Use a SQL Server login with restricted permissions as well as the
application access lists.

See [configuration](docs/configuration.md) for SQL authentication, environment
overrides, metadata, logging, and automatic configuration migration.

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
.\SqlToAi.exe query sql_list_databases
.\SqlToAi.exe query sql_execute_query --database ReportingDB --query "SELECT TOP 5 name FROM sys.tables"
```

Tool results contain Markdown or JSON. Successful CLI calls write data to stdout;
notices and errors go to stderr. Failed calls exit with code `1`. See
[development and CLI usage](docs/development.md) for export and deployment.

## Protection and limits

Read-only execution uses SQL statement validation and rollback transactions.
`ReadWrite` access permits changes. These guards complement SQL Server
permissions; they do not replace them.

String masking applies to query and script results for `ReadOnlyAnonymized`
databases. Numeric values, dates, schema definitions, query-comparison difference
samples, and execution plan content can remain visible. Tokens are reversible
within the running server. This is not a guarantee that all sensitive data is
removed.

Query comparison checks schema, row counts, and distinct set differences. It
does not establish row ordering or duplicate multiplicity. Benchmark results
describe the measured executions and do not guarantee a production improvement.

Tool-call and response logging is enabled in the shipped configuration. SQL
arguments and results can contain sensitive data. Review the
[security boundaries](docs/security.md) and logging settings before use.

## Documentation and development

- [Documentation index](docs/README.md)
- [Configuration and operation](docs/configuration.md)
- [Security and data handling](docs/security.md)
- [Tool reference](docs/tools.md)
- [Architecture](docs/architecture.md)
- [Build, tests, CLI, and deployment](docs/development.md)

Run the xUnit v3 test suite with `dotnet test SqlToAi.slnx`.

## License

[MIT](LICENSE).
