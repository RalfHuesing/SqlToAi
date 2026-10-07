# Architecture

SqlToAi is a .NET 10 / C# 14 process hosting MCP over stdio, with a CLI that dispatches the same SQL tools. It connects to Microsoft SQL Server through `Microsoft.Data.SqlClient`; Dapper is used for metadata queries. It does not expose an HTTP listener. SQL Server connections still use the network when the configured instance is remote.

## Runtime

[Program.cs](../src/SqlToAi/Program.cs) migrates and loads configuration, resolves environment/file values, creates Serilog sinks, registers services and selects server or CLI execution. The server uses the ModelContextProtocol SDK. Stdio is reserved for MCP messages; manual CLI calls send data to stdout and notices/errors to stderr.

`ToolRegistry` defines SQL tool names, schemas and descriptions. `SqlMcpToolRegistrations` creates SDK registrations; `ToolDispatcher` invokes application services and formats MCP results. The CLI builds its commands from the registry. Optional feedback registration comes from the observability package.

Query services share `QuerySafetyValidator`. It combines database policy, access-level selection, read-only parsing and statement-count restrictions. Execution and script services control transactions and result serialization. The protection limits are in [security.md](security.md); argument/result contracts are in [tools.md](tools.md).

## Source map

| Location | Responsibility |
| --- | --- |
| [Configuration](../src/SqlToAi/Configuration/) | Options, migration, environment expansion and metadata-query file loading |
| [Mcp](../src/SqlToAi/Mcp/) | Registry, dispatcher, SDK registrations and MCP content |
| [Security](../src/SqlToAi/Security/) | Database exclusions/access levels and ScriptDom read-only guard |
| [Database](../src/SqlToAi/Database/) | Connections, schema, metadata, queries, scripts, comparison, measurements and indexes |
| [Anonymization](../src/SqlToAi/Anonymization/) | String transformations, central policy resolution and process-local token vault |
| [Domain](../src/SqlToAi/Domain/) | Arguments, results, access levels and error catalog |
| [Cli](../src/SqlToAi/Cli/) | Registry-generated CLI commands |
| [tests](../tests/) | Unit and SQL Server integration tests |
| [scripts](../scripts/) | Publish/deployment and release helpers |
| [sql-scripts](../sql-scripts/) | Fictional schema and supporting SQL setup scripts |

## Lifetimes and state

Application services and the token vault are registered as singletons. Query calls create connections and transactions for their work. Access levels come from bound in-memory lists; central anonymization rules have their own cache. Token mappings exist for the server process lifetime and are lost on restart. Configuration changes require a restart.

Schema discovery returns primary metadata first, with foreign keys, indexes and constraints available through separate tools. Metadata descriptions come from extended properties or configured SQL queries. String result masking is part of query execution, rather than a general middleware applied to all tool output.

These pages describe the current implementation. Historical concepts and implementation plans in `tasks/` are context, not evidence that a behavior is implemented. Build, test and operational commands are in [development.md](development.md).
