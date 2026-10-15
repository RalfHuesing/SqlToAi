# SqlToAi — Windows package setup

This self-contained Windows x64 package needs no separately installed .NET runtime.
Extract all package files into an installation directory. The supplied
`appsettings.json` is a conservative **template**: it grants no
database access, contains no working credentials, and disables full SQL/tool-argument
and response logging. Application/error diagnostics remain enabled.

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

Inside the `SqlToAi` object, configure `SqlServer.Server` and either
`IntegratedSecurity=true` for the process Windows identity, or `UserId`/`Password`
for SQL authentication. Add only deliberate database grants to `Databases`:
`SchemaOnly`, `ReadOnlyAnonymized`, `ReadOnly`, or `ReadWrite`. Unlisted databases
are denied. Restrict SQL Server permissions too. The separate
`demo-profile.json` contains fictional local DemoDB credentials
and `ReadWrite` access. Copy selected values only for that deliberate local demo;
the host never loads this profile automatically.

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
$env:SqlToAi__SqlServer__Server = 'localhost\SQLEXPRESS'
$env:SqlToAi__SqlServer__IntegratedSecurity = 'true'
```

Double underscores address nested keys; SQL authentication can use
`SqlToAi__SqlServer__UserId` and `SqlToAi__SqlServer__Password`.
There is no special `SQLTOAI_CONNECTION_STRING` setting.

## Logs and updates

Default application/error logs are below the user root's `log\app` and `log\error`.
Observability and feedback records use `log\observability\SqlToAi\{yyyy-MM-dd}`.
Relative metadata SQL files and relative log overrides resolve from the user root;
absolute paths remain absolute. User-selected SQL input and schema-export output
paths still resolve from CWD. The installation may be read-only; the user root and
enabled log destinations must be writable.

To update, stop/restart your own server session around replacement of package
files in the installation directory. Settings and existing logs under the separate
user root are preserved. Compare the new template and apply desired configuration
changes manually; startup does not rewrite keys, copy files, or create backups.

Older executable-adjacent settings/logs are ignored and left untouched. For an
upgrade from that layout, manually copy/adapt your old settings to the required
user path, review credentials/access/logging against the new conservative template,
and relocate relative metadata SQL files (or use absolute paths). Move old logs
manually only if wanted. There is no automatic fallback, import, or migration command.

Treat settings and logs as sensitive. Enabling tool-call/response logging can expose
SQL arguments and results. String masking does not cover every type or output;
tokens are reversible within the running server. Application access checks do not
replace SQL Server permissions.

Full configuration, security limitations, and tool documentation are available in
the [online repository](https://github.com/RalfHuesing/SqlToAi#documentation-and-development).
The package is [MIT licensed](LICENSE).
