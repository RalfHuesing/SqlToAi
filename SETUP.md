# SqlToAi — Windows package setup

SqlToAi ships as a self-contained Windows x64 ZIP and as an MCPB bundle. The ZIP is
extracted into an installation directory and launched through your MCP client's
stdio configuration. MCPB installs the same executable as a client launch entry;
it does not install SQL Server or create SqlToAi settings. Both packages need no
separately installed .NET runtime. Neither package is code-signed. The supplied
`appsettings.json` is a conservative **template**: it grants no
database access and contains no working credentials. Application/error
diagnostics remain enabled.

Before installing the MCPB or starting either package, create the user settings
file manually as described below. Keep the package's `LICENSE` and
`THIRD-PARTY-NOTICES.txt` with the extracted files.

## Upgrading from single-server settings

Named-server configuration is a breaking change. Updates preserve the user settings file, but the new version does not load the old single-server schema or migrate it; obsolete sections and environment keys make startup fail with exit code `1`. Before the first new startup, manually move the primary connection settings into `SqlToAi.Servers.<alias>`, move each `MetadataProvider` and `AnonymizationRules` configuration under its owning server, convert database grants to qualified `<alias>/<database>` names, and rename old environment keys to the `SqlToAi__Servers__<alias>__...` form. Keep any chosen query and logging options by carrying them forward manually. Startup never changes the settings file automatically.

This complete minimal example uses a fictional server name, leaves database access denied, and disables both optional providers:

```json
{
  "SqlToAi": {
    "Servers": {
      "primary": {
        "Server": "sql.example.invalid",
        "IntegratedSecurity": true,
        "TrustServerCertificate": false,
        "ConnectTimeoutSeconds": 30,
        "ExcludedDatabases": [],
        "MetadataProvider": { "Enabled": false },
        "AnonymizationRules": { "Enabled": false }
      }
    },
    "Databases": {
      "SchemaOnly": [],
      "ReadOnlyAnonymized": [],
      "ReadOnly": [],
      "ReadWrite": []
    }
  }
}
```

Replace the fictional server before connecting. Omitted root settings retain their defaults: `Anonymizer.Enabled=true`, `Anonymizer.DefaultMode=ScramblePattern`, tokenization disabled, `QueryExecution.DefaultRowLimit=100`, `MaxRowLimit=1000`, `CommandTimeoutSeconds=30`, and `MaxScriptFileSizeBytes=10485760`; `SchemaBatch.MaxInputCount=10` and `MaxResponseBytes=65536`; application/error logging enabled. `Databases.CacheTtlSeconds` defaults to `300`. See the [full settings template](src/SqlToAi/appsettings.json) before carrying forward additional options.

## Create your settings manually

Run PowerShell under the same Windows account that will run the MCP client.
Set the package path to the directory you extracted, then copy the template once:

```powershell
$package = 'C:\Tools\SqlToAi'
$userRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SqlToAi'
New-Item -ItemType Directory -Path $userRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $package 'appsettings.json') -Destination (Join-Path $userRoot 'appsettings.json') -ErrorAction Stop -Confirm
notepad.exe (Join-Path $userRoot 'appsettings.json')
```

The copy command asks before replacing a file. Keep existing settings when updating.
The expected location is `%LOCALAPPDATA%\SqlToAi\appsettings.json`, derived from
Windows Local Application Data, independently of installation directory and CWD.
Changing the `LOCALAPPDATA` environment variable is not a supported root override.

Inside the `SqlToAi` object, configure `Servers.<alias>.Server` and either
`IntegratedSecurity=true` for the process Windows identity, or `UserId`/`Password`
for SQL authentication. Valid server aliases match `^[A-Za-z][A-Za-z0-9_-]*$`.
Add only deliberate database grants to `Databases`: `SchemaOnly`, `ReadOnlyAnonymized`,
`ReadOnly`, or `ReadWrite` using qualified target names (`<alias>/<database>`),
such as `primary/DemoDB`. Unlisted databases are denied. Restrict SQL Server
permissions too. The separate `demo-profile.json` contains fictional local DemoDB
credentials under server alias `test` and `ReadWrite` access to `test/DemoDB`.
Copy selected values only for that deliberate local demo; the host never loads
this profile automatically.

SQL connections require encryption and validate the server certificate by default.
Existing settings that omit `TrustServerCertificate` now validate certificates;
startup does not rewrite or migrate them. For a local SQL Server with a
self-signed certificate, deliberately set `Servers.<alias>.TrustServerCertificate` to
`true` in the user file or process environment. This skips certificate identity
validation while keeping encryption required. It applies only to that server alias;
separately configured secondary metadata and anonymization-rule servers require
their own `Servers.<alias>.MetadataProvider.TrustServerCertificate` or
`Servers.<alias>.AnonymizationRules.TrustServerCertificate` setting. The demo profile
leaves all exceptions disabled.

Save the file and restart the MCP server in your client after every configuration
change. Configure a stdio entry with an absolute executable path, for example:

```json
{
  "mcpServers": {
    "sql-to-ai": {
      "command": "C:\\Tools\\SqlToAi\\SqlToAi.exe",
      "args": []
    }
  }
}
```

To check CLI availability after setup:

```powershell
& (Join-Path $package 'SqlToAi.exe') query --help
```

## Startup diagnostics and overrides

Without the required user file, server/CLI/export startup exits with code `1`
before MCP transport or database work. Stdout remains empty. Example stderr
(the account path and exception type can differ):

```text
Configuration startup failed: A required settings file is missing (FileNotFoundException). Expected base settings file: 'C:\Users\Alice\AppData\Local\SqlToAi\appsettings.json'. Create and configure this file manually from the supplied appsettings.json template, then restart.
```

Invalid/unreadable settings and unwritable data/log directories also report directly
to stderr, independently of file logging. Client display of stderr depends on the
client. The template beside the executable does not satisfy the required user file.

Precedence, lowest to highest: required user `appsettings.json`, optional user
`appsettings.{DOTNET_ENVIRONMENT}.json` (default `Production`), environment variables.
Overrides never replace the required base file and are never persisted. Set them
in the environment of the process/client that launches SqlToAi, then restart:

```powershell
$env:SqlToAi__Servers__primary__Server = 'localhost\SQLEXPRESS'
$env:SqlToAi__Servers__primary__IntegratedSecurity = 'true'
```

Double underscores address nested keys; SQL authentication can use
`SqlToAi__Servers__<alias>__UserId` and `SqlToAi__Servers__<alias>__Password`.
Environment-variable paths under `SqlToAi` are normalized by replacing `__`
with `:` and compared case-insensitively. Exactly the legacy sections
`SqlServer`, `MetadataProvider`, and `AnonymizationRules` are rejected when the
normalized path names the section under `SqlToAi` or a descendant; colon,
double-underscore, and mixed delimiters therefore all match. Similar section
names such as `SqlServerless`, `MetadataProviderExtra`, and
`AnonymizationRulesOther`, along with variables under unrelated application
roots, are accepted. Obsolete JSON root sections or matching environment paths
cause startup exit code `1` with safe actionable stderr diagnostics.
There is no special `SQLTOAI_CONNECTION_STRING` setting.

## Logs and updates

Default application/error logs are below the user root's `log\app` and `log\error`.
Relative metadata SQL files and relative log overrides resolve from the user root;
absolute paths remain absolute. User-selected SQL input and schema-export output
paths still resolve from CWD. The installation may be read-only; the user root and
enabled log destinations must be writable.

To update, stop/restart your own server session around replacement of package
files in the installation directory. Settings and existing logs under the separate
user root are preserved. For older single-server settings, the new named-server
configuration is a breaking change: manually migrate the old sections, provider
ownership, database grants and environment keys before the first new startup as
described in [Upgrading from single-server settings](#upgrading-from-single-server-settings).
Compare the new template and apply desired configuration changes manually;
startup does not rewrite keys, copy files, or create backups.
After replacing ZIP files, reload the MCP server entry or restart the client. For
MCPB updates, use the client's bundle update/install flow and restart or reload its
server entry so it launches the new files.

Older executable-adjacent settings/logs are ignored and left untouched. For an
upgrade from that layout, manually copy/adapt your old settings to the required
user path, review credentials/access/logging against the new conservative template,
and relocate relative metadata SQL files (or use absolute paths). Move old logs
manually only if wanted. There is no automatic fallback, import, or migration command.

Treat settings and logs as sensitive. String masking does not cover every type or output;
tokens are reversible within the running server. Application access checks do not
replace SQL Server permissions.

To remove SqlToAi, stop its server session, remove the MCPB/client entry if installed
that way, and delete the extracted package directory. This leaves
`%LOCALAPPDATA%\SqlToAi\appsettings.json` and `%LOCALAPPDATA%\SqlToAi\log\` in
place; remove those separately only if you intend to delete your configuration and
logs. Existing executable-adjacent settings from older layouts are also left
untouched.

Further reading: [configuration](docs/configuration.md), [security and data
handling](docs/security.md), [tool reference](docs/tools.md), and
[support and contribution guidance](docs/development.md#support-and-contributions).
See [LICENSE](LICENSE) and [third-party notices](THIRD-PARTY-NOTICES.txt) for the
included terms.

For setup questions or non-sensitive bugs, use [GitHub Issues](https://github.com/RalfHuesing/SqlToAi/issues)
with a sanitized report. For suspected security vulnerabilities, follow
[SECURITY.md](SECURITY.md); do not put exploit details or secrets in a public issue.
The later publication handoff is to run the release helper deliberately, review and
publish its checked draft, then verify namespace ownership and publish the matching
Registry metadata; see [release instructions](docs/development.md#release).
The package is [MIT licensed](LICENSE).
