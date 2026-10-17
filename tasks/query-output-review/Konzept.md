---
status: draft
---

# Query-output review and deferred delivery extensions

## Intention

Record the evidence and the user's decision about query output without scheduling
an unproven output redesign. Preserve useful information in the normal agent
workflow; revisit representation only when a concrete problem justifies it.

## Settled decisions

- The user's normal agent workflow uses targeted queries and aggregates.
- CSV and retained-result snapshots/resources are deferred. A dedicated CSV export
  is also deferred; it is not part of the current feature scope.
- Do not introduce artificial shortening or another row-dropping response budget.
- There is no demonstrated need to change the ordinary output/default today.

## Current behavior and evidence

- [`sql_execute_query`](../../docs/tools.md#query-and-script-execution) returns
  separate text blocks for a masking notice when applicable, execution metrics,
  row data and a catalog/schema hint when applicable. These serve distinct
  purposes; no unnecessary block has been established by this review.
- [`QueryExecutionService.Anonymization.cs`](../../src/SqlToAi/Database/QueryExecutionService.Anonymization.cs)
  serializes one JSON row dictionary per line: NDJSON, with `[]` for empty data.
  Repeated column names are structural redundancy, not additional business data.
  Removing repeated keys could preserve information, but would change the reader
  contract and is not automatically useful for a model's reasoning.
- Shipped [row limits](../../docs/configuration.md#settings-reference) are 100
  default and 1,000 maximum. They limit rows, not individual large cell values or
  response bytes. Actual large-output frequency has not been measured; this review
  did not inspect user records or logs and cannot claim MB-scale dumps occur.
- An illustrative synthetic fixture of 200 rows with four short columns used
  13,692 bytes as NDJSON, 5,851 as positional JSON with typed column metadata and
  4,725 as CSV. These are ASCII payload bytes, excluding notices, metrics and MCP
  escaping; they are not token counts, a workload benchmark or a guaranteed saving.
- A separate correctness concern exists: case-insensitive row dictionaries can
  overwrite duplicate/case-variant result labels. That deserves a narrow,
  reproduction-based assessment if prioritized; it does not justify dropping rows
  or redesigning every result contract. No correction is implemented in this task.

## Scope

### Must

1. Keep the existing normal result behavior and useful notices/metrics/hints.
   No new formatter, output-byte cutoff, file exporter or snapshot store is
   authorized by this review. Existing configured row limits remain unchanged.
2. Reconsider compact encoding only with representative synthetic/approved query
   shapes, measured full-response bytes and evidence of actual agent benefit.
   Evaluate ease of interpretation as well as size. Any candidate must preserve
   every returned cell, column identity, null/type semantics and existing masking.
3. Treat missing-column correctness and repeated-key efficiency as separate
   questions. A future correctness task must reproduce duplicate labels first and
   choose a verifiable contract; no fixed saving percentage is an acceptance goal.
4. Revisit CSV only for a deliberate presentation/file-handoff need; ordinary CSV
   cannot reliably preserve SQL type and NULL/empty distinctions. Revisit snapshots
   only when stable follow-up reads are repeatedly needed. A resource URI alone
   does not guarantee context savings because
   [MCP hosts control resource inclusion](https://modelcontextprotocol.io/specification/2025-06-18/server/resources).

### Out of scope

- Implementation or a roadmap for compact JSON, CSV, data export, response budgets,
  snapshots, resource handlers or a duplicate-label correction.
- Collecting production rows/logs, adding tokenizer dependencies or optimizing for
  an unsupported claim that the normal agent workload fetches huge tables.

## Verification

- Review agrees with the current tool/reader contracts and configured row limits.
- No runtime, configuration default or query output changes in this planning slice.
- The active feature concepts remain
  [named servers](../named-connections/Konzept.md),
  [environment context](../database-environment/Konzept.md) and
  [estimated plans](../estimated-query-plan/Konzept.md).
- Before any deferred feature becomes active, obtain an explicit scope decision
  and replace this review with a focused concept and measurable acceptance criteria.
