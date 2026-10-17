# Architecture

SqlToAi is a .NET 10 / C# 14 process hosting MCP over stdio, with a CLI that dispatches the same SQL tools. It connects to Microsoft SQL Server through `Microsoft.Data.SqlClient`; Dapper is used for metadata queries. It does not expose an HTTP listener. SQL Server connections still use the network when the configured instance is remote.

## Runtime

[Program.cs](../src/SqlToAi/Program.cs) loads required user configuration through [UserConfiguration](../src/SqlToAi/Configuration/UserConfiguration.cs), resolves environment/file values, creates Serilog sinks, registers services and selects server or CLI execution. The shared loading boundary requires `%LOCALAPPDATA%\SqlToAi\appsettings.json` before services, logging or transport start. Missing, invalid or unreadable settings return exit code `1` with an actionable direct stderr diagnostic; startup does not create, import, rewrite or back up configuration. See [manual setup and loading precedence](configuration.md). The server uses the ModelContextProtocol SDK. Stdio is reserved for MCP messages; manual CLI calls send data to stdout and notices/errors to stderr.

`ToolRegistry` defines SQL tool names, schemas and descriptions. `SqlMcpToolRegistrations` creates SDK registrations; `ToolDispatcher` invokes application services and formats MCP results. The CLI builds its commands from the registry.

Registered SDK input schemas come from the same registry, including required
server and database fields. The SDK delegates bind these fields as nullable raw
JSON so absent or invalid shapes reach the dispatcher's catalog error handling
instead of producing a generic SDK invocation error. This does not make the
public fields optional or introduce implicit server selection.

The selected user root follows configuration through direct options, DI post-configuration, logging and retention. Relative metadata and logging paths resolve there once per options instance; DI binds its own instance against the same root. The loading boundary checks every relevant JSON object before source merging; direct and bound options share effective validation after resolution. Default application and error logs reside under `%LOCALAPPDATA%\SqlToAi\log\`. Explicit absolute paths remain supported; relative SQL input and CLI schema-export output still use CWD; MCP export requires a fully qualified Windows path. Root and logging failures have direct stderr diagnostics independent of file sinks. See [path and logging details](configuration.md#logs).

[SqlToAi.Exploration](exploration.md) reuses the executable's configuration and service-provider factories to invoke `IToolDispatcher` directly. It retains all inputs, content blocks and failures for manual inspection without starting MCP transport; scenarios do not run as automated tests.

Query services share `QuerySafetyValidator`. It combines database policy, access-level selection, read-only parsing and statement-count restrictions. Execution and script services control transactions and result serialization. The protection limits are in [security.md](security.md); argument/result contracts are in [tools.md](tools.md).

The named-server architecture resolves `alias/database` through
`DatabaseTargetResolver` into an immutable `DatabaseTargetContext`. The factory's
`CreateTargetConnection` uses that alias's endpoint and the exact raw database as
InitialCatalog. `IsTargetAllowed`, `GetTargetAccessLevelAsync` and the validator's
`ValidateTargetQuerySafetyAsync` / `ValidateTargetBatchSafetyAsync` evaluate the
same context; successful safety results retain it in `Target`. These paths use
qualified global grants, server-local raw-name exclusions and default deny.
`CreateCatalogConnection` separately requires a configured alias and may use the
login's default database for discovery. `PrimaryConnection.OpenTargetAsync` and
`OpenCatalogAsync` retain safe diagnostics identifying `SqlToAi:Servers:<alias>`.
Schema export resolves its target once at the export entry, including an empty
catalog. Context overloads on `ISchemaService` carry that same instance through
discovery, selection, primary rendering, trigger definitions and every detail
section. Direct string-based schema APIs resolve and delegate to those same
implementations. Policy checks remain in the context-based operations before SQL.
Optimization benchmarks likewise resolve once and pass the identical context to
comparison and both measurements; their direct string APIs share the same cores.
The safety validator accepts resolved contexts only and does not parse targets.

All tools, CLI commands, and internal services operate exclusively on qualified targets;
obsolete single-server contracts and fallback behaviors have been completely removed.

## Source map

| Location | Responsibility |
| --- | --- |
| [Configuration](../src/SqlToAi/Configuration/) | Required user configuration, options, environment expansion and metadata-query file loading |
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

The internal [SchemaObjectSelection](../src/SqlToAi/Database/SchemaObjectSelection.cs) boundary normalizes JSON scalar/list selections in scalar-first input order, trimming surrounding whitespace and ignoring null or empty entries. It rejects incorrect types and requires a remaining name unless empty selection is explicitly allowed. `SchemaService.ResolveObjectsAsync` checks database access before querying the shared discovery catalog. Selection accepts one-part or SQL-schema-qualified identifiers, including SQL quoting; cross-database names are rejected. Name and schema equality are evaluated by SQL Server against its catalog, preserving database identifier comparison semantics. Unqualified names must match exactly one catalog object. Each input retains its own result or catalog error, while repeated identifiers reuse lookups and aliases share the resolved object by object ID. Primary-schema selection supports tables, views and SQL routines; export selection also supports table/view DML triggers. The MCP/CLI batch path uses this boundary; scalar schema lookup retains its existing resolution behavior.

`SchemaService.GetSchemaBatchAsync` is exposed through `ISchemaService` and the
existing `sql_get_schema` tool. Registry and SDK registration share the primary
schema description and nullable selection schema. The SDK binds selections as
nullable JSON elements, preserving argument shapes for service validation. The
dispatcher selects the scalar Markdown path for absent/null `object_names`
and batch composition for supplied arrays, mapping `Complete` to MCP `isError`.
The generated CLI parses one JSON array/null token for `--object_names`.
The service validates normalized input count and identifying omission metadata
before opening a connection, then checks database access and resolves the
whole selection. It uses the same interactive renderers as scalar inspection and
reuses schema/error fetches by resolved object ID within the request. Each input,
including aliases and duplicates, retains an ordered result. Object-specific errors
allow subsequent siblings; validation, access and catalog/connection failures fail
the request, and cancellation propagates.

[SchemaBatchComposer](../src/SqlToAi/Database/SchemaBatchComposer.cs) writes one JSON
envelope containing `complete` and `results`. Each result identifies `object_name`
and `status`, plus `markdown`, a catalog `error` (`code`, `message`), or omission
`reason`. `complete` is true only when every entry is `ok`. Its retained serialized
text is the exact text measured against the [batch byte budget](configuration.md#schema-batch-limits),
including JSON escaping and all pending omission metadata. When a complete schema
or error no longer fits, it marks that input and every remaining input
`not_returned`/`response_limit`, retaining earlier results. No schema/error text is
truncated and no continuation state is created. Foreign-key, index, constraint and
routine-parameter details remain separate requests.

For batch table/view enrichment, typed identities keep SQL lookup names separate
from metadata/rule names. Native descriptions use the safely quoted qualified name;
custom metadata queries receive the raw base object name. Anonymization policies
receive the resolved SQL schema and object names, so quoted names, literal dots and
escaped brackets do not change explicit exemptions. All enrichment and rule lookup calls carry the resolved server alias and raw database name (`DatabaseTargetContext`), so each alias uses its own provider settings and rule-cache partition. Scalar inspection and offline export use these same target-aware paths.

`GetExportTriggerDefinitionAsync` validates the trigger's object ID, SQL schema/name and table/view parent association together, then retrieves its definition by that same ID. It shares definition retrieval and rendering with the existing trigger operation. The MCP trigger operation retains its simple-name definition lookup and its limitation for triggers outside the default schema; the export entry does not change MCP tool contracts or results.

The export schema, foreign-key, index, constraint, parameter and referencing-entity entries use an immutable [SchemaRenderingContext](../src/SqlToAi/Database/SchemaRenderingContext.cs) per call. It carries the typed source object. The coordinator snapshots its object-to-document path mapping and lookup indexes once per non-empty export; source-specific contexts share that immutable snapshot. Schema services remain stateless singletons: their existing MCP entries use the default presentation without a context, preserving enrichment, discovery instructions, columns, notes and errors.

Offline presentation selects the column format before enrichment and never invokes metadata, anonymization-policy or rule providers, including when central rules are enabled. It omits descriptions and the `Anonymized` column. Trigger overviews link by object ID; foreign keys and static referencing entities link from structured schema/name fields. Unknown link targets remain readable text. Relative link targets are URI-escaped using the supplied path mapping. SQL definitions retain their comments and existing unavailable-definition notes; generated navigation contains no MCP invocation instructions. Both presentations share database queries, access checks, failure handling and the Markdown table formatter. CLI orchestration and file output are separate from these rendering entries.

Structured offline names in headings, navigation, detail tables and unavailable/empty-result notes use the shared [OfflineSqlNameFormatter](../src/SqlToAi/Database/OfflineSqlNameFormatter.cs) before Markdown composition. It makes control characters visible, escapes literal text and chooses code-span delimiters that retain backticks and entity-like names. SQL identities, lookup parameters and definition text remain unchanged; no rendered Markdown is parsed or rewritten.

`export-schema` resolves the singleton [SchemaExportService](../src/SqlToAi/Cli/SchemaExportService.cs) directly from the existing service provider and exits after writing UTF-8 Markdown. It composes typed unrestricted discovery, the offline entries and typed trigger definitions. Tables/views collect all available detail sections in their document; routines include parameters, and triggers identify/link their table/view parent. A grouped root overview links every document.

The same singleton coordinator serves `sql_export_schema` through the dispatcher,
SDK registration and registry-generated query CLI, with explicit selected/all scope.
The MCP boundary validates fully qualified Windows destinations, rejects device
namespaces and returns compact JSON outcomes without file contents. The original
`export-schema` CLI retains relative paths and full-catalog behavior.
`ISchemaService.GetSelectedExportObjectsAsync` reuses the shared SQL identifier
resolver with export object kinds and database access checks, resolves the whole
selection, rejects any unknown, ambiguous or unsupported entry and deduplicates
successful identities by object ID. Normalization and resolution finish before
path mapping or output creation. Only selected objects enter the mapping; references
and trigger parents outside it remain readable names. There is no recursive
expansion, batch response limit or interactive enrichment.

The coordinator returns a typed outcome with completed, failed or cancelled
status, its resolved destination when path resolution succeeded, and a catalog
error or cancellation reason. It checks cancellation after awaited operations,
including services that convert cancellation into a failed result, and before
reporting completion. Failed/cancelled outcomes inspect retained files and child
directories, including incomplete files, to determine partial output; an empty
root alone is false. Completed outcomes set partial output to false. If an
inspection fails, previously observed creation is retained as evidence. Output
is never removed automatically. The CLI export adapter preserves its full-export
result and cancellation behavior and accepts paths relative to CWD.

[SchemaExportPaths](../src/SqlToAi/Cli/SchemaExportPaths.cs) supplies one deterministic Windows-safe path mapping for filenames and every relative link, retaining original SQL names in headings. It checks all planned object paths for case-insensitive collisions before writes. The output must be absent or empty. Files use exclusive creation; the first failed service result or filesystem write stops export with a nonzero CLI exit code. Partial output remains, so retries need a new or manually emptied directory. Successful unavailable-definition notes permit exit zero. Unused kind directories are omitted. See [CLI export details](development.md#schema-export) for invocation, included sections and provider exclusions.

These pages describe the current implementation. Historical concepts and implementation plans in `tasks/` are context, not evidence that a behavior is implemented. Build, test and operational commands are in [development.md](development.md).
