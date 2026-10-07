---
status: draft
---

# Database schema export to Markdown

## Intention

Export the schema and SQL definitions of one configured SQL Server database into a directory of Markdown files. An LLM agent can read these files offline and follow references from separate domain documentation to specific database objects. The files represent the database as read during export; retaining a particular application or database version is the responsibility of the consuming project.

Reuse SqlToAi's existing database access, schema queries, and Markdown rendering primitives. The new functionality coordinates schema operations and writes files with an explicit offline presentation; it does not establish another schema discovery or documentation engine. Existing MCP server output is an invariant, not a target for improvement in this task.

## Scope

### Must

- Add a CLI subcommand with exactly two required inputs: database name and output directory. Use the existing application configuration, connection setup, dependency injection, logging, and access checks.
- Produce UTF-8 Markdown, with one file per exported object and SQL-schema-qualified names in the content. Include foreign keys, indexes, and constraints in the corresponding table/view file rather than creating separate files for these details.
- Export tables, views, SQL stored procedures, SQL scalar/table-valued functions, and table/view DML triggers using the existing schema services. Retrieve all objects of those kinds without applying the interactive search result limit.
- Include the actual available SQL definitions for views, routines, and triggers, preserving SQL comments within those definitions. Do not interpret, summarize, or strip the SQL text.
- Provide a small `README.md` linking to object files, grouped by kind. This is navigation for an offline reader, not an export report.
- Make generated content usable without MCP access. Schema detail sections and routine parameters must be present in the files; discovery instructions to invoke MCP tools must be omitted or replaced with local section/file references through an explicit rendering contract, not by parsing and rewriting rendered Markdown.
- Use stable, filesystem-safe filenames derived from SQL schema and object name. Apply deterministic escaping for Windows-invalid characters and reserved filenames. Before writing any files, check all planned output paths for collisions using Windows case-insensitive comparison; abort on a collision instead of overwriting or choosing order-dependent names. Files and links must use the same name-to-path mapping; headings and the overview retain the original SQL object names.
- Create the output directory when absent. Require an empty directory before writing; fail if it already contains files or directories. No overwrite or cleanup switches.
- Stop on the first access, query, or file-writing error and return a nonzero exit code using existing error handling. Already written files remain; there is no filesystem rollback or automatic cleanup. A repeat run requires a new or manually emptied output directory.
- A successful export returns exit code zero. If a shared schema operation returns its existing unavailable-definition note as a successful result, include that note and continue; it does not by itself fail the export. An actual failed service result must still abort. No separate completeness evaluation is added.
- Preserve the existing `server` and `query` commands and all existing MCP tool output: content, headings, column order, formatting, discovery instructions, enrichment, notes, and errors for the same input and database state. Do not add export parameters to MCP tools or alter their defaults.
- Omit table/column descriptions sourced from metadata and metadata enrichment from export. Skip the metadata-provider calls before rendering, using the explicit offline presentation described below; never remove descriptions by parsing Markdown. This exclusion does not apply to comments embedded in the original SQL definitions.
- Omit the `Anonymized` column and skip anonymization-policy/rule-provider calls during export. These describe SqlToAi configuration rather than database structure. The MCP path retains its current anonymization indicators and rule handling.
- After the general implementation audit, use `tools/SqlToAi.Exploration` for schema observations and perform exploratory exports of `DemoDB` through the actual CLI. Agents must state their expectations before inspecting results and assess whether the output is what they would expect, with concrete evidence about correctness and usefulness to an offline LLM reader. Resolve findings and verify the corrections with fresh observations/exports before declaring the task complete; follow the verification procedure below.

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
- [TableSchemaRenderer.cs](../../src/SqlToAi/Database/TableSchemaRenderer.cs): already renders column schemas, trigger overviews, view definitions, and SQL routine definitions. Reuse its data access and rendering primitives through the presentation boundary below. Offline output omits descriptions, the `Anonymized` column, and their metadata/policy/rule-provider calls; the existing MCP rendering path remains intact.
- [DetailSchemaRenderer.cs](../../src/SqlToAi/Database/DetailSchemaRenderer.cs): existing detail operations supply foreign keys, indexes, constraints, trigger definitions, referencing entities, and routine parameters. Compose their results into the corresponding files.

Resolve each exported trigger definition through the same typed trigger identity validated against its parent table/view. The existing MCP trigger operation first matches a simple trigger name against the parent, then resolves the definition separately with `OBJECT_ID` on that name; this can fail or resolve the wrong object outside the default schema. This task must not change that existing MCP behavior. Provide an identity-based schema-service entry for export and reuse/extract the existing data-access primitives with typed, parameterized identities; do not copy a second trigger catalog query into the exporter or repair the MCP operation as adjacent work.

`SearchObjectsAsync` currently executes a limited object search and returns a Markdown table. It does not expose an unrestricted typed object list or trigger-parent associations. Extract/reuse the object discovery query in the schema layer with a small typed result containing the identifiers needed by both callers. Keep the interactive search's limit and Markdown output intact. The export must not parse Markdown tables for object identity, copy the catalog query into its own layer, or simulate an unrestricted search using an arbitrary large limit.

New behavior is limited to the CLI entry, export coordination, filesystem output, shared discovery/data-access primitives, and explicit offline presentation and trigger identity. No general exporter framework or second set of catalog queries is needed.

For test and exploratory export directories, reuse [TestTempDirectory.cs](../../tests/SqlToAi.Tests/TestSupport/TestTempDirectory.cs). It creates owned directories below the repository's `temp/` and cleans them up on disposal. Its owner marker makes the managed directory itself nonempty: use a fresh child directory, such as `GetPath("dump")`, as the CLI output target. Keep the owning instance alive until file inspection is finished, then dispose it; do not add another temporary-directory or cleanup mechanism.

### Presentation boundary and MCP output preservation

Prefer a small, immutable, per-call rendering context passed by the export coordinator through application services. It identifies offline presentation and supplies local link targets from the shared object-to-path mapping. Existing service entry points retain their current MCP presentation by default; the context is internal application state, not a new CLI option, MCP tool argument, or global configuration switch. Avoid scattered boolean flags, ambient state, or mutable mode fields on singleton services.

If inspection shows that a context would entangle database access and presentation or spread export branches through the existing renderer, use a narrowly scoped offline renderer over shared typed schema data and formatting primitives instead. Keep data retrieval, access checks, object identity, and error handling in the shared schema layer; only document presentation may differ. Record the choice and its reason in roadmap item 2 before implementation. This is a bounded architectural choice within scope, not permission for a generic renderer/plugin framework or duplicated discovery logic.

Select presentation before enrichment: the offline path never invokes metadata, anonymization-policy, or rule providers. Render columns, definitions, detail sections, and links from their structured inputs. Do not call MCP tools and strip their output, parse/rewrite rendered Markdown, suppress content via string replacement, or mutate shared configuration to obtain export output.

Before refactoring, capture representative current MCP results in focused regression tests using fixed inputs/data. Compare the full deterministic content, notes, and error results after the change, including calls interleaved with export rendering to detect mode leakage. Exploration artifacts supplement these tests; they are manual observations, not approved snapshots. A finding in current MCP output is recorded as outside this task's scope and does not authorize changing it.

## Implementation roles and model assignment

These assignments apply when the [stage-3 orchestrator](../../.agents/agent-workflow/03-orchestrierte-umsetzung.md) is invoked for this task. They do not start implementation now. The requested `luna-6-high` maps to model `gpt-6-luna` with reasoning effort `high`; `sol-6.1-medium|high` maps to model `gpt-6.1-sol` with the effort specified below.

| Role / roadmap scope | Model | Reasoning effort | Assignment rationale |
| --- | --- | --- | --- |
| Orchestration and evidence sampling (recommended launch setting) | `gpt-6.1-sol` | `medium` | Select bounded work, enforce contracts, and check delegated evidence. |
| Shared schema discovery, trigger identity, and presentation architecture (items 1–2) | `gpt-6.1-sol` | `high` | Changes cross service/rendering boundaries and must preserve MCP output exactly. |
| CLI, file composition, focused tests, and documentation (item 3) | `gpt-6.1-sol` | `medium` | Implement the defined contracts after the shared architecture is settled. |
| General implementation audit (item 4) | `gpt-6.1-sol` | `high` | Independently assess architecture, compatibility, and failure paths. |
| Schema exploration and CLI/source comparison (first review in item 5) | `gpt-6-luna` | `high` | Execute bounded scenarios, inspect artifacts/files, and report concrete mismatches. |
| Independent offline-reader review (second review in item 5) | `gpt-6-luna` | `high` | Evaluate fresh files and navigation with a separate reader context. |
| Findings correction | `gpt-6.1-sol` | `medium` or `high` | Use `high` for shared schema/rendering/MCP compatibility; use `medium` for isolated CLI/file/documentation fixes. |

Delegate sequentially with exactly these model/effort settings and task-local context: the assigned item, binding concept sections, rules, and relevant evidence. Use separate review agents; the offline reviewer does not receive the source review's findings or exploration artifacts before its independent assessment. Reviewers may author temporary exploration scenarios and record evidence, but do not change production code. The orchestrator checks their findings against scope before assigning fixes.

Use Luna for the bounded observational reviews and Sol for production changes and compatibility decisions; do not dispatch the entire task to every agent or repeat unchanged audits. Escalate a genuinely unresolved architectural/compatibility question from Luna to Sol `high`; do not upgrade routine file inspection merely because it is a review. If a required delegated model/effort is unavailable, report the blocker instead of silently substituting another model. Sol `medium` is the recommended launch setting for the parent orchestrator; delegation cannot change its running model. The delegated assignments remain mandatory regardless of the parent's launch setting.

## Verification

- Exercise CLI input validation and file output with focused tests: required arguments, empty-directory handling, filename collisions, and propagation of service/file errors.
- Verify Windows-invalid/reserved names and case collisions, the shared path mapping used by relative links, and collision rejection before any file is written.
- Verify that unavailable-definition notes permit success, while actual service/file failures stop the export with a nonzero exit code; check the specified partial-output and repeat-run behavior.
- Verify composition using known shared-service results: table details stay in the table file, definitions and routine parameters are included, and trigger files identify their parents.
- Verify a trigger outside `dbo` and same-named triggers in different SQL schemas: each exported definition must match the typed trigger identity validated against its parent, and existing MCP results remain unchanged.
- Use a known object set covering every supported kind, including scalar, inline table-valued, and table-valued SQL functions. Include more objects than the interactive search's default limit and check that every expected file is generated. These are implementation tests, not a runtime completeness report.
- Verify that offline files do not instruct the reader to invoke MCP tools and that full existing MCP output remains unchanged under fixed inputs/data, including after interleaved offline calls.
- Verify that export omits metadata-sourced descriptions and does not invoke the metadata provider, while the existing MCP path retains enrichment and exported SQL definitions retain their comments.
- Verify that export omits the `Anonymized` column and does not invoke anonymization-policy/rule providers even when central rules are enabled; the existing MCP path retains its indicators and rule handling.
- During implementation, run the repository's required build, tests, and quality checks and update the CLI documentation in `README.md`, `docs/development.md`, and `docs/architecture.md`.

### Exploratory DemoDB verification after the general audit

The [transport-free exploration runner](../../docs/exploration.md) is mandatory for the first review's direct schema-tool observations. Follow its [scenario authoring guidance](../../tools/SqlToAi.Exploration/Scenarios/README.md). Run a temporary task-specific scenario through `pwsh -File scripts/explore.ps1 -Scenario <temporary-scenario-name>`, using sequential `context.CallAsync` calls to the relevant existing schema tools. The runner observes the unchanged MCP-facing dispatcher output; it does not implement the exporter or replace actual CLI invocations, owned temporary directories, or either independent review below.

- Run this practical phase after the general code/test/documentation audit and its corrections. Use sequential exploratory agents; their findings are feedback for an implementation agent, not permission for the reviewers to change production code.
- Before each review, state concrete expectations derived from the concept and known object identity: which sections, definitions, parent relationships, and navigation should be present, and which generated enrichment/tool instructions should be absent. Ask explicitly: "Is this output what I would expect for this object and for an offline reader?" Evaluate observed output against those expectations and cite files/sections for mismatches. Expectations are agent review notes in the roadmap, not assertions added to exploration scenarios.
- The first agent invokes the actual `export-schema` CLI against configured `DemoDB` and runs the temporary exploration scenario for comparison. Inspect `request.json`, all content blocks in `response.json` (including `isError`), `response.txt`, and any `error.txt`; runner exit zero alone proves neither successful tool calls nor useful output. Compare object files to source observations, inspect available object kinds, table details, SQL definitions, trigger-parent associations, and relative links. Treat intentionally different MCP/export presentation according to the concept; do not demand removed enrichment in export or change MCP output to make both presentations identical. For a source operation with the known trigger-resolution limitation, use the typed schema path to check identity rather than copying the faulty observation into export.
- A second agent independently performs a fresh `DemoDB` export and then evaluates the files as an offline reader, using only the exported directory during content review. Start at its `README.md`, find SQL objects by name, follow table relationships and trigger links, and assess whether the columns, constraints, and SQL definitions provide understandable context. Report concrete missing, misleading, inconsistent, malformed, or unusable content against the agreed scope; do not invent business explanations or request excluded features.
- Each CLI export uses an isolated `TestTempDirectory` below repository `temp/` and a fresh child output directory, with cleanup after inspection. Runner observations remain in its separate retained `temp/exploration/` directory through inspection; do not use that nonempty directory as the CLI export target. Remove temporary scenario sources and delete observations when no longer needed under the runner's documented lifecycle. The database remains read-only and no business records are exported; use only schema/definition tools and read-only catalog metadata where necessary. Missing object kinds in `DemoDB` are documented as limits of the live check; the known-object tests still cover every supported kind.
- Fix actionable findings through the shared implementation, add focused regression tests where needed, and rerun the CLI into a fresh directory to check the affected output. Repeat affected checks when new findings remain; neither a successful general audit nor a successful CLI exit replaces content review. Completion requires both exploratory reviews and verification of their fixes, with no unresolved findings against the concept. An unavailable `DemoDB` or another genuine blocker leaves this phase incomplete.
- Record concise scenario/CLI invocations, inspected objects/artifact paths, expectations versus observations, findings, and their verified resolution in the corresponding roadmap item. Keep credentials and full dumps out of committed evidence. Temporary dumps are verification artifacts, not committed deliverables or a runtime completeness report. No new audit/export framework is required.
