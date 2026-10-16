# Development and CLI

## Build and test

Use the .NET 10 SDK and PowerShell from the repository root:

```powershell
dotnet build SqlToAi.slnx
dotnet test SqlToAi.slnx
```

Tests use xUnit v3. [SqlServerFixture](../tests/SqlToAi.Tests/Integration/SqlServerFixture.cs) explicitly loads the separate fictional DemoDB profile copied as `appsettings.integration.json`, then a checked-in test-only overlay copied as `appsettings.integration.test.json`; it never reads the maintainer's user configuration root. The demo profile and application defaults keep certificate validation enabled. The overlay opts only general local integration tests into trusting the existing self-signed server certificate, while encryption remains required. This exception is limited to the test fixture and has no effect on appsettings, user configuration, or the dedicated TLS cases. General SQL integration tests require a local SQL Server matching the demo profile and fictional schema. [sql-scripts/README.md](../sql-scripts/README.md) describes setup. Contributor instructions start at [AGENTS.md](../AGENTS.md).

## Support and contributions

Use [GitHub Issues](https://github.com/RalfHuesing/SqlToAi/issues) for non-sensitive
setup questions and reproducible bugs. Include the SqlToAi version, Windows/client
versions, a short reproduction, and the relevant sanitized error category. Replace
hostnames, database/object names, user names, paths, identifiers, SQL literals,
parameters, result rows, and tokens with fictional values. Do not attach settings
files, connection strings, raw logs, customer data, or full query/results. Check
the [security policy](../SECURITY.md) before reporting a suspected vulnerability;
the private report form is enabled for this repository.

Contributions should be focused and include the applicable tests and documentation.
Start with the repository [agent and development instructions](../AGENTS.md), run
the relevant build/test commands below, and describe any environment-limited checks.
Do not include real credentials or customer data in issues, examples, or changes.

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

Optional `-ResultsDirectory <path>` also writes the offline run's standard `offline.trx` there. The release helper uses this to summarize the same run without repeating it; ordinary validation needs no results directory.

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

For primary schemas, pass `--object_names` as **one JSON array token**. PowerShell
single quotes preserve the JSON double quotes; separate space-delimited names are
not array syntax. Scalar, list, combined and empty-array forms use the same
selection rules as [MCP calls](tools.md#primary-schema-selection):

```powershell
.\SqlToAi.exe query sql_get_schema --database DemoDB --object_name Orders
.\SqlToAi.exe query sql_get_schema --database DemoDB --object_names '["Orders","Customers"]'
.\SqlToAi.exe query sql_get_schema --database DemoDB --object_name ' Orders ' --object_names '[null," ","Customers","Orders"]'
.\SqlToAi.exe query sql_get_schema --database DemoDB --object_name Orders --object_names '[]'
```

Omitting `--object_names` or passing the JSON token `null` preserves scalar
Markdown. Any array selects the JSON envelope, even a single name or `[]` combined
with a scalar. An empty normalized selection fails. Malformed JSON/non-array
tokens are CLI parse errors; incorrect array entries fail tool validation. Batch
errors and omissions set the error flag, so their complete JSON envelope goes to
stderr and the command exits with code 1; successful batches go to stdout.

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

This command continues to export the entire catalog and accepts relative output
paths, resolved against the process working directory. The shared coordinator's
internal selected scope resolves every requested identity before writing, exports
each selected object once and keeps references to unselected objects as readable
names. It does not recursively include dependencies or trigger parents. The
internal scope is not a command option or an exposed MCP tool.

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
export. Cancellation also stops the export, including when a downstream service
converts cancellation into a failure. Files and child directories already created
remain, including incomplete files; there is no rollback or cleanup. The shared
outcome distinguishes completed, failed and cancelled exports and retains the
resolved path. For failed/cancelled exports, partial output means retained files
or child directories; an empty root alone is not partial output and can be retried.
Otherwise, retry with a new directory or manually empty the failed output first.

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

[package.ps1](../scripts/package.ps1) creates a new output directory and publishes the current project **once** as a self-contained, single-file `win-x64` executable with native-library extraction. It archives the complete publish directory without a directory prefix as `SqlToAi-win-x64.zip`, copies that archive to `SqlToAi-win-x64.mcpb`, and adds only `manifest.json`. Both contain byte-identical application payloads. Project publish rules supply `appsettings.json`, `demo-profile.json`, the package `README.md`, `LICENSE`, [THIRD-PARTY-NOTICES.txt](../THIRD-PARTY-NOTICES.txt) and the [unmodified .NET notices](../packaging/notices/) under `notices/`, including ordinary build/deploy output. Review the repository-owned dependency list and license terms when dependencies or the SDK/runtime change; the native SqlClient SNI component has separate Microsoft distribution terms.

Notice provenance: `packaging/notices/dotnet-10.0.11.txt` is the `THIRD-PARTY-NOTICES.TXT` from NuGet `Microsoft.NETCore.App.Runtime.win-x64/10.0.11`, SHA-256 `6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21`. `dotnet-9.0.13.txt` comes from `Microsoft.Bcl.Cryptography/9.0.13`, SHA-256 `40686c6447a7d5b5d3693068e4571b5f483d7ed335aeee773ef662440de4c5d5`; the reviewed 9.x runtime dependencies ship the same text. The .NET 10 packages' repeated text is identical to the 10.0.11 runtime notice. The root notice preserves package metadata attribution, the MIT/Apache texts, the MCP SDK's licensing transition and the full native SNI license from `Microsoft.Data.SqlClient.SNI.runtime/6.0.2` (SHA-256 `9335e8bad875dd7be4eebd55d2335eb6433d1cea61aadb3817af7807bef8932a`). These texts are included with every publish; they are not fetched during packaging.

The [manifest template](../packaging/manifest.json) takes its version from the project's explicit `Version`. It declares a binary server, Windows compatibility (`win32`), the x64 limitation and `${__dirname}/SqlToAi.exe` with no arguments. It has no `user_config`, credential mapping or extra runtime requirement. Bundle installation supplies client startup configuration; the required user settings file must still be prepared manually before startup. Packaging does not start the executable or install/register a client.

Manifest validation uses PowerShell's `Test-Json` with the unchanged [official MCPB v0.3 schema](../packaging/mcpb-manifest-v0.3.schema.json) from [revision `70fe3b34cd6dff1b3bba046638edc72a6467a4fb`](https://github.com/modelcontextprotocol/mcpb/blob/70fe3b34cd6dff1b3bba046638edc72a6467a4fb/schemas/mcpb-manifest-v0.3.schema.json), SHA-256 `3a0ac9d845711a1b9b17dfa5a52f8b60628239d6a86a9db417206a9efc78592d`. The [official manifest specification](https://github.com/modelcontextprotocol/mcpb/blob/70fe3b34cd6dff1b3bba046638edc72a6467a4fb/MANIFEST.md) and schema were checked on 2026-10-16. MCPB is a ZIP format; Node/npm or an MCPB CLI is not required to build or run these binary packages.

Missing required input, invalid metadata, failed native publish, missing published files or missing runtime notices stop packaging before a result object is returned. An existing output directory is refused; partial output from a failed operation remains for inspection.

```powershell
$output = Join-Path $env:TEMP ('SqlToAi-package-' + [guid]::NewGuid().ToString('N'))
$package = & .\scripts\package.ps1 -OutputDirectory $output
$package | Format-List
```

The script retains the result object's absolute `ZipPath`, lowercase `Sha256` and `ChecksumPath` properties and adds `McpbPath`, `McpbSha256`, `McpbChecksumPath` and `ServerJsonPath`. Each archive has a matching `.sha256` file with its lowercase SHA-256 and filename in standard checksum-file form. The output also contains the generated `manifest.json`, Registry `server.json` and `publish/` staging directory; these are not release assets. The source `SETUP.md` keeps checkout-relative documentation links; the package builder resolves those in its copied `README.md` to `blob/vX.Y.Z` links while preserving local license and notice links. Keep the checked archive bytes for later handoff; changed bytes require fresh checks. The smoke check and prepared draft flow support both routes; actual execution evidence remains recorded separately in the [distribution roadmap](../tasks/mcp-server-dictionary/roadmap.md).

[smoke-package.ps1](../scripts/smoke-package.ps1) requires Windows and explicit `-DisposableProfile`. Run it only on a fresh GitHub Windows runner or an already disposable Windows profile. It resolves Windows Local Application Data exactly as the application does and refuses any existing `SqlToAi` user root before launching. The switch asserts that the current profile is disposable; it does not create isolation. Do not run it against the maintainer's normal profile or redirect the production configuration root through environment variables. Local startup tests continue to use the existing temporary-root startup harness.

Only in that disposable environment, with the package object above:

```powershell
$checked = & .\scripts\smoke-package.ps1 -ZipPath $package.ZipPath -DisposableProfile
if ($checked.Sha256 -cne $package.Sha256) { throw 'Package checksum mismatch.' }
$checkedBundle = & .\scripts\smoke-package.ps1 -McpbPath $package.McpbPath -DisposableProfile
if ($checkedBundle.Sha256 -cne $package.McpbSha256) { throw 'Bundle checksum mismatch.' }
```

The mutually exclusive `-ZipPath` and `-McpbPath` routes extract the archive into an owned temporary path containing spaces and run its unchanged `SqlToAi.exe` from a different working directory. ZIP retains its original launch contract. MCPB first validates `manifest.json` against the same pinned official schema, requires binary type and `win32` compatibility on Windows x64, and checks the included executable's PE machine type. Its entry point must be `SqlToAi.exe` and its command `${__dirname}/SqlToAi.exe`; arguments may be omitted, empty, or the single literal `server` argument, passed through `ProcessStartInfo.ArgumentList`. Other arguments, `user_config`, environment mapping and platform overrides are unsupported and fail before startup. The check is deliberately limited to this package's stdio launch.

Both routes first require exit `1`, empty stdout, actionable missing-settings stderr and no user-root/settings creation. The check then prepares settings from the shipped conservative template with empty grants/server/credentials, disabled optional providers and file sinks, and an unknown-key preservation probe. Application environment overrides are removed only from the child environment. It completes MCP `initialize` / `notifications/initialized` / `tools/list` using protocol `2025-06-18`, requires the exact 17 documented tool names, parses stdout as protocol, drains stderr separately and checks a clean exit after stdin closes. It makes no SQL tool calls.

Settings and archive SHA-256 values must remain unchanged. Each response, process-exit and stream-drain wait is bounded by `-TimeoutSeconds` (default 30, range 1–120). Failure stops the check; cleanup terminates only its owned running child process tree and removes its own temporary extraction and newly created user root. The successful result contains the checked absolute `ZipPath` or `McpbPath`, respectively, and lowercase `Sha256`; diagnostic stderr is available with `-Verbose`. A self-contained publish plus a successful packaged EXE startup on an SDK-equipped runner is not evidence of execution on an SDK-free machine. Hosted ZIP execution is recorded separately in the [release-validation roadmap](../tasks/release-validation/roadmap.md#t4--smoke-test-the-actual-windows-zip). Record local copied-script/stub checks separately from actual ZIP/MCPB launches in the [distribution roadmap](../tasks/mcp-server-dictionary/roadmap.md). If no disposable profile is available, actual launches remain unverified; structural inspection never substitutes for packaged EXE execution.

## Release

### Prepare Registry metadata locally

The package command above also generates `server.json` beside the final archives from [server.template.json](../packaging/server.template.json). The template's `repository.url` configures `https://github.com/RalfHuesing/SqlToAi`; packaging derives the intended identity `io.github.ralfhuesing/SqlToAi` from that owner and repository, the project version, and `https://github.com/RalfHuesing/SqlToAi/releases/download/vX.Y.Z/SqlToAi-win-x64.mcpb`. Both server and package versions use the project version. The single `mcpb` package declares `stdio` and hashes the finished bundle, including its manifest. Missing inputs, malformed repository URLs, unresolved template placeholders and conflicting metadata fail packaging without a success result. No candidate hash is committed.

Packaging validates the template before publishing and the final document after hashing with PowerShell `Test-Json` and the unchanged [official Registry schema 2025-12-11](../packaging/registry-server-2025-12-11.schema.json), downloaded from the [versioned official URL](https://static.modelcontextprotocol.io/schemas/2025-12-11/server.schema.json), SHA-256 `3fba09590c99f61735d234822279f4223fab9e300c0a81e81c91ab62a4114de0`. The [MCPB package requirements](https://github.com/modelcontextprotocol/registry/blob/11b7b0e274d518b6f449c5ee3518a6151d9848b9/docs/modelcontextprotocol-io/package-types.mdx) and [GitHub namespace rules](https://github.com/modelcontextprotocol/registry/blob/11b7b0e274d518b6f449c5ee3518a6151d9848b9/docs/modelcontextprotocol-io/authentication.mdx) were checked at Registry revision `11b7b0e274d518b6f449c5ee3518a6151d9848b9` on 2026-10-16. Local schema validation is supplemented by checks for the exact MCPB type, URL, version, hash and stdio transport because the schema alone does not require every MCPB publication constraint.

After generation, independently validate the publication input and hash without starting the executable:

```powershell
Test-Json -LiteralPath $package.ServerJsonPath -SchemaFile .\packaging\registry-server-2025-12-11.schema.json -ErrorAction Stop
$server = Get-Content -LiteralPath $package.ServerJsonPath -Raw | ConvertFrom-Json
if ($server.packages[0].fileSha256 -cne (Get-FileHash -LiteralPath $package.McpbPath -Algorithm SHA256).Hash.ToLowerInvariant()) {
    throw 'Registry bundle checksum mismatch.'
}
```

This local procedure needs no account, authentication or public download. The namespace is intended and ownership remains unverified; the versioned URL is prepared and its availability is unverified. Metadata generation does not make the packages startup-checked: complete both archive checks in a disposable profile before a later release. Regenerate metadata and rerun affected checks whenever package bytes change.

Later, under a separate publication request, deliberately create/review/publish the checked GitHub draft, confirm that its public MCPB bytes match `server.json`, then use the official publisher's GitHub authentication to verify the owner's namespace and manually publish that exact metadata. Recheck the current Registry instructions before doing so. No namespace verification or publication runs during packaging or the release workflow; retain credentials outside the repository.

### Tagged package draft and manual publication

The [tag workflow](../.github/workflows/release.yml) creates a **draft**, never an automatically public release. Both jobs check out the triggering source commit. [release-source.ps1](../scripts/release-source.ps1) requires the existing `vX.Y.Z` tag, checked-out commit, workflow source commit and the project's explicit `Version` to agree. The Windows build job has read-only repository permissions and runs the shared dependency audit/build/offline checks before one application publish creates ZIP and MCPB. It checks the actual packaged EXE through both archive routes with the fresh runner's disposable profile, including the MCPB manifest and declared command. No SQL credentials or SQL tests run on GitHub. This remains an SDK-equipped runner check.

The workflow transfers `SqlToAi-win-x64.zip`, `SqlToAi-win-x64.mcpb`, both matching `.sha256` files and release notes through one run's artifact. Only the dependent draft job has `contents: write`. [create-release-draft.ps1](../scripts/create-release-draft.ps1) rechecks source agreement and verifies both downloaded archives/checksums against their separately transferred SHA-256 values produced after both smoke checks, then uses `gh release create --draft --verify-tag`. It verifies the MCPB version and notes against the same source/version/hashes, holds all five input files read-only through upload, and attaches exactly those four archive/checksum assets. The notes identify source commit, version, both hashes, automated results and the run URL. Package creation is not repeated in the draft job.

Before publishing in GitHub's normal release editor:

1. Run the local helper against the final release commit as described below. It records local SQL/TLS facts in the verified draft automatically. Inspect the automated run, source/version/hash and those facts. For a manually created tag, record equivalent local results against that **exact source** yourself. Ordinary SQL must be nonempty, execute and pass without failures or skips. Include commit, commands, executed/failed/skipped counts and a short sanitized result. A manual tag or green workflow does not prove local SQL execution; missing/failed/skipped ordinary SQL blocks publication.
2. Review the dedicated TLS command/results and unverified cases recorded by the helper (or add them for a manual tag). The accepted lack of trusted/untrusted TCP endpoints remains a disclosed limitation to review for this release. Failed configured TLS cases block publication. Local success with a trusted self-signed certificate exception does not establish trusted TCP certificate validation.
3. Review this source's `NuGetAuditSuppress` items and record each specific approved advisory/package/version/risk exception, or explicitly record none after review. A successful audit can include suppressions; unresolved findings cannot be treated as reviewed exceptions.
4. Publish the **existing draft and checked assets** through GitHub. Do not rebuild or replace either archive after review. Changed source or package bytes require the applicable checks and fresh review.

GitHub trusts the maintainer's recorded local result; a commit or checksum does not prove that SQL tests ran. Local test builds are not claimed to be byte-identical to CI binaries. Keep credentials, connection strings and profile contents out of release notes.

Every existing release for the tag is refused, including drafts; public assets and prior review notes are never silently overwritten. A failed upload can leave a partial draft. Inspect it before deliberately deleting that draft and rerunning; the new notes start all manual review fields as pending again. A public release is not a retry target. Same-tag workflow executions are serialized, and a concurrent release creation fails rather than updating assets. No automatic promotion service or external-publication task is needed to publish the completed draft.

The workflow wiring and controlled local verification are recorded in the [release-validation roadmap](../tasks/release-validation/roadmap.md#t5--keep-releases-usable-through-github-drafts). They do not establish a successful hosted run; fresh-runner EXE execution and real draft creation remain separate execution gates until authorized and performed.

### Local release helper

[scripts/release.ps1](../scripts/release.ps1) requires PowerShell 7, configured Git authentication, authenticated GitHub CLI (`gh auth login`) and a clean checkout on `main`; it refuses another branch or detached HEAD. A regular run checks `gh` authentication before version/file/Git mutations. It increments the patch version and commits that change **before** validating. Against this final source it runs the shared dependency audit/build/offline checks once, then ordinary SQL and dedicated TLS selections with `--no-build --no-restore`. It uses any existing `SQLTOAI_TLS_TEST_PROFILE` without changing it. Ordinary SQL must pass without skips; missing TLS cases remain visibly unverified, while configured TLS failures stop the helper. There is no `-SkipTests` path.

Preview the complete plan without validation or file/Git mutations:

```powershell
.\scripts\release.ps1 -DryRun

# Prepare, validate, push, wait for the draft and record local results:
.\scripts\release.ps1
```

Running without `-DryRun` intentionally commits the version and publishes Git state; use it only when releasing is intended. The helper reads standard temporary TRX files, prints the tested commit/version, commands, result counts and names of each unverified TLS case, and removes the files afterward. Normal terminal test output remains available. Before push/tag, it requires the same HEAD, a clean checkout and `main` again. It pushes `main`, tags the tested commit and pushes `vX.Y.Z`; the tag workflow creates the checked ZIP/MCPB draft.

The helper waits for the successful `release.yml` push run filtered to that tag and source SHA. It refuses unexpected workflow sources, failed queries, workflow failure/timeouts and draft tag/target/source/version/run mismatches. It requires exactly the four expected assets and unique workflow hashes for both formats. It downloads only `SqlToAi-win-x64.zip`, `SqlToAi-win-x64.mcpb` and both `.sha256` files into an owned temporary directory, verifies each archive against its checksum file and workflow-generated hash in the notes, and removes the downloads afterward. It then uses `gh release edit --notes-file` to record sanitized local offline/SQL/TLS counts, source/version, commands and unverified TLS case names. Other notes, source/checksum facts and dependency review remain intact; the standard SQL/TLS placeholders are replaced with recorded-fact pointers and explicitly pending TLS review. It checks that the release is still a draft, that its asset identities/content metadata are unchanged before and after editing, and that the saved notes match. Download counts are allowed to change. Assets are never uploaded/replaced by the helper. Local results do not automatically approve TLS limitations or dependency exceptions. Complete the review checklist above and publish that existing draft through GitHub manually.

Native validation and Git failures stop dependent steps. Git inspection errors also fail the dry-run; its status query disables optional locks so it does not refresh the index. Earlier completed Git operations are not rolled back: a failed local check leaves the version commit locally for inspection/correction, and a later failure may leave a pushed commit or local tag. Do not blindly rerun the helper to recover: it would increment the version again. Revalidate the intended final source before deliberately continuing the documented tag/draft flow.

Success reports **verified draft readiness with local results for maintainer review**, never public publication. `-DryRun` requires no `gh` authentication and performs no GitHub queries or file/Git mutations. Manually created tags use the same automated draft path and still require independently recorded successful local SQL, TLS-limit and dependency-exception review.
