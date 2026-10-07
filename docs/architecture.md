# Architecture

SqlToAi is a .NET 10 / C# 14 process hosting MCP over stdio, with a CLI that dispatches the same SQL tools. It connects to Microsoft SQL Server through `Microsoft.Data.SqlClient`; Dapper is used for metadata queries. It does not expose an HTTP listener. SQL Server connections still use the network when the configured instance is remote.

## Runtime

[Program.cs](../src/SqlToAi/Program.cs) migrates and loads configuration, resolves environment/file values, creates Serilog sinks, registers services and selects server or CLI execution. The server uses the ModelContextProtocol SDK. Stdio is reserved for MCP messages; manual CLI calls send data to stdout and notices/errors to stderr.

`ToolRegistry` defines SQL tool names, schemas and descriptions. `SqlMcpToolRegistrations` creates SDK registrations; `ToolDispatcher` invokes application services and formats MCP results. The CLI builds its commands from the registry. Optional feedback registration comes from the observability package.

[SqlToAi.Exploration](exploration.md) reuses the executable's configuration and service-provider factories to invoke `IToolDispatcher` directly. It retains all inputs, content blocks and failures for manual inspection without starting MCP transport; scenarios do not run as automated tests.

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
| [Cli](../src/SqlToAi/Cli/) | Registry-generated query commands and schema export composition/files |
| [tests](../tests/) | Unit and SQL Server integration tests |
| [scripts](../scripts/) | Publish/deployment and release helpers |
| [sql-scripts](../sql-scripts/) | Fictional schema and supporting SQL setup scripts |

## Lifetimes and state

Application services and the token vault are registered as singletons. Query calls create connections and transactions for their work. Access levels come from bound in-memory lists; central anonymization rules have their own cache. Token mappings exist for the server process lifetime and are lost on restart. Configuration changes require a restart.

Schema discovery returns primary metadata first, with foreign keys, indexes and constraints available through separate tools. Metadata descriptions come from extended properties or configured SQL queries. String result masking is part of query execution, rather than a general middleware applied to all tool output.

`SchemaService.SearchObjectsAsync` and `GetExportObjectsAsync` share the [object catalog query](../src/SqlToAi/Database/SchemaObjectDiscovery.cs). Interactive search retains its name/type filters, ordering and default limit of 100 results. Export discovery has no result limit and returns typed SQL-schema-qualified identities for tables, views, SQL procedures, SQL scalar/inline-table-valued/table-valued functions, and table/view DML triggers with their parent identities. Both entries apply the same database access checks, including support for `SchemaOnly`.

`GetExportTriggerDefinitionAsync` validates the trigger's object ID, SQL schema/name and table/view parent association together, then retrieves its definition by that same ID. It shares definition retrieval and rendering with the existing trigger operation. The MCP trigger operation retains its simple-name definition lookup and its limitation for triggers outside the default schema; the export entry does not change MCP tool contracts or results.

`GetExportSchemaAsync`, `GetExportSchemaForeignKeysAsync` and `GetExportObjectReferencesAsync` use an immutable [SchemaRenderingContext](../src/SqlToAi/Database/SchemaRenderingContext.cs) per call. It carries the typed source object and copies the coordinator's object-to-document path mapping. Schema services remain stateless singletons: their existing MCP entries use the default presentation without a context, preserving enrichment, discovery instructions, columns, notes and errors.

Offline presentation selects the column format before enrichment and never invokes metadata, anonymization-policy or rule providers, including when central rules are enabled. It omits descriptions and the `Anonymized` column. Trigger overviews link by object ID; foreign keys and static referencing entities link from structured schema/name fields. Unknown link targets remain readable text. Relative link targets are URI-escaped using the supplied path mapping. SQL definitions retain their comments and existing unavailable-definition notes; generated navigation contains no MCP invocation instructions. Both presentations share database queries, access checks, failure handling and the Markdown table formatter. CLI orchestration and file output are separate from these rendering entries.

`export-schema` resolves the singleton [SchemaExportService](../src/SqlToAi/Cli/SchemaExportService.cs) directly from the existing service provider and exits after writing UTF-8 Markdown. It composes typed unrestricted discovery, the offline entries, existing SQL-qualified index/constraint/parameter operations and typed trigger definitions. Tables/views collect all available detail sections in their document; routines include parameters, and triggers identify/link their table/view parent. A grouped root overview links every document.

[SchemaExportPaths](../src/SqlToAi/Cli/SchemaExportPaths.cs) supplies one deterministic Windows-safe path mapping for filenames and every relative link, retaining original SQL names in headings. It checks all planned object paths for case-insensitive collisions before writes. The output must be absent or empty. Files use exclusive creation; the first failed service result or filesystem write stops export with a nonzero CLI exit code. Partial output remains, so retries need a new or manually emptied directory. Successful unavailable-definition notes permit exit zero. Unused kind directories are omitted. See [CLI export details](development.md#schema-export) for invocation, included sections and provider exclusions.

These pages describe the current implementation. Historical concepts and implementation plans in `tasks/` are context, not evidence that a behavior is implemented. Build, test and operational commands are in [development.md](development.md).
