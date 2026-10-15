---
status: draft
---

# SQL connection encryption and certificate validation

## Intention

Protect every SQL Server connection with encryption and server certificate validation by default. Make the development exception deliberate and configurable, while ensuring that a trust exception never lowers the encryption requirement. Primary and independently configured secondary connections must follow the same contract.

This concept defines a technical change that can be implemented independently of the separate user-configuration path change. Implementation will use the settings mechanism that exists when this task is scheduled.

## Current evidence

- [SqlConnectionFactory](../../src/SqlToAi/Database/SqlConnectionFactory.cs) constructs primary SQL connections with `TrustServerCertificate=true`; it does not explicitly require encryption.
- [SecondaryConnectionBuilder](../../src/SqlToAi/Database/SecondaryConnectionBuilder.cs) independently constructs dedicated secondary connections with the same trust bypass. It serves metadata and central anonymization-rule databases.
- When a secondary server is not configured, the builder delegates to the primary `IDatabaseConnectionFactory`. This fallback must retain the primary policy.
- [SqlToAiOptions](../../src/SqlToAi/Configuration/SqlToAiOptions.cs) and the shipped [appsettings template](../../src/SqlToAi/appsettings.json) define the current SQL and secondary credential settings; there is no certificate-validation option today.
- [Configuration documentation](../../docs/configuration.md) records that certificate validation is disabled and no switch is exposed. [Security documentation](../../docs/security.md) identifies the primary factory's trust bypass as a boundary.
- The parent distribution concept already settles encrypted connections, certificate validation by default, and an explicit development trust exception. This file defines that connection-security slice only.

## Scope

### Must

- Require encrypted transport for primary and dedicated secondary SQL connections, including when a secondary connection falls back to the primary factory.
- Enable server certificate validation by default. The default must not trust an untrusted or self-signed certificate automatically.
- Expose an explicit, consistently named setting through the current options and configuration template that allows `TrustServerCertificate=true` for a deliberately selected local or self-signed development server.
- Keep encryption enabled when that trust exception is selected. Certificate-validation failure must never trigger a retry with encryption or validation disabled.
- Apply identical option semantics to the primary connection and dedicated metadata and anonymization-rule connections. Secondary connections must have sensible fallbacks that preserve the policy and must not silently bypass it.
- Keep option defaults authoritative and bind the setting through the existing `IOptions<SqlToAiOptions>` mechanism. Keep the options class, shipped `appsettings.json` template, and canonical configuration/security documentation consistent.
- Surface TLS/certificate connection failures as explicit connection/trust errors through the existing error handling contract. Error messages and logs must not disclose passwords, full connection strings, or other secrets.
- Preserve existing SQL Server access checks, read-only behavior, transaction and rollback guarantees, stdio protocol behavior, and the documented error catalog apart from any narrowly required TLS-specific clarification.

### Out of scope

- Changes to configuration-file location, per-user directory setup, legacy import, or configuration migration. The separate user-configuration concept owns the move to `%LOCALAPPDATA%/SqlToAi` with no legacy import; this task remains independently plannable and uses the settings mechanism then in place.
- CI workflow, release gates, test-suite partitioning, SQL integration orchestration, and client test matrices. The separate release-validation concept owns those decisions; SQL integration remains local and no client matrix is added.
- Certificate issuance, installation, rotation, pinning, custom trust stores, vault integration, paid certificates, or a general certificate-management feature.
- Azure authentication or other authentication-scope changes.
- SQL guard, access-policy, anonymization, metadata, or query behavior changes unrelated to transport security.
- Any silent fallback that retries after a trust failure with encryption disabled or certificate checks relaxed.

## Target contract

- `TrustServerCertificate` defaults to `false` in the primary and dedicated secondary connection options. Encryption is always required by this contract; it is not a separate user-disableable setting.
- A deliberate configuration value of `TrustServerCertificate=true` relaxes certificate authentication only for the configured development case; it does not disable encryption.
- Use `SqlServer.TrustServerCertificate` for the primary endpoint and the corresponding `MetadataProvider.TrustServerCertificate` / `AnonymizationRules.TrustServerCertificate` options for independently configured endpoints. Each dedicated endpoint requires its own deliberate exception; relaxing trust for a local primary server must not automatically relax trust for an independently configured remote server.
- A secondary connection without a dedicated server uses the primary factory and its effective policy, preserving the existing fallback relationship. Invalid, missing, or unavailable secondary settings follow explicit existing failure/fallback semantics; they never construct a less protected connection as a recovery action.
- A server certificate failure remains visible as a connection failure. Diagnostics identify the TLS/certificate problem without including credentials or a raw connection string.
- The setting is documented with its secure default, the development exception, and the fact that `true` trusts the presented server certificate rather than validating its identity.

### Connection invariants

- The effective encryption requirement is fixed on for every SqlClient builder covered by this concept.
- Certificate-trust semantics and secure defaults are uniform; deliberate exceptions belong to the endpoint's own connection configuration.
- The primary factory remains the policy source for secondary fallback connections; dedicated secondary construction receives its own configured trust value explicitly.
- Authentication choice, target database, application name, and connection timeout retain their existing responsibilities.
- A validation or handshake error fails the attempted connection and is mapped through current error handling without credential-bearing diagnostics.

## Verification

- Confirm option defaults and template values agree: encryption is required and certificate trust bypass is off unless the user explicitly opts in.
- Verify connection-string construction for primary and dedicated secondary builders, including independent trust exceptions, and verify secondary fallback delegates to the primary policy. A primary exception must not propagate to a dedicated secondary endpoint.
- Verify option binding and documented configuration names agree across the options class, template, and docs.
- Run local SQL integration against a trusted-certificate SQL Server and confirm successful connection with defaults; against an untrusted/self-signed certificate, confirm default rejection; then confirm the explicit trust exception succeeds while encryption remains required.
- Keep builder/connection-string checks distinct from transport evidence: string construction alone does not prove that a TLS handshake encrypted traffic or that certificate validation rejected a server. Record unavailable trusted/untrusted SQL infrastructure as unverified rather than passed.
- Verify representative connection failures remain explicit and that diagnostics/logs contain no credentials or full connection strings. Confirm stdio output and the existing error catalog remain usable on startup/connection failures.

### Acceptance criteria

- A fresh/default configuration cannot establish a SQL connection by skipping server certificate validation, and requests encrypted transport.
- The explicit development setting permits the chosen untrusted certificate while retaining encrypted transport; no automatic retry changes either setting.
- Primary and dedicated secondary paths require encryption and default certificate validation, with exceptions scoped to the selected endpoint. Secondary-to-primary fallback inherits the primary policy.
- A connection failure remains actionable through the existing tool/error surface and does not expose authentication secrets in the response or logs.
- Configuration and security documentation state the exact default and exception, and do not imply that the exception validates server identity.

## Dependencies

- The publication concept is the scope's provenance and consumes this task's result, not an implementation prerequisite. This task can be completed before any package or directory is published.
- Uses the current `SqlToAiOptions` binding and SQL connection builders; implementation is compatible with the then-current settings path.
- Requires access to local SQL Server instances or equivalent local integration fixtures with a trusted certificate and an untrusted/self-signed certificate for transport-level evidence.
- Coordinates documentation wording with [user configuration](../user-configuration/Konzept.md) and [release validation](../release-validation/Konzept.md) without depending on their implementation or changing their scope. The [publication concept](../mcp-server-dictionary/Konzept.md) consumes this task's verification evidence.

This concept remains draft pending explicit concept approval; extraction does not authorize implementation or publication.
