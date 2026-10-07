# Configuration

Settings are under the `SqlToAi` root. The [shipped template](../src/SqlToAi/appsettings.json) is copied beside the executable; [option classes](../src/SqlToAi/Configuration/SqlToAiOptions.cs) define binding defaults. The template enables **ReadWrite for DemoDB** with a local demo SQL login. Replace those settings before connecting to another database.

## Loading and credentials

Configuration loads from the executable directory, in this order: `appsettings.json`, `appsettings.{DOTNET_ENVIRONMENT}.json` (default environment: `Production`), then environment variables. Restart after changes. Double underscores address nested keys:

```powershell
$env:SqlToAi__SqlServer__Server = 'localhost\SQLEXPRESS'
$env:SqlToAi__SqlServer__IntegratedSecurity = 'true'
```

Selected connection fields, database access lists, metadata queries, token delimiters and logging paths support `%VARIABLE%` expansion. There is no special `SQLTOAI_CONNECTION_STRING` override: configure individual `SqlServer` fields. `IntegratedSecurity=true` uses the process identity; otherwise both `UserId` and `Password` are required. Keep credentials outside version control, for example through `SqlToAi__SqlServer__Password`.

Database names are selected by tool arguments. The primary connection factory currently sets `TrustServerCertificate=true`; configuration does not expose a certificate-validation switch. See [security limits](security.md#boundaries-and-data-exposure).

## Settings reference

| Section | Fields and behavior |
| --- | --- |
| `SqlServer` | `Server`, `UserId`, `Password`, `IntegratedSecurity`, `ConnectTimeoutSeconds` (30), `ExcludedDatabases` (glob patterns, empty by default) |
| `Databases` | `SchemaOnly`, `ReadOnlyAnonymized`, `ReadOnly`, `ReadWrite`: exact database names; see [access policy](security.md#database-access). `CacheTtlSeconds` defaults to 300; access-level resolution itself reads in-memory lists. |
| `Anonymizer` | `Enabled` (true), `DefaultMode` (`ScramblePattern` or `Hash`), `Tokenization.Enabled` (false), `Tokenization.Prefix` / `Suffix` (both `§§§`) |
| `AnonymizationRules` | `Enabled` (false), `Server`, `Database`, `UserId`, `Password`, `IntegratedSecurity`, `TableName` (`dbo.AnonymizationRules`), `CommandTimeoutSeconds` (30), `CacheTtlSeconds` (300) |
| `MetadataProvider` | `Enabled` (true), `Server`, `Database`, `UserId`, `Password`, `IntegratedSecurity`, `TableMetadataQuery`, `ColumnMetadataQuery`, `CommandTimeoutSeconds` (30 in options) |
| `QueryExecution` | `DefaultRowLimit` (100), `MaxRowLimit` (1000), `CommandTimeoutSeconds` (30), `MaxScriptFileSizeBytes` (10485760 = 10 MiB) |
| `Logging` | `Directory` (`log`), `AppLog`, `ErrorLog`, legacy `McpTrail` settings; see below |
| `Observability` | `Enabled`, `EnableToolCallLogging`, `EnableFeedbackTool`, `LogDirectory`, `EnableResponseLogging`, `MaxResponseLength`, `FeedbackConfirmationMessage`; see below |

Row limits constrain returned rows; they do not bound SQL Server's query cost or row scans. Command and connection timeouts are separate.

## Configuration migration

At startup, `AppSettingsMigrator` compares the local `appsettings.json` with the embedded template. It inserts missing keys, removes keys absent from the template and preserves existing values for retained keys. Before changing the file, it creates `appsettings.json.bak`. Review the backup and effective settings after upgrading, especially access lists and secrets. Migration applies to the base JSON file, rather than environment overrides.

## Metadata enrichment

Without custom queries, schema lookup reads SQL Server's `MS_Description` extended properties. Custom descriptions use `@TableName`:

```json
{
  "TableMetadataQuery": "SELECT Description FROM dbo.TableDocs WHERE TableName = @TableName",
  "ColumnMetadataQuery": "SELECT ColumnName, Description FROM dbo.ColumnDocs WHERE TableName = @TableName"
}
```

These are `MetadataProvider` fields. A value ending in `.sql` is loaded from a file; relative paths resolve beside the executable. Table and column lookups run in parallel. A non-empty secondary `Server` selects separate credentials and requires `Database`; an empty server uses primary connection settings, with a configured secondary `Database` overriding the target database. The central rule provider uses the same connection fallback. Rule semantics are in [security.md](security.md#central-anonymization-rules).

## Logs and observability

Serilog writes to `{exe-dir}/log/app/` and `{exe-dir}/log/error/`. `Logging.Directory` changes the root. Each sink supports `Enabled`, `Level`, `RollingInterval` and `RetainedFileCount`. Defaults are daily rotation, Information/30 files for the application sink and Warning/90 files for the error sink. File counts are not a guarantee of retention in days.

Tool-call logging and optional `report_observability_feedback` registration use `RalfHuesing.Mcp.Observability`. Shipped settings enable tool-call and response logging, leave `LogDirectory=null` and set `MaxResponseLength=0` (unlimited). The package default location is `%LOCALAPPDATA%\RalfHuesing\McpObservability\SqlToAi\{yyyy-MM-dd}\`; set `LogDirectory` explicitly when a fixed location is required. Feedback types are `issue`, `positive`, `comment`, `suggestion` and `incident`.

The package records tool names, timings, status, sanitized arguments and optionally responses. Secret-key redaction does not imply that SQL text, arbitrary parameter values or returned data are safe to log. Treat logs and configuration backups as sensitive files. Disable response logging with `EnableResponseLogging=false`, or set a positive response-length limit where appropriate.

`Logging.McpTrail` remains in the template and options, but the current host registers observability rather than a separate legacy trail writer. Do not rely on that section as an active request/response logging control.
