# Release validation roadmap

## Basis and execution rules

Implement the approved [concept](Konzept.md). Its [scope](Konzept.md#scope), [validation contract](Konzept.md#validation-contract), [verification](Konzept.md#verification) and accepted TCP TLS limitation govern every item. This roadmap creates no implementation, push, tag or public release.

Use this single roadmap; T1–T11 are bounded implementation assignments and T12 is the final read-only audit. Execute sequentially in the listed order. Each item includes inspection, implementation, meaningful verification, canonical documentation and an atomic commit. If an item exceeds one agent session, split it before executing without expanding scope. In stage 3, use the repository's subagent defaults unless the user assigns otherwise; record the actual model and effort with completion evidence. Do not start stage 3 merely because this roadmap exists.

Record completion evidence directly under the corresponding item: relevant commands, exit codes, test counts, source/artifact identifiers, limitations and commit references after they exist. Do not create task execution logs or duplicate tracking files. Machine-readable candidate/evidence files produced by the release tooling are validation artifacts, stored with workflow artifacts or the release decision, not a committed task-history inventory. Never record secrets or unsanitized SQL output.

For code changes, follow [project validation rules](../../.agents/rules/development.mdc) and run from the repository root:

```powershell
dotnet build SqlToAi.slnx
dotnet test SqlToAi.slnx
```

The full suite remains a local verification command; public CI uses the explicit database-independent selection introduced by T1. Record unavailable SQL cases separately, never as passes. The accepted TCP endpoint limitation permits only the candidate-specific disclosure and review defined in the concept; ordinary SQL failures or failed configured TLS cases remain blocking. Add xUnit v3 coverage for nontrivial C# behavior. Verify PowerShell/workflow failure behavior using bounded child-process tests or controlled fixtures without performing actual Git pushes, releases, maintainer process termination or destructive deployment. Do not repeat successful checks on unchanged inputs without a reason.

Update [development documentation](../../docs/development.md) in each affected slice; update [SQL setup guidance](../../sql-scripts/README.md), [configuration guidance](../../docs/configuration.md) and [agent validation rules](../../.agents/rules/system-specs.mdc) only where their commands or contracts change. Preserve user configuration, access policy, SQL/TLS semantics and existing stdio contracts. Validate Markdown links/examples and stage only related files. Use German imperative Conventional Commit descriptions; do not push.

## T1 — Classify tests by actual SQL dependency

- [ ] T1: Establish explicit database-independent, ordinary SQL and TLS selections.

**Intention:** Allow contributors and CI to run all applicable checks without a database while retaining explicit local SQL coverage.

**Scope:** Inspect the [test project](../../tests/SqlToAi.Tests/SqlToAi.Tests.csproj), real SQL collections, [fixture configuration checks](../../tests/SqlToAi.Tests/Integration/SqlServerFixtureConfigurationTests.cs), [TLS tests](../../tests/SqlToAi.Tests/Integration/TlsConnectionIntegrationTests.cs) and existing startup tests. Use explicit traits: `RequiresSql=true` on tests that need a real SQL endpoint, and `SqlSuite=General` or `SqlSuite=Tls` on those tests to distinguish ordinary integration from transport evidence. Keep database-independent tests outside SQL-dependent collections where necessary. Preserve existing categories when still useful; neither `Integration` in a name nor a folder is a selection rule.

Document and verify these PowerShell command selections against actual discovery:

```powershell
dotnet test SqlToAi.slnx --filter 'RequiresSql!=true'
dotnet test SqlToAi.slnx --filter 'RequiresSql=true&SqlSuite=General'
dotnet test SqlToAi.slnx --filter 'RequiresSql=true&SqlSuite=Tls'
```

**Exclusions:** No functional test rewrite for count/coverage targets, SQL provisioning, certificate changes or new client matrix.

**Acceptance:**
- Discovered ordinary SQL and TLS cases are selected separately and excluded from the database-independent command. Configuration-only, startup and any in-memory MCP tests remain selected without SQL.
- Database-independent execution succeeds with no personal configuration or available SQL endpoint. Missing ordinary SQL prerequisites fail or report unavailability explicitly; a filter selecting zero cases cannot establish success.
- Existing TLS skips retain their unverified meaning. Selection commands and local prerequisites are documented; build/test evidence and the classification review are recorded.

**Completion evidence:** Pending.

## T2 — Propagate native command failures in touched helpers

- [ ] T2: Make validation and deployment stop on failed native prerequisites.

**Intention:** Prevent successful-looking output and later operations after failed tests or publishing.

**Scope:** Inspect [deploy.ps1](../../scripts/deploy.ps1) and [release.ps1](../../scripts/release.ps1). Check native exit codes immediately for touched `dotnet`, Git and archive invocations, preserve working-directory cleanup, and share a small invocation helper only where it removes real duplication. Keep deployment's existing package shape and process policy; later items may reuse reliable invocation behavior.

**Exclusions:** No general deployment rewrite, process-management redesign, actual deployment or release, or changes to unrelated scripts.

**Acceptance:**
- Controlled build/test/publish failures return nonzero, suppress success claims and prevent dependent operations. Test failure prevents destructive publish-output replacement.
- Successful invocations retain the intended behavior; cleanup occurs on both paths. Verification uses temporary outputs and controlled child processes rather than stopping the maintainer's MCP sessions.
- Existing dry-run behavior performs no native mutations, Git writes, push or publication. Documentation no longer needs to warn that touched commands ignore exit status.

**Completion evidence:** Pending.

## T3 — Provide reusable database-independent validation and dependency disposition

- [ ] T3: Add one fail-closed local/CI validation entry point.

**Intention:** Give local contributors and workflows the same reproducible validation path.

**Scope:** Compose restore/build, T1's database-independent selection and dependency-vulnerability reporting into reusable PowerShell tooling with explicit inputs and checked exits. Include direct and transitive dependencies. Parse structured results supported by the installed toolchain; distinguish scan failure, clean result, findings requiring review and approved exceptions. Findings block candidate readiness unless each has an explicit reviewed disposition. A narrow exception identifies package/version, advisory, rationale, reviewer and bounded applicability; never add blanket suppression merely to make CI green.

**Exclusions:** No paid scanner, new hosted service, SQL connectivity, unrelated dependency upgrades or automatic acceptance of findings based on command exit zero.

**Acceptance:**
- A clean checkout can run the documented entry point without SQL or private configuration; the actual selected test run is nonempty.
- Test/build failure, malformed/missing scan output and scanner failure cause nonzero status. A report containing findings is visible and cannot silently become a clean report.
- Controlled reports cover clean results, unreviewed findings and applicable/inapplicable exceptions. Actual findings are fixed within focused scope or explicitly surfaced for review; no review approval is fabricated.

**Completion evidence:** Pending.

## T4 — Run required checks on pull requests and main

- [ ] T4: Add database-independent Windows CI using T3.

**Intention:** Check relevant changes before release preparation without access to the maintainer's environment.

**Scope:** Add a workflow under [.github/workflows](../../.github/workflows/) for relevant pull requests and `main` changes, using a standard GitHub-hosted Windows runner, .NET 10 and T3's entry point. Include source, tests, dependency/build configuration, validation scripts and workflow changes in trigger coverage. Keep job permissions minimal and fork-compatible; expose sanitized test/dependency summaries and failure artifacts. Give checks stable names suitable for repository branch rules without claiming those external settings have been configured.

**Exclusions:** No SQL credentials, private network, paid features, public release permission or repository-settings changes.

**Acceptance:**
- Workflow structure invokes the shared checks and propagates their failure; no test-selection duplication can drift from the documented local command.
- Trigger/path-filter review includes changes to the validation machinery itself. PR jobs need no secrets or write token and do not run untrusted PR code with privileged release access.
- Validate syntax and command behavior locally. Record any unavailable hosted-run evidence explicitly; a local review does not establish a successful GitHub run.

**Completion evidence:** Pending.

## T5 — Create one identifiable Windows candidate package

- [ ] T5: Extract reusable candidate packaging with source and ZIP identity.

**Intention:** Make the bytes to be checked and eventually published identifiable without rebuilding them later.

**Scope:** Factor package creation from the current [release workflow](../../.github/workflows/release.yml) into reusable tooling. Publish the self-contained, single-file `win-x64` output in Release configuration; include the current templates, package README and license as defined by the [application project](../../src/SqlToAi/SqlToAi.csproj). Create the ZIP, calculate SHA-256 and record exact commit, project version, runtime target and relevant build/dependency identity in a candidate manifest. Use dedicated temporary/output paths rather than deployment's process-killing path. T10 will wire this into the tag workflow.

**Exclusions:** No MCPB, Registry/manifests for external directories, Native AOT, package-content redesign or actual release.

**Acceptance:**
- A clean, versioned source revision produces a ZIP with the intended root layout and companion identity manifest. Dirty or ambiguous source identity cannot be silently labeled as an exact candidate.
- Failed publish/archive steps produce failure, not a ready candidate. The manifest hash matches the actual ZIP and changes when its bytes change.
- Scripts do not access personal settings or stop existing SqlToAi processes. Local reproduction limits are recorded; matching source does not imply byte-identical local/CI builds.

**Completion evidence:** Pending.

## T6 — Isolate Windows profile and first-run package checks

- [ ] T6: Launch the actual packaged EXE in verified isolated user storage.

**Intention:** Exercise real application startup without reading or modifying the maintainer's settings, logs or processes.

**Scope:** Implement a test-only Windows launch fixture for the T5 ZIP. Inspect `Environment.GetFolderPath(LocalApplicationData)` behavior used by [UserConfiguration](../../src/SqlToAi/Configuration/UserConfiguration.cs) and establish verified OS/profile isolation for the child process. Use a disposable test-only Windows account with its own loaded profile as the default isolation strategy, with narrowly scoped access to fixture files; an existing isolated Windows environment can supply that identity instead. Do not assume setting an environment variable redirects the Windows known folder. Document any local privileges/setup required, scope account/profile cleanup to resources created by the fixture, and fail before starting the server if isolation cannot be established. Existing [StartupHarness](../../tests/SqlToAi.StartupHarness/StartupHarness.cs) checks can supplement this fixture, but cannot replace the unchanged candidate EXE.

Run from an extraction path containing spaces and a different working directory. First run without settings must fail. Then prepare a conservative settings file manually inside the isolated user root for T7, with no grants, secrets or remote provider lookups. Retain a byte-identical settings sentinel and test binary replacement without overwriting it. Constrain cleanup to fixture-owned paths and child processes.

**Exclusions:** No production configuration override/portable mode, known-folder changes for the maintainer, actual personal-profile launch or new settings migration.

**Acceptance:**
- The fixture proves that the server's resolved user root is isolated. Lack of a safe local mechanism is a visible blocked/unverified check, never permission to use the real profile.
- Missing settings yield exit `1`, actionable stderr, empty stdout and no generated settings file. Prepared settings and sentinels survive starts and package replacement unchanged.
- Child cleanup runs on success, timeout and failure; fixture operations never terminate unrelated SqlToAi instances or delete paths outside their verified temporary root.

**Completion evidence:** Pending.

## T7 — Verify MCP and self-contained execution from the ZIP

- [ ] T7: Add reusable package protocol checks on top of T6.

**Intention:** Prove that the distributed executable starts and speaks MCP without SQL or an installed server runtime.

**Scope:** Start the extracted T5 EXE under T6 isolation with prepared settings. Use the MCP SDK/client or a bounded protocol harness to complete initialization, send the initialized notification as required and discover tools, including pagination if returned. Assert required registered tools and meaningful schemas against the existing registry without requesting SQL execution. Capture stdout/stderr separately, validate protocol framing and apply bounded startup/request/shutdown timeouts. Establish execution where an installed .NET SDK/runtime cannot silently supply missing server dependencies: verify that the server's loaded runtime modules originate from the package or its fixture-owned bundle extraction, and fail if an external runtime installation supplies them. Inspect bundled runtime/dependency content as well; a PATH change alone is insufficient. The harness may use the build toolchain.

**Exclusions:** No client certification matrix, SQL query execution, performance benchmark or runtime behavior changes.

**Acceptance:**
- The current ZIP's actual executable passes initialization/tool discovery from the space-containing path and different working directory; stdout contains valid protocol messages only.
- Invalid protocol output, missing tools, early exit, timeout and broken package/runtime requirements cause nonzero failure with bounded sanitized diagnostics. All started children are cleaned up.
- The result records the tested ZIP hash and source identity. Evidence explains how external runtime fallback was excluded; testing a rebuilt harness assembly is not reported as package proof.

**Completion evidence:** Pending.

## T8 — Produce candidate-specific local SQL evidence

- [ ] T8: Add local ordinary SQL and TLS evidence collection.

**Intention:** Give the release owner a verifiable local SQL result tied to the candidate rather than a confirmation flag.

**Scope:** Add a local command consuming T5's candidate identity and T1's SQL selections. Require the exact clean candidate source, version and matching functional dependencies; a release-version edit must precede validation. Run the ordinary fixture against the dedicated fictional database using its explicit test-only trust overlay. Run dedicated TLS cases separately using their existing private profile contract; record passed, failed and unavailable cases individually. Produce a sanitized summary with command/exit/result counts, commit, ZIP SHA-256, relevant nonsecret environment/dependency identity, trust settings, observed transports and evidence limitations. Keep raw test output private; publish only a deliberately sanitized summary. No credentials or full configuration dump enters summaries.

**Exclusions:** No SQL provisioning, certificate/trust-store changes, customer database, claim that the ordinary trust exception proves secure-default TCP behavior, or local/CI binary-equivalence claim without evidence.

**Acceptance:**
- A successful ordinary SQL run contains executed tests and no failures/unexplained skips; missing credentials/endpoint/schema, wrong source/dependencies or a failed run cannot produce passing evidence.
- The accepted unavailable trusted/untrusted TCP cases remain explicitly unverified; available local/default-rejection cases keep their narrower results. Failed configured TLS tests remain blocking.
- A controlled failed/unavailable result and candidate mismatch are handled correctly. Sensitive input markers do not appear in the distributable summary. Evidence can be attached to the existing release/PR workflow without committing secrets or task logs.

**Completion evidence:** Pending.

## T9 — Enforce a reusable release-readiness boundary

- [ ] T9: Validate automated results, local SQL evidence and the separate owner decision together.

**Intention:** Prevent tag creation or a green CI result from substituting for local integration evidence and release approval.

**Scope:** Implement a reusable readiness validator consuming the candidate manifest, T3/T7 results and T8 evidence. Match source, version and ZIP SHA-256, verify recorded run provenance/result structure, require successful ordinary SQL execution and reject failed configured TLS cases. Require explicit candidate-specific review/disclosure for the accepted missing TCP cases. Keep the owner's deliberate authorization distinct from test execution; a boolean assertion cannot fabricate missing test evidence. Define the validated handoff consumed by the later [publication task](../mcp-server-dictionary/Konzept.md), including verification of bytes immediately before public activation. This task implements the gate and prevents existing bypasses, not public promotion itself.

**Exclusions:** No public-release execution, paid approval environment, external registry workflow or claim of cryptographic attestation for manually supplied local evidence.

**Acceptance:**
- Controlled positive evidence is eligible for owner approval; missing/failed ordinary SQL, missing automated results, zero-test results, wrong commit/hash, changed ZIP, failed TLS, unreviewed TLS limitation or a bare confirmation flag is rejected.
- Replacing/rebuilding a validated package invalidates readiness until applicable checks are rerun. Preserve the distinction between eligible candidate, explicit owner authorization and published release.
- Document the trust boundary of local evidence and how the publisher must consume it; no unsigned report is described as tamper-proof. Gate failures leave public release actions unreachable.

**Completion evidence:** Pending.

## T10 — Replace automatic tag publication with validated candidates

- [ ] T10: Wire tag/manual candidate creation through shared validation and packaging.

**Intention:** Eliminate the current tag-to-public-release bypass.

**Scope:** Change [release.yml](../../.github/workflows/release.yml) so version tags, and an explicit candidate trigger if needed, select one exact revision, run T3, create T5's ZIP and run T6/T7 on those same bytes. Upload the checked ZIP, manifest and sanitized automated evidence with an explicit retention period sufficient for the documented local review window. Require tag/project-version agreement. Remove the automatic public GitHub Release job and its unnecessary write permissions. Preserve a documented handoff for T9 and later publication; expiration or rebuilding requires revalidation.

**Exclusions:** No public promotion job, MCPB build, local SQL in GitHub, secret upload, actual tag push or repository-settings modification.

**Acceptance:**
- A manually pushed tag cannot reach a public-release creation action, even when CI passes. Candidate jobs require no SQL credential or `contents: write` permission.
- Workflow dependencies prevent upload as a validated candidate after build/test/dependency/package failure. The downloaded artifact retains the tested hash, version and source identity without a second publish step.
- Version mismatch, missing evidence and failed package checks are covered by local/controlled workflow validation. Hosted executions not performed under current authorization remain explicitly unverified.

**Completion evidence:** Pending.

## T11 — Align local release preparation and contributor guidance

- [ ] T11: Update release preparation to use exact-source candidates and evidence.

**Intention:** Make the maintainer's normal release command follow the same gates as the documented process.

**Scope:** Adapt [release.ps1](../../scripts/release.ps1) around version preparation, candidate creation, local T8 evidence and T9 readiness. Version/source preparation must happen before candidate validation; never test one commit and label a later version commit as the tested source. Keep any existing explicitly invoked Git push/tag behavior clearly separated from preview/local validation, and explain that a tag now creates a candidate only. Remove `-SkipTests` as a path to release eligibility; if retained for preparation, it must leave readiness unverified. Update waiting/status messages to describe candidate creation rather than public publication. Consolidate canonical contributor/release commands and the future publication handoff without duplicating implementation detail in AGENTS.md.

**Exclusions:** No actual version bump, Git push/tag, release, deployment, public-promotion implementation or general Git-tooling rewrite during verification.

**Acceptance:**
- Controlled end-to-end preparation associates final source/version, checked ZIP and local SQL evidence correctly. Failed or skipped checks and accepted TLS limitations retain their actual status.
- Dry-run performs no file/Git/network mutation or release side effects. Mocked failed Git/native/CI prerequisites stop progression and cannot print a public-release success message.
- Documentation separates PR validation, candidate build, local SQL, owner decision and later public activation; describes prerequisites, retention/expiry, review exceptions and failure recovery; all links/commands match the implementation.

**Completion evidence:** Pending.

## T12 — Independent acceptance audit

- [ ] T12: Audit the completed implementation against the concept and application.

**Intention:** Establish that the complete release-validation path enforces its promises and preserves the agreed exclusions.

**Scope:** Read-only audit of T1–T11, changed production/test/helper/workflow files and recorded evidence. Trace every concept Must and Verification criterion to an implemented check or the exact accepted TLS limitation. Inspect test discovery, native failure propagation, dependency dispositions, real packaged-process isolation, external-runtime exclusion, source/hash binding, local SQL provenance and every remaining release action. Sample controlled negative cases and verify documentation. Review sanitization claims against actual summary generation. Record findings with file/line locations here; do not create an additional audit inventory.

**Exclusions:** No production edits by the auditor, repeated audit/correction loops, broadened security/client certification, actual push/publication or treating unavailable hosted execution as a pass.

**Acceptance:**
- All earlier checkboxes have supporting evidence; no silent SQL omission, personal-profile access, automatic tag publication or rebuild-after-validation bypass remains.
- End-to-end candidate/readiness evidence identifies an exact revision and package, with successful ordinary local SQL and explicit review of the accepted TLS limitation. Separate local/hosted execution gaps from verified results; unresolved required evidence keeps acceptance open.
- Build/test and relevant script/package checks passed on the audited revision, or narrowly documented accepted limitations apply. Documentation matches behavior. No paid service, SQL provisioning, new production configuration mode or public release was introduced.
- If findings exist, permit at most one bounded correction item with targeted revalidation under the stage-3 contract. Surface unresolved findings and leave acceptance open; with no findings, close the audit without adding a correction item.

**Completion evidence:** Pending.
