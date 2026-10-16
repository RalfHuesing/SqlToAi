---
status: ready
---

# SqlToAi distribution and discovery preparation

## Intention

Prepare the repository, packages and documentation for later free distribution and discovery, with a release process that a single maintainer can sustain. Reuse the completed configuration, security and release work. Actual publication and external submissions are outside this task.

The user approved this preparation-only scope. Implementation requires its separately invoked workflow stage; approval and roadmap creation authorize no release, external registration or submission.

## Settled decisions

- Support Windows x64, local stdio and Microsoft SQL Server initially.
- Keep the self-contained ZIP as the primary download and add an MCPB wrapper from the same application output. NuGet is outside the initial scope.
- Use free distribution without a hosted SQL gateway, paid listings or required project-operated services.
- The user already uses Claude, Cursor and Codex. Keep focused package checks; no new client compatibility matrix or mandatory VS Code validation.
- Preserve manual user-directory configuration, conservative defaults, encrypted SQL connections and deliberate endpoint-specific trust exceptions.
- Publication remains the maintainer's deliberate decision on a checked GitHub draft.
- This task prepares the repository only. Do not publish releases or Registry entries, submit directory pull requests/forms, create external accounts or activate publishing automation. Those actions require a separate explicit request.

## Current evidence

Repository inspection and linked completion evidence establish this baseline; no build, SQL test or release operation was rerun for this review.

| Completed work | Evidence and remaining boundary |
| --- | --- |
| [User configuration](../user-configuration/roadmap.md) | Implementation and final acceptance are complete. Settings are manually prepared under `%LOCALAPPDATA%\SqlToAi`; startup never creates or migrates them. The package already includes [SETUP.md](../../SETUP.md) as its README, plus templates and LICENSE. |
| [SQL connection security](../sql-connection-security/roadmap.md) | Implementation and audit are complete with the [accepted environment limitation](../sql-connection-security/Konzept.md#accepted-environment-limitation-2026-10-16). Trusted/untrusted TCP transport evidence remains unavailable; completion does not mean those cases passed. |
| [Release validation](../release-validation/roadmap.md) | T1–T8 and independent reviews are complete. Audit/build/offline checks, real ZIP EXE smoke checks and draft creation exist. Recorded hosted evidence covers `v1.0.31` and leaves publication review pending; live GitHub state was not rechecked. The latest helper's automatic local-result handoff still awaits its first intentional end-to-end release invocation. |

The [release workflow](../../.github/workflows/release.yml), [package builder](../../scripts/package.ps1), [package smoke check](../../scripts/smoke-package.ps1) and [release helper](../../scripts/release.ps1) are the existing foundation. Do not reopen completed prerequisites. Each new release still needs the candidate-specific checks in the [release instructions](../../docs/development.md).

The checkout has no MCPB manifest or Registry `server.json`. These and concise public support/security guidance are the remaining repository work. Repository settings such as private vulnerability reporting have not been checked in this review; any necessary activation remains a later maintainer action.

## Agreed preparation

### Preparation targets

| Channel | Preparation commitment |
| --- | --- |
| GitHub repository and Releases | Prepare existing ZIP, additional MCPB, checksums and short setup/release instructions. Do not execute a release or upload artifacts in this task. |
| Official MCP Registry | Prepare and validate `server.json` and document later namespace verification/publication. Do not authenticate, register or publish. |
| `punkpeye/awesome-mcp-servers` | Possible later manual submission; no submission or submission-specific preparation is required here. |
| GitHub MCP directory, Claude Extensions Directory, Glama, Smithery, PulseMCP | Defer until there is a concrete adoption reason. No initial account, fee investigation, automation or acceptance obligation. |
| WinGet | Defer. It adds an installation/update contract and per-version manifest work without being required for MCP discovery. |

NuGet, Docker, Scoop, Chocolatey, a separate client plugin and paid listings remain outside initial scope. Listing counts and presumed search-ranking benefits are not acceptance criteria. No extra package format or hosting solely for a listing.

### MCPB as packaging

Use a binary manifest pointing to the existing self-contained EXE. The [MCPB specification](https://github.com/modelcontextprotocol/mcpb/blob/main/MANIFEST.md) supports binary servers and makes `user_config` optional. Set Windows platform metadata and state the x64 limitation in the asset name and description.

Initially omit `user_config` and extra credential/environment mapping. Both packages use the same manually prepared user file and access lists. Bundle installation configures client startup; it does not create SqlToAi's required base settings file. Explain this before installation. No setup wizard, bootstrap executable or second configuration store.

The [Registry package specification](https://github.com/modelcontextprotocol/registry/blob/main/docs/modelcontextprotocol-io/package-types.mdx) supports MCPB assets hosted on GitHub Releases with `fileSha256`. A generic ZIP is not that Registry package route. MCPB therefore serves a concrete discovery purpose even when SQL setup stays manual.

### Reuse checks and documentation

Extend the existing package/release path where MCPB changes it. Build the application once; package those same files twice. Reuse the startup/protocol check for the extracted bundle using its declared command, plus manifest validation, version/content comparison and final hashes. Do not repeat the full SQL suite separately for two wrappers around the same build.

Keep support and contribution instructions short, using existing documentation where practical. Add a concise security reporting policy that accurately states the available route; document any pending private-reporting activation for the maintainer without changing external settings. Review shipped dependency notices once and include what is required. Reuse [SETUP.md](../../SETUP.md) rather than writing another complete installation manual.

## Scope

### Must

- Consume the completed prerequisite contracts and accepted verification limits. Retain per-release SQL, dependency and maintainer review through the existing process.
- Generate ZIP and MCPB from one Windows x64 self-contained build and one application version. Include the executable, conservative template, setup instructions, license and required dependency notices.
- Validate MCPB manifest, declared binary launch, included files, version and final SHA-256. Prepare the workflow/helper to transfer only checked artifacts during a later explicitly requested release; do not run that release in this task.
- Adapt the quickstart for direct download and both routes. Explain manual settings preparation, client startup, update and removal, including deliberate handling of retained settings/logs. Package links must work outside a checkout and identify the matching release version.
- Provide concise support/contribution/security instructions and accurate metadata. State any pending private-reporting activation accurately. Keep masking and unsigned-binary claims within documented product limits.
- Prepare locally validated Registry metadata with version, transport and a documented way to supply the later public MCPB URL/hash. Document namespace verification and publication as later manual steps; no credentials or external registration are required for completion.
- Record repository/package verification briefly in existing task context. Completion requires no live release, Registry entry or accepted directory listing.

### Out of scope

- Actual releases, artifact uploads, Registry publication, directory submissions, external account creation, repository-setting changes and automatic catalog publishing.
- Reimplementing configuration, SQL security, general CI or release validation; provisioning TLS endpoints to reopen the accepted evidence limitation.
- Extra databases, platforms, CPU builds, hosted services, containers, NuGet packages, installers, an updater, signing purchases or a new client plugin.
- MCPB configuration UI, credential storage, automatic settings creation/migration or client registration outside normal bundle installation.
- Additional channel commitments, exhaustive platform research, client matrices, custom evidence schemas, approval engines or multi-directory publishing frameworks.
- Customer data in examples, secrets in metadata, guaranteed directory acceptance, download targets, paid services or SEO promises.
- Unrelated features or personal-website changes; implementation during concept/roadmap planning and external publication actions during this task.

## Handoff for a later publication

Prepare the existing helper and GitHub draft flow for both checked packages and hashes without invoking a release. Normal local SQL checks remain required for any later release source. Preserve accepted TLS limitations; missing evidence is not a pass or a requirement for new infrastructure.

Document the later steps: deliberately run the release helper, review/publish the checked draft, verify namespace ownership and publish Registry metadata referencing the public versioned MCPB URL/hash. A community submission can be requested separately. These are handoff instructions, not executable scope for this task. One short procedure is sufficient; no directory synchronization is needed.

Integrate MCPB into a subsequent deliberate release rather than retrofitting the already checked `v1.0.31` draft without applicable fresh checks. The existing ZIP publication path remains usable independently.

## Verification

- ZIP and MCPB contain the same application build/version. Manifest validation succeeds; the extracted MCPB's declared command completes MCP initialization and tool discovery with prepared conservative settings and no SQL access. Missing settings retain the existing actionable failure. Personal settings are never used for package checks.
- Final local package hashes match their checksum files and generated metadata. The prepared workflow/helper preserves these checks for later draft assets. Changed content triggers affected checks again; existing release checks are not duplicated per wrapper.
- Packaged instructions work without a checkout. Both routes use the manual settings contract; updates preserve the user file. No one-click SQL setup, universal client compatibility or SDK-free-machine test is claimed without evidence.
- Support/security instructions and required notices are available. Any pending external activation is explicit. No new paid service or hosted endpoint is required.
- Registry metadata validates locally and agrees with package version, transport and hash; later publication URL and namespace steps are documented. No public URL resolution, account ownership verification or live listing is required for this task's acceptance.

## Approval

The user approved repository preparation: ZIP + minimal MCPB + Registry metadata and concise handoff documentation. No design decisions remain open. All publication and directory submissions require a separate explicit request. Roadmap creation does not start implementation.
