# SQL connection security roadmap

## Basis and execution rules

Implement the approved [concept](Konzept.md), in the order below. Its [target contract](Konzept.md#target-contract), [acceptance criteria](Konzept.md#acceptance-criteria) and [exclusions](Konzept.md#out-of-scope) govern every item. No concept decisions remain open. This roadmap does not start implementation or authorize publication.

Each T1–T5 item is one bounded agent session, including inspection, changes, validation and its atomic commit. Dependencies are sequential: T2 requires T1, T3 requires T2, T4 requires T3, and T5 requires T4. Keep execution evidence under the corresponding item in this file; do not create separate tracking artifacts. Mark an item complete only when every acceptance criterion is verified. If an item proves too large, split it before execution without expanding the approved scope.

For every code-changing item, add or update meaningful xUnit v3 tests for its success and failure behavior, then run from the repository root:

```powershell
dotnet build SqlToAi.slnx
dotnet test SqlToAi.slnx
```

Compiler/analyzer warnings are errors. Record commands, exit codes and relevant results, distinguishing database-independent failures from unavailable local SQL infrastructure. An unavailable SQL scenario is unverified, never passed. A verified code slice may be committed with that limitation recorded, but T4 and the final audit remain open until required transport evidence exists. Do not repeat successful checks on an unchanged revision without a new reason.

Update canonical documentation in the same slice as the behavior it describes. Verify Markdown links and examples. Stage only related changes, include the roadmap update, use a German imperative Conventional Commit message, and do not push. Record commit references after they exist; do not amend a completed commit solely to record its own hash.

## T1 — Apply secure endpoint settings to every connection path

- [x] T1: Implement and verify explicit encryption and endpoint-specific certificate trust.

**Intention:** Make encrypted transport and default certificate validation consistent for primary, dedicated secondary and secondary-to-primary fallback connections.

**Scope:** Inspect [options](../../src/SqlToAi/Configuration/SqlToAiOptions.cs), [SqlConnectionFactory](../../src/SqlToAi/Database/SqlConnectionFactory.cs), `SecondaryConnectionSettings` in [SecondaryConnectionBuilder](../../src/SqlToAi/Database/SecondaryConnectionBuilder.cs) and both consumers: [MetadataProvider](../../src/SqlToAi/Metadata/MetadataProvider.cs) and [AnonymizationRuleProvider](../../src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs). Add a default-false `TrustServerCertificate` option to each of `SqlServer`, `MetadataProvider` and `AnonymizationRules`, pass each dedicated provider's value explicitly, and set `SqlConnectionEncryptOption.Mandatory` in both builders. Retain the primary factory as the policy source when no dedicated server is configured.

Keep the [main template](../../src/SqlToAi/appsettings.json) and [demo profile](../../src/SqlToAi/demo-profile.json) secure by default. Update [configuration](../../docs/configuration.md), [security](../../docs/security.md), and the affected setup guidance in [SETUP.md](../../SETUP.md) and [development documentation](../../docs/development.md). Explain the compatibility change, exact option names, deliberate local exception and lack of automatic settings migration. Extend the existing option, builder and provider tests; exercise actual configuration binding and provider wiring as well as direct builder inputs.

**Exclusions:** TLS error classification and diagnostic changes belong to T2/T3; live transport proof belongs to T4. Do not add an encryption-disable option, implicit development detection, credential/authentication changes, new configuration loading rules or changes to connection timeouts.

**Acceptance:**

- Omitted options and explicit `false` validate certificates; explicit `true` affects only its configured endpoint. Both builders explicitly require `Mandatory` for both trust values, including SQL and integrated authentication.
- A primary exception cannot relax a dedicated metadata or rule connection. A secondary exception cannot relax the primary connection or another dedicated endpoint.
- Without a dedicated server, fallback uses the primary factory and its effective policy even when the unused secondary trust value differs. Configured database selection and existing missing-server/database/credential behavior remain intact.
- Tests demonstrate that both providers forward their own bound option. Options, templates and documented JSON/environment names agree; the demo profile does not opt in automatically.
- Existing credentials, application names, authentication, timeouts and user-owned settings retain their responsibilities. Documentation states that the exception skips identity validation while keeping encryption enabled.
- Required build/test results and any infrastructure limitations are recorded; relevant tests pass and the slice is committed.

**Completion evidence:** Implementation agent: `gpt-6-luna`, reasoning effort `high`. Inspected the primary and secondary builders and both provider call paths. Both builders now set `Encrypt=Mandatory` and take `TrustServerCertificate` from their endpoint options, whose omitted/default value is `false`; secondary fallback still delegates to the primary factory. Configuration binding, main and demo template defaults, explicit trust exceptions, provider-specific option forwarding, primary/secondary isolation, and fallback policy are covered by tests. Updated `configuration.md`, `security.md`, `SETUP.md`, and `development.md`; docs describe the compatibility change, exact JSON/environment names, the certificate-identity exception with encryption retained, and the absence of automatic migration. The shipped DemoDB profile remains strict by default.

Validation on this revision: `dotnet build SqlToAi.slnx` exit 0 (0 warnings, 0 errors); `dotnet test SqlToAi.slnx --filter "FullyQualifiedName!~Integration"` exit 0 (784 passed); full `dotnet test SqlToAi.slnx --logger "console;verbosity=minimal"` exit 1 (793 passed, 36 failed, 829 total). All 36 failures are SQL integration cases unable to pass TLS login because the local SQL Server presents a certificate chain from an untrusted CA; no additional server/certificate was provisioned and the demo profile was not changed to trust it automatically. This does not establish T4 transport evidence. `git diff --check` passed. Atomic commit: `9d6afd6`.

## T2 — Keep primary TLS failures actionable and safe

- [x] T2: Verify and, where necessary, correct primary TLS error mapping and diagnostics.

**Intention:** Report certificate/handshake failures through the existing infrastructure error contract without exposing authentication secrets.

**Scope:** Inspect [SqlToAiErrorMapper](../../src/SqlToAi/Database/SqlToAiErrorMapper.cs), its production callers and other primary connection-opening services, including paths with their own exception handling. Trace connection-open failures through tool dispatch and CLI formatting; do not assume all failures reach the shared mapper. Make only the changes needed for safe TLS handling, including affected logging before error mapping. Preserve useful diagnostics identifying `SqlServer` and the TLS/certificate problem. Clarify the existing [error catalog](../../docs/tools.md#error-catalog) and [security documentation](../../docs/security.md) as needed. Extend mapper and affected service/dispatcher tests using representative SqlClient failure shapes and captured diagnostics.

**Exclusions:** Do not redesign all logging, introduce new error codes, change ordinary query-error behavior, implement secondary-provider policy or add retry/downgrade behavior.

**Acceptance:**

- Representative primary certificate/handshake failures produce `SQL-AI-0105` through the actual affected service/tool path; actual timeouts retain `SQL-AI-0106`. Ordinary SQL failures retain their existing handling.
- Safe connection diagnostics remain actionable and identify the primary configuration section. Synthetic password and raw connection-string markers in exception messages or inner exceptions appear in neither returned errors nor captured connection-failure logs, including exception payloads.
- Tests cover affected logging and custom-message paths, so sanitizing only the shared mapper cannot leave an earlier disclosure unnoticed.
- MCP stdout remains protocol-only; CLI errors retain stderr/nonzero-exit behavior. Access checks, transaction guarantees, masking and token handling remain intact.
- Required build/test results and any infrastructure limitations are recorded; relevant tests pass and the slice is committed.

**Completion evidence:** Implementation agent: `gpt-6.1-sol`, reasoning effort `high`; based on T1 commit `9d6afd6`. Inspected all 12 primary connection-open sites and their service logging, schema/index custom-error paths, script reports, dispatcher formatting and CLI output. A small primary-open helper now discards original provider exceptions before service logging, retaining only a safe error code and `SqlServer` diagnostic without inner exceptions. Certificate/SSPI codes and bounded provider/handshake shapes map to `SQL-AI-0105`; actual timeouts retain `SQL-AI-0106`, including nested timeouts and timeout messages mentioning TLS. Ordinary SQL errors containing `certificate`, `TLS` or provider words retain their query contract. Database discovery surfaces TLS/open-timeout errors instead of concealing them behind the configured-list fallback; other existing discovery fallbacks remain.

Added 42 database-independent test cases: mapper failure shapes and query-error regressions, real service-to-dispatcher paths for discovery/schema, index suggestions, anonymized query execution, validation, comparison, performance and benchmark; both script transaction modes and export-object discovery; cancellation and existing discovery fallback; actual CLI formatting of a TLS tool failure (empty stdout, stderr error, exit 1). Captured service logs verify both messages and exception payloads, including absent inner exceptions, against synthetic password/raw-connection-string markers. Each controlled failure opens exactly once. Updated the canonical error catalog and security documentation; secondary provider behavior remains for T3.

Validation: `dotnet build SqlToAi.slnx` exit 0 (0 warnings, 0 errors); `dotnet test SqlToAi.slnx` exit 1 (833 passed, 38 failed, 871 total); `dotnet test SqlToAi.slnx --filter "FullyQualifiedName!~Integration"` exit 0 (826 passed). All 38 failures belong to SQL integration cases blocked by the existing untrusted local server certificate. The two additional failures relative to T1 are database-list/search tests that previously passed through the static-list fallback; these now correctly surface TLS `SQL-AI-0105`. Actual local primary service responses show the safe TLS diagnostic. The unavailable trusted/encrypted-session scenarios remain unverified for T4; no trust override, user-settings change or SQL/certificate-system change was made. `git diff --check` and affected Markdown-link checks passed. Atomic commit is reported after creation; T3–T5 remain open.

## T3 — Preserve safe optional-provider failure behavior

- [ ] T3: Verify and, where necessary, correct secondary TLS diagnostics and fallback behavior.

**Intention:** Keep optional enrichment failures observable without weakening transport security or changing their established results.

**Scope:** Apply the TLS diagnostic handling established in T2 to metadata table/column lookups and central rule loading. Inspect the dedicated-server and primary-fallback paths, their connection-open handling and provider caches. Update [configuration](../../docs/configuration.md) and [security](../../docs/security.md) to describe optional-provider behavior precisely. Extend [metadata tests](../../tests/SqlToAi.Tests/Metadata/MetadataProviderTests.cs) and [rule-provider tests](../../tests/SqlToAi.Tests/Anonymization/AnonymizationRuleProviderTests.cs) with controlled connection failures and captured logs.

**Exclusions:** Do not make optional-provider failures newly fail the enclosing tool call, change cache policy or rule precedence, change masking semantics, or reconnect to another server after a dedicated endpoint fails. No broad provider/API refactoring.

**Acceptance:**

- Metadata TLS failures retain missing table/column descriptions. Rule-provider TLS failures retain the existing empty-rule/default-masking behavior, including existing cache semantics.
- Safe diagnostics identify `MetadataProvider` or `AnonymizationRules` and the TLS/connection failure. Captured messages and exception payloads contain no synthetic credentials or raw connection strings.
- Failure tests verify no retry through a different endpoint and no relaxation of encryption or certificate validation. Default masking remains active when rules cannot be loaded; existing exempt-value behavior still works when valid rules are available.
- Documentation distinguishes this optional-provider result fallback from the primary connection-error response and from a prohibited transport downgrade.
- Required build/test results and any infrastructure limitations are recorded; relevant tests pass and the slice is committed.

**Completion evidence:** Pending.

## T4 — Verify actual local TLS behavior

- [ ] T4: Establish the approved transport evidence for primary and dedicated secondary connections.

**Intention:** Demonstrate the difference between a correctly constructed connection string and an encrypted, authenticated SQL session.

**Scope:** Add narrowly scoped local TLS integration cases in the existing [integration test area](../../tests/SqlToAi.Tests/Integration/) and document their local inputs and execution in [development documentation](../../docs/development.md). Reuse the application factories/providers and error paths from T1–T3. Inspect [SqlServerFixture](../../tests/SqlToAi.Tests/Integration/SqlServerFixture.cs) and its explicitly copied demo profile before adapting local test setup. Keep deliberate development exceptions separate from secure-default cases; never make tests or startup infer a bypass. Use already available, explicitly configured local TCP endpoints with a trusted, name-matching certificate and an untrusted/self-signed certificate. Verify encryption on each successful session with a session/server observation, not only connection-string assertions.

**Exclusions:** Do not provision/install certificates, alter machine trust stores or SQL Server configuration, introduce integration orchestration/test-suite partitioning, modify CI/release gates, use production data or persist secrets in tests, documentation or evidence.

**Acceptance:**

- For the primary factory and each dedicated provider path: defaults connect successfully to the trusted endpoint; defaults reject the untrusted certificate; an explicit exception for that endpoint connects to the untrusted endpoint with observed session encryption still enabled.
- With only the primary exception enabled, dedicated untrusted secondary connections still fail certificate validation. No dedicated failure reconnects through the primary endpoint. Secondary-to-primary fallback follows the primary policy established in T1.
- Real primary certificate failure reaches `SQL-AI-0105`; optional secondary failures retain the T3 results and safe diagnostics. The successful exception case does not overwrite or hide the preceding default-rejection evidence.
- Record each case's path, certificate condition, explicit trust setting, execution/result and encryption observation, using non-secret endpoint labels. Record missing infrastructure as unverified and leave this item open until all required cases are verified.
- Local setup instructions explain how to supply deliberate test inputs without changing secure shipped defaults or rewriting the user's configuration. Required build/test results are recorded and completed changes are committed.

**Completion evidence:** Pending.

## T5 — Audit the completed concept

- [ ] T5: Audit all concept acceptance criteria and record the final result.

**Intention:** Close the task only when implementation, documentation and transport evidence agree with the approved concept.

**Scope:** Review the final changes against the [concept acceptance criteria](Konzept.md#acceptance-criteria) and all T1–T4 acceptance criteria. Inspect every covered connection-construction path, option flow, failure response and diagnostic sink. Check secure defaults, endpoint isolation, fallback delegation, absence of downgrade/retry behavior and preservation of security/protocol invariants. Verify documentation links/examples and the required build/test evidence for the final code revision. Record concise acceptance-to-test/evidence references here; fix bounded findings with relevant tests/docs and rerun affected checks before closure.

**Exclusions:** No publication, push, release work, new feature scope or unrelated cleanup. No completion claim based solely on unit tests or a live connection using the trust exception.

**Acceptance:**

- T1–T4 are complete with verifiable evidence, all concept criteria are covered, and no required trusted/untrusted transport case remains unverified.
- The final build succeeds without compiler/analyzer warnings and the required tests pass; reuse successful results only when they cover the final code revision. Security/access/transaction and masking regressions are resolved.
- Canonical docs describe the implemented default, compatibility change, development exception, primary errors and optional-provider results accurately. No secrets or temporary local settings are staged.
- Audit findings are resolved, this roadmap reflects verified completion, and the final related changes are committed with unrelated user changes left intact.

**Completion evidence:** Pending.
