# Practical release validation roadmap

## Execution rules

Implement the approved [concept](Konzept.md) in order. T1–T6 are implementation assignments; T7 is the final read-only audit. Each item must fit one agent session, including inspection, verification, documentation and an atomic commit. Split only if execution reveals a real size problem; do not add scope or start implementation from this planning stage.

Use one roadmap and record concise evidence under each item: relevant commands/results, limitations and commit references. Do not create additional tracking files. Keep scripts small and reuse existing .NET, PowerShell and GitHub mechanisms. No custom evidence format, approval engine, Windows account management, dedicated VM or module-origin audit is required.

For code changes, follow the [project rules](../../.agents/rules/development.mdc), add meaningful xUnit v3 tests for nontrivial C# behavior and run `dotnet build SqlToAi.slnx` and `dotnet test SqlToAi.slnx`. Public CI runs the explicit database-independent selection from T1. Keep SQL infrastructure limitations distinct from test failures and passes. Verify script failures with controlled child processes/stubs without terminating personal MCP sessions, deploying, pushing, tagging or publishing. Record unavailable hosted execution honestly.

Update [development documentation](../../docs/development.md) and any affected contributor commands in the same slice. Check links/examples, stage only related files and use German imperative Conventional Commits. Stage 3 uses the repository's subagent defaults unless the user specifies otherwise. Close only verified items; no push or actual release is authorized by this roadmap.

## T1 — Separate offline, SQL and TLS tests

- [x] T1: Establish and document reliable test selections.

**Intention:** Run all database-independent checks without excluding them merely because their name or folder contains `Integration`.

**Scope:** Review the [test project](../../tests/SqlToAi.Tests/SqlToAi.Tests.csproj) and actual fixture use. Keep `Category=Integration` for ordinary tests needing SQL; give dedicated TLS cases `Category=TlsIntegration`. Remove SQL-only categorization/collection coupling from configuration-only tests where appropriate. Document these commands and verify them against test discovery:

```powershell
dotnet test SqlToAi.slnx --filter 'Category!=Integration&Category!=TlsIntegration'
dotnet test SqlToAi.slnx --filter 'Category=Integration'
dotnet test SqlToAi.slnx --filter 'Category=TlsIntegration'
```

**Exclusions:** No functional test rewrite, new SQL setup, certificate changes or client matrix.

**Acceptance:** The offline run is nonempty and succeeds without SQL or personal settings; ordinary SQL and TLS are selected separately; configuration-only/startup checks remain offline. Missing ordinary SQL fails or is explicitly unavailable, while dedicated TLS skips remain unverified. Commands match actual discovered cases.

**Completion evidence:** `SqlServerFixtureConfigurationTests` no longer carries the SQL category or collection fixture; `TlsConnectionIntegrationTests` uses `Category=TlsIntegration`. `docs/development.md` documents the three filters and their infrastructure requirements. MCP navigation confirmed the fixture's explicit checked-in profiles and TLS's explicit endpoint profile; test project discovery includes 106 C# test documents. Using gpt-6-luna / high: `dotnet build SqlToAi.slnx` succeeded with 0 warnings/errors; offline selection passed 834/834; `Category=Integration` passed 45/45; `Category=TlsIntegration` discovered 19 and skipped all 19 because `SQLTOAI_TLS_TEST_PROFILE` was unset (unverified); full `dotnet test SqlToAi.slnx` passed 879, skipped 19, total 898. Configuration/startup checks remain in the offline selection and use checked-in test assets or temporary roots. No personal settings or unrelated processes were changed.

## T2 — Stop scripts on failed native commands

- [x] T2: Correct native exit handling in touched deployment/release helpers.

**Intention:** Prevent success messages and subsequent release/deployment work after a failed prerequisite.

**Scope:** Inspect [deploy.ps1](../../scripts/deploy.ps1) and [release.ps1](../../scripts/release.ps1). Check relevant native exit codes immediately, preserve directory cleanup and share invocation code only if needed to remove real duplication. Keep current deployment/process behavior otherwise unchanged.

**Exclusions:** No general script framework, process-management redesign or actual deployment/release.

**Acceptance:** Controlled test/build/publish failures return nonzero and stop dependent steps; failed tests prevent publish-output replacement; success messages only follow successful commands. Dry-run remains free of file/Git mutations and push/tag/release effects. Verification never stops unrelated processes or deletes real package output.

**Completion evidence:** Using gpt-6.1-sol / medium: `deploy.ps1` checks native test/publish exits before success or dependent work and restores the test location in `finally`. `release.ps1` checks Git root/status/tag queries and authenticated `gh` queries; its status query disables optional locks. `docs/development.md` describes failure handling and partial-output/no-rollback limits. `pwsh -NoProfile -File <temporary-root>/verify.ps1` passed 13 controlled child-process scenarios: deployment test/publish failures and success; release build/test/root/status/tag-query/add/workflow-query/release-query failures, success and dry-run. Stub native `pwsh` processes returned exit 23; script failures returned 1, suppressed subsequent steps/success messages and restored the caller directory. Failed deployment tests preserved a sentinel in copied publish output. All scripts, fake package writes and stubs were confined to temporary copies; process discovery was stubbed empty and termination forbidden. `pwsh -NoProfile -File <temporary-root>/verify-dry-run.ps1` ran the copied release helper against an actual clean temporary Git repository without a remote: exit 0; all 32 files including `.git`, index, refs and project were SHA-256 identical before/after. `dotnet build SqlToAi.slnx` passed with 0 warnings/errors; `dotnet test SqlToAi.slnx` passed 879, skipped 19 TLS cases, total 898 (TLS remains unverified). No real deployment, personal MCP termination, publish-directory replacement, push, tag or release was performed. Temporary verification root: `%TEMP%/SqlToAi-T2-b4f3c8b6610c4329a894d4528733cfc2`; verification scripts/logs are local disposable artifacts, not a committed test framework.

## T3 — Add the ordinary GitHub checks

- [x] T3: Run shared offline build/test and dependency checks locally and in CI.

**Intention:** Catch ordinary regressions on pull requests and `main` with existing free tooling.

**Scope:** Provide a small reusable validation command for restore/build, T1's offline tests and .NET/NuGet vulnerability checks including transitive packages. Prefer SDK audit diagnostics and normal error handling over a separate report parser. Add a standard Windows GitHub workflow under [.github/workflows](../../.github/workflows/) for relevant PR/main changes, including changes to scripts, workflows and build/dependency configuration. Use minimal read permissions and no private secrets. Show results in normal workflow logs/summary. Explain how actual findings are fixed or recorded as specific maintainer-reviewed exceptions using standard tooling and concise documentation.

**Exclusions:** No paid scanner, SQL access, custom vulnerability-policy engine, branch-settings changes or automatic exception approval.

**Acceptance:** Local and CI paths use the same nonempty offline selection. Build/test/audit execution failures propagate; vulnerability findings are visible and cannot silently count as clean. Exercise a controlled failed prerequisite and a finding, including the chosen documented exception handling. PRs need no SQL credentials or write token. Validate workflow structure and report whether a real hosted run occurred.

**Completion evidence:** Using gpt-6.1-sol / high: `scripts/validate.ps1` is the common local/Windows CI command. It forces restore with HTTP-cache bypass and SDK audit settings `true` / `all` / `low`, treats restore warnings as errors, builds without restore and runs T1's offline filter with `RunConfiguration.TreatNoTestsAsError=true`. `NuGet.Config` explicitly selects nuget.org for audit data. The PR/`main` workflow covers source, tests, tools, scripts, workflows and dependency/build settings, uses only `contents: read`, disables persisted checkout credentials and references no private secrets. Results remain in normal logs. `docs/development.md` documents fixes and exact-URL, maintainer-reviewed `NuGetAuditSuppress` exceptions with source review and release-note disclosure; no product exceptions were added.

`pwsh -NoProfile -File .\scripts\validate.ps1` passed on SDK 10.0.400: no audit diagnostics, build 0 warnings/errors, offline 834/834. An intentionally nonexistent test filter with the same standard empty-selection setting returned exit 1. Required `dotnet build SqlToAi.slnx` passed with 0 warnings/errors; full `dotnet test SqlToAi.slnx` passed 879, skipped 19 TLS cases, total 898 (TLS remains unverified). Four copied-script child-process scenarios verified native restore/build/test failures (stub exit 23, script exit 1), success (exit 0), dependent-step stopping, success-message suppression and location restoration. A temporary local package depended on Newtonsoft.Json 12.0.1, making the actual SDK finding transitive: restore failed with `NU1903` and `https://github.com/advisories/GHSA-5crp-9r3c-p9vr`; its exact test-only suppression allowed restore, while a nonmatching suppression still failed. A source without vulnerability data failed with `NU1905`; an unreachable localhost audit source failed with `NU1900` (and `NU1905`). Two additional copied-script runs used that actual SDK fixture restore: the finding stopped before build/test, and the exact test-only suppression reached the subsequent successful build/test stubs. These probes establish SDK/command handling, not approval of the fixture exception or real fixture build/test results. `actionlint` 1.7.12 (download checksum verified) accepted `validate.yml`; no real GitHub-hosted run occurred. Temporary fixtures/logs: `%TEMP%/SqlToAi-T3-5e5780dc052c42da989490dcf7f1e0f6`; nothing was deployed, terminated, pushed, tagged or released. T4 remains open.

## T4 — Smoke-test the actual Windows ZIP

- [ ] T4: Add reusable package creation and a bounded MCP startup check.

  - [x] Implement package/smoke commands and verify safe local packaging, harness behavior and controlled failures.
  - [ ] Execution gate (no implementation assignment): run the exact ZIP EXE on a fresh GitHub Windows runner using its existing disposable profile. This needs an authorized hosted tag run outside this local implementation; T5 wires the workflow. Keep aggregate T4 open until that execution evidence exists.

**Intention:** Check what users download without building local Windows isolation infrastructure.

**Scope:** Reuse the current self-contained, single-file `win-x64` publish settings and package contents from the [application project](../../src/SqlToAi/SqlToAi.csproj). Extract the generated ZIP, start its unchanged EXE and complete MCP initialization/tool discovery with separate stdout/stderr and bounded timeouts. Calculate the ZIP's SHA-256. Run required package checks on the fresh GitHub Windows runner using its existing profile; prepare only conservative test settings there. Verify missing settings first, then successful startup, paths with spaces, a different working directory and settings preservation. Existing [startup harness](../../tests/SqlToAi.StartupHarness/StartupHarness.cs) tests continue using temporary roots locally. The smoke command can also run in an already disposable Windows environment; it must not use or replace the maintainer's real profile locally.

**Exclusions:** No extra Windows accounts, VM provisioning, production root override, runtime-module inspection, dedicated SDK-free host or SQL calls. Do not describe the SDK-equipped runner as an SDK-free-machine test.

**Acceptance:** The ZIP includes the executable, current templates, package README and license. Its actual EXE exposes the expected tools with protocol-only stdout and no SQL dependency. Missing settings give exit `1`, actionable stderr, empty stdout and no settings creation. Existing settings remain byte-identical; timeouts/early exit/invalid protocol fail and clean up only children/temp paths owned by the check. The checksum identifies the checked ZIP. Self-contained publish settings and successful EXE execution are recorded without claiming stronger runtime-environment proof.

**Completion evidence:** Using gpt-6.1-sol / high: `scripts/package.ps1 -OutputDirectory <absent-directory>` preserves the existing self-contained/single-file `win-x64` publish switches and complete project-supplied package contents. It checks native publish failure, refuses existing output, and returns `ZipPath`, lowercase `Sha256` and `ChecksumPath`; the checksum is standard `hash  filename` text. An actual publish into an owned temporary directory succeeded. The resulting ZIP contains exactly `SqlToAi.exe`, `appsettings.json`, `demo-profile.json`, `README.md` and `LICENSE`; every archived file's SHA-256 equals its publish source, and the checksum file matches ZIP SHA-256 `cc80c190ab0ec3bfe9ce99418d79e06fc80688518966ce305c025347e04e9739`. This ZIP was built locally, not executed against the maintainer's profile.

`scripts/smoke-package.ps1 -ZipPath <zip> -DisposableProfile [-TimeoutSeconds 30]` refuses an existing Windows user root and requires an explicit disposable-profile assertion. It runs the unchanged extracted EXE from a different CWD with spaces, checks missing-settings exit/stderr/stdout/no-creation behavior, then conservative settings and MCP initialization/tool discovery. It checks protocol stdout, separate stderr, settings preservation, unchanged ZIP SHA-256 and bounded child/stream waits, and cleans up owned running children/temp output. Successful output returns only `ZipPath` and `Sha256` as an object. Canonical development documentation explains these interfaces, profile constraints and runtime-proof limits.

Safe local verification used copied scripts whose Windows-root expression was replaced with a literal owned temporary root; production root resolution was unchanged. The actual existing `StartupHarness` then passed missing-settings checks and the same prepared conservative settings plus MCP `2025-06-18` initialization and all 17 tools, clean exit and settings-byte preservation. This validates application behavior through the test harness, not execution of the release ZIP EXE. MCP source navigation confirmed startup loading/validation; the installed SDK's protocol reference confirms the requested revision is supported. Twelve controlled stub-EXE cases passed: success with separate stderr; early exit; timeout including termination of its owned descendant; invalid JSON; initialization error; missing tool; settings mutation; trailing stdout noise; and missing-settings stdout, diagnostic, exit-code and file-creation failures. Negative cases returned failure; all recorded owned processes stopped. No-opt-in and existing-root guards refused before child launch and preserved a sentinel; native publish failure stopped archive creation, and an existing output directory was refused. Temporary fixtures/logs remain under `%TEMP%/SqlToAi-T4-61e8ad5cd168493490655cc790ecc7c6` (original actual publish under `%TEMP%/SqlToAi-T4-57ef8672e0e64b4f923bc925c76d714b`). These disposable probes are not a committed test/evidence framework.

`dotnet build SqlToAi.slnx` passed with 0 warnings/errors; `dotnet test SqlToAi.slnx` passed 879, skipped all 19 unconfigured TLS cases, total 898. PowerShell syntax checks, ZIP/checksum comparisons and `git diff --check` passed. No C# production/test code changed. No personal settings, deployment or unrelated process was modified; no push/tag/release occurred. The real packaged EXE has **not** run on a fresh hosted runner or another disposable Windows profile; that execution gate and aggregate T4 remain open. Self-contained publish settings and the local harness are not SDK-free-machine proof.

## T5 — Keep releases usable through GitHub drafts

- [ ] T5: Integrate checks and draft creation into the existing tag workflow.

**Intention:** Prevent automatic public publication while keeping a complete path to publishing the existing ZIP.

**Scope:** Update [release.yml](../../.github/workflows/release.yml) to run T3 checks, build one ZIP and run T4 against it before creating a **draft** GitHub release. Reuse the current release action or GitHub CLI rather than adding a promotion service. Attach those checked ZIP bytes, a simple checksum file and concise notes identifying source commit/version and automated results. Provide a short notes section for the maintainer's local SQL command/result and TLS limitation. Validate tag/project-version agreement. Limit write permissions to draft creation. Do not remove the release job and leave publication dependent on another task: the owner publishes this draft through GitHub's normal UI after reviewing local SQL results. Refuse to overwrite an already public release; changed draft bytes require a fresh review.

**Exclusions:** No public promotion workflow, custom manifest/evidence validator, signing system, local SQL on GitHub, external distribution channels or actual tag/release operations during verification.

**Acceptance:** A tag runs checks and creates only a draft; failures prevent a successful candidate handoff. The attached ZIP hash equals the checked ZIP hash, with no rebuild between checking and upload. Draft notes identify the tested source and clearly leave unperformed local SQL as pending. The documented owner action publishes the existing draft/assets without rebuilding. Published assets cannot be silently replaced by a rerun. Controlled workflow/script checks cover failed prerequisites and version mismatch; missing real GitHub-run evidence stays explicit.

**Completion evidence:** Pending.

## T6 — Align the local release command and documentation

- [ ] T6: Validate the final release source locally and document the short release checklist.

**Intention:** Make the normal release command support the draft workflow without treating a flag or green CI as SQL evidence.

**Scope:** Adapt [release.ps1](../../scripts/release.ps1) so version preparation and its commit happen before local validation. Run offline/build checks and ordinary SQL tests against that final source, then dedicated TLS cases using the existing profile when available. Stop on ordinary SQL failure or configured TLS failure before push/tag; remove `-SkipTests` as a successful-release path. Print a short sanitized summary containing commit, commands/result counts and unverified TLS cases for the maintainer to copy into the draft notes. Preserve intentionally invoked push/tag and dry-run behavior; status monitoring reports draft readiness rather than public publication. Update [development guidance](../../docs/development.md) and affected contributor rules with the full flow: local checks, tag/automatic package checks, add local result to draft, review and publish through GitHub.

The existing accepted trusted/untrusted TCP limitation needs no new endpoints: disclose it and review it for this release. GitHub trusts the owner's recorded local result; no cryptographic proof or formal evidence processor is introduced. If the source or ZIP changes, rerun the applicable checks before publishing. Local test builds are not claimed to be byte-identical to CI binaries.

**Exclusions:** No credential/profile dumps, actual version bump/commit by the script during verification, push/tag/publication, certificate changes or redesign of Git operations.

**Acceptance:** Controlled execution verifies version/source ordering, failure stopping and mutation-free dry-run. Ordinary SQL must execute successfully; failed configured TLS blocks and accepted missing TCP cases remain visibly unverified. Manually created tags can create drafts but do not imply SQL success. The concise notes/checklist preserve that distinction, contain no secrets and lead to publication of the existing checked ZIP without new infrastructure or dependence on the external-publication task.

**Completion evidence:** Pending.

## T7 — Final independent audit

- [ ] T7: Check the implemented flow against the simplified concept.

**Intention:** Confirm the practical checks work together and no unnecessary infrastructure or release gap was introduced.

**Scope:** Read-only review of T1–T6, relevant changes and their evidence against the [concept verification](Konzept.md#verification). Inspect actual test selection, failure handling, package startup, draft/checksum wiring, local SQL results and the manual publication instructions. Record concrete findings here with file/line locations. Sample necessary negative behavior rather than creating a second test framework.

**Exclusions:** No production edits by the auditor, new requirements, actual release, repeated audit loops or claims that locally reviewed YAML already ran successfully on GitHub.

**Acceptance:** Earlier items have evidence; local SQL requirements and accepted TLS limitations retain their actual status; personal settings/processes are protected; tags cannot automatically publish; the normal draft-to-public ZIP path remains usable. All required checks are verified or remaining execution gaps are explicitly open. No new accounts, servers, VM setup, custom evidence processor or paid tooling was added. If findings require correction, add at most one bounded correction item with targeted revalidation; leave unresolved acceptance open.

**Completion evidence:** Pending.
