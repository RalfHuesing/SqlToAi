---
status: draft
---

# Database schema export to Markdown

## Intention

Export the schema and SQL definitions of one configured SQL Server database into a directory of Markdown files. An LLM agent can read these files offline and follow references from separate domain documentation to specific database objects. The files represent the database as read during export; retaining a particular application or database version is the responsibility of the consuming project.

Reuse SqlToAi's existing database access, schema queries, and Markdown rendering. The new functionality coordinates existing schema operations and writes files; it does not establish another schema discovery or documentation engine.

## Scope

### Must

- Add a CLI subcommand with exactly two required inputs: database name and output directory. Use the existing application configuration, connection setup, dependency injection, logging, and access checks.
- Produce UTF-8 Markdown, with one file per exported object and SQL-schema-qualified names in the content. Include foreign keys, indexes, and constraints in the corresponding table/view file rather than creating separate files for these details.
- Export tables, views, SQL stored procedures, SQL scalar/table-valued functions, and table/view DML triggers using the existing schema services. Retrieve all objects of those kinds without applying the interactive search result limit.
- Include the actual available SQL definitions for views, routines, and triggers, preserving SQL comments within those definitions. Do not interpret, summarize, or strip the SQL text.
- Provide a small `README.md` linking to object files, grouped by kind. This is navigation for an offline reader, not an export report.
- Make generated content usable without MCP access. Schema detail sections and routine parameters must be present in the files; discovery instructions to invoke MCP tools must be omitted or replaced with local section/file references through shared rendering, not by parsing and rewriting rendered Markdown.
- Use stable, filesystem-safe filenames derived from SQL schema and object name. Apply deterministic escaping for Windows-invalid characters and reserved filenames. Before writing any files, check all planned output paths for collisions using Windows case-insensitive comparison; abort on a collision instead of overwriting or choosing order-dependent names. Files and links must use the same name-to-path mapping; headings and the overview retain the original SQL object names.
- Create the output directory when absent. Require an empty directory before writing; fail if it already contains files or directories. No overwrite or cleanup switches.
- Stop on the first access, query, or file-writing error and return a nonzero exit code using existing error handling. Already written files remain; there is no filesystem rollback or automatic cleanup. A repeat run requires a new or manually emptied output directory.
- A successful export returns exit code zero. If a shared schema operation returns its existing unavailable-definition note as a successful result, include that note and continue; it does not by itself fail the export. An actual failed service result must still abort. No separate completeness evaluation is added.
- Preserve the existing `server` and `query` commands and the current MCP tool behavior.
- Omit table/column descriptions sourced from metadata and metadata enrichment from export. Skip the metadata-provider calls through a small shared-renderer adjustment; do not introduce a separate renderer or remove descriptions by parsing Markdown. This exclusion does not apply to comments embedded in the original SQL definitions.

### Not

- Example records, row sampling, generated demo data, or querying business data.
- Snapshot identities, version parameters, timestamps, fingerprints, manifests, or version management.
- Completeness reports, export audit frameworks, object count reconciliation, or verification of a restorable database dump.
- Metadata-sourced descriptions, extended-property enrichment, and custom metadata-provider queries in export.
- Reconstructing table `CREATE` scripts or extending the existing schema detail coverage with additional SQL Server features.
- SQL Server object kinds beyond tables, views, SQL routines, and table/view DML triggers; server-level objects and DDL triggers are excluded.
- New dependency analysis, dependency graphs, resolution of dynamic SQL, or exporting other databases referenced by definitions.
- LLM calls, business-domain inference, or generation/modification of the consuming project's domain documentation.
- JSON/YAML export contracts, selectable formats, filters, export profiles, incremental exports, or a plugin architecture.
- A new MCP export tool, another executable, or a client connection to the running MCP server.

## CLI and files

Invocation:

```powershell
.\SqlToAi.exe export-schema --database DemoDB --output C:\Doku\Database
```

The command runs in the existing executable and exits after export. It calls shared application services directly, without starting the MCP stdio server or invoking tools through the protocol.

File layout:

```text
Database/
  README.md
  tables/
    dbo.Customer.md
    dbo.Order.md
  views/
    dbo.OrderOverview.md
  procedures/
    dbo.CreateOrder.md
  functions/
    dbo.CalculatePrice.md
  triggers/
    dbo.OrderAfterInsert.md
```

Tables and views contain the existing column schema, incoming/outgoing foreign keys, indexes, constraints, and static referencing entities. Views additionally contain their SQL definition. SQL procedures and functions contain their definition and existing parameter details. Each DML trigger has a separate definition file identifying its parent table/view; the parent's trigger overview links to that file. Empty object-kind directories are unnecessary.

Technical fields follow the existing renderers' coverage. This export is a readable schema reference, not a schema migration script. All emitted file links are relative.

## Existing infrastructure and reuse

The following implementation points were inspected for this concept:

- [Program.cs](../../src/SqlToAi/Program.cs): `BuildRootCommand` builds `server` and `query` with `System.CommandLine`; startup already supplies configuration and a shared service provider. Register the new command in that tree.
- [ToolCommandFactory.cs](../../src/SqlToAi/Cli/ToolCommandFactory.cs): generates the existing `query <tool>` commands from the registry. Keep that mechanism for single-tool invocation; the export coordinates several service operations and does not require artificial tool registration.
- [ISchemaService.cs](../../src/SqlToAi/Database/ISchemaService.cs) and [SchemaService.cs](../../src/SqlToAi/Database/SchemaService.cs): existing schema operations perform database access checks, use the configured connection factory, and return Markdown through the established result/error types.
- [TableSchemaRenderer.cs](../../src/SqlToAi/Database/TableSchemaRenderer.cs): already renders column schemas, trigger overviews, view definitions, and SQL routine definitions. Reuse it; adapt only the shared rendering behavior needed for offline output and omission of descriptions and metadata-provider calls.
- [DetailSchemaRenderer.cs](../../src/SqlToAi/Database/DetailSchemaRenderer.cs): existing detail operations supply foreign keys, indexes, constraints, trigger definitions, referencing entities, and routine parameters. Compose their results into the corresponding files.

`SearchObjectsAsync` currently executes a limited object search and returns a Markdown table. It does not expose an unrestricted typed object list or trigger-parent associations. Extract/reuse the object discovery query in the schema layer with a small typed result containing the identifiers needed by both callers. Keep the interactive search's limit and Markdown output intact. The export must not parse Markdown tables for object identity, copy the catalog query into its own layer, or simulate an unrestricted search using an arbitrary large limit.

New behavior is limited to the CLI entry, export coordination, filesystem output, and the small shared discovery/rendering adjustments. No general exporter framework or second set of catalog queries/renderers is needed.

## Verification

- Exercise CLI input validation and file output with focused tests: required arguments, empty-directory handling, filename collisions, and propagation of service/file errors.
- Verify Windows-invalid/reserved names and case collisions, the shared path mapping used by relative links, and collision rejection before any file is written.
- Verify that unavailable-definition notes permit success, while actual service/file failures stop the export with a nonzero exit code; check the specified partial-output and repeat-run behavior.
- Verify composition using known shared-service results: table details stay in the table file, definitions and routine parameters are included, and trigger files identify their parents.
- Use a known object set covering every supported kind, including scalar, inline table-valued, and table-valued SQL functions. Include more objects than the interactive search's default limit and check that every expected file is generated. These are implementation tests, not a runtime completeness report.
- Verify that offline files do not instruct the reader to invoke MCP tools and that existing MCP output keeps its current behavior.
- Verify that export omits metadata-sourced descriptions and does not invoke the metadata provider, while the existing MCP path retains enrichment and exported SQL definitions retain their comments.
- Run an export against the configured demo database and inspect a table, a view, and a trigger/routine when present. Check that a domain-document reference can lead an agent to the relevant file using only the directory contents. Do not introduce example-data extraction or a completeness report for this check.
- During implementation, run the repository's required build, tests, and quality checks and update the CLI documentation in `README.md` and `docs/architecture-spec.md`.
