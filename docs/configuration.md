# Configuration

The required settings file is `%LOCALAPPDATA%\SqlToAi\appsettings.json`, resolved from Windows Local Application Data independently of the installation and working directories. The JSON settings use the `SqlToAi` section. Create the file manually from the [shipped template](../src/SqlToAi/appsettings.json); [option classes](../src/SqlToAi/Configuration/SqlToAiOptions.cs) define binding defaults. The conservative template contains no database grants or working credentials. The separate [fictional DemoDB example](../src/SqlToAi/demo-profile.json) deliberately grants `ReadWrite` and includes throwaway local credentials. Its filename is outside the automatic `appsettings.{Environment}.json` pattern. Copy its selected values into the user file only for a local demo database.

## Loading and credentials

Configuration loads from the user directory, in this order: required `appsettings.json`, optional `appsettings.{DOTNET_ENVIRONMENT}.json` (default environment: `Production`), then environment variables. An environment-specific file or environment variables cannot replace the required base file. Overrides apply only in memory; startup never persists credentials or other overrides. Restart after changes. Double underscores address nested keys:

```powershell
$env:SqlToAi__Servers__primary__Server = 'localhost\SQLEXPRESS'
$env:SqlToAi__Servers__primary__IntegratedSecurity = 'true'
```

Selected connection fields, database access lists, metadata queries, token delimiters and logging paths support `%VARIABLE%` expansion. There is no special `SQLTOAI_CONNECTION_STRING` override. Use per-field environment overrides such as `SqlToAi__Servers__<alias>__Server`, `SqlToAi__Servers__<alias>__UserId`, `SqlToAi__Servers__<alias>__Password`, and `SqlToAi__Servers__<alias>__IntegratedSecurity`; double underscores address nested JSON fields. `IntegratedSecurity=true` uses the process identity; otherwise both `UserId` and `Password` are required. Keep credentials outside version control, for example through `SqlToAi__Servers__primary__Password`.

Database targets are selected by qualified tool arguments (`<alias>/<database>`). SQL connections require encryption and validate the server certificate by default. Existing settings that omit the trust options validate certificates; startup does not rewrite or migrate settings. For a deliberate local/self-signed development exception, set the server's `TrustServerCertificate` to `true`. This skips validation of the presented server certificate while encryption remains required. Set it only for the intended endpoint, then restart the server. For example, the primary server option can be set with `$env:SqlToAi__Servers__primary__TrustServerCertificate = 'true'`; dedicated secondary endpoints use `SqlToAi__Servers__<alias>__MetadataProvider__TrustServerCertificate` or `SqlToAi__Servers__<alias>__AnonymizationRules__TrustServerCertificate`. See [security limits](security.md#boundaries-and-data-exposure).

Obsolete top-level sections (`SqlServer`, `MetadataProvider`, `AnonymizationRules`) and obsolete environment variables (`SqlToAi__SqlServer*`) are strictly rejected during configuration loading with exit code `1` and actionable stderr guidance.

Each JSON source is inspected before merging, using the configuration provider's
colon-delimited paths, including flattened JSON keys. Every effective `SqlToAi` and
`Servers` occurrence is checked for obsolete sections and exact or case-only duplicate aliases.
The same alias may still be overridden across the base file, active environment
file and process environment. Effective options are validated after expansion and
SQL-file resolution, including direct options and the separately bound DI options.
This validation requires configured server addresses and qualified grants, positive
schema-batch limits, and positive retention counts for enabled file sinks.

The general local integration-test fixture has a separate, checked-in test-only trust overlay for the existing self-signed test server. It does not change the demo profile, application defaults, user configuration, or the isolated TLS-default cases. See [development test setup](development.md#build-and-test).

## Settings reference

| Section | Fields and behavior |
| --- | --- |
| `Servers.<alias>` | One or more named servers. Valid aliases match `^[A-Za-z][A-Za-z0-9_-]*$`. Per-server: `Server`, `UserId`, `Password`, `IntegratedSecurity`, `TrustServerCertificate` (false), `ConnectTimeoutSeconds` (30), `ExcludedDatabases` (glob patterns, empty by default), plus optional server-scoped `MetadataProvider` (disabled by default) and `AnonymizationRules` (disabled by default) with their own dedicated connection fields. |
| `Databases` | `SchemaOnly`, `ReadOnlyAnonymized`, `ReadOnly`, `ReadWrite`: qualified database grants (`<alias>/<database>`); all lists are empty by default; see [access policy](security.md#database-access). Grants must reference configured server aliases. `CacheTtlSeconds` defaults to 300; access-level resolution itself reads in-memory lists. |
| `Anonymizer` | `Enabled` (true), `DefaultMode` (`ScramblePattern` or `Hash`), `Tokenization.Enabled` (false), `Tokenization.Prefix` / `Suffix` (both `§§§`) |
| `QueryExecution` | `DefaultRowLimit` (100), `MaxRowLimit` (1000), `CommandTimeoutSeconds` (30), `MaxScriptFileSizeBytes` (10485760 = 10 MiB) |
| `SchemaBatch` | `MaxInputCount` (10), `MaxResponseBytes` (65536 UTF-8 bytes); both must be greater than zero |
| `Logging` | `Directory` (`log`), `AppLog`, `ErrorLog`, `McpTrail` settings; see below |

`QueryExecution.CommandTimeoutSeconds` applies to query and script execution, query validation, query comparison (schema, counts and differences), and performance warmup/measured runs. Their session `SET` and transaction-integrity commands use the same timeout. The value is per command; `0` leaves ordinary commands unlimited. Metadata and anonymization-rule queries retain their separate provider timeout settings.

Query and script execution, validation, comparison, and performance measurement use cancellation independent of the request for session cleanup and rollback. Each cleanup operation uses the configured positive timeout, or the existing 30-second option default when `0` is configured; `ROWCOUNT`/`NOEXEC` reset commands also receive that command timeout. Cleanup durations are capped at the timer API maximum, rounded down to whole seconds (derived from `uint.MaxValue - 1` milliseconds); ordinary command timeouts are not capped. Transaction-integrity rejection uses the same independent rollback cancellation. Rollback cancellation is cooperative: the current Windows SqlClient provider executes rollback synchronously and cannot guarantee a wall-clock limit. Cleanup is always awaited; an expired cleanup token is checked again when the operation finishes.

Row limits constrain returned rows; they do not bound SQL Server's query cost or row scans. Command and connection timeouts are separate.

### Schema batch limits

The `sql_get_schema` batch service counts normalized non-empty input entries before
deduplication. `SchemaBatch.MaxInputCount` rejects excessive selections before
database/schema work. `MaxResponseBytes` bounds the exact serialized JSON response
text, including the envelope, all input names, status fields, complete catalog
errors, omission reasons and JSON escaping. The service reserves omission metadata
for every input before opening a connection; if that metadata cannot fit, it rejects
the request. Non-positive limits fail configuration loading with a settings-path
diagnostic.

Schemas and errors remain whole. When the next complete entry would exceed the
response budget, that input and all remaining inputs receive `not_returned` with
`reason=response_limit`; earlier results remain available. Duplicate names retain
their own entries and consume response space even when their schema fetch is reused.
These options apply only to batch composition; scalar schema responses and
filesystem exports retain their existing limits and behavior. Batch mode is selected
by a supplied `object_names` array through MCP or the [CLI](development.md#manual-tool-calls);
absent/null arrays preserve scalar lookup.

## Manual setup and updates

Create `%LOCALAPPDATA%\SqlToAi\` yourself, copy the supplied conservative template there as `appsettings.json`, and edit the server, credentials, and access policy deliberately before restarting. The main template grants no database access. The separate DemoDB example is an opt-in local test profile: it includes fictional throwaway credentials and grants `ReadWrite` to `test/DemoDB`. It keeps certificate validation enabled by default. If the local SQL Server uses a self-signed certificate, deliberately set `Servers.<alias>.TrustServerCertificate` to `true` in the user settings or process environment; this keeps encryption enabled but skips certificate identity validation. This setting applies only to that server alias. Independently configured secondary metadata and rule endpoints need their own `TrustServerCertificate` setting. Copy the profile's values only when intentionally using that local demo database. Its filename is not matched by automatic environment-file loading. This setup is required for the default MCP server, explicit `server`, `query`, `export-schema`, and exploration scenario execution. Exploration listing and argument usage remain available without configuration.

If the base file is absent, startup exits with code `1` before services, MCP transport or database work. It writes a direct stderr diagnostic containing the reason, absolute expected settings path, and manual template setup/restart guidance. Stdout contains no diagnostic text. Invalid or unreadable settings also produce a direct stderr diagnostic independently of file logging. Client display of stderr depends on the client.

Startup never creates settings, migrates or rewrites JSON, imports installation files, or generates configuration backups. Existing values and unknown keys remain unchanged. Executable-adjacent files are left untouched and are never a fallback. Manually create, relocate and adapt your settings at the user path. Compare future templates and apply configuration changes manually after binary updates; there is no migration command. Upgrades to named servers require manually updating obsolete `SqlServer`, `MetadataProvider`, `AnonymizationRules`, and database grants to the new `Servers.<alias>` schema and qualified grants (`<alias>/<database>`); obsolete top-level sections or variables trigger a fatal exit code `1`.

## Metadata enrichment

Without custom queries, schema lookup reads SQL Server's `MS_Description` extended properties. Custom descriptions use `@TableName`:

```json
{
  "TableMetadataQuery": "SELECT Description FROM dbo.TableDocs WHERE TableName = @TableName",
  "ColumnMetadataQuery": "SELECT ColumnName, Description FROM dbo.ColumnDocs WHERE TableName = @TableName"
}
```

These are `MetadataProvider` fields configured per server under `Servers.<alias>.MetadataProvider`. A value ending in `.sql` is loaded from a file after environment expansion; relative paths resolve from `%LOCALAPPDATA%\SqlToAi\`, and explicit absolute paths remain absolute. Missing files fail configuration loading with the resolved path on stderr. Inline SQL is used directly. This base applies to the server, CLI, schema export and exploration, independently of installation directory and CWD. User-selected SQL script input and schema-export output retain their CWD semantics. Table and column lookups run in parallel. A non-empty secondary `Server` selects separate credentials and requires `Database`; an empty server uses primary connection settings, with a configured secondary `Database` overriding the target database. The central rule provider uses the same connection fallback. If an optional lookup cannot connect or query, table descriptions remain missing and column descriptions remain empty; the enclosing schema result still succeeds. Rule loading returns an empty rule set, so default masking remains active. These failures are logged with a safe `MetadataProvider` or `AnonymizationRules` TLS/connection diagnostic that omits exception details, credentials and connection strings. A configured dedicated endpoint is not retried through the primary server. Rule semantics are in [security.md](security.md#central-anonymization-rules).

Enrichment and rule lookups are owner-scoped: each configured server alias owns its provider settings, a disabled provider makes no connection, and rule caches are partitioned by policy owner (canonical alias) and effective source (endpoint, actual source database including the per-target fallback and the rules table name). Aliases remain distinct owners even when they share one endpoint, and rule database patterns match the raw target database inside that owner's context. Shared dedicated sources work only when every owner explicitly configures them; a primary-endpoint fallback never reuses rules cached for another target database.

## Logs

Serilog writes to `%LOCALAPPDATA%\SqlToAi\log\app\` and `%LOCALAPPDATA%\SqlToAi\log\error\`. `Logging.Directory` changes the log root: relative paths resolve from the user configuration directory, while absolute paths remain absolute. Logging paths support `%VARIABLE%` expansion. Each sink supports `Enabled`, `Level`, `RollingInterval` and `RetainedFileCount`. Defaults are daily rotation, Information/30 files for the application sink and Warning/90 files for the error sink. File counts are not a guarantee of retention in days. Files open on the first matching log event, so a quiet startup may create directories without log files.

When a file sink is enabled, its `RetainedFileCount` must be greater than zero. Invalid values stop startup with exit code `1` and a direct stderr diagnostic naming the setting and settings file, with instructions to correct the value manually and restart. A disabled sink does not validate or use its retention count.

The user root must be writable, including when file logging is disabled. Startup checks it without changing settings. Active Serilog destinations are checked before use. Root/directory failures return exit `1` with a direct stderr diagnostic naming the affected path and permission guidance; Serilog's later sink errors also go directly to stderr. These diagnostics do not depend on a working file logger, and stdout remains reserved for MCP or requested CLI data. No installation-directory writes are needed.

`Logging.McpTrail` remains in the template and options, but the current host does not register a separate trail writer. Do not rely on that section as an active request/response logging control. Its directory is relative to the resolved `Logging.Directory` unless absolute; the existing optional retention sweep uses that same path and does not implicitly sweep executable-adjacent files. Its default remains disabled; retention limits are unchanged.
