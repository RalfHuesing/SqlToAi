# Development and CLI

## Build and test

Use the .NET 10 SDK and PowerShell from the repository root:

```powershell
dotnet build SqlToAi.slnx
dotnet test SqlToAi.slnx
```

Tests use xUnit v3. [SqlServerFixture](../tests/SqlToAi.Tests/Integration/SqlServerFixture.cs) locates an `appsettings.json` in the test output or source tree and reads it directly, without the host's environment-variable configuration provider. Integration tests require its configured SQL Server and fictional schema. [sql-scripts/README.md](../sql-scripts/README.md) describes setup. Contributor instructions start at [AGENTS.md](../AGENTS.md).

## Manual tool calls

With no arguments, or with `server`, the executable starts MCP stdio. `query <tool>` calls a SQL tool once through the same dispatcher:

```powershell
.\SqlToAi.exe query --help
.\SqlToAi.exe query sql_execute_query --help
.\SqlToAi.exe query sql_list_databases
.\SqlToAi.exe query sql_execute_query --database ReportingDB --query 'SELECT TOP 5 * FROM dbo.Customers'
.\SqlToAi.exe query sql_execute_file --file_path .\scripts\report.sql --database ReportingDB --requested_row_limit 100 --parameters '{"CustomerId":42}'
```

The last successful content block goes to stdout; preceding notices/metrics go to stderr. Errors go to stderr and exit with code 1. Export query rows as JSON Lines with normal shell redirection:

```powershell
.\SqlToAi.exe query sql_execute_query --database ReportingDB --query 'SELECT TOP 5 * FROM dbo.Customers' > rows.jsonl
```

Configuration is loaded beside the executable; relative script input paths resolve against the process working directory. See [tool arguments](tools.md) and [configuration](configuration.md).

## Schema export

The existing executable exports schema through shared application services,
without starting MCP stdio. Both options are required:

```powershell
.\SqlToAi.exe export-schema --database DemoDB --output C:\Doku\Database
```

Configuration, credentials, database access checks and logging use the same
startup as `server` and `query`; `SchemaOnly` access is sufficient. The target
must be absent or empty, including no child directories. There are no overwrite
or cleanup options.

The root `README.md` groups relative links by kind. Object documents go into
`tables/`, `views/`, `procedures/`, `functions/` and `triggers/`; unused directories
are omitted. Tables and views include columns, incoming/outgoing foreign keys,
indexes, constraints, static referencing entities and links to DML triggers.
Views also include their SQL definition. SQL procedures and scalar, inline
table-valued and table-valued functions include definitions and parameter
details. Trigger files include the definition and identify/link their parent
table or view. Coverage follows the existing schema operations, rather than
reconstructing table creation scripts.

SQL names appear literally in document headings, navigation and detail tables.
For names containing control characters, presentation uses visible escapes:
`\r`, `\n`, `\t`, or `\uXXXX`; a literal backslash is shown as `\\` to keep these
distinct. Markdown escaping and code-span delimiters retain literal backticks
and entity-like names such as `A&amp;B`. These presentation rules also apply to
the overview's database name and names in empty/unavailable-result notes.
They do not alter SQL identity, lookup parameters or original SQL definitions
and comments. Filenames
escape Windows-invalid characters, reserved names and mapping delimiters;
long names use a bounded prefix and deterministic hash. All links use that same
mapping with URI-escaped relative targets. All planned object paths are checked
for case-insensitive collisions before any directory or file is created.

Exit code zero means every operation and write succeeded. Existing successful
unavailable-definition notes are included and permit success. The first failed
service result or filesystem error returns a nonzero exit code and stops the
export. Files already written remain; there is no rollback or cleanup. Retry
with a new directory or manually empty the failed output first.

Export does not query business records or invoke metadata, anonymization-policy
or rule providers. Metadata descriptions, `Anonymized` indicators and generated
MCP invocation instructions are omitted. Comments in SQL definitions are retained.
Existing `server`, `query` and MCP output remain unchanged.

## Publish and redeploy

[scripts/deploy.ps1](../scripts/deploy.ps1) stops running processes named `SqlToAi`, invokes tests, replaces `publish/` and produces a self-contained single-file `win-x64` build with its configuration. It does not use Native AOT. Existing publish configuration must be backed up before replacement.

```powershell
.\scripts\deploy.ps1
```

The current script does not explicitly check native `dotnet` exit codes at each step; verify test and publish results instead of treating completion text as proof. After redeployment, reload the MCP server entry in the client or restart the client: its old stdio session was terminated.

## Release

[scripts/release.ps1](../scripts/release.ps1) requires PowerShell 7 and a clean checkout on `main`. It increments the patch version, builds/tests, commits the version change, pushes `main` and a `vX.Y.Z` tag. The [release workflow](../.github/workflows/release.yml) builds the GitHub release. Preview the planned steps without making changes:

```powershell
.\scripts\release.ps1 -DryRun
```

Running without `-DryRun` publishes Git state; use it only when releasing is intended. `-SkipTests` bypasses tests. Local Git authentication must be configured; `gh` is optional for status monitoring.
