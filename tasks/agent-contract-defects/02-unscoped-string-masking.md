# Literals and intrinsics are masked

Order: 3. Implement after [03](03-hidden-browse-columns.md) and [05](05-german-statistics-messages.md).

## Problem

On a `ReadOnlyAnonymized` target, `AnonymizeCell` tokenizes every string whose column origin is missing. These statements were tokenized, and `BuildAnonymizationNote` described the output alias as a searchable column:

```sql
SELECT @@LANGUAGE AS LanguageName
SELECT 'hello' AS Msg, 1 AS N
```

`LanguageName` became a token. `N` stayed `1`. There is no base column. On a cleartext target, `@@LANGUAGE` returned `Deutsch`.

A bare projection of a protected column was tokenized, the notice named that column, and reusing the token in a predicate on the same target returned the same row. That path stays.

`docs/security.md` says an incomplete origin stays masked in two places: "If any part of that origin is unavailable, the result stays masked by default." and "including default masking when the origin is incomplete." That fail-safe stays for a statement that still references a column. It is the wrong outcome for a statement that references none.

## Definitions

A column reference is a ScriptDom `ColumnReferenceExpression` or `SelectStarExpression`. `FROM dbo.T` is not a column reference.

`SqlParseResult.Success` means `Errors.Count == 0` and the fragment is not null. Anything else is a parse failure.

## Required outcome

When anonymization would otherwise run, and `SqlParseResult.Success` is true, and the tree has no column reference: return string cells unchanged and emit no anonymization notice.

`SELECT 'hello' AS Msg`, `SELECT 'hello' AS Msg FROM dbo.T`, and `SELECT @@LANGUAGE AS LanguageName` are in this group. The `@@LANGUAGE` cell is the unchanged language string.

`SELECT *` and `SELECT t.*` have a `SelectStarExpression`. They stay on the current masking path.

A string expression that references a protected column and whose origin is null stays masked.

`SELECT LEN(ProtectedName)`, `WHERE ProtectedName LIKE '%'`, and `CASE WHEN ProtectedName = 'x' THEN 1 ELSE 0 END` execute. Do not reject them. Do not require the derived `LEN` or `CASE` value to be masked.

On parse failure, keep today's cell masking. Do not return that statement in clear.

## Implementation

After the existing ScriptDom parse, and only when this execution would anonymize:

- Success and no column reference: serialize with anonymization disabled.
- Success and at least one column reference: keep today's `AnonymizeCell` behavior, including masking a null origin.
- Parse failure: keep today's `AnonymizeCell` behavior.

Do not change `BuildAnonymizationNote`. A no-column statement runs with anonymization disabled, so `WasAnonymized` stays false and `ToolDispatcher` does not emit the notice.

In `docs/security.md`, replace both incomplete-origin sentences with: an expression that contains a column reference and has an incomplete origin stays masked; a statement with no column reference, as defined above, is returned unchanged. Leave the sentence that empty strings and non-string values stay unchanged.

## Tests

Fixture or the existing anonymization doubles. No production rows.

- `SELECT 'hello' AS Msg` returns `hello`, `WasAnonymized=false`, and no notice.
- `SELECT 'hello' AS Msg FROM dbo.T` returns `hello`.
- `SELECT @@LANGUAGE AS LanguageName` returns the unchanged language string, `WasAnonymized=false`, and no notice.
- `SELECT *` from a table with a protected string column stays on the masking path: that column is masked or tokenized.
- A string expression over a protected column with a null origin stays masked.
- `SELECT LEN(ProtectedName)`, a `LIKE '%'` predicate on that column, and `CASE WHEN ProtectedName = 'x' THEN 1 ELSE 0 END` all execute and are not `SQL-AI-0107`.
- A cleartext access level is unchanged and emits no notice.

## Out of scope

- Rejecting predicates or functions on protected columns.
- Detokenizing parameters.
- Changing the schema flag or which access level returns cleartext.
