# SQL tool reference

The server registers 19 SQL tools. Required arguments are listed below; `database` is an explicit qualified target (`alias/database`) wherever shown. Failed tool results set MCP `isError=true`; catalog errors appear as `[SQL-AI-...] message` in text content or structured `error` objects in JSON envelopes. Successful content can be Markdown or JSON, depending on the tool.

Discover aliases with `sql_list_servers` and permitted qualified targets with
`sql_list_databases`. Both discovery schemas and CLI help describe the required
`alias/database` form, for example `test/AppDb`. Registered MCP calls with a
missing, null, incorrectly typed, malformed or unknown `database`/`server`
return `SQL-AI-0001` with `isError=true` before SQL work when the other arguments
are bindable. For example, missing required arguments or arguments with
incompatible types may be rejected by the SDK with a generic tool error before
target validation. The generated CLI retains required options: missing options
fail during command parsing; invalid supplied targets return the same catalog
error through the dispatcher.

## Discovery and schema

| Tool | Required arguments | Optional arguments | Result |
| --- | --- | --- | --- |
| `sql_list_servers` | None | None | List of configured named server aliases |
| `sql_list_databases` | `server` | None | Permitted databases on the specified server alias |
| `sql_search_databases` | `server`, `search_term` | None | Permitted databases on the specified server matching a substring |
| `sql_validate_query` | `query`, `database` | `parameters` | Validation message; SQL Server `NOEXEC` compilation without executing the query |
| `sql_search_objects` | `search_term`, `database` | `max_results`, `object_type` | Object matches; `object_type` matches SQL Server `type_desc`, supporting LIKE patterns such as `SQL_%` |
| `sql_get_schema` | `database` | `object_name`, `object_names` | Scalar primary-schema Markdown, or an ordered JSON batch envelope |
| `sql_export_schema` | `database`, `scope`, `output_path` | `object_name`, `object_names` | Persistent server filesystem dump; compact JSON outcome |
| `sql_get_schema_foreign_keys` | `object_name`, `database` | None | Inbound/outbound foreign keys |
| `sql_get_schema_indexes` | `object_name`, `database` | None | Indexes, key and included columns |
| `sql_get_schema_constraints` | `object_name`, `database` | None | Default/check constraint definitions |
| `sql_get_trigger_definition` | `object_name`, `trigger_name`, `database` | None | Trigger DDL; `object_name` identifies its parent table/view |
| `sql_get_object_references` | `object_name`, `database` | None | Referencing entities for a table/view through `sys.dm_sql_referencing_entities` |
| `sql_get_routine_parameters` | `object_name`, `database` | None | Procedure/function parameter names, types and directions |

Schema details are separate calls so clients can request only relevant metadata. Validation is not a guarantee that all objects exist or that later execution will succeed: `NOEXEC` can defer name resolution. Caller `SET NOEXEC`, `SET PARSEONLY` and `SET FMTONLY` are rejected before opening the validation connection, even inside nested blocks (`SQL-AI-0104`); unparseable SQL returns `SQL-AI-0102`. Schema definitions are raw text; [security.md](security.md) describes exposure limits.

### Primary schema selection

`sql_get_schema` preserves scalar validation and Markdown when `object_names` is
omitted or null: a non-empty string `object_name` is then required. A supplied
array, including `[]`, selects batch mode. Combine `object_name` first, followed
by `object_names` in order; null, empty and whitespace-only names are ignored and
other names are trimmed. At least one name must remain. Both selection fields
and array entries accept null; other non-string names and non-array lists fail
with `SQL-AI-0001`. Details remain separate tools.

These argument objects are accepted by the registered MCP tool:

```json
{"database":"primary/DemoDB","object_name":"Orders"}
{"database":"primary/DemoDB","object_names":["Orders","Customers"]}
{"database":"primary/DemoDB","object_name":" Orders ","object_names":[null," ","Customers","Orders"]}
{"database":"primary/DemoDB","object_name":"Orders","object_names":[]}
```

Batch identifiers can be unqualified or SQL-schema-qualified, including quoted
names such as `[sales].[Order Details]`. Unqualified names must be unambiguous;
SQL Server's catalog comparison rules apply. Cross-database names are rejected.
Duplicate inputs and aliases retain separate ordered results while sharing fetches.

Batch output is one JSON text block with `complete` and `results`. Every normalized
input has `object_name`, `status` and either `markdown` for `ok`, `error` with
catalog `code`/`message` for `error`, or `reason=response_limit` for `not_returned`.
Object errors allow successful siblings. Validation/access/catalog failures fail
the whole request. `complete=true` and MCP `isError=false` only when every entry
is `ok`; mixed errors and omissions retain the envelope with `isError=true`.

The defaults allow 10 non-empty inputs before deduplication and 65,536 UTF-8 bytes
of exact JSON text, including metadata. A whole schema/error that cannot fit and
all following entries become `not_returned`; earlier entries remain. If identifying
omission metadata alone cannot fit, the call fails before database work. No text
is silently truncated and no cursor is returned. Narrow the selection or request
fewer names after omissions. See [batch limits](configuration.md#schema-batch-limits)
and [CLI array syntax](development.md#manual-tool-calls).

### Schema export

Call `sql_export_schema` deliberately: it creates persistent schema documentation
on the **server filesystem**, can write a large dump with many files and take
substantial time. The agent needs access to that filesystem. Database access
(`SchemaOnly` suffices) and server process write permissions are required.

Required `scope` is exactly `selected` or `all`; it never defaults to full export.
Selected scope uses the scalar/list normalization described above, requires at
least one name and resolves the entire selection before writes. Aliases export
once; dependencies and trigger parents are not recursively included. Full scope
rejects non-empty selections; absent/null/empty/whitespace selections are harmless.
Wrong selection types fail in either scope.

`output_path` must be a fully qualified Windows directory on the server, such as
`C:\Doku\Selected` or `\\server\share\Selected`. Relative, drive-relative,
root-relative, URL and device-namespace paths are rejected. The directory must be
absent or completely empty, including no child directories; an existing file fails.

```json
{"database":"primary/DemoDB","scope":"selected","output_path":"C:\\Doku\\Selected","object_name":"dbo.FakeProjects","object_names":["dbo.FakeAddresses"]}
```

Both scopes reuse the [CLI dump layout](development.md#schema-export), with UTF-8
Markdown `README.md` and individual documents for tables, views, SQL routines and
table/view DML triggers. This is offline schema documentation, not a SQL backup.
Batch response budgets do not apply; existing command timeouts and cancellation do.

One compact JSON text block contains `status` (`completed`, `failed`, `cancelled`),
`output_path` when resolved, `partial_output`, and a catalog `error` (`code`,
`message`) or cancellation `reason=cancelled` when applicable. No dump contents
are returned. Failed/cancelled outcomes set `isError=true`, stop immediately and
retain output. `partial_output=true` means retained files or child directories,
including incomplete files; an empty root alone is false. Completed exports set
it false. Successful unavailable-definition notes are document content.
Retry can reuse an empty root; otherwise use a new or manually emptied directory.
There is no automatic cleanup or staging.

## Query and script execution

| Tool | Required arguments | Optional arguments |
| --- | --- | --- |
| `sql_execute_query` | `query`, `database` | `requested_row_limit`, `parameters` |
| `sql_execute_file` | `file_path`, `database` | `use_transaction` (true), `requested_row_limit`, `parameters` |

`sql_execute_query` accepts one main statement with supported leading declarations. The `database` argument is resolved as a qualified `alias/database` target: malformed, unqualified or unknown targets fail with `SQL-AI-0001` before any database work. Returned rows are capped by `QueryExecution` limits. It returns separate text blocks: a masking notice when applicable, an execution header, JSON rows, and a schema hint when the parsed query references a supported SQL Server catalog view or `OBJECT_DEFINITION`. The hint directs agents to `sql_get_schema`, `sql_get_trigger_definition` or `sql_search_objects`; it does not make another tool call. Catalog query string results may be anonymized. The header reports returned rows, client query time, server CPU time and logical reads from `STATISTICS IO/TIME`. Client elapsed time and server CPU time measure different things.

Query and script JSON rows omit provider fields marked `IsHidden=true` in the
reader's schema table, including browse-only keys and `rowversion` fields. These
fields remain available as source metadata and do not appear in masking notices.
Anonymized reads retain `SequentialAccess | KeyInfo`; cleartext reads use
`SequentialAccess`. If schema metadata is unavailable or has no `IsHidden` column,
all reader fields are serialized. Explicitly selected binary values, including
`rowversion`, retain JSON base64 encoding.

`sql_execute_file` accepts local `.sql` files, absolute or relative to the server working directory, with `GO` batch separators. URLs and UNC paths are rejected. Its `database` argument is resolved exactly once as a qualified `alias/database` target (`SQL-AI-0001` for malformed, unqualified or unknown targets before any database work, `SQL-AI-0104` for denied or excluded targets); the report's database field carries the canonical qualified target, and all batches run against that one immutable context. Size is checked against `MaxScriptFileSizeBytes` (10 MiB default). Parameters are shared across batches; row limits apply per SELECT batch. Results are a Markdown report with file metadata, transaction mode, batch results, metrics and diagnostics. Executed batches that reference a supported catalog view or `OBJECT_DEFINITION` include the same schema hint. Server-managed transactions reject explicit transaction control and `SET IMPLICIT_TRANSACTIONS` anywhere in the script before the first batch runs (`SQL-AI-0104`). Unparseable batches fail with `SQL-AI-0102`. Procedures, dynamic SQL and triggers remain supported in ReadWrite but can change transaction state outside that analysis; atomicity is not guaranteed for such code. See [transaction behavior](security.md#query-guards-and-transactions) before enabling writes or disabling the server-managed transaction.

## Parameters

Execution, validation, comparison and benchmark tools accept dictionaries of SQL parameters. Primitive/date inference is supported; explicit `dbType` uses a .NET `DbType` name:

```json
{
  "CustomerId": 42,
  "Code": { "value": "123", "dbType": "AnsiString" }
}
```

Parameterize values with names such as `@CustomerId`. Choose explicit types where inferred types would cause conversions. Comparison/benchmark use `parameters_a` and `parameters_b` when supplied; each falls back to shared `parameters` otherwise. Independent parameter sets must use distinct names, ignoring letter casing and an optional leading `@`; overlapping names are rejected before query validation or database access because both queries otherwise share one SQL command during difference checks. A single shared `parameters` set remains supported.

## Comparison and performance

| Tool | Required arguments | Optional arguments |
| --- | --- | --- |
| `sql_compare_queries` | `database`, `query_a`, `query_b` | `parameters_a`, `parameters_b`, `parameters`, `max_diff_rows` (5) |
| `sql_measure_performance` | `database`, `query` | `parameters`, `warmup_runs` (1), `execution_runs` (1), `include_plan_analysis` (true) |
| `sql_benchmark_optimization` | `database`, `query_a`, `query_b` | `parameters_a`, `parameters_b`, `parameters`, `warmup_runs` (1), `execution_runs` (1) |
| `sql_suggest_indexes` | `database` | `table_name`, `min_score` (0), `top` (10) |

### Comparison

Compares column names/types, exact row counts (`COUNT_BIG`) and both set differences (`EXCEPT`). Results include `is_equal`, `schema_match`, `count_match`, `row_count_a`, `row_count_b`, `schema_differences`, `rows_in_a_not_in_b` and `rows_in_b_not_in_a`. `max_diff_rows` is clamped to the inclusive range `1` through `QueryExecution.MaxRowLimit` for each difference direction. Zero and negative values therefore retain one example row when differences exist; values above the configured limit cannot increase the sample cap.

`EXCEPT` removes duplicates. Equal total counts plus empty differences do not prove equal duplicate multiplicities or row ordering. Comparisons run several queries under ReadCommitted; changing data between checks can affect results. Difference samples are raw values, including for `ReadOnlyAnonymized` databases.

### Measurement

Runs unmeasured warmups followed by measured executions. `STATISTICS IO/TIME` provides CPU/elapsed time and logical, physical and read-ahead reads; English and German SQL Server messages are parsed, and LOB read counters are excluded. Optional XML plan analysis reports missing indexes, table scans and implicit conversions. Without `SHOWPLAN`, it can return metrics and a permission note.

The JSON result contains `database`, `runs_evaluated`, `warmup_runs`, `metrics`, `warnings`, `has_showplan_permission` and `showplan_note`. Metrics are averages; `min_elapsed_ms`, `max_elapsed_ms`, `min_cpu_ms` and `max_cpu_ms` are populated only for multiple measured runs. Warnings carry type/severity/message/impact; missing-index warnings can include `missing_index_statement` with generated DDL, or null if no key columns support a statement. Review suggestions against existing indexes and workload before executing DDL.

### Benchmark

Combines comparison and measurements, returning `database`, `verdict`, `summary`, `comparison`, `performance_a`, `performance_b` and `deltas`. Each CPU/elapsed/logical-read/physical-read delta includes baseline, candidate, absolute and percentage values; a negative percentage means a reduction.

Verdicts are `Recommended` (CPU and logical reads no worse, at least one improved), `NotRecommended`, `Neutral`, or `UnsafeDueToDataMismatch`. The verdict inherits comparison limits and describes sampled runs; it does not establish performance for all parameters, concurrency levels or future data.

### Index suggestions

Reads SQL Server missing-index DMVs, filtered to the requested database. Score is `avg_total_user_cost * avg_user_impact * (user_seeks + user_scans)`, sorted descending. `table_name` applies a LIKE substring filter to the DMV statement; `min_score` excludes lower scores; `top` caps output.

Markdown contains a restart-reset notice and columns `Score`, `Table`, `Equality Columns`, `Inequality Columns`, `Include Columns`, `Seeks`, `Scans`, `Last Seek`. Column lists reflect IDs supplied by the DMV. Recognized permission failures return a note rather than a hard error. See [permissions](security.md#sql-server-permissions). Statistics accumulate since restart; recommendations are evidence to review, not automatically executed changes.

## Error catalog

The implementation source is [SqlToAiError.cs](../src/SqlToAi/Domain/SqlToAiError.cs).

| Code | Meaning |
| --- | --- |
| `SQL-AI-0001` | Invalid or missing arguments |
| `SQL-AI-0101` | Multiple main statements/batches in a single-query call |
| `SQL-AI-0102` | SQL syntax or execution error |
| `SQL-AI-0103` | Object not found |
| `SQL-AI-0104` | Database access denied, exclusion matched or `USE` rejected |
| `SQL-AI-0105` | Connection/infrastructure failure, including primary TLS/certificate validation or handshake failure |
| `SQL-AI-0106` | Connection, execution, or database cleanup timeout |
| `SQL-AI-0107` | Mutation blocked or access level does not permit data execution |
| `SQL-AI-0108` | Object-reference lookup requires table/view |
| `SQL-AI-0109` | Routine-parameter lookup requires procedure/function |
| `SQL-AI-0110` | Detail lookup requires table/view |
| `SQL-AI-0111` | Script file not found |
| `SQL-AI-0112` | Script exceeds configured size limit |
| `SQL-AI-0113` | Script extension is not `.sql` |

Primary connection-open failures use safe diagnostics identifying the `SqlToAi:Servers:<alias>` configuration section. TLS/certificate failures return `SQL-AI-0105`, including database discovery, schema/export operations and index suggestions; database discovery does not conceal these failures behind its configured-list fallback. Actual connection timeouts return `SQL-AI-0106`, even when the timeout occurs during a TLS handshake. The diagnostics discard raw provider messages and inner exception payloads before service logging, so authentication secrets and full connection strings from those exceptions cannot reach the response or connection-failure logs. Ordinary SQL execution errors retain their existing handling. No connection failure triggers a retry with weaker encryption or certificate checks.
