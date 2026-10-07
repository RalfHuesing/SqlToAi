# SQL tool reference

The server registers 17 SQL tools. Optional observability feedback is separate. Required arguments are listed below; `database` is an explicit target name wherever shown. Failed tool results set MCP `isError=true`; catalog errors appear as `[SQL-AI-...] message` in text content. Successful content can be Markdown or JSON, depending on the tool.

## Discovery and schema

| Tool | Required arguments | Optional arguments | Result |
| --- | --- | --- | --- |
| `sql_list_databases` | None | None | Permitted databases |
| `sql_search_databases` | `search_term` | None | Permitted databases matching a substring |
| `sql_validate_query` | `query`, `database` | `parameters` | Validation message; SQL Server `NOEXEC` compilation without executing the query |
| `sql_search_objects` | `search_term`, `database` | `max_results`, `object_type` | Object matches; `object_type` matches SQL Server `type_desc`, supporting LIKE patterns such as `SQL_%` |
| `sql_get_schema` | `object_name`, `database` | None | Primary schema and descriptions for tables, views, procedures or functions |
| `sql_get_schema_foreign_keys` | `object_name`, `database` | None | Inbound/outbound foreign keys |
| `sql_get_schema_indexes` | `object_name`, `database` | None | Indexes, key and included columns |
| `sql_get_schema_constraints` | `object_name`, `database` | None | Default/check constraint definitions |
| `sql_get_trigger_definition` | `object_name`, `trigger_name`, `database` | None | Trigger DDL; `object_name` identifies its parent table/view |
| `sql_get_object_references` | `object_name`, `database` | None | Referencing entities for a table/view through `sys.dm_sql_referencing_entities` |
| `sql_get_routine_parameters` | `object_name`, `database` | None | Procedure/function parameter names, types and directions |

Schema details are separate calls so clients can request only relevant metadata. Validation is not a guarantee that all objects exist or that later execution will succeed: `NOEXEC` can defer name resolution. Schema definitions are raw text; [security.md](security.md) describes exposure limits.

## Query and script execution

| Tool | Required arguments | Optional arguments |
| --- | --- | --- |
| `sql_execute_query` | `query`, `database` | `requested_row_limit`, `parameters` |
| `sql_execute_file` | `file_path`, `database` | `use_transaction` (true), `requested_row_limit`, `parameters` |

`sql_execute_query` accepts one main statement with supported leading declarations. Returned rows are capped by `QueryExecution` limits. It returns separate text blocks: a masking notice when applicable, an execution header, then JSON rows. The header reports returned rows, client query time, server CPU time and logical reads from `STATISTICS IO/TIME`. Client elapsed time and server CPU time measure different things.

`sql_execute_file` accepts local `.sql` files, absolute or relative to the server working directory, with `GO` batch separators. URLs and UNC paths are rejected. Size is checked against `MaxScriptFileSizeBytes` (10 MiB default). Parameters are shared across batches; row limits apply per SELECT batch. Results are a Markdown report with file metadata, transaction mode, batch results, metrics and diagnostics. See [transaction behavior](security.md#query-guards-and-transactions) before enabling writes or disabling atomic execution.

## Parameters

Execution, validation, comparison and benchmark tools accept dictionaries of SQL parameters. Primitive/date inference is supported; explicit `dbType` uses a .NET `DbType` name:

```json
{
  "CustomerId": 42,
  "Code": { "value": "123", "dbType": "AnsiString" }
}
```

Parameterize values with names such as `@CustomerId`. Choose explicit types where inferred types would cause conversions. Comparison/benchmark use `parameters_a` and `parameters_b` when supplied; each falls back to shared `parameters` otherwise.

## Comparison and performance

| Tool | Required arguments | Optional arguments |
| --- | --- | --- |
| `sql_compare_queries` | `database`, `query_a`, `query_b` | `parameters_a`, `parameters_b`, `parameters`, `max_diff_rows` (5) |
| `sql_measure_performance` | `database`, `query` | `parameters`, `warmup_runs` (1), `execution_runs` (1), `include_plan_analysis` (true) |
| `sql_benchmark_optimization` | `database`, `query_a`, `query_b` | `parameters_a`, `parameters_b`, `parameters`, `warmup_runs` (1), `execution_runs` (1) |
| `sql_suggest_indexes` | `database` | `table_name`, `min_score` (0), `top` (10) |

### Comparison

Compares column names/types, exact row counts (`COUNT_BIG`) and both set differences (`EXCEPT`). Results include `is_equal`, `schema_match`, `count_match`, `row_count_a`, `row_count_b`, `schema_differences`, `rows_in_a_not_in_b` and `rows_in_b_not_in_a`. Sample counts are capped by `MaxRowLimit`.

`EXCEPT` removes duplicates. Equal total counts plus empty differences do not prove equal duplicate multiplicities or row ordering. Comparisons run several queries under ReadCommitted; changing data between checks can affect results. Difference samples are raw values, including for `ReadOnlyAnonymized` databases.

### Measurement

Runs unmeasured warmups followed by measured executions. `STATISTICS IO/TIME` provides CPU/elapsed time and logical, physical and read-ahead reads. Optional XML plan analysis reports missing indexes, table scans and implicit conversions. Without `SHOWPLAN`, it can return metrics and a permission note.

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
| `SQL-AI-0105` | Connection/infrastructure failure |
| `SQL-AI-0106` | Execution timeout |
| `SQL-AI-0107` | Mutation blocked or access level does not permit data execution |
| `SQL-AI-0108` | Object-reference lookup requires table/view |
| `SQL-AI-0109` | Routine-parameter lookup requires procedure/function |
| `SQL-AI-0110` | Detail lookup requires table/view |
| `SQL-AI-0111` | Script file not found |
| `SQL-AI-0112` | Script exceeds configured size limit |
| `SQL-AI-0113` | Script extension is not `.sql` |
