# A committed write looks like an empty read

Order: 4. Run after [05](05-german-statistics-messages.md). Leave the metrics at the end of the line as they are.

## Problem

Every successful `sql_execute_query` is reported as `N rows returned`. On a `ReadWrite` target, `UPDATE ... WHERE 1 = 0` completed on the committing path and came back as `0 rows returned` plus `[]`, the same shape as an empty `SELECT`.

`SQL-AI-0107` already rejects writes on read-only targets. Leave that guard as it is.

## Required outcome

Keep the current tail: `in {ms} ms | cpu: {cpu} ms | logical reads: {reads}.`

Insert the access level and the transaction outcome at the front:

```text
Execution Info: accessLevel={Name} | transaction={committed|rolled_back} | {count phrase} in {ms} ms | cpu: {cpu} ms | logical reads: {reads}.
```

`{Name}` is the `AccessLevel` enum name. Use `transaction=committed` only for `ReadWrite`. Every other level uses `transaction=rolled_back`.

`{count phrase}` is `{rowCount} rows returned` when any result set has `FieldCount > 0`. When every result set has `FieldCount == 0`, it is `{N} rows affected`, where `N` is `command.RecordsAffected` after the reader is disposed. The data block stays `[]` when no row was serialized.

`INSERT ... OUTPUT` has `FieldCount > 0`, so it keeps `rows returned`.

Do not change `sql_execute_file`. Do not change `sql_list_databases`.

## Documentation

In `docs/tools.md`, replace the sentence that the header reports returned rows, client query time, server CPU time and logical reads. State the line above, and that `rows affected` is used only when every result set has `FieldCount == 0`.

## Tests

- A read-only `UPDATE` is still `SQL-AI-0107`.
- A `ReadWrite` update with a false predicate succeeds with `accessLevel=ReadWrite`, `transaction=committed`, `0 rows affected`, data `[]`, and without `rows returned`.
- A `ReadWrite` `SELECT` contains `rows returned` and `transaction=committed`, and does not contain `rows affected`.
- A `ReadOnly` `SELECT` contains `accessLevel=ReadOnly` and `transaction=rolled_back`.
- A `ReadOnlyAnonymized` `SELECT` contains `accessLevel=ReadOnlyAnonymized` and `transaction=rolled_back`.

## Out of scope

- Script-report suffixes.
- Changing which databases are `ReadWrite`.
