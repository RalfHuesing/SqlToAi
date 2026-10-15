# Configuration

The required settings file is `%LOCALAPPDATA%\SqlToAi\appsettings.json`, resolved from Windows Local Application Data independently of the installation and working directories. The JSON settings use the `SqlToAi` section. Create the file manually from the [shipped template](../src/SqlToAi/appsettings.json); [option classes](../src/SqlToAi/Configuration/SqlToAiOptions.cs) define binding defaults. The conservative template contains no database grants or working credentials. The separate [fictional DemoDB example](../src/SqlToAi/demo-profile.json) deliberately grants `ReadWrite` and includes throwaway local credentials. Its filename is outside the automatic `appsettings.{Environment}.json` pattern. Copy its selected values into the user file only for a local demo database.

## Loading and credentials

Configuration loads from the user directory, in this order: required `appsettings.json`, optional `appsettings.{DOTNET_ENVIRONMENT}.json` (default environment: `Production`), then environment variables. An environment-specific file or environment variables cannot replace the required base file. Overrides apply only in memory; startup never persists credentials or other overrides. Restart after changes. Double underscores address nested keys:

```powershell
$env:SqlToAi__SqlServer__Server = 'localhost\SQLEXPRESS'
$env:SqlToAi__SqlServer__IntegratedSecurity = 'true'
```

Selected connection fields, database access lists, metadata queries, token delimiters and logging paths support `%VARIABLE%` expansion. There is no special `SQLTOAI_CONNECTION_STRING` override. Use per-field environment overrides such as `SqlToAi__SqlServer__Server`, `SqlToAi__SqlServer__UserId`, `SqlToAi__SqlServer__Password`, and `SqlToAi__SqlServer__IntegratedSecurity`; double underscores address nested JSON fields. `IntegratedSecurity=true` uses the process identity; otherwise both `UserId` and `Password` are required. Keep credentials outside version control, for example through `SqlToAi__SqlServer__Password`.

Database names are selected by tool arguments. SQL connections require encryption and validate the server certificate by default. Existing settings that omit the new trust options now validate the certificate; startup does not rewrite or migrate settings. For a deliberate local/self-signed development exception, set the endpoint's `TrustServerCertificate` to `true`. This skips validation of the presented server certificate while encryption remains required. Set it only for the intended endpoint, then restart the server. For example, the primary option can be set with `$env:SqlToAi__SqlServer__TrustServerCertificate = 'true'`; dedicated endpoints use `SqlToAi__MetadataProvider__TrustServerCertificate` or `SqlToAi__AnonymizationRules__TrustServerCertificate`. See [security limits](security.md#boundaries-and-data-exposure).

## Settings reference

| Section | Fields and behavior |
| --- | --- |
| `SqlServer` | `Server`, `UserId`, `Password`, `IntegratedSecurity`, `TrustServerCertificate` (false), `ConnectTimeoutSeconds` (30), `ExcludedDatabases` (glob patterns, empty by default) |
| `Databases` | `SchemaOnly`, `ReadOnlyAnonymized`, `ReadOnly`, `ReadWrite`: exact database names; all lists are empty by default; see [access policy](security.md#database-access). `CacheTtlSeconds` defaults to 300; access-level resolution itself reads in-memory lists. |
| `Anonymizer` | `Enabled` (true), `DefaultMode` (`ScramblePattern` or `Hash`), `Tokenization.Enabled` (false), `Tokenization.Prefix` / `Suffix` (both `§§§`) |
| `AnonymizationRules` | `Enabled` (false), `Server`, `Database`, `UserId`, `Password`, `IntegratedSecurity`, `TrustServerCertificate` (false), `TableName` (`dbo.AnonymizationRules`), `CommandTimeoutSeconds` (30), `CacheTtlSeconds` (300) |
| `MetadataProvider` | `Enabled` (true), `Server`, `Database`, `UserId`, `Password`, `IntegratedSecurity`, `TrustServerCertificate` (false), `TableMetadataQuery`, `ColumnMetadataQuery`, `CommandTimeoutSeconds` (30 in options) |
| `QueryExecution` | `DefaultRowLimit` (100), `MaxRowLimit` (1000), `CommandTimeoutSeconds` (30), `MaxScriptFileSizeBytes` (10485760 = 10 MiB) |
| `Logging` | `Directory` (`log`), `AppLog`, `ErrorLog`, legacy `McpTrail` settings; see below |

Row limits constrain returned rows; they do not bound SQL Server's query cost or row scans. Command and connection timeouts are separate.

## Manual setup and updates

Create `%LOCALAPPDATA%\SqlToAi\` yourself, copy the supplied conservative template there as `appsettings.json`, and edit the server, credentials, and access policy deliberately before restarting. The main template grants no database access. The separate DemoDB example is an opt-in local test profile: it includes fictional throwaway credentials and grants `ReadWrite` to `DemoDB`. It keeps certificate validation enabled by default. If the local SQL Server uses a self-signed certificate, deliberately set `SqlServer.TrustServerCertificate` to `true` in the user settings or process environment; this keeps encryption enabled but skips certificate identity validation. This setting applies only to the primary endpoint. Independently configured metadata and rule servers need their own `TrustServerCertificate` setting. Copy the profile's values only when intentionally using that local demo database. Its filename is not matched by automatic environment-file loading. This setup is required for the default MCP server, explicit `server`, `query`, `export-schema`, and exploration scenario execution. Exploration listing and argument usage remain available without configuration.

If the base file is absent, startup exits with code `1` before services, MCP transport or database work. It writes a direct stderr diagnostic containing the reason, absolute expected settings path, and manual template setup/restart guidance. Stdout contains no diagnostic text. Invalid or unreadable settings also produce a direct stderr diagnostic independently of file logging. Client display of stderr depends on the client.

Startup never creates settings, migrates or rewrites JSON, imports legacy files, or generates configuration backups. Existing values and unknown keys remain unchanged. Executable-adjacent files are left untouched and are never a fallback. This intentionally changes the old location: manually create, relocate and adapt your settings at the user path. Compare future templates and apply configuration changes manually after binary updates; there is no migration command.

## Metadata enrichment

Without custom queries, schema lookup reads SQL Server's `MS_Description` extended properties. Custom descriptions use `@TableName`:

```json
{
  "TableMetadataQuery": "SELECT Description FROM dbo.TableDocs WHERE TableName = @TableName",
  "ColumnMetadataQuery": "SELECT ColumnName, Description FROM dbo.ColumnDocs WHERE TableName = @TableName"
}
```

These are `MetadataProvider` fields. A value ending in `.sql` is loaded from a file after environment expansion; relative paths resolve from `%LOCALAPPDATA%\SqlToAi\`, and explicit absolute paths remain absolute. Missing files fail configuration loading with the resolved path on stderr. Inline SQL is used directly. This base applies to the server, CLI, schema export and exploration, independently of installation directory and CWD. User-selected SQL script input and schema-export output retain their CWD semantics. Table and column lookups run in parallel. A non-empty secondary `Server` selects separate credentials and requires `Database`; an empty server uses primary connection settings, with a configured secondary `Database` overriding the target database. The central rule provider uses the same connection fallback. Rule semantics are in [security.md](security.md#central-anonymization-rules).

## Logs

Serilog writes to `%LOCALAPPDATA%\SqlToAi\log\app\` and `%LOCALAPPDATA%\SqlToAi\log\error\`. `Logging.Directory` changes the log root: relative paths resolve from the user configuration directory, while absolute paths remain absolute. Logging paths support `%VARIABLE%` expansion. Each sink supports `Enabled`, `Level`, `RollingInterval` and `RetainedFileCount`. Defaults are daily rotation, Information/30 files for the application sink and Warning/90 files for the error sink. File counts are not a guarantee of retention in days. Files open on the first matching log event, so a quiet startup may create directories without log files.

When a file sink is enabled, its `RetainedFileCount` must be greater than zero. Invalid values stop startup with exit code `1` and a direct stderr diagnostic naming the setting and settings file, with instructions to correct the value manually and restart. A disabled sink does not validate or use its retention count.

The user root must be writable, including when file logging is disabled. Startup checks it without changing settings. Active Serilog destinations are checked before use. Root/directory failures return exit `1` with a direct stderr diagnostic naming the affected path and permission guidance; Serilog's later sink errors also go directly to stderr. These diagnostics do not depend on a working file logger, and stdout remains reserved for MCP or requested CLI data. No installation-directory writes are needed.

`Logging.McpTrail` remains in the template and options, but the current host does not register a separate legacy trail writer. Do not rely on that section as an active request/response logging control. Its directory is relative to the resolved `Logging.Directory` unless absolute; the existing optional retention sweep uses that same path and does not implicitly sweep executable-adjacent legacy files. Its default remains disabled; retention limits are unchanged.
