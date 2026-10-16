# Development and CLI

## Build and test

Use the .NET 10 SDK and PowerShell from the repository root:

```powershell
dotnet build SqlToAi.slnx
dotnet test SqlToAi.slnx
```

Tests use xUnit v3. [SqlServerFixture](../tests/SqlToAi.Tests/Integration/SqlServerFixture.cs) explicitly loads the separate fictional DemoDB profile copied as `appsettings.integration.json`, then a checked-in test-only overlay copied as `appsettings.integration.test.json`; it never reads the maintainer's user configuration root. The demo profile and application defaults keep certificate validation enabled. The overlay opts only general local integration tests into trusting the existing self-signed server certificate, while encryption remains required. This exception is limited to the test fixture and has no effect on appsettings, user configuration, or the dedicated TLS cases. General SQL integration tests require a local SQL Server matching the demo profile and fictional schema. [sql-scripts/README.md](../sql-scripts/README.md) describes setup. Contributor instructions start at [AGENTS.md](../AGENTS.md).

Use test categories to choose what infrastructure a run needs. The offline selection includes configuration checks and in-memory MCP tests even when their files are under `Integration`; ordinary SQL tests use the shared `SqlServerFixture`, while TLS tests use only their explicitly configured endpoint profile:

```powershell
dotnet test SqlToAi.slnx --filter 'Category!=Integration&Category!=TlsIntegration'
dotnet test SqlToAi.slnx --filter 'Category=Integration'
dotnet test SqlToAi.slnx --filter 'Category=TlsIntegration'
```

The first command must discover and run tests without SQL Server or personal settings. Ordinary SQL tests fail when their configured local database is unavailable. TLS cases with no `SQLTOAI_TLS_TEST_PROFILE` or with a missing endpoint section are reported as skipped and remain unverified; configured endpoint failures fail the selection.

The general integration fixture accepts explicit test-process environment overrides after loading the demo profile and test-only overlay, using the `SQLTOAI_INTEGRATION_` prefix followed by the configuration path with double underscores. An explicit environment value such as `SQLTOAI_INTEGRATION_SqlToAi__SqlServer__TrustServerCertificate=false` overrides the overlay. The checked-in overlay makes the ordinary local suite work with the existing self-signed instance without a manual environment override:

```powershell
dotnet build SqlToAi.slnx
dotnet test SqlToAi.slnx
```

The overlay accepts the presented certificate without authenticating server identity; encryption remains required. The exception is explicit, fixture-only, never inferred, and does not edit shipped profiles or user settings. Other fixture options use the same prefix, for example `SQLTOAI_INTEGRATION_SqlToAi__SqlServer__Server`. Dedicated metadata/rule endpoints require their own explicit trust settings. A successful general suite using this exception does not establish secure-default certificate authentication.

### Local TLS evidence

[TlsConnectionIntegrationTests](../tests/SqlToAi.Tests/Integration/TlsConnectionIntegrationTests.cs) loads only the test input file explicitly selected by `SQLTOAI_TLS_TEST_PROFILE`. It ignores the general fixture profile, overlay, and environment overrides, and selects trust independently for every case. Its endpoint inputs must omit the trust property or set it to `false`; an absent file setting or endpoint section is reported as skipped/unverified, never passed. An invalid configured file or failed configured case fails the test.

Store this file outside the checkout. Each optional section has this shape, using an existing fictional test database and an account allowed to read its own connection row in `sys.dm_exec_connections`:

```json
{
  "LocalUntrusted": {
    "Database": "DemoDB",
    "SqlServer": {
      "Server": "localhost\\MSSQLSERVER2022",
      "IntegratedSecurity": true,
      "ConnectTimeoutSeconds": 5,
      "TrustServerCertificate": false
    }
  }
}
```

Use credentials appropriate to that existing test instance; SQL authentication uses `UserId`, `Password` and `IntegratedSecurity=false`. Do not commit credentials or print the input file. The input must omit `TrustServerCertificate` or set it to `false`; the test itself applies deliberate exceptions. No certificate, trust-store or server configuration changes are performed.

- `TrustedTcp`: an already available `tcp:host,port` endpoint whose certificate chain, validity and name validate for the test process. Default connections must succeed.
- `UntrustedTcp`: an already available `tcp:host,port` endpoint with an untrusted certificate. Defaults must reject it; separate explicit exceptions must succeed with observed encryption.
- `LocalUntrusted`: an optional existing local untrusted named instance. This provides limited evidence when TCP is unavailable. The observed transport is reported and must not be presented as TCP evidence.

```powershell
$env:SQLTOAI_TLS_TEST_PROFILE = 'C:\LocalTests\sqltoai-tls.json'
try {
    dotnet test SqlToAi.slnx --filter 'FullyQualifiedName~TlsConnectionIntegrationTests' --logger 'console;verbosity=detailed'
} finally {
    Remove-Item Env:SQLTOAI_TLS_TEST_PROFILE
}
```

The cases open real primary and provider-created dedicated connections and query `encrypt_option` and `net_transport` for `@@SPID`; TCP sections require `TRUE|TCP`. Provider connection creation is inspected through each provider's existing private method, without a production test hook. Metadata also runs the observation query inside its actual custom-description lookup. The rule provider's actual optional lookup runs separately against a deliberately missing fictional table: it verifies connection-error handling/default masking, but its lookup session is not directly observed. The separately observed rule-provider-created connection must not be described as observation of that lookup session. No business rows or SQL objects are modified by these TLS cases.

Rejection cases retain their own results alongside successful exception cases. Dedicated strict providers run with the primary exception enabled and verify that the primary factory is never called. Local fallback cases use opposite secondary settings and verify primary policy; a real primary validation-service failure must return `SQL-AI-0105`. Captured optional-provider logs identify the section and TLS failure without exception payloads. A skipped trusted/TCP case remains missing acceptance evidence even if the rest of the suite succeeds. Record endpoint labels, trust settings, observed transport/encryption and limitations when reporting results.

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

Configuration is loaded from `%LOCALAPPDATA%\SqlToAi\appsettings.json`; create and configure it manually from the shipped conservative template. Relative script input paths resolve against the process working directory. See [tool arguments](tools.md) and [configuration](configuration.md).

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

[scripts/deploy.ps1](../scripts/deploy.ps1) stops running processes named `SqlToAi`, invokes tests, replaces `publish/` and produces a self-contained single-file `win-x64` build with manual settings templates, a package README and license. It does not use Native AOT. Runtime settings and logs live under the separate Windows user root and are preserved when package files are replaced; publish-directory settings are templates only. See [manual package setup](../SETUP.md) for initial setup and manual adaptation of legacy settings.

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
