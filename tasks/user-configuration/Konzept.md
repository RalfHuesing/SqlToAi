---
status: draft
---

# Per-user configuration and data paths

## Intention

Give every SqlToAi entry point on Windows x64 one predictable, user-owned settings and logging location. Configuration setup and upgrades are manual; startup requires an existing settings file, and binary updates preserve deliberate user settings without importing or rewriting older configuration.

## Current behavior and evidence

- [Configuration documentation](../../docs/configuration.md) says JSON files load beside the executable, followed by environment-specific settings and environment variables. Relative metadata query files also resolve beside the executable.
- The same document describes startup migration that adds template keys, removes unknown keys, preserves retained values, and writes a backup. [AppSettingsMigrator](../../src/SqlToAi/Configuration/AppSettingsMigrator.cs) is the implementation to replace rather than extend with a new migration path.
- [Program.cs](../../src/SqlToAi/Program.cs) currently invokes that migrator before loading configuration; the migrator creates a missing file from the embedded template, and `BuildConfiguration` marks the base JSON file optional. Both behaviors conflict with the required startup failure when the user file is absent.
- The shipped [settings template](../../src/SqlToAi/appsettings.json) currently grants `DemoDB` `ReadWrite`, contains demo SQL credentials, and enables tool-call and unlimited response logging.
- Application/error logs default below the executable directory. Observability can write to its package default under `%LOCALAPPDATA%\RalfHuesing\McpObservability\SqlToAi`; response logging may include query results. [Security documentation](../../docs/security.md) states that logs and errors can expose sensitive data and that masking does not cover every output.

## Scope

### Must

- Use `%LOCALAPPDATA%\SqlToAi\` as the single default user root for MCP server, CLI, schema export, and exploration entry points. Store `appsettings.json` and all default logs below it, including application, error, observability, and feedback output.
- Require an existing `%LOCALAPPDATA%\SqlToAi\appsettings.json`. If it is missing, terminate startup immediately with exit code `1`, before starting MCP transport or database work. Never create or copy a settings file automatically, including on first run.
- Write the startup failure directly to stderr, independently of configured file logging, so clients can capture it. Include the failure reason, the resolved absolute expected file path, and an instruction to create/configure that file manually from the supplied template and restart. Keep stdout free of diagnostic text. Client-specific display of stderr is outside the server's control.
- Supply a conservative settings template for manual setup. It contains no working credentials, grants no database access, and does not enable full SQL or response logging. Keep the fictional demo as a separate example that users enable deliberately.
- Keep relevant option defaults and the shipped template consistent. Environment-variable overrides remain supported for an existing base settings file; neither environment variables nor an environment-specific JSON file replace the required base file. Environment-provided secrets are never persisted by configuration startup.
- Resolve relative configuration-owned paths, including metadata SQL files, from the user root. Preserve explicit absolute paths and the existing current-working-directory behavior for user-selected SQL input and output files.
- Load an existing user file without automatic migration, schema rewriting, relocation, or fallback. Invalid/unreadable settings and an unwritable user root produce useful diagnostics. Startup diagnostics must not write non-MCP text to stdout.
- Remove the automatic migrator and its obsolete configuration-backup behavior, update affected tests and entry points, and document manual setup at the new location. Fix the stale connection-string override hint rather than claiming an unsupported special environment setting.
- Preserve the user file and logs during binary/package replacement. Leave executable-adjacent legacy files untouched and never import them implicitly. Document the intentional break, manual setup at the new path, and manual configuration updates when settings change. Provide no migration command or migration utility.
- Keep current access-control, anonymization, and logging-limit claims accurate; changing paths or defaults does not strengthen their guarantees. The user reports live use with Claude, Cursor, and Codex; no additional client test matrix is required.

### Out of scope

- Automatic first-run settings creation, portable mode, executable-directory fallback, legacy import, configuration migration tooling, or automatic schema conversion. Moving/adapting existing settings is a manual user task.
- Credential vaults, profile managers, configuration wizards/editors, or a second configuration-path command-line option.
- New client compatibility testing. SQL TLS and certificate options belong to [SQL connection security](../sql-connection-security/Konzept.md); CI and release gates belong to [release validation](../release-validation/Konzept.md).
- Implementation, executable roadmap items, directory submissions or release publication within this concept-planning stage.

## Target contract

- The root is derived from the Windows per-user Local Application Data location, independent of install directory and working directory. All entry points share it; no executable-directory fallback exists.
- A missing base settings file is fatal for every entry point, including first run. Startup emits the actionable stderr diagnostic and exits with code `1`; it does not start an MCP session, connect to SQL Server, or generate a settings file. Concurrent starts cannot create or replace configuration. Existing files remain byte-for-byte unchanged unless the user edits them.
- The template and bound option defaults agree on a no-access, no-demo-credentials, no-full-query/response-logging profile. Demo setup is separately and explicitly selected.
- Environment overrides apply at runtime only after the required base file is present and are never copied into configuration files or backups. Relative configuration-owned files resolve under the user root; SQL input/output paths retain CWD semantics.
- Logs default under `%LOCALAPPDATA%\SqlToAi\log\`, covering application, error, observability, and feedback output. Existing security and masking limitations remain documented.
- Replacing binaries preserves settings and logs. Legacy executable-adjacent files are neither read, changed, nor deleted by configuration startup. Users manually create, relocate, and adapt configuration; no migration or backup mechanism is supplied.

## Verification

- Exercise missing-file/first-run, existing-file, concurrent-start, invalid/unreadable-file, unwritable-root, arbitrary-working-directory, and read-only-install cases on Windows x64.
- Capture a server process start without the base settings file: assert exit code `1`, a stderr message with reason, resolved absolute file path and manual setup/restart guidance, no stdout output, no MCP initialization or SQL connection, and no generated settings file. Repeat with only environment overrides, only an environment-specific JSON file, and only an executable-adjacent legacy file; each must still fail. Verify the shared failure contract for CLI, export, and exploration without adding a client test matrix.
- Verify shared path resolution for server, CLI, export, and exploration, plus relative/absolute metadata paths and unchanged CWD SQL-file behavior.
- Compare the relevant template values with bound option defaults; confirm a manually configured profile based on the supplied template has no grants, working demo credentials, full SQL logging, or response logging, and that demo activation is deliberate. Normal application/error diagnostics remain available and do not imply universal secret or query-data sanitization.
- Check environment overrides without persisting secrets, all default log destinations, and byte-for-byte preservation across starts and binary replacement. Confirm legacy files remain untouched and unused, and startup performs no configuration migration, schema rewriting, relocation, or backup creation.
- Run automated configuration tests against an isolated temporary user-data root; tests must never access the maintainer's real `%LOCALAPPDATA%\SqlToAi` directory. No client test matrix is added.
- Update configuration, security, and packaged setup documentation to explain mandatory manual setup and configuration updates, the fatal missing-file behavior and stderr diagnostic, without expanding anonymization or log-redaction claims.

## Dependencies and coordination

- Scope provenance: [SqlToAi public distribution and discovery](../mcp-server-dictionary/Konzept.md). Publication consumes this task's completed behavior; implementing it does not depend on publishing or completing that parent task.
- Parallel contracts: [SQL connection security](../sql-connection-security/Konzept.md) and [release validation](../release-validation/Konzept.md).
- This concept is fully specified and has no open design decisions; it remains `draft` until explicit concept approval.
