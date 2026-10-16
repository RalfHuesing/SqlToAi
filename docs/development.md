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

### Shared local and CI validation

Run the same command used by the [Windows validation workflow](../.github/workflows/validate.yml):

```powershell
pwsh -NoProfile -File .\scripts\validate.ps1
```

[validate.ps1](../scripts/validate.ps1) restores dependencies, audits direct and transitive packages at every severity, builds with warnings as errors and runs the offline selection above. An empty test selection is an error. Each failed native command stops the remaining steps and returns failure. Output, including advisory URLs, is retained in the normal terminal/workflow log. The workflow runs on relevant pull requests and `main` changes with read-only repository permissions, no SQL credentials and no private secrets.

Restore uses `NuGetAudit=true`, `NuGetAuditMode=all`, `NuGetAuditLevel=low` and `TreatWarningsAsErrors=true`. It forces reevaluation and bypasses the HTTP cache. [NuGet.Config](../NuGet.Config) explicitly selects nuget.org as the audit source; unavailable vulnerability data (`NU1900`) and a source without vulnerability information (`NU1905`) fail alongside findings (`NU1901`–`NU1904`). A successful audit covers the resolved dependency graph against the source's available advisory data at that time; it is not proof that dependencies contain no vulnerabilities.

Fix findings by upgrading the affected dependency or its parent; `dotnet nuget why SqlToAi.slnx <package-id>` helps trace a transitive dependency. If a fix cannot be applied, a maintainer must review the specific advisory and risk before adding a `NuGetAuditSuppress` item with its exact advisory URL to the affected project (or [Directory.Build.props](../Directory.Build.props) for a repository-wide exception). Add a nearby comment linking the review/issue, naming the affected package/version, explaining the accepted risk and stating when to revisit it. Include accepted exceptions in release notes. Suppression removes that advisory from SDK diagnostics, so review these source changes and the recorded exceptions explicitly; a green run with an exception does not mean no findings existed. There are no approved exceptions currently. Do not disable auditing, raise its severity threshold or suppress entire warning codes to accept a finding or source failure. See [NuGet's audit and per-advisory suppression reference](https://learn.microsoft.com/nuget/concepts/auditing-packages).

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

The script checks each native `dotnet` exit code and stops on failure. Failed tests leave the existing `publish/` directory intact; the test step restores the caller's working directory even on failure. A failed publish stops before package verification and completion messages, but may leave partial output after the previous package was removed. After redeployment, reload the MCP server entry in the client or restart the client: its old stdio session was terminated.

### Create and check a Windows ZIP

[package.ps1](../scripts/package.ps1) creates a new output directory, publishes the current project as a self-contained, single-file `win-x64` executable with native-library extraction, and archives the complete publish directory without a directory prefix. Project publish rules supply `appsettings.json`, `demo-profile.json`, the package `README.md` and `LICENSE`. Native publish failure stops packaging. An existing output directory is refused; partial output from a failed publish remains for inspection.

```powershell
$output = Join-Path $env:TEMP ('SqlToAi-package-' + [guid]::NewGuid().ToString('N'))
$package = & .\scripts\package.ps1 -OutputDirectory $output
$package | Format-List
```

The script returns one object with absolute `ZipPath`, lowercase `Sha256` and `ChecksumPath` properties. `SqlToAi-win-x64.zip.sha256` contains the SHA-256 and ZIP filename in standard checksum-file form. Keep the checked ZIP bytes when handing the package to the release workflow; do not rebuild between checking and upload.

[smoke-package.ps1](../scripts/smoke-package.ps1) requires Windows and explicit `-DisposableProfile`. Run it only on a fresh GitHub Windows runner or an already disposable Windows profile. It resolves Windows Local Application Data exactly as the application does and refuses any existing `SqlToAi` user root before launching. The switch asserts that the current profile is disposable; it does not create isolation. Do not run it against the maintainer's normal profile or redirect the production configuration root through environment variables. Local startup tests continue to use the existing temporary-root startup harness.

Only in that disposable environment, with the package object above:

```powershell
$checked = & .\scripts\smoke-package.ps1 -ZipPath $package.ZipPath -DisposableProfile
if ($checked.Sha256 -cne $package.Sha256) { throw 'Package checksum mismatch.' }
```

The check extracts the ZIP into an owned temporary path containing spaces and runs its unchanged `SqlToAi.exe` from a different working directory. First it requires exit `1`, empty stdout, actionable missing-settings stderr and no user-root/settings creation. It then prepares settings from the shipped conservative template with empty grants/server/credentials, disabled optional providers and file sinks, and an unknown-key preservation probe. Application environment overrides are removed only from the child environment. It completes MCP `initialize` / `notifications/initialized` / `tools/list` using protocol `2025-06-18`, requires the exact 17 documented tool names, parses stdout as protocol, drains stderr separately and checks a clean exit after stdin closes. It makes no SQL tool calls.

Settings and ZIP SHA-256 values must remain unchanged. Each response, process-exit and stream-drain wait is bounded by `-TimeoutSeconds` (default 30, range 1–120). Failure stops the check; cleanup terminates only its owned running child process tree and removes its own temporary extraction and newly created user root. The successful result contains `ZipPath` and `Sha256`; diagnostic stderr is available with `-Verbose`. A self-contained publish plus a successful packaged EXE startup on an SDK-equipped runner is not evidence of execution on an SDK-free machine. Hosted package execution is recorded separately from local harness/stub verification in the [release-validation roadmap](../tasks/release-validation/roadmap.md#t4--smoke-test-the-actual-windows-zip).

## Release

### Tagged ZIP draft and manual publication

The [tag workflow](../.github/workflows/release.yml) creates a **draft**, never an automatically public release. Both jobs check out the triggering source commit. [release-source.ps1](../scripts/release-source.ps1) requires the existing `vX.Y.Z` tag, checked-out commit, workflow source commit and the project's explicit `Version` to agree. The Windows build job has read-only repository permissions and runs the shared dependency audit/build/offline checks before creating one ZIP and running the actual packaged EXE with the fresh runner's disposable profile. No SQL credentials or SQL tests run on GitHub. This remains an SDK-equipped runner check.

The workflow transfers the checked ZIP, its `.sha256` file and release notes through one run's artifact. Only the dependent draft job has `contents: write`. [create-release-draft.ps1](../scripts/create-release-draft.ps1) rechecks source agreement and verifies the downloaded ZIP and checksum against the SHA-256 produced after smoke verification, then uses `gh release create --draft --verify-tag`. It attaches those unchanged ZIP/checksum files and puts source commit, version, hash, automated results and the run URL in the draft notes. Package creation is not repeated in the draft job.

Before publishing in GitHub's normal release editor:

1. Inspect the automated run and the draft's source/version/hash. Record successful ordinary SQL execution against that **exact source** in its pending local SQL section: `dotnet test SqlToAi.slnx --filter 'Category=Integration'`, commit, executed/failed/skipped counts and a short sanitized result. A tag or green workflow does not prove local SQL execution; missing/failed ordinary SQL blocks publication.
2. Add dedicated TLS command/results and unverified cases. The accepted lack of trusted/untrusted TCP endpoints remains a disclosed limitation to review for this release. Failed configured TLS cases block publication. Local success with a trusted self-signed certificate exception does not establish trusted TCP certificate validation.
3. Review this source's `NuGetAuditSuppress` items and record each specific approved advisory/package/version/risk exception, or explicitly record none after review. A successful audit can include suppressions; unresolved findings cannot be treated as reviewed exceptions.
4. Publish the **existing draft and checked assets** through GitHub. Do not rebuild or replace the ZIP after review. Changed source or package bytes require the applicable checks and fresh review.

Every existing release for the tag is refused, including drafts; public assets and prior review notes are never silently overwritten. A failed upload can leave a partial draft. Inspect it before deliberately deleting that draft and rerunning; the new notes start all manual review fields as pending again. A public release is not a retry target. Same-tag workflow executions are serialized, and a concurrent release creation fails rather than updating assets. No automatic promotion service or external-publication task is needed to publish the completed draft.

The workflow wiring and controlled local verification are recorded in the [release-validation roadmap](../tasks/release-validation/roadmap.md#t5--keep-releases-usable-through-github-drafts). They do not establish a successful hosted run; fresh-runner EXE execution and real draft creation remain separate execution gates until authorized and performed.

### Local release helper

[scripts/release.ps1](../scripts/release.ps1) currently requires PowerShell 7 and a clean checkout on `main`. It increments the patch version, builds/tests, commits the version change, pushes `main` and a `vX.Y.Z` tag. The tag workflow above creates a draft for manual review. Preview the planned steps without making changes:

```powershell
.\scripts\release.ps1 -DryRun
```

Running without `-DryRun` publishes Git state; use it only when releasing is intended. `-SkipTests` currently bypasses tests and does not establish release validation. The helper does not yet produce the exact-source SQL/TLS summary required for publication; follow the manual review steps above. Its final release URL/status is not proof of a public release. Native build/test and Git failures stop dependent steps. Git inspection errors also fail the dry-run; its status query disables optional Git locks so it does not refresh the index. Local Git authentication must be configured; `gh` is optional for status monitoring. When authenticated, failed workflow or release queries stop monitoring with an error rather than reporting release success. Earlier completed Git operations are not rolled back.
