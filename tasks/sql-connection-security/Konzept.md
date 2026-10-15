---
status: ready
---

# SQL connection encryption and certificate validation

## Intention

Protect every SQL Server connection with encryption and server certificate validation by default. Make the development exception deliberate and configurable, while ensuring that a trust exception never lowers the encryption requirement. Primary and independently configured secondary connections must follow the same contract.

This concept defines a technical change independent of the user-configuration path change. It uses the existing per-user settings mechanism without changing its paths or loading behavior.

## Current evidence

- [SqlConnectionFactory](../../src/SqlToAi/Database/SqlConnectionFactory.cs) constructs primary SQL connections with `TrustServerCertificate=true`; it does not explicitly require encryption.
- [SecondaryConnectionBuilder](../../src/SqlToAi/Database/SecondaryConnectionBuilder.cs) independently constructs dedicated secondary connections with the same trust bypass. It serves metadata and central anonymization-rule databases.
- When a secondary server is not configured, the builder delegates to the primary `IDatabaseConnectionFactory`. This fallback must retain the primary policy.
- [SqlToAiOptions](../../src/SqlToAi/Configuration/SqlToAiOptions.cs) and the shipped [appsettings template](../../src/SqlToAi/appsettings.json) define the current SQL and secondary credential settings; there is no certificate-validation option today.
- [Configuration documentation](../../docs/configuration.md) records that certificate validation is disabled and no switch is exposed. [Security documentation](../../docs/security.md) identifies the primary factory's trust bypass as a boundary.
- [Microsoft.Data.SqlClient](../../Directory.Packages.props) is pinned to 7.0.2. Encryption is already enabled by the driver's default, but neither builder sets the requirement explicitly. The change makes encryption an application invariant and removes the default certificate-validation bypass; it does not claim that current traffic is necessarily unencrypted. Microsoft's [encryption contract](https://learn.microsoft.com/en-us/sql/connect/ado-net/encryption-and-certificate-validation) documents `Mandatory` with either trust setting; `Strict` would prevent the intended development exception.
- [MetadataProvider](../../src/SqlToAi/Metadata/MetadataProvider.cs) logs connection failures and returns missing descriptions. [AnonymizationRuleProvider](../../src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs) logs failures and returns no central rules, retaining default masking. These optional-provider fallbacks are distinct from reconnecting with weaker transport settings.
- [SqlToAiErrorMapper](../../src/SqlToAi/Database/SqlToAiErrorMapper.cs) maps infrastructure failures to the existing `SQL-AI-0105` contract; TLS failure classification and safe diagnostics must be verified rather than assumed.
- The [local demo profile](../../src/SqlToAi/demo-profile.json) currently has no trust setting. The [test project](../../tests/SqlToAi.Tests/SqlToAi.Tests.csproj) copies it as `appsettings.integration.json`, which [SqlServerFixture](../../tests/SqlToAi.Tests/Integration/SqlServerFixture.cs) loads explicitly. Secure defaults therefore also affect existing local integration setup.
- The parent distribution concept already settles encrypted connections, certificate validation by default, and an explicit development trust exception. This file defines that connection-security slice only.

## Scope

### Must

- Require encrypted transport for primary and dedicated secondary SQL connections, including when a secondary connection falls back to the primary factory.
- Enable server certificate validation by default. The default must not trust an untrusted or self-signed certificate automatically.
- Expose an explicit, consistently named setting through the current options and configuration template that allows `TrustServerCertificate=true` for a deliberately selected local or self-signed development server.
- Keep encryption enabled when that trust exception is selected. Certificate-validation failure must never trigger a retry with encryption or validation disabled.
- Apply identical option semantics to the primary connection and dedicated metadata and anonymization-rule connections. Secondary connections must have sensible fallbacks that preserve the policy and must not silently bypass it.
- Keep option defaults authoritative and bind the setting through the existing `IOptions<SqlToAiOptions>` mechanism. Keep the options class, shipped `appsettings.json` template, and canonical configuration/security documentation consistent.
- Surface primary TLS/certificate connection failures through the existing connection/infrastructure error contract. Optional secondary providers retain their existing missing-description/default-masking behavior and emit an explicit, safe TLS/connection diagnostic. Error messages and logs on these failure paths must not disclose passwords, full connection strings, or other secrets.
- Explain the compatibility change for existing user settings and the local demo/integration setup: an omitted trust option now validates the certificate. Any development bypass must be explicitly configured, never inferred from the server name, environment, or test execution.
- Preserve existing SQL Server access checks, read-only behavior, transaction and rollback guarantees, stdio protocol behavior, and the documented error catalog apart from any narrowly required TLS-specific clarification.

### Out of scope

- Changes to configuration-file location, per-user directory setup, legacy import, or configuration migration. The separate user-configuration concept owns the move to `%LOCALAPPDATA%/SqlToAi` with no legacy import; this task remains independently plannable and uses the settings mechanism then in place.
- CI workflow, release gates, test-suite partitioning, SQL integration orchestration, and client test matrices. The separate release-validation concept owns those decisions; SQL integration remains local and no client matrix is added.
- Certificate issuance, installation, rotation, pinning, custom trust stores, vault integration, paid certificates, or a general certificate-management feature.
- Azure authentication or other authentication-scope changes.
- SQL guard, access-policy, anonymization, metadata, or query behavior changes unrelated to transport security.
- Any silent fallback that retries after a trust failure with encryption disabled or certificate checks relaxed.

## Target contract

- Both connection builders explicitly set `Encrypt=Mandatory` (`SqlConnectionEncryptOption.Mandatory`) and take `TrustServerCertificate` from the endpoint's options, defaulting to `false`. Encryption is not a separate user-disableable setting. `Strict`, certificate-name overrides and protocol-version controls are outside this change.
- A deliberate configuration value of `TrustServerCertificate=true` relaxes certificate authentication only for the configured development case; it does not disable encryption.
- Use `SqlServer.TrustServerCertificate` for the primary endpoint and the corresponding `MetadataProvider.TrustServerCertificate` / `AnonymizationRules.TrustServerCertificate` options for independently configured endpoints. Each dedicated endpoint requires its own deliberate exception; relaxing trust for a local primary server must not automatically relax trust for an independently configured remote server.
- A secondary connection without a dedicated server uses the primary factory and its effective policy, preserving the existing fallback relationship. Invalid, missing, or unavailable secondary settings follow explicit existing failure/fallback semantics; they never construct a less protected connection as a recovery action.
- A primary server certificate failure returns `SQL-AI-0105`, retaining the existing timeout contract for actual timeouts. Optional metadata/rule-provider failures remain visible through safe diagnostics while preserving their current fallback results; they do not newly fail the enclosing tool call. Neither provider retries on the primary server after a dedicated endpoint fails.
- Diagnostics identify the affected configuration section and TLS/certificate problem without including credentials or a raw connection string. This is scoped to connection failure handling, not a general logging/redaction redesign.
- The setting is documented with its secure default, the development exception, and the fact that `true` trusts the presented server certificate rather than validating its identity.
- Existing user settings are not rewritten. The main template and shipped demo profile retain certificate validation by default; local self-signed setups must deliberately opt in. Transport-security verification uses separate secure-default and explicit-exception cases, so a permissive local test configuration cannot mask a default-policy regression.

### Connection invariants

- The effective encryption requirement is fixed on for every SqlClient builder covered by this concept.
- Certificate-trust semantics and secure defaults are uniform; deliberate exceptions belong to the endpoint's own connection configuration.
- The primary factory remains the policy source for secondary fallback connections; dedicated secondary construction receives its own configured trust value explicitly.
- Authentication choice, target database, application name, and connection timeout retain their existing responsibilities.
- A validation or handshake error fails the attempted connection and follows the existing primary error or optional-provider fallback contract without credential-bearing diagnostics.

## Verification

- Confirm option defaults and template values agree: encryption is required and certificate trust bypass is off unless the user explicitly opts in.
- Verify connection-string construction for primary and dedicated secondary builders, including independent trust exceptions, and verify secondary fallback delegates to the primary policy even if the unused secondary trust setting differs. A primary exception must not propagate to a dedicated secondary endpoint. Verify the metadata and anonymization-rule providers actually pass their respective options to the shared builder.
- Verify option binding and documented configuration names agree across the options class, template, and docs.
- Run local SQL integration over TCP against a trusted-certificate SQL Server and confirm successful connection with defaults; against an untrusted/self-signed certificate, confirm default rejection; then confirm the explicit trust exception succeeds. Verify encryption on the established sessions, not only in the connection string. Cover primary and dedicated secondary paths and verify a primary exception cannot make an untrusted dedicated secondary succeed.
- Keep builder/connection-string checks distinct from transport evidence: string construction alone does not prove that a TLS handshake encrypted traffic or that certificate validation rejected a server. Record unavailable trusted/untrusted SQL infrastructure as unverified rather than passed.
- Verify representative TLS/certificate failures map to `SQL-AI-0105` on primary operations. Verify optional secondary TLS failures retain missing-description/default-masking results and emit safe diagnostics, without retrying through a different endpoint or trust policy. Use synthetic credential markers to check responses and captured logs for disclosure. Confirm stdout remains reserved for the existing protocol or requested CLI data.

### Acceptance criteria

- A fresh/default configuration cannot establish a SQL connection by skipping server certificate validation, and requests encrypted transport.
- The explicit development setting permits the chosen untrusted certificate while retaining encrypted transport; no automatic retry changes either setting.
- Primary and dedicated secondary paths require encryption and default certificate validation, with exceptions scoped to the selected endpoint. Secondary-to-primary fallback inherits the primary policy.
- A primary TLS/certificate failure remains actionable as `SQL-AI-0105`. Optional secondary failures retain existing fallback results with explicit safe diagnostics. Responses and logs on these paths do not expose authentication secrets or raw connection strings.
- Configuration and security documentation state the exact default and exception, and do not imply that the exception validates server identity.

## Dependencies

- The publication concept is the scope's provenance and consumes this task's result, not an implementation prerequisite. This task can be completed before any package or directory is published.
- Uses the current `SqlToAiOptions` binding and SQL connection builders; implementation is compatible with the then-current settings path.
- Requires access to local SQL Server instances or equivalent local integration fixtures with a trusted certificate and an untrusted/self-signed certificate for transport-level evidence.
- Coordinates documentation wording with [user configuration](../user-configuration/Konzept.md) and [release validation](../release-validation/Konzept.md) without depending on their implementation or changing their scope. The [publication concept](../mcp-server-dictionary/Konzept.md) consumes this task's verification evidence.

No concept decisions remain open. Roadmap creation and implementation require their separately invoked workflow stages; concept approval does not authorize publication.
