# Hidden browse columns in anonymized results

Order: 1.

Status: completed and verified.

Implementation: `gpt-6.1-sol`, reasoning effort `high`.

Visible result columns retain their reader ordinals. Schema rows marked
`IsHidden=true` are omitted from JSON and anonymization notices on both execution
paths; full origin metadata and the existing reader behaviors remain intact.
Unavailable hidden metadata preserves all fields. Selected binary values remain
base64. The result contract is documented in [tools](../../docs/tools.md#query-and-script-execution).

Verification: `dotnet build SqlToAi.slnx` succeeded with no warnings or errors;
`dotnet test SqlToAi.slnx` passed 1,970 tests with 19 configured TLS integration
cases skipped (1,989 total). The 14 new fake-reader regression cases all passed,
including hidden intermediate ordinals, reordered schema rows, source exclusions,
null/missing/unsupported schema metadata, projected keys and selected `byte[]`.
The new cases use no database; the full suite uses its existing test fixtures.

`02` turns cell anonymization off when a statement has no column reference. This file's serializer test does not use that production decision. It calls the serializer with anonymization forced on, so the reader is opened with `CommandBehavior.KeyInfo`.

## Problem

Anonymized execution opens the reader with `CommandBehavior.SequentialAccess | CommandBehavior.KeyInfo` (`QueryExecutionService.ExecuteAndSerializeAsync`). `KeyInfo` asks SQL Server for browse metadata. The server appends hidden columns: the key columns of the row, and the `rowversion` when the table has one.

`GetColumnNames` copies `reader.FieldCount` through `GetName` and never reads `IsHidden`. `AppendSerializedRow` serializes every name.

Reproduced on a `ReadOnlyAnonymized` target, on a table with a composite integer key and a `rowversion`:

```sql
SELECT TOP (1) 1 AS X FROM <that table>
```

The JSON object contained `X`, both key columns, and the `rowversion`. The same extra columns appeared for a projection of two protected strings and for a projection of `LEN(...)` expressions.

`SELECT TOP (1) 1 AS OnlyThis` from a table without a `rowversion`, on the `ReadWrite` target, returned only `OnlyThis`. That path uses `CommandBehavior.SequentialAccess` only. `SESSIONPROPERTY('NO_BROWSETABLE')` was null.

## Required outcome

Serialized JSON keys are the projected columns only.

Anonymized reads keep `CommandBehavior.SequentialAccess | CommandBehavior.KeyInfo`. Cleartext reads keep `CommandBehavior.SequentialAccess` only. Do not remove `KeyInfo` from the anonymized path. Do not add `KeyInfo` to the cleartext path.

`GetColumnNames` skips a field whose schema row has `IsHidden=true`. Apply that filter on both paths whenever the schema table has an `IsHidden` column. Hidden key and `rowversion` columns remain available to origin resolution and are absent from the serialized row and from the anonymization notice.

When `GetSchemaTable()` returns null, or the schema table has no `IsHidden` column, serialize every `GetName(i)` for `i` in `0 .. FieldCount-1`. Do not throw.

A `rowversion` the caller actually selects stays a `byte[]` and stays base64. Do not add a hex encoding.

## Tests

No production database.

Force `Anonymize=true` so the reader uses `KeyInfo`. Use a reader whose schema marks the key columns and the `rowversion` `IsHidden=true` for the statement `SELECT 1 AS X FROM that table`. The serialized object has only the key `X`.

On a `ReadOnlyAnonymized` target, `SELECT ProtectedName FROM that table` serializes only `ProtectedName`, tokenized, and the notice does not name the key or the `rowversion`.

`SELECT Id FROM that table`, where `Id` is the primary key and is not hidden, serializes only `Id`.

A cleartext executor test runs `SELECT 1 AS X FROM that table` with `CommandBehavior.SequentialAccess` only and serializes only `X`.

A test with `GetSchemaTable()` returning null, and a test whose schema table has no `IsHidden` column, serializes the same names as `GetName` over `FieldCount`, including browse columns when the reader has them, and does not throw.

## Out of scope

- Hex encoding of a selected `byte[]`.
- Duplicate output aliases (`SELECT 1 AS A, 2 AS A`). [query-output-review](../query-output-review/Konzept.md) already deferred that.
