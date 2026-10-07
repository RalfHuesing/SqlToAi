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
- Omit the `Anonymized` column and skip anonymization-policy/rule-provider calls during export. These describe SqlToAi configuration rather than database structure. The MCP path retains its current anonymization indicators and rule handling.
- After the general implementation audit, have agents perform exploratory exports of `DemoDB` through the actual CLI and inspect the generated files for correctness and usefulness to an offline LLM reader. Resolve findings and verify the corrections with fresh exports before declaring the task complete; follow the verification procedure below.

### Not

- Example records, row sampling, generated demo data, or querying business data.
- Snapshot identities, version parameters, timestamps, fingerprints, manifests, or version management.
- Completeness reports, export audit frameworks, object count reconciliation, or verification of a restorable database dump.
- Metadata-sourced descriptions, extended-property enrichment, and custom metadata-provider queries in export.
- Anonymization-policy indicators and anonymization-rule queries in export.
- Reconstructing table `CREATE` scripts or extending the existing schema detail coverage with additional SQL Server features.
- SQL Server object kinds beyond tables, views, SQL routines, and table/view DML triggers; server-level objects and DDL triggers are excluded.
- New dependency analysis, dependency graphs, resolution of dynamic SQL, or exporting other databases referenced by definitions.
- LLM calls by the exporter, business-domain inference, or generation/modification of the consuming project's domain documentation. Agent review of the generated files is part of verification.
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
- [TableSchemaRenderer.cs](../../src/SqlToAi/Database/TableSchemaRenderer.cs): already renders column schemas, trigger overviews, view definitions, and SQL routine definitions. Reuse it; adapt the shared rendering behavior for offline output, omitting descriptions, the `Anonymized` column, and their metadata/policy/rule-provider calls. Keep the current MCP rendering path intact; no new policy abstraction is needed.
- [DetailSchemaRenderer.cs](../../src/SqlToAi/Database/DetailSchemaRenderer.cs): existing detail operations supply foreign keys, indexes, constraints, trigger definitions, referencing entities, and routine parameters. Compose their results into the corresponding files.

Resolve each trigger definition through the same trigger identity validated against its parent table/view. The existing trigger operation first matches a simple trigger name against the parent, then resolves the definition separately with `OBJECT_ID` on that name; this can fail or resolve the wrong object outside the default schema. Correct this in the shared schema operation while retaining support for existing MCP callers, without adding an export-only trigger query.

`SearchObjectsAsync` currently executes a limited object search and returns a Markdown table. It does not expose an unrestricted typed object list or trigger-parent associations. Extract/reuse the object discovery query in the schema layer with a small typed result containing the identifiers needed by both callers. Keep the interactive search's limit and Markdown output intact. The export must not parse Markdown tables for object identity, copy the catalog query into its own layer, or simulate an unrestricted search using an arbitrary large limit.

New behavior is limited to the CLI entry, export coordination, filesystem output, the small shared discovery/rendering adjustments, and the shared trigger-identity correction. No general exporter framework or second set of catalog queries/renderers is needed.

For test and exploratory export directories, reuse [TestTempDirectory.cs](../../tests/SqlToAi.Tests/TestSupport/TestTempDirectory.cs). It creates owned directories below the repository's `temp/` and cleans them up on disposal. Its owner marker makes the managed directory itself nonempty: use a fresh child directory, such as `GetPath("dump")`, as the CLI output target. Keep the owning instance alive until file inspection is finished, then dispose it; do not add another temporary-directory or cleanup mechanism.

## Verification

- Exercise CLI input validation and file output with focused tests: required arguments, empty-directory handling, filename collisions, and propagation of service/file errors.
- Verify Windows-invalid/reserved names and case collisions, the shared path mapping used by relative links, and collision rejection before any file is written.
- Verify that unavailable-definition notes permit success, while actual service/file failures stop the export with a nonzero exit code; check the specified partial-output and repeat-run behavior.
- Verify composition using known shared-service results: table details stay in the table file, definitions and routine parameters are included, and trigger files identify their parents.
- Verify a trigger outside `dbo` and same-named triggers in different SQL schemas: each exported definition must match the trigger validated against its parent, and existing MCP callers remain supported.
- Use a known object set covering every supported kind, including scalar, inline table-valued, and table-valued SQL functions. Include more objects than the interactive search's default limit and check that every expected file is generated. These are implementation tests, not a runtime completeness report.
- Verify that offline files do not instruct the reader to invoke MCP tools and that existing MCP output keeps its current behavior.
- Verify that export omits metadata-sourced descriptions and does not invoke the metadata provider, while the existing MCP path retains enrichment and exported SQL definitions retain their comments.
- Verify that export omits the `Anonymized` column and does not invoke anonymization-policy/rule providers even when central rules are enabled; the existing MCP path retains its indicators and rule handling.
- During implementation, run the repository's required build, tests, and quality checks and update the CLI documentation in `README.md`, `docs/development.md`, and `docs/architecture.md`.

### Exploratory DemoDB verification after the general audit

The [transport-free exploration runner](../../docs/exploration.md) is available for temporary supporting scenarios and direct schema-tool observations. Follow its [scenario authoring guidance](../../tools/SqlToAi.Exploration/Scenarios/README.md). It does not implement the exporter or replace the actual CLI invocations, owned temporary directories, or either independent review below.

- Run this practical phase after the general code/test/documentation audit and its corrections. Use sequential exploratory agents; their findings are feedback for an implementation agent, not permission for the reviewers to change production code.
- One agent invokes the actual `export-schema` CLI against the configured `DemoDB`, reads the generated object files, and checks their correspondence to the source schema using existing schema operations. Inspect the available object kinds, table details, definitions, trigger-parent associations, and relative links. Check the resulting text, not just the exit code or file count.
- A second agent independently performs a fresh `DemoDB` export and then evaluates the files as an offline reader, using only the exported directory during content review. Start at its `README.md`, find SQL objects by name, follow table relationships and trigger links, and assess whether the columns, constraints, and SQL definitions provide understandable context. Report concrete missing, misleading, inconsistent, malformed, or unusable content against the agreed scope; do not invent business explanations or request excluded features.
- Each run uses an isolated `TestTempDirectory` below repository `temp/` and a fresh child output directory, with cleanup after inspection. The database remains read-only and no business records are exported. Missing object kinds in `DemoDB` are documented as limits of the live check; the known-object tests still cover every supported kind.
- Fix actionable findings through the shared implementation, add focused regression tests where needed, and rerun the CLI into a fresh directory to check the affected output. Repeat affected checks when new findings remain; neither a successful general audit nor a successful CLI exit replaces content review. Completion requires both exploratory reviews and verification of their fixes, with no unresolved findings against the concept. An unavailable `DemoDB` or another genuine blocker leaves this phase incomplete.
- Record concise invocation/inspection evidence, findings, and their resolution in the corresponding roadmap item. Temporary dumps are verification artifacts, not committed deliverables or a runtime completeness report. No new audit/export framework is required.
