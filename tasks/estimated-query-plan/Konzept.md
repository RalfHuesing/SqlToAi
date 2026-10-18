---
status: draft
---

# Estimated query-plan diagnosis without execution

## Intention

Let agents inspect how SQL Server expects to execute a SELECT before spending
time on execution or proposing an optimization. Return a small operator tree and
optimizer estimates rather than an XML dump or a premature tuning verdict.

## Settled direction

The user has requested this additional concept. First scope is an estimated plan
without executing the query; actual runtime diagnosis remains with existing tools.

## Current behavior

- [`sql_measure_performance`](../../docs/tools.md#measurement) already measures
  CPU/time/reads and extracts selected warnings from actual XML plans.
  [`PerformanceMeasurementService`](../../src/SqlToAi/Database/PerformanceMeasurementService.cs)
  executes warmups/measured runs; setting runs to zero is not a no-execution mode.
- Existing XML extraction covers missing indexes, scans and implicit conversions,
  but not a bounded operator hierarchy or its row/cost estimates.
- [`sql_validate_query`](../../docs/tools.md#discovery-and-schema) uses NOEXEC
  compilation, not a dedicated estimated-plan output contract.

## Scope

### Must

1. Add `sql_explain_query` with required `database` and `query`, optional typed
   `parameters`. Use the [qualified target](../../docs/tools.md)
   resolver in the final combined feature set, and the existing binder, safe connection errors,
   command timeouts and cancellation conventions.
2. Permit a single read-only SELECT with supported leading declarations. Enforce
   the read-only AST guard even at ReadWrite access and reject caller execution,
   transaction, SHOWPLAN and STATISTICS controls before SQL work. Permit SchemaOnly
   because this operation returns planning metadata; None remains denied. SQL
   Server still requires SELECT and SHOWPLAN permissions on referenced databases.
3. Use a dedicated connection path with `SET SHOWPLAN_XML ON`, the parameterized
   query, and `SET SHOWPLAN_XML OFF` as separate batches. Never use the measurement
   service or retry through normal execution. Do not layer this path on NOEXEC.
   Microsoft documents these semantics and permissions in
   [SET SHOWPLAN_XML](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-showplan-xml-transact-sql).
4. Reset SHOWPLAN before other cleanup SQL using cancellation independent of the
   caller. On uncertain reset/cleanup, dispose and exclude the affected connection
   from reuse through the provider-supported mechanism. Failure, timeout or missing
   permissions must never cause a normal data-query fallback.
5. Return a compact JSON summary with qualified target, `plan_kind=estimated`,
   logical/physical operator, stable node/parent IDs, estimated rows and subtree
   cost when present, plus allowlisted compiler-warning categories. A truncated
   tree identifies omitted children and preserves their visible parent identity.
   Cost is an optimizer unit, not elapsed milliseconds or measured resource use.
6. Do not return raw XML, StatementText, parameter values, ScalarString or arbitrary
   expression text. Row masking is not a protection for plan text. Use allowlisted
   fields and restrict object identities to metadata needed for diagnosis; do not
   copy the existing unfiltered conversion-expression warning into the new tool.
7. Propose options for 2 MiB XML input, 100 returned operators, depth 16 and
   64 KiB serialized output. Bound XML reads/parsing and reject external entities.
   Keep metadata whole; indicate operator omissions explicitly. Distinguish an
   unavailable/invalid plan, a bounded partial summary and a valid warning-free
   plan. XML beyond the input limit fails explicitly without executing the query.
8. Reuse existing warning extraction only where its contracts fit; shared parser
   changes must preserve existing measurement behavior and error handling.

### Out of scope

- Actual row counts, runtime cardinality differences, measured spills, observed
  memory grants, CPU/IO measurements or an optimization recommendation verdict.
- Query Store, workload collection, plan persistence, raw XML export, automatic
  SQL rewriting, index creation or execution of stored procedures.
- Claims that a scan alone is a defect or that an estimated plan proves speed.

## Verification

- Integration checks demonstrate no result rows or normal SELECT execution,
  including permission failure and cancellation paths; no execute fallback exists.
- DML, EXEC, SELECT INTO, multiple main statements and caller control commands
  fail before SHOWPLAN work, including on a ReadWrite target.
- Parameterized queries and supported declarations work with the actual SqlClient
  invocation. Missing SELECT/SHOWPLAN rights return a bounded actionable failure.
- Success, SQL error, parse failure, timeout and cancellation leave no reusable
  connection with SHOWPLAN enabled. Test cleanup failure and discard behavior.
- XML fixtures cover joins, scans, estimated row/cost attributes, absent attributes,
  warning categories, unknown nodes, input limits and tree truncation. Unknown
  estimates stay null rather than becoming fabricated zero values.
- Sensitive literals in StatementText/expressions/parameter sections do not occur
  in the summary. Serialized output stays within budget with escaped identifiers.
- Existing measurement/benchmark behavior and registered SDK/AOT contracts remain
  covered when parser code is shared.

## Decisions awaiting approval

- Additional concept requested; proposed metadata-only SELECT scope is documented.
- Proposed limits and SchemaOnly eligibility await final concept approval.
