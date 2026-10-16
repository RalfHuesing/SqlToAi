---
status: draft
---

# SqlToAi public distribution and discovery

## Intention

Make SqlToAi easy to download and discover through free channels, with a release process that a single maintainer can sustain. Reuse the completed configuration, security and release work.

This revised draft recommends a smaller initial scope. The channel reduction and minimal MCPB configuration below are proposals, not newly approved decisions. This stage authorizes no implementation, roadmap, accounts, submissions or publication.

## Settled decisions

- Support Windows x64, local stdio and Microsoft SQL Server initially.
- Keep the self-contained ZIP as the primary download and add an MCPB wrapper from the same application output. NuGet is outside the initial scope.
- Use free distribution without a hosted SQL gateway, paid listings or required project-operated services.
- The user already uses Claude, Cursor and Codex. Keep focused package checks; no new client compatibility matrix or mandatory VS Code validation.
- Preserve manual user-directory configuration, conservative defaults, encrypted SQL connections and deliberate endpoint-specific trust exceptions.
- Publication remains the maintainer's deliberate decision on a checked GitHub draft.

## Current evidence

Repository inspection and linked completion evidence establish this baseline; no build, SQL test or release operation was rerun for this review.

| Completed work | Evidence and remaining boundary |
| --- | --- |
| [User configuration](../user-configuration/roadmap.md) | Implementation and final acceptance are complete. Settings are manually prepared under `%LOCALAPPDATA%\SqlToAi`; startup never creates or migrates them. The package already includes [SETUP.md](../../SETUP.md) as its README, plus templates and LICENSE. |
| [SQL connection security](../sql-connection-security/roadmap.md) | Implementation and audit are complete with the [accepted environment limitation](../sql-connection-security/Konzept.md#accepted-environment-limitation-2026-10-16). Trusted/untrusted TCP transport evidence remains unavailable; completion does not mean those cases passed. |
| [Release validation](../release-validation/roadmap.md) | T1–T8 and independent reviews are complete. Audit/build/offline checks, real ZIP EXE smoke checks and draft creation exist. Recorded hosted evidence covers `v1.0.31` and leaves publication review pending; live GitHub state was not rechecked. The latest helper's automatic local-result handoff still awaits its first intentional end-to-end release invocation. |

The [release workflow](../../.github/workflows/release.yml), [package builder](../../scripts/package.ps1), [package smoke check](../../scripts/smoke-package.ps1) and [release helper](../../scripts/release.ps1) are the existing foundation. Do not reopen completed prerequisites. Each new release still needs the candidate-specific checks in the [release instructions](../../docs/development.md).

The checkout has no MCPB manifest or Registry `server.json`. These, concise public support/security guidance and selected submissions are the remaining publication work. Repository settings such as private vulnerability reporting have not been checked in this review.

## Proposed simplification

### Initial channels

| Channel | Recommended commitment |
| --- | --- |
| GitHub repository and Releases | Required foundation: existing ZIP, additional MCPB, checksums and short setup/release instructions. |
| Official MCP Registry | Required discovery target: one `server.json`, namespace ownership and a versioned public MCPB asset. |
| `punkpeye/awesome-mcp-servers` | One manual community submission after Registry publication. External acceptance is not a release gate. |
| GitHub MCP directory, Claude Extensions Directory, Glama, Smithery, PulseMCP | Defer until there is a concrete adoption reason. No initial account, fee investigation, automation or acceptance obligation. |
| WinGet | Defer. It adds an installation/update contract and per-version manifest work without being required for MCP discovery. |

NuGet, Docker, Scoop, Chocolatey, a separate client plugin and paid listings remain outside initial scope. Listing counts and presumed search-ranking benefits are not acceptance criteria. No extra package format or hosting solely for a listing.

### MCPB as packaging

Use a binary manifest pointing to the existing self-contained EXE. The [MCPB specification](https://github.com/modelcontextprotocol/mcpb/blob/main/MANIFEST.md) supports binary servers and makes `user_config` optional. Set Windows platform metadata and state the x64 limitation in the asset name and description.

Initially omit `user_config` and extra credential/environment mapping. Both packages use the same manually prepared user file and access lists. Bundle installation configures client startup; it does not create SqlToAi's required base settings file. Explain this before installation. No setup wizard, bootstrap executable or second configuration store.

The [Registry package specification](https://github.com/modelcontextprotocol/registry/blob/main/docs/modelcontextprotocol-io/package-types.mdx) supports MCPB assets hosted on GitHub Releases with `fileSha256`. A generic ZIP is not that Registry package route. MCPB therefore serves a concrete discovery purpose even when SQL setup stays manual.

### Reuse checks and documentation

Extend the existing package/release path where MCPB changes it. Build the application once; package those same files twice. Reuse the startup/protocol check for the extracted bundle using its declared command, plus manifest validation, version/content comparison and final hashes. Do not repeat the full SQL suite separately for two wrappers around the same build.

Keep support and contribution instructions short, using existing documentation or GitHub sections where practical. Add a concise security reporting policy and verify its private reporting route. Review shipped dependency notices once and include what is required. Reuse [SETUP.md](../../SETUP.md) rather than writing another complete installation manual.

## Scope

### Must

- Consume the completed prerequisite contracts and accepted verification limits. Retain per-release SQL, dependency and maintainer review through the existing process.
- Generate ZIP and MCPB from one Windows x64 self-contained build and one application version. Include the executable, conservative template, setup instructions, license and required dependency notices.
- Validate MCPB manifest, declared binary launch, included files, version and final SHA-256. Transfer only checked artifacts to the draft; extend the helper's existing artifact checks for MCPB.
- Adapt the quickstart for direct download and both routes. Explain manual settings preparation, client startup, update and removal, including deliberate handling of retained settings/logs. Package links must work outside a checkout and identify the matching release version.
- Provide concise support/contribution instructions, a verified private vulnerability-reporting route and accurate metadata. Keep masking and unsigned-binary claims within documented product limits.
- Prepare Registry metadata and namespace ownership, publish after the checked MCPB asset is public, then make the single proposed community submission. Confirm free submission before activation; omit a channel if its free route is unavailable.
- Record results briefly in existing release/task context. Distinguish a live Registry entry from a pending/rejected community submission. External review does not block the working GitHub/Registry release.

### Out of scope

- Reimplementing configuration, SQL security, general CI or release validation; provisioning TLS endpoints to reopen the accepted evidence limitation.
- Extra databases, platforms, CPU builds, hosted services, containers, NuGet packages, installers, an updater, signing purchases or a new client plugin.
- MCPB configuration UI, credential storage, automatic settings creation/migration or client registration outside normal bundle installation.
- Additional channel commitments, exhaustive platform research, client matrices, custom evidence schemas, approval engines or multi-directory publishing frameworks.
- Customer data in examples, secrets in metadata, guaranteed directory acceptance, download targets, paid services or SEO promises.
- Unrelated features or personal-website changes; implementation, executable roadmap creation or external actions during this review.

## Publication approach

Use the existing helper and GitHub draft flow, extended for both checked packages and hashes. Normal local SQL checks remain required for the release source. Preserve accepted TLS limitations in the notes; missing evidence is not a pass or a requirement for new infrastructure.

The maintainer reviews and publishes that draft. Registry publication then references the final public versioned MCPB URL and hash. A manual community submission follows independently. One short procedure in existing release documentation is sufficient; no automated directory synchronization is needed.

Integrate MCPB into a subsequent deliberate release rather than retrofitting the already checked `v1.0.31` draft without applicable fresh checks. The existing ZIP publication path remains usable independently.

## Verification

- ZIP and MCPB contain the same application build/version. Manifest validation succeeds; the extracted MCPB's declared command completes MCP initialization and tool discovery with prepared conservative settings and no SQL access. Missing settings retain the existing actionable failure. Personal settings are never used for package checks.
- Final package hashes match the draft assets. Changed content triggers affected checks again. Existing offline/SQL/review requirements hold for the release source without duplication per wrapper.
- Packaged instructions work without a checkout. Both routes use the manual settings contract; updates preserve the user file. No one-click SQL setup, universal client compatibility or SDK-free-machine test is claimed without evidence.
- Support/security instructions and required notices are available. Private reporting and chosen free submission routes are checked before activation. No new paid service or hosted endpoint is required.
- Public Registry metadata validates and resolves to the correct public MCPB asset, version, transport and hash. The community submission is recorded with its actual status; acceptance is outside the maintainer's control.

## Open decision

Approve or adjust the recommended initial scope: GitHub ZIP + minimal MCPB + Official Registry + one manual `awesome-mcp-servers` submission, with other channels deferred. The reduced channel commitment and omission of MCPB configuration UI are not yet approved. This concept remains `draft`; approval does not itself authorize publication.
