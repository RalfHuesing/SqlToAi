---
status: ready
---

# Schema batching and schema export

## Intention

Let agents retrieve several relevant schemas in one call and export selected or
complete schema documentation through the existing export implementation.

## Scope

### Must

#### Schema requests

- Extend `sql_get_schema` with optional `object_names` (string array), alongside
  existing `object_name` (string) and required `database`. When `object_names` is
  absent or null, preserve the existing single-object validation and Markdown
  response. A supplied array selects batch mode, including a one-element array
  or an empty array combined with a valid `object_name`. No separate batch tool.
- In batch mode, combine the scalar first and then array entries in input order.
  Ignore null, empty and whitespace-only names; trim surrounding whitespace from
  other names. Reject incorrect argument types or no remaining names before work.
  Use this same normalization for selected export.
- Resolve new batch/export selections as SQL object identifiers within the specified
  database, respecting its identifier comparison semantics and quoted names.
  Unqualified names are accepted only if unambiguous; otherwise require the SQL
  schema. Unknown or ambiguous names produce identifiable errors. Reuse work for
  repeated names and names resolving to the same object ID; do not assume all
  databases use case-insensitive names.
- Bound batch mode through options: default maximum 10 normalized non-empty input
  entries before deduplication and 65,536 UTF-8 bytes of aggregate response text,
  including identifying metadata. Reject over-count requests before schema work.
  Include these defaults in the configuration template. They apply only to batch
  responses, not legacy scalar calls or filesystem exports.
- Return primary schemas for the same object kinds as legacy scalar lookup, with
  existing interactive enrichment; keep detail sections separately requested.
  Batch output is one JSON envelope with `complete` and
  ordered `results`: one entry per normalized input, including duplicates, with
  `object_name`, `status` and either schema `markdown`, a catalog `error`, or an
  omission `reason`. Status is `ok`, `error` or `not_returned`. Every requested
  object remains identifiable even when its schema was not returned.
- Continue after object-specific errors and retain successful siblings. Request-wide
  validation/access failures fail the whole call. `complete=true` only when every
  entry is `ok`; any error/omission sets MCP `isError=true`. Keep whole schema texts:
  if the next schema would exceed the byte budget, mark it and all remaining
  entries `not_returned` with reason `response_limit`. This also covers an oversized
  first schema. Reserve space for identifying metadata; if that alone cannot fit,
  reject the request before schema work. No silent truncation or cursor protocol.

#### Schema export

- Add `sql_export_schema` with required `database`, `scope` (`selected` or `all`)
  and `output_path`. `selected` accepts `object_name` and/or `object_names` using
  the normalization above and requires a non-empty selection. `all` rejects a
  non-empty selection; omitted/null/empty selection fields are harmless. Neither
  omitted scope nor an empty selection ever activates full export.
- Reuse the existing export service, rendering, filename mapping and object kinds:
  tables, views, SQL procedures/functions and table/view DML triggers. Both scopes
  write its UTF-8 directory layout with `README.md` and individual object documents.
  Full export retains current contents. Selected export resolves its entire
  selection against the permitted discovery catalog before creating output: any
  unknown, ambiguous or unsupported selection fails the call without writes.
  A normalized empty `selected` selection is an invalid-parameters error.
- Filter selected objects before path mapping and composition, exporting each
  resolved object once. Do not recursively include referenced objects or trigger
  parents. References to unexported objects remain readable names, with no broken
  file links. An empty database in full mode still produces its root overview.
- Require a fully qualified Windows filesystem directory path on the server;
  drive-rooted and UNC paths qualify, while relative, drive-relative, root-relative,
  URL and device-namespace paths do not. The destination must be absent or
  completely empty, including no child directories; an existing file is invalid.
  Preserve exclusive file creation, database access checks and `SchemaOnly` support.
  Add path validation at the MCP boundary; retain current CLI path behavior.
- Return a compact JSON result with `status` (`completed`, `failed` or `cancelled`),
  resolved `output_path` when valid, `partial_output` and a catalog error/reason on
  failure or cancellation; failed/cancelled results set MCP `isError=true`.
  Return no dump contents. Stop on the first composition or write error, or
  cancellation; retain output already created. For failed/cancelled exports,
  `partial_output=true` means files or child directories remain, including an
  incomplete file; an empty root alone is false. Completed exports set it false.
  Partial output must never be reported as completed. Retry may reuse an
  empty root; otherwise it needs a new or manually emptied destination. No
  automatic cleanup or staging.
- Preserve successful unavailable-definition notes as document content; they are
  not export failures. Offline output omits interactive enrichment and is schema
  documentation rather than a SQL backup. Do not impose the batch response limits
  on the dump; it is a deliberate potentially large operation, with existing
  database command timeouts and cancellation support.
- State prominently in the tool description that export creates persistent schema
  documentation on the server filesystem, can produce a large dump with many
  files and take substantial time, and should only be called deliberately. Explain
  selected/full scope, absolute-path and empty/absent-directory requirements,
  partial-output behavior and that the agent needs access to that filesystem.

The [existing CLI export](../../docs/development.md#schema-export) already checks
for an absent/empty directory and writes Markdown documents through
[SchemaExportService](../../src/SqlToAi/Cli/SchemaExportService.cs). It currently
resolves relative paths and exports all objects. Reuse it directly where possible;
MCP registration, absolute-path validation and selection require focused changes.
Registry, SDK registration, dispatcher, registry-generated CLI and documentation
must agree on the extended schema-call and new export contracts. Existing CLI
commands retain their behavior; their schema command gains the optional array input.

#### Practical agent review

- During implementation, the agent must run a temporary task-specific scenario
  through the existing [exploration runner](../../docs/exploration.md), following
  its [scenario conventions](../../tools/SqlToAi.Exploration/Scenarios/README.md).
  Use the real dispatcher, configured database and production service registrations;
  retain requests and complete responses under `temp/exploration/` for inspection.
- Exercise scalar-only, one-element/multi-element array, combined scalar/array,
  empty/null/whitespace, duplicate and invalid-type inputs. Include a realistic
  related-table lookup, a missing object among valid names and a limit-exceeded
  response. Compare legacy scalar results with the corresponding batch entries.
- State expected behavior before running, then read `request.json`, every response
  content block and `isError` in `response.json`, `response.txt` and any `error.txt`.
  Judge whether the advertised parameters are easy to use, results are clearly
  associated with objects, response volume is useful, and errors/omissions give
  enough information for an agent to correct or narrow its next call.
- Check the exposed input schema and tool description against those same argument
  shapes. The runner bypasses SDK argument binding: supplement it with in-process
  tests invoking the registered SDK tools, without requiring MCP infrastructure.
  A dispatcher-only success does not prove that the advertised call is accepted.
- For export, focus on the new MCP contract: selected/full scope, single/list
  selection, destination validation, returned status/path and actual file creation.
  Use fresh output directories and inspect representative output. The existing
  CLI dump layout/content is accepted; no new comprehensive dump-content audit.
- Record artifact paths, expected versus observed behavior, usability verdict and
  findings in the implementation's existing completion evidence. Fix findings and
  rerun affected cases before completion; green tests or runner exit code zero
  alone are insufficient. Remove temporary scenario source after review, retaining
  observation artifacts according to runner conventions. If execution is blocked,
  report the missing practical verification instead of claiming acceptance.

### Out of scope

Data export, batching other tools, extra export-root configuration, overwrite or
cleanup options, new rendering formats, background jobs and unrelated CLI changes.

## Verification

- Legacy scalar calls retain validation and response; one-element and multi-element
  arrays use the documented envelope. Combined/empty inputs, whitespace, wrong
  types, duplicate inputs and aliases follow the normalization and reuse rules.
- Batch tests cover unknown/ambiguous names, identifier casing/quoting, count limits,
  exact UTF-8 response budgets, an oversized first schema and mixed successes/errors;
  every normalized input is accounted for and MCP error/completeness flags agree.
- Both exports retain the existing layout and offline presentation. Full export
  matches CLI coverage, including an empty catalog. Selected export includes only
  selected objects; bad selections write nothing and external references stay readable.
- Fully qualified drive/UNC destinations that are absent or empty work. Relative,
  drive/root-relative, URL/device paths, existing files and non-empty directories
  fail without overwriting existing files. CLI relative paths still work.
- Denied databases stay denied; schema-only access works. Injected service/write
  failures and cancellation retain partial output with accurate status/path metadata.
  Unavailable-definition notes allow success; the tool description announces the dump.
- Registry, SDK registration, dispatcher and CLI tests verify matching argument and
  result contracts. Implementation updates canonical tool documentation and passes
  the repository build/test checks; this concept does not claim those tests ran.
- The practical agent review above is completed with inspectable input/output
  artifacts and an explicit usability verdict, especially for parameter handling.
  All discovered usability or contract defects are fixed and checked again.
