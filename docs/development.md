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
