---
status: ready
---

# Practical build, package and release validation

## Intention

Catch broken changes and release packages before publication using GitHub's existing Windows runners, the maintainer's existing SQL test instance and a deliberate manual release decision. Extend the current ZIP release process without requiring new infrastructure or a custom release-management system.

## Current behavior and evidence

- The [release workflow](../../.github/workflows/release.yml) builds a self-contained Windows x64 ZIP and immediately creates a public release for a version tag. It does not run tests or start the packaged EXE.
- [release.ps1](../../scripts/release.ps1) checks locally before changing the version and creating the tagged commit, and permits `-SkipTests`. [deploy.ps1](../../scripts/deploy.ps1) does not consistently check native process exit codes.
- [Development documentation](../../docs/development.md) describes the full solution test command and the existing local fictional SQL setup, but no supported database-independent CI selection. Existing integration categories also include configuration-only tests.
- [User configuration](../user-configuration/Konzept.md) requires manually prepared user settings; missing settings must fail before MCP startup. The existing [startup harness](../../tests/SqlToAi.StartupHarness/StartupHarness.cs) already supports temporary roots for configuration tests.
- [SQL connection security](../sql-connection-security/Konzept.md#accepted-environment-limitation-2026-10-16) records the accepted lack of trusted/untrusted TCP TLS endpoints. Those cases remain unverified.

These are inspected behavior and documented constraints, not evidence that a release passed validation.

## Scope

### Must

- Separate database-independent tests, ordinary SQL integration and dedicated TLS tests by their actual requirements. Configuration-only and any in-memory MCP tests belong in the database-independent selection.
- Document local commands and automatically run build, database-independent tests and the existing .NET/NuGet dependency-vulnerability checks for relevant pull requests and `main` changes. Use standard GitHub-hosted Windows runners without SQL credentials.
- Check native exit codes in touched build/deployment/release scripts and stop before dependent operations or success messages after failure.
- Build the current self-contained Windows x64 ZIP once for a tagged revision. Extract it, start its actual EXE and verify MCP initialization/tool discovery, with stdout reserved for protocol messages. Keep package creation and the smoke check callable from scripts.
- Run required packaged-process checks on the fresh GitHub Windows runner, using that runner's existing disposable profile. Test missing settings and prepared conservative settings, paths containing spaces, a different working directory and preservation of settings. Local configuration tests continue to use the existing temporary-root harness.
- Keep the current release workflow operational by creating a GitHub **draft release** only after successful automated checks. Attach the checked ZIP, its SHA-256 and the source commit/version. Publication is the maintainer's explicit action on that draft; no tag automatically publishes it.
- Require successful ordinary SQL integration against the existing dedicated fictional database for the exact release source before publication. Record a short sanitized result in the draft release notes: commit, command, result counts and relevant TLS limitations. A manual tag can create a draft but does not establish local SQL success.
- Keep dependency findings visible. Fix them or record a specific maintainer-reviewed exception before publication; command exit zero alone does not establish absence of findings. Use normal SDK diagnostics and release notes rather than creating an exception-policy engine.
- Update canonical development/release instructions in the same implementation slices.

### Out of scope

- New CI servers, self-hosted runners, Windows test accounts, dedicated VMs, paid services, SQL provisioning or new certificate infrastructure.
- A custom candidate manifest/report schema, evidence validator, signing/attestation system, approval service or automatic proof of local test execution.
- Runtime-module inspection or a separate SDK-free Windows test installation. Retain self-contained publishing and real EXE startup; do not claim an SDK-free-machine test was performed on a runner with the SDK installed.
- Production configuration hooks/portable modes, SQL/TLS behavior changes, client matrices, benchmarks or coverage-percentage targets.
- MCPB, Registry/package-manager metadata, external listings or actual pushes/releases during this task's planning or implementation. The [publication task](../mcp-server-dictionary/Konzept.md) owns the additional distribution channels; the existing ZIP release path remains usable.

## Release flow

1. Prepare the release version and commit before running the local release validation. Ordinary local SQL must pass; dedicated TLS results retain their individual status. A failed local check stops the release helper before push/tag.
2. When intentionally invoked for a release, the helper pushes that tested source and its version tag. The tag workflow runs the automatic checks, builds the ZIP once, checks the extracted EXE and creates a draft release containing those same bytes, checksum and commit/version.
3. The maintainer checks the automated result and adds the short local SQL result and TLS limitation to the draft notes. GitHub trusts this maintainer-supplied result; neither a commit nor a checksum proves local test execution. No additional machine-readable report is required.
4. The maintainer publishes the existing draft through GitHub. Do not rebuild or replace its ZIP between checking and publishing. Changed source/package bytes require the applicable checks and a fresh review. A workflow rerun must not silently overwrite a published release or reuse an earlier review for changed bytes.

The manual decision is a documented maintainer responsibility, not a tamper-proof restriction on the repository owner. This task configures draft creation and the usable manual handoff; it does not perform a publication.

## Verification

- The documented database-independent selection runs nonempty tests without SQL Server or personal configuration. Ordinary SQL and TLS selections are distinct; absent prerequisites and skipped cases are never reported as executed passes.
- A failed build, selected test, dependency check or package smoke check fails the relevant script/workflow and prevents creation of a release presented as ready for review. Reviewed dependency exceptions remain visible; unreviewed findings cannot be silently ignored.
- The exact extracted ZIP executable completes MCP initialization and tool discovery on the fresh runner using prepared conservative settings and no SQL access. Missing settings produce exit `1`, actionable stderr, no stdout and no generated settings file.
- Install paths with spaces, a different working directory and byte-identical preservation of settings are checked. Local tests never read or modify the maintainer's real user directory; no account/profile management is introduced.
- The tagged version and recorded source agree. The ZIP attached to the draft has the same SHA-256 as the tested ZIP; publishing the draft requires no rebuild.
- The local release helper validates the final release source, stops on failure and has a mutation-free dry-run. Skipping tests cannot be presented as a successful release validation.
- A passing ordinary SQL result is required for manual publication. Missing trusted/untrusted TCP TLS cases remain explicitly unverified and are reviewed/disclosed for that release, as accepted by the user. Failed configured TLS cases and missing/failed ordinary SQL still block publication.
- The documented process ends in a usable draft-to-public ZIP release path without depending on completion of the external-publication task. Actual hosted runs and publications not performed are never claimed as verified.

## Dependencies and approval

[User configuration](../user-configuration/Konzept.md) and [SQL connection security](../sql-connection-security/Konzept.md) own the runtime contracts consumed by these checks. This task owns their validation/release integration; it does not reimplement those changes.

The user approved the concept and requested this pragmatic simplification. No design decisions remain open. Implementation requires its separately invoked workflow stage.
