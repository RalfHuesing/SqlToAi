Bitte validiere folgende Ideen aus agenten sicht im täglichen programmier alltag.


Bestehende tool funktionen sollten - soweit sinnvoll auch arrays als input parameter übergeben bekommen
Beispiel: Getschema für 3 Tabellen gleichzeitig statt 3 tool calls zu machen

es gibt neue export mcp funktionen die direkt in dateien und/oder verzeichnisse schreiben was der agent übergibt.
beispiel:
- kompletter schema export (haben wir schon aber als cli parameter): path
- spezifischer schema export: N-tabellen, filename
- daten export: sql query, max rows, filename:

---

# Agentic assessment and implementation recommendation

This is an assessment of the proposal, not an approved implementation plan. Tool
names, limits and contracts below are recommendations. They are not implemented.
The intended outcome is to help a coding agent obtain relevant database context
and create usable local artifacts with fewer interactions and predictable costs.

## Verdict

I would implement bounded schema batching first, then a schema export tool that
supports explicit selection and explicit full export. I would defer data export
until there is a concrete recurring workflow requiring it. I would reject a
general conversion of all tools to array inputs and unrestricted filesystem writes.

The ideas address real friction, but fewer tool calls alone are not a sufficient
success criterion. A useful agent tool must also keep context small, distinguish
partial results from complete results, support safe retries and return an artifact
the client can actually access. There is no measured speed or token saving yet.

| Proposal | Decision | Reason and priority |
| --- | --- | --- |
| Fetch primary schemas for several known objects | Implement first | Common prerequisite for joins, query changes and investigating related tables; small, coherent batch contract. |
| Add arrays to every existing tool | Reject | Different operations need different error, cost and transaction semantics; a generic rule would increase complexity without demonstrated benefit. |
| Export selected objects to one Markdown file | Implement second | Useful for a bounded task context, review or handoff; avoids having the model reproduce schema text in a file. |
| Expose full schema export through MCP | Implement with schema export | Reuses an existing capability for clients without a shell; require explicit full-export selection and resource limits. |
| Export query results to a file | Defer | Useful for fixtures and local analysis, but adds persistent data exposure, serialization and execution-policy work. |
| Accept any file path or arbitrary content supplied by the agent | Reject | SqlToAi should export its own SQL-derived artifacts, rather than become a general filesystem writer. |

## Verified starting point

The following findings come from the current repository, rather than assumptions
about MCP clients:

- There are 17 SQL tools. `sql_get_schema` accepts one `object_name` and an explicit
  `database`. Foreign keys, indexes, constraints, routine parameters and references
  are separate calls. This separation deliberately bounds context.
- The executable already supports `export-schema --database ... --output ...`.
  It writes a Markdown directory tree with an overview and object documents. It
  includes tables, views, SQL procedures/functions and table/view DML triggers.
  It is documentation export, not a restorable SQL backup or migration script.
- CLI export requires an absent or empty target directory and creates files
  exclusively. The first failure stops export; files already written remain.
  This is not an atomic export and an unchanged retry can fail on its own output.
- Offline export deliberately omits metadata enrichment, anonymization indicators
  and generated MCP instructions. Definitions and their comments remain raw.
  It is not equivalent to saving the interactive schema response.
- Query execution defaults to 100 rows and a configured maximum of 1,000 rows in
  the shipped configuration. It materializes serialized rows in memory. The
  existing result does not prove whether more rows existed beyond the limit.
- `ReadWrite` query execution may commit mutations. Masking is specific to query
  execution and its settings; it is not applied automatically to every tool output.
- The registry, SDK registrations, dispatcher and registry-generated CLI are
  contract consumers. Updating a JSON input schema alone would be insufficient.

Evidence: [tool contracts](../../docs/tools.md),
[architecture and export behavior](../../docs/architecture.md),
[CLI export](../../docs/development.md#schema-export),
[security limits](../../docs/security.md),
[configuration defaults](../../docs/configuration.md#settings-reference),
[export coordinator](../../src/SqlToAi/Cli/SchemaExportService.cs),
[query execution](../../src/SqlToAi/Database/QueryExecutionService.cs) and
[query safety policy](../../src/SqlToAi/Database/QuerySafetyValidator.cs).

## 1. Implement bounded schema batching

### Benefit and limits

For a join involving three known tables, one request can replace three primary
schema requests. This removes two client/server interactions and simplifies result
association. It does not necessarily reduce SQL round trips or schema content.
Clients that already execute independent tools concurrently may see a smaller
latency benefit. A ten-table response can consume more context than three focused
single-object requests.

Batch only objects the agent has already identified. Discovery still comes first
when names are unknown; do not automatically expand foreign-key neighborhoods or
return every schema detail.

### Recommended first contract

Add one `sql_get_schemas(database, object_names)` tool. Keep `sql_get_schema` and its
current response unchanged. This modest addition preserves existing consumers
without introducing scalar-or-array unions or changing single-object result shapes.
Use the single-object tool for one lookup and the batch tool for several known
objects. Do not add a separate plural tool for every existing operation.

```json
{
  "database": "DemoDB",
  "object_names": ["dbo.Customers", "sales.Orders", "sales.OrderLines"]
}
```

Recommended behavior:

- Accept one database per call and 1–10 explicit object names initially. Make the
  maximum configurable. Reject empty names and over-limit requests before work.
- Require unambiguous schema-qualified names for the new contract. Resolve them
  as SQL identifiers and preserve actual SQL identity; never infer identity from
  filenames or split quoted identifiers with a naive dot operation.
- Return primary schema only. Keep the current enrichment and masking guidance
  of interactive schema inspection. Request detail tools separately.
- Return a structured envelope containing ordered entries with the requested name,
  resolved identity when available, `status`, schema Markdown or a catalog error.
  Preserve one result per input, including repeated inputs; repeated resolved
  objects may reuse work within the call. Do not silently lose an input.
- Report `complete`, success/failure counts and any unprocessed entries explicitly.
  Invalid envelopes and database-wide denial fail the call before object work.
  Object-specific errors preserve successful siblings; any failure or unprocessed
  entry sets overall MCP `isError=true`, while the envelope retains usable results.
- Bound aggregate response bytes as well as object count. Stop at whole-object
  boundaries and identify objects not returned. If even one object exceeds the
  response budget, return an explicit size status and direct the agent to export
  that object. Never cut Markdown or JSON silently. Do not add cursor state in the
  first version; the agent can retry the explicitly omitted objects in smaller calls.
- Start with sequential service calls and cancellation support. Introduce bounded
  concurrency only if measurements justify additional SQL Server load. Batching
  is not a promise of a consistent database snapshot across objects.

I would not batch query execution, script execution, benchmarks or performance
measurements in this change. They have materially different side effects and cost
profiles. Batching foreign-key/index inspection is a later decision requiring
evidence of frequent repeated calls, rather than an automatic extension of scope.

## 2. Implement schema export as a bounded artifact operation

### Where it helps an agent

Direct export avoids sending a large schema through the model merely to have the
agent write it back to disk. It also produces a reusable artifact for humans and
local search. This saves model context only when the agent reads selected parts
afterward; loading the complete export back into context loses that advantage.

A coding agent with shell access can already invoke CLI full export. The MCP
benefit is larger for clients without shell access. Exposing the CLI service is
therefore useful, but less urgent than bounded schema batching.

### Recommended contract and scope

Use one `sql_export_schema` tool with an explicit `scope` of `selected` or `all`,
one `database` and an `output_path` relative to an administrator-configured export
root. Full export must never be the consequence of an omitted or empty object list.

```json
{
  "database": "DemoDB",
  "scope": "selected",
  "object_names": ["dbo.Customers", "sales.Orders"],
  "output_path": "order-investigation/schema.md"
}
```

- `selected` requires a non-empty bounded object list and writes one UTF-8 Markdown
  file. Start with tables and views. Include their existing schema, foreign-key,
  index, constraint and reference sections. Do not recursively export referenced
  objects or trigger definitions. Describe these inclusions in the tool contract;
  table-only selection can therefore satisfy the original N-table use case.
- `all` rejects `object_names` and writes the existing directory layout to a new
  destination, including its currently supported object kinds. Preserve existing
  unavailable-definition notes and report them in the export summary.
- Both scopes use the existing offline presentation. State the omitted enrichment
  explicitly so the agent does not interpret absent anonymization labels as an
  authorization to expose data. A selected document must not link to nonexistent
  sibling files; render unexported references as readable names or valid in-file
  links.
- Return a compact structured manifest with status, export identifier, database,
  scope, exported objects, file paths, byte counts and warnings. Return neither the
  full document nor data previews by default. Distinguish full completion from a
  failure and identify any retained temporary output.
- Extract reusable composition/output services from the CLI as needed. Do not
  launch a child CLI process, duplicate schema rendering or parse Markdown back
  into object identities. Keep CLI behavior stable unless separately changed.

### Filesystem and retry requirements

An export path belongs to the server process's filesystem. Even with stdio, a
container, remote execution environment or different user account can make it
unreadable to the agent. State this in the tool description and return the resolved
server-side path. The manifest does not prove client access. For clients without
shared filesystem access, this path-based design does not solve artifact delivery;
keep interactive schema tools available and defer a separate retrieval capability
until that deployment is actually required.

Before exposing writes through MCP:

- Require a configured export root; leave export disabled until it is configured.
  Reject absolute caller paths, UNC/device paths, traversal and destinations
  escaping the root through Windows junctions or other reparse points. Use a
  dedicated directory with appropriate Windows permissions; a string-prefix check
  alone is insufficient.
- Validate database access, selection, output shape and path rules before creating
  artifacts. Schema export remains available at `SchemaOnly`; it does not grant
  data-query permission.
- Default to exclusive creation. Do not overwrite, merge into existing output,
  delete unrelated files or accept arbitrary caller-supplied file contents.
- Stage output inside an operation-owned temporary location on the same volume,
  then publish to a previously absent destination only after success. Never label
  partial output as completed. On failure/cancellation, clean up only that
  operation's temporary files, or report their location if cleanup fails.
- A successful repeated call to an existing destination returns an explicit
  already-exists error without rewriting files. After a lost response, the agent
  must inspect the reported destination before choosing another one. Do not claim
  exactly-once execution or add persistent job/idempotency storage in this scope.
- Bound selected-object count, total files, aggregate bytes and total duration;
  support cancellation. Full export must not inherit the CLI's unrestricted
  discovery without an overall artifact budget. Bind limits through options and
  include them in the configuration template.

I would not offer overwrite switches, ZIP archives, SQL reconstruction, incremental
sync, arbitrary text writing or a background export/job-management framework in
the first version. These are separate products or requirements without evidence
in the original proposal.

## 3. Defer data export, with a clear admission criterion

Implement data export only when a recurring task needs query results as a local
artifact, for example a small fixture or a bounded input to an analysis script.
For inspecting a few records, `sql_execute_query` already provides the required
capability. Bulk extraction, backups and unrestricted large exports are out of
scope for this coding assistant.

If that use case is confirmed, add one `sql_export_query` tool accepting `database`,
`query`, the existing typed `parameters`, `requested_row_limit` and `output_path`.
Start with a documented JSON format; defer CSV and its additional escaping,
spreadsheet-formula and type-conversion decisions.

Required constraints before implementation:

- Enforce a result-producing read-only query, including on `ReadWrite` databases.
  Reuse the access checks, parser/statement restrictions and transaction handling,
  but add an explicit export read-only policy. Calling `ExecuteQueryAsync`
  unchanged is insufficient because its `ReadWrite` mode allows committing writes.
  Reject scripts, `EXEC`, DML/DDL and `SELECT INTO`; rollback alone is insufficient.
- Preserve typed parameter binding, token resolution and the existing masking
  policy, including exempt values. Never serialize raw reader values through a
  second path that bypasses those transformations. Tokens remain process-local
  and must not be advertised as durable identifiers in saved fixtures.
- Do not promise anonymous data: strings can remain raw by configuration and
  policy, and numeric/date/binary values are not generally transformed. Export
  persists those exposure limits beyond the session. Include masking metadata and
  its limitations in the manifest without returning sample records.
- Require a positive row limit capped by `QueryExecution.MaxRowLimit` initially,
  plus an aggregate output-byte limit and timeout. Reject zero/negative limits;
  never interpret them as unlimited. Keep the first version in the current bounded
  range; large streaming exports need a separate design.
- Report `rows_written`, `effective_row_limit`, `limit_reached` and byte counts.
  Reaching the limit means completeness is unknown unless truncation is proven.
  Do not infer `truncated=false` from the existing `RowCount`. A future extra-row
  probe must be bounded and tested before claiming exact truncation detection.
- Document JSON representation of nulls, dates, decimals and binary values. Preserve
  column order and handle duplicate column names without silent loss, for example
  through a column descriptor array plus positional row arrays. Require an explicit
  `ORDER BY` when the caller needs reproducible row order; export cannot invent it.
- Apply the same export-root, exclusive creation, staging, cancellation and manifest
  rules as schema export. Do not put row data, resolved tokens or parameters into
  additional export diagnostics.

The current serializer materializes text in memory. Reusing transformed output can
be sufficient for a small, byte-bounded first version, but a byte limit checked only
after materializing unbounded values would not bound peak memory. Enforce budgets
while producing output and stop oversized values explicitly. A new streaming path
is justified only when it preserves the same transformations and has a proven need.

## Acceptance criteria for a later implementation

These criteria describe what I would require before considering the proposed
features usable. They are not claims that the checks have already passed.

| Area | Verifiable outcome |
| --- | --- |
| Compatibility | Existing single-object MCP calls and the CLI retain their contracts. Registry, SDK registrations, dispatcher, CLI parsing and documentation agree on new arguments and results. |
| Batch usefulness | Three known objects return correctly associated primary schemas in one call. Detail sections are fetched only through explicit detail calls. |
| Batch failures | A missing object preserves successful siblings and is identifiable by name/status. Invalid input and denied databases fail predictably. Duplicate inputs, ambiguous names and response-budget exhaustion have tested behavior. |
| Resource bounds | Oversized object lists, huge definitions/values, total artifact limits, timeouts and cancellation stop predictably without silent omission or false completion. |
| Schema artifacts | Selected tables/views produce one usable Markdown file; explicit full export retains existing object coverage. Links resolve or render as text. Missing definitions remain visible warnings/notes. |
| Filesystem | Absolute/UNC/device paths, traversal, junction escape and existing destinations are rejected. Write failures and cancellation do not publish a completed artifact or remove unrelated output. |
| Access and privacy | Denied databases stay denied; `SchemaOnly` permits schema export and rejects data export. If added, data export rejects mutations even on `ReadWrite`, and tests cover masked and exempt values. |
| Data fidelity | If data export is added, nulls, Unicode, dates, decimals, binary values and duplicate column names round-trip under the documented format. Limit metadata never falsely asserts completeness. |
| Agent benefit | Compare the existing and new flows on a three-table join, selected schema handoff and full schema documentation. Record tool calls, elapsed time and model-visible output size; claim only measured benefits. |

## Recommended delivery scope

**Implement:** one bounded primary-schema batch tool, followed by one explicitly
scoped schema export tool with safe output handling and a compact manifest.

**Defer:** data export until a recurring artifact workflow is demonstrated; batching
individual schema-detail tools until repeated-call evidence supports it.

**Do not implement in this scope:** arrays everywhere, batch mutations/performance
runs, unrestricted filesystem access, automatic relationship expansion, full
database dumps, backup/migration generation or a generic artifact platform.

The main uncertainty is the deployment environment's shared filesystem and the
frequency of real export workflows. The recommendations assume a locally operated
coding assistant with access to a deliberately configured export directory. This
assumption is sufficient for the assessment, but artifact accessibility must be
verified before treating path-based export as a usable client feature.
