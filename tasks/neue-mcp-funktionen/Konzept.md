---
status: draft
---

# Schema batching and schema export

## Intention

Let agents retrieve several relevant schemas in one call and export selected or
complete schema documentation through the existing export implementation.

## Scope

### Must

1. Extend `sql_get_schema` to accept `object_name` for a single object and
   `object_names` for an array, with one explicit `database` per call. Normalize
   both forms internally; if both are supplied, combine them without duplicate
   object work. Require at least one non-empty object name. Preserve the existing
   single-object call and response. Do not require a separate batch tool.
2. Bound batches by configurable object-count and aggregate response-size limits.
   Return primary schemas only, keeping details separately requested. Associate
   every result/error with its object; retain successful results when another
   object fails and identify any objects omitted because of a limit.
3. Add `sql_export_schema` with explicit `database`, `scope` (`selected` or `all`)
   and `output_path`. Selected export accepts the same single/list selection forms;
   full export rejects object selections. An empty selection never means full export.
4. Reuse the existing export service, rendering, filename mapping and supported
   object kinds. Full export retains its current layout and contents. Selected
   export filters the discovered objects before composition; it does not recursively
   export referenced objects and must not generate links to unexported files.
5. Require a fully qualified server-side output path. The destination directory
   must be absent or completely empty. Preserve exclusive file creation, existing
   database access checks and `SchemaOnly` support. Add path validation at the MCP
   boundary; retain the CLI's current relative-path behavior.
6. Return completion status and the resolved output location without returning the
   dump contents. Preserve the existing failure behavior: stop on the first error,
   report failure, and leave any partial output in place. A retry requires a new or
   manually emptied destination. Do not introduce automatic cleanup or staging.
7. State prominently in the tool description that export creates persistent schema
   documentation on the server filesystem, can produce a large dump with many
   files and take substantial time, and should only be called deliberately. Explain
   selected/full scope, the absolute-path requirement, empty/absent destination,
   partial-output behavior and that the agent needs access to that filesystem.

The [existing CLI export](../../docs/development.md#schema-export) already checks
for an absent/empty directory and writes Markdown documents through
[SchemaExportService](../../src/SqlToAi/Cli/SchemaExportService.cs). It currently
resolves relative paths and exports all objects. Reuse it directly where possible;
MCP registration, absolute-path validation and selection require focused changes.
Its offline output omits interactive enrichment and is schema documentation rather
than a SQL backup.

### Out of scope

Data export, batching other tools, extra export-root configuration, overwrite or
cleanup options, new rendering formats, background jobs and changes to CLI behavior.

## Verification

- Single-name, one-element array, multi-name array and combined inputs work;
  duplicate objects are not fetched twice. Existing single-object clients work.
- Missing input, batch limits, object failures and response limits produce explicit
  results without silently discarding successful siblings or requested objects.
- Full export matches existing CLI coverage; selected export contains exactly the
  selected objects and valid links or readable names for unexported references.
- Relative paths, file destinations and non-empty directories are rejected. Absolute
  paths to absent or empty directories work without overwriting existing files.
- Denied databases stay denied; schema-only access works. Failures report partial
  output honestly, and the tool description clearly announces a potentially large dump.

## Open decision

For selected export, use the existing directory layout (recommended for maximum
code reuse), or combine the selected objects into one Markdown file (requires
additional composition and different destination validation)? Full export remains
a directory tree in either case.
