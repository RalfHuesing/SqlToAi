# Agent-contract defects

Status: completed and verified. No production data, database names, or customer identifiers belong in code, tests, docs, or commit messages.

Implement in this order. One file at a time.

| Order | File | Change |
| --- | --- | --- |
| 1 | [03-hidden-browse-columns.md](03-hidden-browse-columns.md) | Drop hidden browse columns from serialized rows |
| 2 | [05-german-statistics-messages.md](05-german-statistics-messages.md) | Parse German `STATISTICS` text |
| 3 | [02-unscoped-string-masking.md](02-unscoped-string-masking.md) | Leave literals and intrinsics with no column reference unmasked |
| 4 | [04-write-result-looks-like-a-read.md](04-write-result-looks-like-a-read.md) | Name access level and rows affected on the existing success line |

`02` turns cell anonymization off when a statement has no column reference. The serializer test in `03` forces anonymization on, so that later change does not skip `CommandBehavior.KeyInfo`.

## Final audit

Independent review with `gpt-6.1-sol`, reasoning effort `high`, found no actionable
defects within these four specifications. Reviewed the actual changes from
`a561111`, their query/script and dispatcher consumers, regression fixtures,
canonical documentation, and scope exclusions. Implementation commits follow the
prescribed order: `37a4b8e`, `de48549`, `90d4973`, `3356ff2`.

Latest full validation: `dotnet build SqlToAi.slnx` passed with 0 warnings and
0 errors; `dotnet test SqlToAi.slnx` passed 2,015 tests, with 19 unconfigured TLS
integration cases skipped and 0 failures (2,034 total). The final audit checked
the acceptance evidence and `git diff --check`; it did not repeat the full suite
or use production data. The individual specifications retain their slice-specific
verification history.

## Not in scope

- Cleartext on a `ReadWrite` or `ReadOnly` target. Do not tie the schema flag to the access level.
- Rejecting `LEN`, `LIKE`, or `CASE` on protected columns.
- Changing `SET ROWCOUNT`, `sql_list_databases`, `sql_execute_file` report text, or `byte[]` encoding.
- A new error when statistics text is unrecognized. `05` only adds German patterns.
- Scalar versus batch schema labels.
