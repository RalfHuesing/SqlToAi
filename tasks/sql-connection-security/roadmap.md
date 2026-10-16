# SQL connection security roadmap

## Basis and execution rules

Implement the approved [concept](Konzept.md), in the order below. Its [target contract](Konzept.md#target-contract), [acceptance criteria](Konzept.md#acceptance-criteria) and [exclusions](Konzept.md#out-of-scope) govern every item. No concept decisions remain open. This roadmap does not start implementation or authorize publication.

Each T1–T5 item is one bounded agent session, including inspection, changes, validation and its atomic commit. Dependencies are sequential: T2 requires T1, T3 requires T2, T4 requires T3, and T5 follows T4. Keep execution evidence under the corresponding item in this file; do not create separate tracking artifacts. Mark an item complete only when every acceptance criterion is verified or an explicit user-accepted limitation is recorded without turning missing evidence into a pass. If an item proves too large, split it before execution without expanding the approved scope.

For every code-changing item, add or update meaningful xUnit v3 tests for its success and failure behavior, then run from the repository root:

```powershell
dotnet build SqlToAi.slnx
dotnet test SqlToAi.slnx
```

Compiler/analyzer warnings are errors. Record commands, exit codes and relevant results, distinguishing database-independent failures from unavailable local SQL infrastructure. An unavailable SQL scenario is unverified, never passed. On 2026-10-16 the user explicitly accepted T4 closure with the trusted/untrusted TCP evidence gap recorded; this does not constitute transport proof. T5 remains a separate independent audit. Do not repeat successful checks on an unchanged revision without a new reason.

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

Validation: `dotnet build SqlToAi.slnx` exit 0 (0 warnings, 0 errors); `dotnet test SqlToAi.slnx` exit 1 (833 passed, 38 failed, 871 total); `dotnet test SqlToAi.slnx --filter "FullyQualifiedName!~Integration"` exit 0 (826 passed). All 38 failures belong to SQL integration cases blocked by the existing untrusted local server certificate. The two additional failures relative to T1 are database-list/search tests that previously passed through the static-list fallback; these now correctly surface TLS `SQL-AI-0105`. Actual local primary service responses show the safe TLS diagnostic. The unavailable trusted/encrypted-session scenarios remain unverified for T4; no trust override, user-settings change or SQL/certificate-system change was made. `git diff --check` and affected Markdown-link checks passed. Atomic commit: `1c8ee60`; T3–T5 remain open.

## T3 — Preserve safe optional-provider failure behavior

- [x] T3: Verify and, where necessary, correct secondary TLS diagnostics and fallback behavior.

**Intention:** Keep optional enrichment failures observable without weakening transport security or changing their established results.

**Scope:** Apply the TLS diagnostic handling established in T2 to metadata table/column lookups and central rule loading. Inspect the dedicated-server and primary-fallback paths, their connection-open handling and provider caches. Update [configuration](../../docs/configuration.md) and [security](../../docs/security.md) to describe optional-provider behavior precisely. Extend [metadata tests](../../tests/SqlToAi.Tests/Metadata/MetadataProviderTests.cs) and [rule-provider tests](../../tests/SqlToAi.Tests/Anonymization/AnonymizationRuleProviderTests.cs) with controlled connection failures and captured logs.

**Exclusions:** Do not make optional-provider failures newly fail the enclosing tool call, change cache policy or rule precedence, change masking semantics, or reconnect to another server after a dedicated endpoint fails. No broad provider/API refactoring.

**Acceptance:**

- Metadata TLS failures retain missing table/column descriptions. Rule-provider TLS failures retain the existing empty-rule/default-masking behavior, including existing cache semantics.
- Safe diagnostics identify `MetadataProvider` or `AnonymizationRules` and the TLS/connection failure. Captured messages and exception payloads contain no synthetic credentials or raw connection strings.
- Failure tests verify no retry through a different endpoint and no relaxation of encryption or certificate validation. Default masking remains active when rules cannot be loaded; existing exempt-value behavior still works when valid rules are available.
- Documentation distinguishes this optional-provider result fallback from the primary connection-error response and from a prohibited transport downgrade.
- Required build/test results and any infrastructure limitations are recorded; relevant tests pass and the slice is committed.

**Completion evidence:** Implementation agent: `gpt-6-luna`, reasoning effort `high`; based on T2 commit `1c8ee60`. Inspected both provider implementations, their secondary connection builder paths, existing result behavior and rule cache. Metadata table failures still return `null`; column failures still return an empty dictionary. Rule-load failures still return and cache an empty rule list for `AnonymizationRules.CacheTtlSeconds`, so no optional rule exempts a column and default masking remains active. Existing valid-rule exemption behavior remains covered by `IsExcludedAsync_ShouldExclude_WhenWildcardRuleAllowsWholeTable`.

The providers now classify caught errors with the existing TLS classifier and emit safe diagnostics naming `MetadataProvider` or `AnonymizationRules`. They never pass exception objects, inner exceptions, credentials, connection strings, table names or database names to those log events. The rule provider's optional `SchemaPattern` check keeps its prior schema-agnostic fallback and now logs without an exception payload. Tests use synthetic TLS OpenAsync failures with credential/raw-connection markers and verify every captured log message/payload, missing-description/default-masking results, and exactly one cached load. Dedicated-provider tests use a local TCP endpoint that accepts then closes the SQL protocol connection; both provider results remain optional, and the primary factory's endpoint count stays zero. Existing connection-builder tests continue to assert `Encrypt=Mandatory` and endpoint-local trust options. This TCP case proves connection failure and no primary retry only; it is not transport-security evidence.

Validation on this revision: `dotnet build SqlToAi.slnx` exit 0 (0 warnings, 0 errors); focused provider tests exit 0 (33 passed); `dotnet test SqlToAi.slnx --filter "FullyQualifiedName!~Integration"` exit 0 (832 passed); full `dotnet test SqlToAi.slnx` exit 1 (839 passed, 38 failed, 877 total). All 38 failures are the known local SQL integration cases blocked by the untrusted SQL Server certificate; no server, machine trust, certificate or user settings were changed. `git diff --check` and Markdown-link checks passed. The true trusted/untrusted transport and encrypted-session cases remain unverified for T4. Atomic commit: `2d77cf2`; T4 and T5 remain open.

## T4 — Verify actual local TLS behavior

- [x] T4: Record transport evidence and close with the user-accepted local infrastructure limitation.

**Intention:** Demonstrate the difference between a correctly constructed connection string and an encrypted, authenticated SQL session.

**Scope:** Add narrowly scoped local TLS integration cases in the existing [integration test area](../../tests/SqlToAi.Tests/Integration/) and document their local inputs and execution in [development documentation](../../docs/development.md). Reuse the application factories/providers and error paths from T1–T3. Inspect [SqlServerFixture](../../tests/SqlToAi.Tests/Integration/SqlServerFixture.cs) and its explicitly copied demo profile before adapting local test setup. Keep deliberate development exceptions separate from secure-default cases; never make tests or startup infer a bypass. Use already available, explicitly configured local TCP endpoints with a trusted, name-matching certificate and an untrusted/self-signed certificate. Verify encryption on each successful session with a session/server observation, not only connection-string assertions.

**Exclusions:** Do not provision/install certificates, alter machine trust stores or SQL Server configuration, introduce integration orchestration/test-suite partitioning, modify CI/release gates, use production data or persist secrets in tests, documentation or evidence.

**Acceptance:**

- The available local self-signed instance and the unavailable trusted/untrusted TCP cases are recorded distinctly; unavailable transport cases remain explicitly unverified.
- The user-accepted limitation is documented in the concept and here. No missing trusted/untrusted TCP case is represented as passed, and no full transport-proof claim is made.
- General integration tests use an explicit checked-in test-only trust overlay to support the existing local instance. Application defaults, shipped profiles, and independent TLS default-policy cases remain strict; environment overrides retain their precedence, including explicit `false`.
- Local setup instructions identify the test-only exception and explain that a green general suite using it does not establish secure-default certificate authentication. Required build/test results are recorded and the related changes are committed.

**Historical partial evidence (2026-10-15):** Implementation agent: `gpt-6.1-sol`, reasoning effort `medium`; based on T3 commit `2d77cf2`. Added 19 narrowly scoped TLS integration cases in `TlsConnectionIntegrationTests`, without production changes. The general integration fixture accepted explicitly supplied `SQLTOAI_INTEGRATION_` environment overrides after its copied demo profile. The TLS cases read the independently supplied `SQLTOAI_TLS_TEST_PROFILE` file, require secure-default inputs, and choose trust afresh per case; the general fixture exception cannot hide a TLS default regression. Missing endpoints produce explicit skipped/unverified results. Updated the canonical development instructions with exact inputs, commands and observation limits.

**User-accepted closure decision (2026-10-16):** The user accepts that this environment cannot complete the trusted/untrusted TCP matrix and authorizes closing T4 with that limitation recorded. This accepts an evidence gap, not a pass. The self-signed local SQL instance remains available; a checked-in test-only overlay now explicitly trusts its certificate for general integration cases. T1 application defaults, shipped profiles, and TLS default cases remain independent and certificate-validating. T5 is still open for its own audit.

Available infrastructure: only the existing local named instance, labelled `LocalUntrusted` below. Read-only host inspection reported no configured certificate, `ForceEncryption=0` and TCP disabled. No additional instance, certificate, SQL configuration, machine trust or user settings was provisioned or changed. The test input used a temporary file outside the checkout with the existing fictional DemoDB profile's endpoint/authentication and a five-second connection timeout; it was removed after each command. No credentials or raw connection strings are recorded here.

| Endpoint/path | Explicit trust | Execution/result | Session observation |
| --- | --- | --- | --- |
| LocalUntrusted / primary factory | false | Real SqlClient TLS/certificate rejection | No established session |
| LocalUntrusted / primary validation service | false | Actual `QueryValidationService` returns `SQL-AI-0105` with safe `SqlServer` TLS diagnostic; one factory call, no retry | No established session |
| LocalUntrusted / dedicated metadata | false; primary true | Provider-created connection rejects TLS; real lookup returns missing description and safe `MetadataProvider` TLS log; primary factory count zero | No established session |
| LocalUntrusted / dedicated rules | false; primary true | Provider-created connection rejects TLS; real lookup retains default masking and safe `AnonymizationRules` TLS log; primary factory count zero | No established session |
| LocalUntrusted / primary factory | true | Successful connection | Own `sys.dm_exec_connections` row: `encrypt_option=TRUE`, `net_transport=Shared memory` |
| LocalUntrusted / dedicated metadata | true | Successful provider-created connection and real metadata lookup | Both separately observed sessions: `TRUE`, `Shared memory`; lookup uses the actual custom metadata query |
| LocalUntrusted / dedicated rules | true | Successful provider-created connection; separate real optional lookup completes without warning/error and retains default masking for a missing fictional table | Provider-created connection: `TRUE`, `Shared memory`; the separate real rule lookup session was not directly observed |
| LocalUntrusted / metadata and rules fallback | primary false; unused secondary true | Both actual providers reject TLS, retain optional results/logs, exactly two primary factory calls | No established session |
| LocalUntrusted / metadata and rules fallback | primary true; unused secondary false | Both actual providers complete without warning/error, exactly two primary factory calls | Actual metadata lookup: `TRUE`, `Shared memory`; rule lookup session not directly observed |
| TrustedTcp / primary and both dedicated paths | false | Unverified: trusted, name-matching TCP endpoint unavailable; 3 cases skipped | Unverified |
| UntrustedTcp / primary and both dedicated paths, plus primary service error | false / separate true cases | Unverified: untrusted TCP endpoint unavailable; 7 cases skipped | Unverified |

Provider-created sessions above use each actual provider's existing private `CreateConnection` method through narrowly scoped test reflection, then open/query that returned real connection. This verifies current provider option wiring and session behavior without a production test hook. It does not observe the later rule lookup's separate session. Shared-memory observations are limited local evidence and make no TCP or trusted-certificate authentication claim. Independent rejection results remain recorded separately from exception successes.

Historical validation before the accepted overlay: `dotnet build SqlToAi.slnx` exit 0 (0 warnings, 0 errors). Focused TLS tests with only the explicit local TLS profile exited 0 (9 passed, 10 skipped/unverified). The full suite without overrides exited 1 (839 passed, 38 failed, 19 skipped, 896 total); the general local SQL tests failed secure-default certificate validation. The full suite with `SQLTOAI_INTEGRATION_SqlToAi__SqlServer__TrustServerCertificate=true` and the independent `SQLTOAI_TLS_TEST_PROFILE` exited 0 (886 passed, 0 failed, 10 skipped, 896 total). These results are historical; the exception run did not satisfy missing TCP/trusted criteria.

**Test-fixture correction evidence:** Implementation agent `gpt-6-luna`, reasoning effort `high`. Added a checked-in overlay used only by `SqlServerFixture`, a regression for overlay defaults and explicit environment `false`, and verified that the demo profile remains `false`. The TLS cases continue to load their separate `SQLTOAI_TLS_TEST_PROFILE`, require omitted/false trust input, and choose trust per case. Canonical development, configuration and security docs identify the test-only exception and preserve the unverified transport limitation.

Validation on the final code revision: `dotnet build SqlToAi.slnx` exit 0 (0 warnings, 0 errors); `dotnet test SqlToAi.slnx` exit 0 (879 passed, 19 skipped/unverified, 898 total). The skips are TLS cases with no `SQLTOAI_TLS_TEST_PROFILE`. Release-script build command `dotnet build --nologo -v q` exit 0 (0 warnings, 0 errors), then release-script test command `dotnet test --nologo -v q --no-build` exit 0 (879 passed, 19 skipped/unverified, 898 total), with no manual environment overrides. Focused TLS run with a temporary external profile for the existing `LocalUntrusted` instance exit 0 (9 passed, 10 skipped/unverified, 19 total); the temporary profile was removed, and the ten unavailable trusted/untrusted TCP cases remain unverified. Existing default/encryption builder checks remained in the passing suite. `git diff --check` and changed-document relative-link checks passed. No production settings, SQL Server, machine trust, certificate, or user configuration changed.

## T5 — Audit the completed concept

- [ ] T5: Audit all concept acceptance criteria and record the final result.

**Intention:** Close the task only when implementation, documentation and transport evidence agree with the approved concept.

**Scope:** Review the final changes against the [concept acceptance criteria](Konzept.md#acceptance-criteria) and all T1–T4 acceptance criteria. Inspect every covered connection-construction path, option flow, failure response and diagnostic sink. Check secure defaults, endpoint isolation, fallback delegation, absence of downgrade/retry behavior and preservation of security/protocol invariants. Verify documentation links/examples and the required build/test evidence for the final code revision. Record concise acceptance-to-test/evidence references here; fix bounded findings with relevant tests/docs and rerun affected checks before closure.

**Exclusions:** No publication, push, release work, new feature scope or unrelated cleanup. No completion claim based solely on unit tests or a live connection using the trust exception.

**Acceptance:**

- T1–T4 are complete with evidence or an explicit user-accepted limitation; the auditor must preserve any unverified transport case as unverified and confirm that the limitation is not presented as a passed criterion.
- The final build succeeds without compiler/analyzer warnings and the required tests pass; reuse successful results only when they cover the final code revision. Security/access/transaction and masking regressions are resolved.
- Canonical docs describe the implemented default, compatibility change, development exception, primary errors and optional-provider results accurately. No secrets or temporary local settings are staged.
- Audit findings are resolved, this roadmap reflects verified completion, and the final related changes are committed with unrelated user changes left intact.

**Completion evidence:** Pending.
