# Distribution and discovery preparation — roadmap

## Basis and execution rules

Implement the approved [concept](Konzept.md) in the order below. This roadmap does not start implementation. Use one roadmap, no separate milestone or leaf files, and one final audit.

- Scope ends with a prepared repository, locally generated packages/metadata and concise handoff instructions. Do not push, tag, invoke a real release, upload artifacts, change external settings, create accounts or submit/publish anything. Prepare the existing release automation without executing its external operations.
- Preserve the completed configuration, SQL security and release-validation contracts. Settings remain manually prepared in the Windows user directory; MCPB adds no configuration UI or credential store. Accepted TLS evidence gaps remain unverified.
- Each numbered item is one implementation session including focused validation, canonical documentation, short completion evidence beneath that item and an atomic commit. Resume at the first open checkbox. Split an item before execution if it no longer fits a session; do not silently expand scope.
- Use the existing project version as the single version source. Use `SqlToAi-win-x64.zip` and `SqlToAi-win-x64.mcpb`, each with a `.sha256` file. Store package metadata templates under `packaging/`; generate final metadata in the package output rather than committing candidate binaries or stale hashes.
- For script/build changes, run `dotnet build SqlToAi.slnx` and `dotnet test SqlToAi.slnx`, plus the focused package/script checks below. Record actual results and skipped/unavailable cases. Do not repeat SQL checks per archive. Documentation-only items need link/example checks, not a fresh test suite.
- Package creation and structural inspection can run locally. The unchanged packaged EXE smoke check requires an already disposable Windows profile, as explained in [development documentation](../../docs/development.md#create-and-check-a-windows-zip). `-DisposableProfile` does not create isolation. Never use the maintainer's normal user root or introduce a production root override. If no suitable environment is available, record the actual-EXE check as unverified and leave its acceptance open; do not provision accounts, VMs or trigger hosted runs as a workaround.
- Test external Git/GitHub operations using bounded copied-script/native-stub probes in owned temporary directories/repositories without a remote. Keep these probes disposable; no new verification framework or custom evidence schema.

## Ordered work

- [x] **1. Produce ZIP and minimal MCPB from one application build.**

  **Intention:** Add the bundle format without another executable, runtime or configuration system.

  **Scope:** Extend [package.ps1](../../scripts/package.ps1), add `packaging/manifest.json`, and adjust project publish items only where required. Stage a binary MCPB manifest with the current project version, explicit Windows compatibility, x64 description and a command resolving to the included EXE. Omit `user_config`. Retain one self-contained application publish; create both archives from that output. Review the shipped runtime dependencies and include required license/notices in both packages using a small repository-owned notice file. Preserve existing ZIP result properties while adding named MCPB path/hash/checksum properties. Update the package-creation section of [development documentation](../../docs/development.md).

  **Exclusions:** No SQL/runtime behavior changes, automatic settings creation, client setup UI, installer, signing, updater, release invocation or second application build. Build-time MCPB tooling may be pinned; it must not become an end-user runtime requirement.

  **Acceptance:** Both archives contain byte-identical application/settings/documentation/notice payloads, apart from bundle metadata. Manifest validation succeeds against the official specification checked during implementation. Versions agree with the project, the entry point exists, final checksums match, and the package object retains its prior ZIP contract. Missing required input, failed native packaging or invalid metadata fails before a success result. Existing-output refusal remains. Inspect actual locally generated archives and record their hashes; do not launch the EXE in the normal user profile.

  **Completion evidence (2026-10-16; implementation: GPT-6.1-Sol, High):** One `dotnet publish` per package invocation produces both archives; MCPB copies the ZIP and adds only the generated manifest. The pinned official v0.3 schema/specification at MCPB revision `70fe3b34cd6dff1b3bba046638edc72a6467a4fb` was checked online; local `Test-Json` validation passes. The template uses binary/Windows x64, the existing EXE command, and no `user_config` or environment mapping. Canonical [package instructions and notice provenance](../../docs/development.md#create-and-check-a-windows-zip) describe the unchanged ZIP result properties plus named MCPB properties. The dependency review identified all 53 resolved Windows runtime package references; the root notice includes MIT/Apache attribution, the MCP licensing transition and native SNI terms. Both unchanged .NET third-party texts are project publish items, including ordinary build/deploy output.

  `dotnet build SqlToAi.slnx`: passed, zero warnings/errors. `dotnet test SqlToAi.slnx`: 879 passed, zero failed, 19 skipped TLS cases without an endpoint profile; the accepted TLS gaps remain unverified. PowerShell parsing and documentation local-link checks passed. Actual archives in `%TEMP%\SqlToAi-item1-reviewed-20261016` contain eight byte-identical payload files; MCPB has exactly one extra `manifest.json`. Structural inspection verified manifest/project/file version `1.0.31`, x64 PE machine, included entry point, unchanged notice bytes, result properties and both final checksum files. SHA-256: ZIP `33b5464b0ee82f7b68e836c0bf0b9de013f182deddf5b44a97e3781ed292291d`; MCPB `a32db999a23d3b252cf344eea2e0e6f44cb469ca47fd2c0df723ecab8c7c687c`.

  Eighteen disposable copied-script probes passed: one-publish/two-archive success and rejection of missing source/license/schema/notices, invalid JSON/schema/version/launch/platform/configuration metadata, existing output (sentinel preserved), native exit 41, absent published EXE/notice, unexpected published manifest and archive-write failure. All failures returned no success object; preflight failures invoked no publish. These are controlled stub/native-exit probes, not application execution. No packaged EXE, real release, upload or external mutation ran. The user confirmed no disposable profile is available; actual archive startup remains the open Item 2 acceptance and is not claimed here.

- [ ] **2. Reuse the package smoke check for the bundle.**

  **Intention:** Verify the bundle's declared launch and protocol behavior without duplicating the existing test machinery.

  **Scope:** Extend [smoke-package.ps1](../../scripts/smoke-package.ps1) to accept the MCPB route while retaining the existing `-ZipPath` caller contract. Validate the extracted manifest, resolve its supported binary command/arguments and run that command through the existing bounded stdio checks. Reuse the missing-settings and conservative-settings cases, separate stderr capture, tool discovery, settings preservation and owned-process cleanup. Return the checked archive path/hash for either route. Update the corresponding development instructions.

  **Exclusions:** No general MCPB runtime/interpreter, client matrix, SQL calls, personal settings, production path hook or new disposable-environment provisioning.

  **Acceptance:** Existing ZIP behavior remains valid. Negative probes reject an invalid manifest, absent/wrong entry point, incompatible bundle, broken protocol, timeout and changed archive without reporting success. Local probes use stubs/harnesses and never assert the normal profile is disposable. In an already disposable profile, both actual archive launches pass missing-settings failure and MCP initialization/tool discovery from a path with spaces and different CWD; settings and archive hashes remain unchanged. Record actual execution separately from stub/harness evidence. If that environment is unavailable, keep this actual-launch acceptance and the item open.

- [ ] **3. Prepare the existing draft and local-helper flow for both packages.**

  **Intention:** Keep the checked artifacts together through the existing release path, ready for a later separately requested release.

  **Scope:** Adapt [release.yml](../../.github/workflows/release.yml), [create-release-draft.ps1](../../scripts/create-release-draft.ps1) and the relevant artifact/handoff functions in [release.ps1](../../scripts/release.ps1). Run one package build, check both final archives, transfer their checksums, and verify both before draft creation. Extend local draft download/hash/asset-identity checks and sanitized notes handoff to both formats. Preserve source/tag/version checks, draft-only behavior, refusal to replace existing releases, SQL/TLS/dependency review and mutation-free `-DryRun`. Update release documentation with the changed asset contract.

  **Exclusions:** No real `gh` mutations, downloads/uploads, push/tag/release invocation, changes to `v1.0.31`, automatic Registry publishing or generalized release-management framework.

  **Acceptance:** Parse scripts and validate workflow syntax. Controlled temporary probes cover successful two-package transfer/handoff, missing/tampered MCPB or checksum, mismatched version/source, native failures, existing/public release refusal and changed assets around notes editing. Failures stop dependent operations. The later draft attaches only checked bytes; the helper only updates notes and never replaces assets or publishes. Dry-run preserves all source/Git bytes and invokes no GitHub operation. Record hosted execution as not performed; local preparation does not require a real release or hosted run.

- [ ] **4. Generate locally valid Registry metadata from the checked bundle.**

  **Intention:** Leave a usable publication input without requiring an account, public asset or live catalog entry.

  **Scope:** Add `packaging/server.template.json` and extend the existing package script to emit the final `server.json` beside the packages. Derive application version and MCPB hash from the produced output; use the versioned GitHub Release URL matching the configured repository and asset name. Use the repository owner's GitHub namespace as the intended identity, explicitly pending later ownership verification. Validate the generated document against the official Registry schema, including MCPB package type and stdio transport. Document one local generation/validation procedure and the later manual ownership/publication steps in [development documentation](../../docs/development.md).

  **Exclusions:** No authentication, namespace registration, URL-availability requirement, Registry API submission, publisher credentials, directory-specific metadata variants or automatic synchronization. No custom candidate report.

  **Acceptance:** Generating a candidate yields schema-valid `server.json` with the exact package version, asset filename, final SHA-256 and intended versioned URL. A changed bundle changes the emitted hash; unresolved placeholders or missing required repository/version/package input fail clearly. No committed candidate hash can silently become stale. Local validation succeeds without an account or published release, and instructions distinguish intended identity/URL from verified ownership/live availability. Schema files/tooling used for checks are identified by version or revision.

- [ ] **5. Finish concise setup, support and security documentation.**

  **Intention:** Make prepared packages understandable outside a source checkout and describe the remaining maintainer handoff honestly.

  **Scope:** Adapt [README.md](../../README.md), [SETUP.md](../../SETUP.md), [development](../../docs/development.md) and [security documentation](../../docs/security.md) where their public instructions change. Add a short root `SECURITY.md`; put support/contribution and sanitized issue guidance in existing documentation rather than creating a policy collection. Explain ZIP versus MCPB, mandatory manual settings before startup, restricted SQL grants, update/restart/removal and retained user settings/logs. Resolve packaged documentation links to the matching version and keep required notices usable after extraction. Check private-reporting availability read-only where accessible; document pending activation truthfully without changing settings.

  **Exclusions:** No new setup mechanism, complete second installation manual, issue workflow framework, directory submission drafts, external setting changes or claims of live package availability.

  **Acceptance:** Both archives' instructions can be followed without a checkout or separate server runtime. MCPB is described as client-launch packaging; it does not create SQL settings. Versioned links and local LICENSE/notice links are consistent. Unsigned-binary, masking, SQL-permission and TLS statements match existing boundaries. Any unavailable private reporting route is explicitly pending with no invented contact or promise. The later release/Registry procedure is short and expressly outside this task. Regenerate packages after document changes and rerun affected structural/hash checks; rerun actual package checks when affected rather than reusing evidence for changed bytes.

- [ ] **6. Audit the prepared repository against the approved concept.**

  **Intention:** Close with concrete preparation evidence and no implied publication.

  **Scope:** Review items 1–5 and [concept verification](Konzept.md#verification). Inspect the final package contents, generated manifest/Registry metadata, matching versions/hashes, script failure behavior, draft/helper wiring, public instructions and scope exclusions. Reuse valid evidence for unchanged code; sample necessary negative behavior rather than repeating complete suites. Record a short result here.

  **Exclusions:** No external activation, hosted run, release, directory review, new infrastructure or repeated audit loops. Audit completion cannot substitute for missing acceptance evidence.

  **Acceptance:** Every earlier item has verified acceptance, or concrete remaining execution gaps stay open. Actual archive launch is distinguished from harness/stub verification and old hosted ZIP evidence. Final package/document changes have the applicable fresh checks. New templates, generated metadata and examples contain no real credentials or customer data; the deliberate fictional demo stays separate. No release, artifact upload, external account/settings change or catalog submission occurred. If corrections are necessary, add one bounded correction item and perform targeted revalidation. Close this item and the roadmap only when all required preparation acceptance is satisfied; live publication and namespace ownership are not completion requirements.
