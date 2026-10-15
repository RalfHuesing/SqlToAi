---
status: draft
---

# SqlToAi public distribution and discovery

## Intention

Prepare and publish SqlToAi through relevant free distribution and MCP discovery channels after its separately planned product and validation prerequisites are completed. Prioritize successful adoption by Microsoft SQL Server developers and maintainable releases; do not rely on search-ranking improvements or listing counts as evidence of success.

The technical product improvements have been extracted into independent prerequisite concepts below. This concept consumes their completed behavior and verification evidence; it does not implement those changes again. This stage creates no roadmap and authorizes no code changes, account creation, external submissions or publication.

## Settled decisions

- The initial supported release target is Windows x64. Keep the existing local stdio product and Microsoft SQL Server focus.
- Keep the self-contained Windows ZIP as the primary download and publish a checked MCPB wrapper built from the same application output. NuGet distribution is outside the initial scope.
- Use free publication paths. Do not introduce paid listings, featured placements, required hosted services or a project-operated remote SQL gateway.
- The user reports live use with Claude, Cursor and Codex. Do not introduce an additional client test matrix or require VS Code validation. Retain focused artifact, bundle and packaged-process checks.
- [User configuration](../user-configuration/Konzept.md) owns the settled user-directory-only design, conservative defaults, separate demo and absence of legacy migration paths.
- [SQL connection security](../sql-connection-security/Konzept.md) owns encrypted connections, default certificate validation and deliberate trust exceptions.
- [Release validation](../release-validation/Konzept.md) owns automated database-independent checks and the separate local SQL integration evidence for each release candidate. No hosted SQL test environment is required.

## Current evidence

These observations describe inspected repository files, not independently executed release tests:

- [README](../../README.md) documents schema inspection, query/script execution, comparisons, performance measurements, access levels, string masking and schema export.
- The [release workflow](../../.github/workflows/release.yml) already creates a self-contained Windows x64 application and ZIP for version tags. Automatic validation gaps are owned by the separate release-validation concept.
- The README currently introduces source builds rather than the existing binary download. The ZIP contains that README without its linked `docs/` or source files, so its relative links need a distribution-specific solution.
- The [application project](../../src/SqlToAi/SqlToAi.csproj) is an executable. The inspected checkout has no official Registry manifest or MCPB wrapper; adding these is part of this publication scope.
- The repository has an MIT [license](../../LICENSE) and English product documentation. Release redistribution obligations, public support guidance and the actual GitHub security-reporting settings still require checking.
- Detailed evidence concerning application paths, defaults, legacy migration, SQL certificate trust and test execution now belongs to the prerequisite concepts. Their presence as documents does not establish that any improvement is implemented.

## Prerequisite concepts

| Independent task | Owned technical outcomes | Required handoff to publication |
| --- | --- | --- |
| [User configuration and conservative defaults](../user-configuration/Konzept.md) | Per-user settings/log paths; first-run initialization; no migration/fallback; separate demo; opt-in SQL/result traces; affected runtime/test/documentation adaptations | Implemented contract and passing verification, accurate configuration/upgrade instructions and no runtime dependence on mutable files inside the package |
| [SQL connection security](../sql-connection-security/Konzept.md) | Encrypted primary/secondary SQL connections, certificate validation by default and deliberate endpoint trust exceptions | Implemented options/template/docs and builder plus local transport evidence; unavailable transport cases remain explicitly unverified |
| [Release validation](../release-validation/Konzept.md) | Database-independent CI, explicit local SQL test selection/result, packaged-process checks, reliable helper failures and publication gates | Passing automated checks and an identifiable local SQL result for the source/candidate to be published |

Each prerequisite is a separate task, initially a draft concept. Approving or implementing one does not approve the others or publish the product. Do not treat an existing concept file as a completed prerequisite.

Configuration and connection-security work can be planned independently, but both touch options, templates and documentation and must be integrated consistently. The validation task consumes the final behavior of both; it does not require public directory publication to be completed.

The publication task retains release documentation, package contents, redistribution notices, support/contribution guidance, private vulnerability reporting, repository metadata, MCPB/Registry metadata and external submissions. Product-code changes and general validation infrastructure remain with the three prerequisite owners.

## Lessons from comparable projects and specifications

- [DBHub installation](https://github.com/bytebase/dbhub/blob/main/docs/installation.mdx) separates package delivery from a bundle-based desktop setup and names its installation prerequisites. Adopt that clarity and a deliberate setup experience; do not copy its multi-database/Node runtime scope.
- Microsoft's [.NET Registry publishing guide](https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/publish-mcp-registry) demonstrates real NuGet publication and namespace/ownership metadata. `dnx` requires a .NET SDK installation; it is not a runtime-free replacement for the existing self-contained binary.
- The [MCPB specification](https://github.com/modelcontextprotocol/mcpb/blob/main/MANIFEST.md) supports bundled binaries and user-configuration fields. A valid bundle and a Registry-supported package type establish format compatibility, not successful installation in every MCP client.
- [Claude Desktop's official local-server guide](https://support.claude.com/en/articles/10949351-getting-started-with-local-mcp-servers-on-claude-desktop) documents custom bundle installation, binary extensions and client-managed sensitive fields. Attribute this storage behavior specifically to that client; do not extend it to arbitrary clients or plain application JSON files.
- [VS Code's MCP guide](https://code.visualstudio.com/docs/agent-customization/mcp-servers) and [OpenAI's MCP documentation](https://learn.chatgpt.com/docs/extend/mcp?surface=cli) document local command-based stdio configuration. Configuration formats are client-specific. The user already operates SqlToAi with Claude, Cursor and Codex; this concept does not add a client test matrix or VS Code validation requirement.

## Scope

### Must

- Require verified completion of the three prerequisite concepts before broad publication; link to their acceptance evidence rather than duplicating implementation tasks.
- Define accurate Windows x64 prerequisites and install/configure/update/uninstall instructions for the settled ZIP/MCPB routes. Source builds remain a contributor path.
- Produce ZIP and MCPB from one self-contained Windows build. Add and validate the MCPB manifest, binary entry point, configuration mapping, Windows compatibility metadata, package version and final SHA-256.
- Extend the validation infrastructure to the actual final MCPB and release bytes. Do not claim that a ZIP check alone verifies the bundle, or that Registry format support proves installation in every client.
- Make the release quickstart and package links usable outside a checkout; include appropriate license/dependency notices and version-pinned documentation references.
- Add concise repository support/contribution/security reporting guidance, practical sanitized issue instructions and accurate discovery metadata. Verify private reporting is enabled rather than only adding a policy file.
- Review the final dependency/security findings, package contents and public claims before activating the release. Describe existing masking boundaries accurately; no guarantee of complete anonymity is introduced.
- Complete the platform assessment and submit through relevant free paths after package publication. Reuse canonical descriptions, versions and installation instructions.
- Keep candidate creation, automated checks, local SQL evidence and the maintainer's public release decision distinct. External review outcomes and live entries must be verified separately from submission.

### Out of scope

- Product-code and general validation changes owned by the linked prerequisite concepts; do not reimplement their settings, TLS or test-suite work here.
- Additional database engines, remote hosting, SaaS services, Linux/macOS/ARM builds, NuGet distribution or a container distribution solely to obtain a listing.
- Paid directory submissions, promotional placements, required subscriptions, purchased signing certificates or project-funded runtime services.
- Guaranteed directory acceptance, automatic inclusion in every downstream registry, download targets or promised SEO improvements.
- A new client-by-client compatibility matrix, VS Code validation or automatic registration in installed clients.
- Unrelated features, broad refactoring, personal-website changes or a new general-purpose updater/installer.
- Implementation, executable roadmap items, account actions, release publication or directory submissions during this concept-planning stage.

## Publication approach under evaluation

1. Verify completion and integration of the separate user-configuration, connection-security and release-validation prerequisites.
2. Prepare the release quickstart, repository support/security guidance, package/license contents and consistent product metadata.
3. Generate candidate ZIP and MCPB from the same source/build; validate the final package bytes, metadata, hashes, settings mapping and packaged stdio process without a new client test matrix.
4. Associate the candidate with passing automated checks and the local SQL integration result; activate the public GitHub release only after the maintainer's release decision.
5. Publish official MCP Registry metadata pointing to the public versioned MCPB asset, then submit to the selected free discovery/package-manager channels.
6. Verify live entries and review outcomes, and maintain them when release versions, installation or product claims change.

No submission may require access to a real customer database. Examples and screenshots use fictional data only.

## Preliminary channel assessment

This table records research candidates rather than settled publication commitments. Recheck current terms and free submission behavior immediately before publishing. Absence of a price in documentation is not proof of a free submission path.

| Channel | Recommendation | Submission and prerequisites | Cost evidence / boundary | Maintenance |
| --- | --- | --- | --- | --- |
| GitHub repository and Releases | Foundation | Maintainer-owned repository; version tag publishes verified Windows artifacts and release notes | Standard public-repository runners are documented as free; check storage and avoid paid runner/features | Release assets, version-pinned documentation and known limitations |
| Official MCP Registry | Primary discovery target | Supported public package first; `server.json`, namespace verification and `mcp-publisher`; a generic release ZIP is not a supported package type | No publisher fee found in official documentation; verify current terms before first publication | Publish metadata for every new version; validate identity, transport, URL and package hash/version |
| MCPB release asset | Selected wrapper for the existing binary | Bundle manifest, binary entry point, Windows-only compatibility and SHA-256; validate package and startup/configuration mapping without adding a client test matrix | No separate host needed; reuse GitHub Release asset delivery | Generate from the same build/version as ZIP; hash final bytes; verify upgrade and settings |
| NuGet.org | Excluded from initial scope by user decision | Future consideration would require real executable tool packaging, owner account and README ownership marker | Microsoft documents NuGet.org as a free hosting service; `dnx` requires SDK on the invoking machine | No initial NuGet package/version commitment |
| GitHub MCP directory | Request after Registry publication | Request manual initial onboarding using the current GitHub channel; include active Registry entry, version, package/transport, repo, license and short description | No listing fee found; no promised acceptance or response time | Verify initial inclusion; version sync is expected only after onboarding |
| punkpeye/awesome-mcp-servers | Include as a targeted community directory | GitHub PR with concise description, category, ordering and correct local/Windows indicators; maintainer review | Contribution route has no documented listing fee; use ordinary contribution, never a paid alternative | Update description and links when material behavior changes |
| Glama | Include if repository indexing needs no product changes | Add MCP Server using repository URL, name and description; automated indexing/quality checks; claim ownership only as needed | Submission documentation does not establish a fee; confirm the free repository-listing route separately from paid hosting | Maintain relevant metadata; distinguish indexing from a successful Windows runtime test |
| Claude Desktop Extensions Directory | High-value candidate when MCPB is selected | Use the desktop-extension submission form linked by Anthropic's local-server guide; reviewed bundle and usable setup, no remote endpoint required | Check the actual form; plugin-portal paid-plan conditions must not be assumed to apply to the separate desktop-extension route | Verify review result and directory-managed updates; do not promise auto-updates for direct release downloads |
| Smithery | Candidate when MCPB is selected | Documented local `smithery mcp publish ./server.mcpb -n <namespace>/<name>` flow; confirm account/namespace rights and configuration | Confirm zero-fee bundle listing; do not substitute remote hosting or a paid gateway | Upload matched bundle and metadata for releases; verify local installation |
| WinGet | Proposed additional Windows distribution channel | PR to `microsoft/winget-pkgs`; versioned URL/hash, portable ZIP manifest, unattended install/update/uninstall checks and moderator review | Open contribution has no documented listing fee; avoid introducing a paid signing purchase | New manifest/version/hash per release and a stable executable/configuration path |
| PulseMCP | Investigate import/direct submission at publication time | Direct submission page could not be verified during research; check whether Registry ingestion already covers the product | No confirmed free direct route yet; do not assume automatic import | Avoid duplicate listings; claim/correct imported metadata only when supported |
| MCP.so | Exclude the currently evidenced paid route | A paid expedited submission exists; no free path was established in this research | Paid placement is outside scope | No ongoing commitment |
| Docker MCP Catalog | Exclude from initial publication | Requires container distribution, network/authentication validation and another support surface | No project need justifies introducing this route solely for discovery | No container release commitment |
| Scoop Main | Exclude from initial publication | Additional manifest route with maintainer admission/popularity criteria; an own bucket would not equal default-catalog discovery | No fee found, but duplication and admission/maintenance cost outweigh current value | Reconsider only if supported by actual adoption/demand |
| Chocolatey | Exclude from initial publication | Adds a separate Windows package format and moderation/update route beyond the existing ZIP/MCPB and proposed WinGet | No detailed fee/admission verification performed because the redundant route is not recommended | No additional package maintenance commitment |
| OpenAI desktop plugin directory | Exclude from initial publication | Current official local-MCP plugin submission requires organization allowlisting via an OpenAI representative and a distinct plugin package | Free self-service admission is not established; ordinary local Codex MCP configuration remains the intended route | No separate client plugin or allowlisting dependency |

Registry metadata and package versions are public. Check names, descriptions, ownership, version and URLs before publishing: existing published version metadata cannot simply be overwritten. A successful submission, review acceptance, indexed entry and working client installation are distinct results.

## Verification

- All three prerequisite concepts have been implemented and their acceptance verified against the candidate source. Their individual draft/implementation status remains visible; no prerequisite is inferred complete from this document.
- ZIP and MCPB are generated from the same Windows x64 application output and have consistent versions. Verify bundle manifest, binary inclusion, final-byte SHA-256 and configuration mapping without a source checkout or separately installed runtime for the server.
- Final artifact/protocol checks pass using the validation infrastructure. Local SQL evidence identifies the candidate source and actual verification limits; database-independent CI is not presented as SQL coverage.
- Package replacement uses the completed user-directory contract. Existing settings are preserved; no duplicate portable mode, legacy importer or bundle-local settings system is introduced here.
- Canonical and packaged documentation match the implemented settings/security contracts, correctly describe the intentional manual reconfiguration requirement and do not broaden masking or client-support claims.
- License notices, dependency/security dispositions, support/contribution guidance and a verified private vulnerability-reporting route are available before public promotion.
- The public release uses the checked artifacts, and Registry/package-manager/listing metadata points to that version. No artifact is rebuilt or replaced after verification without rerunning applicable checks.
- Each activated publication channel has a verified zero-fee path and a reproducible submission/update procedure. Distinguish submitted, accepted/indexed and rejected/pending outcomes; external acceptance is not guaranteed.
- No paid runner/service/signing purchase/listing or project-hosted endpoint is necessary for the agreed publication. Use free CI/storage allowances without enabling paid overage.

## Cost and maintenance boundaries

- The zero-cost constraint covers publication, project-operated infrastructure and required maintainer services. Users still supply their own SQL Server access and MCP client; no new paid client plan or SQL license may be bought for release validation without changing this constraint.
- Use standard GitHub-hosted runners for the public repository, not paid larger runners. GitHub documents free standard-runner use for public repositories, but storage allowances and other billable features still require checking. Use short-lived intermediate build artifacts and the appropriate release assets for public delivery.
- Do not introduce a Windows installer, paid code-signing certificate, hosted gateway, container image, automatic updater or separate client plugin just to increase the number of listings.
- Publish checksums and clear unsigned-binary installation guidance. A SHA-256 checksum is an integrity check, not a claim of publisher authentication, Windows reputation, or reproducible builds.
- Prefer one build reused by ZIP and any selected wrapper, one version source, one canonical quickstart, and the official Registry as upstream metadata where applicable.
- External platform accounts remain owned by the maintainer. Do not persist access tokens in concept artifacts, scripts or release packages; later automation should use minimal-scope credentials or supported short-lived identity.
- Explain manual versus platform-managed update behavior for each selected route. No general auto-update guarantee follows from MCPB support or Registry publication.
- Recheck fees and permissions before every new channel is activated. If the free path cannot be established or the platform requires incompatible hosting, record the exclusion without substituting a paid plan.

## Research references

Research baseline: 2026-10-15. These are starting points; mutable platform policies must be checked again when implementation or publication occurs.

- [MCP Registry quickstart](https://modelcontextprotocol.io/registry/quickstart)
- [Registry package types](https://github.com/modelcontextprotocol/registry/blob/main/docs/modelcontextprotocol-io/package-types.mdx)
- [Registry metadata specification](https://github.com/modelcontextprotocol/registry/blob/main/docs/reference/server-json/generic-server-json.md)
- [GitHub staff clarification: manual initial onboarding, later version synchronization](https://github.com/github/github-mcp-server/discussions/1257)
- [MCPB manifest specification](https://github.com/modelcontextprotocol/mcpb/blob/main/MANIFEST.md)
- [Microsoft: publish a .NET MCP server to the Registry](https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/publish-mcp-registry)
- [NuGet publication](https://learn.microsoft.com/en-us/nuget/nuget-org/publish-a-package)
- [awesome-mcp-servers contribution rules](https://github.com/punkpeye/awesome-mcp-servers/blob/main/CONTRIBUTING.md)
- [Glama FAQ](https://glama.ai/mcp/faq)
- [Smithery publishing](https://smithery.ai/docs/build/publish)
- [Docker MCP Registry contribution rules](https://github.com/docker/mcp-registry/blob/main/CONTRIBUTING.md)
- [MCP.so submission](https://mcp.so/submit?type=server)
- [PulseMCP submission](https://www.pulsemcp.com/submit)
- [Claude Desktop local servers and desktop-extension submission link](https://support.claude.com/en/articles/10949351-getting-started-with-local-mcp-servers-on-claude-desktop)
- [WinGet submission and review](https://learn.microsoft.com/en-us/windows/package-manager/package/repository)
- [WinGet package policies](https://github.com/microsoft/winget-pkgs/blob/master/doc/Policies.md)
- [Scoop Main admission criteria](https://github.com/ScoopInstaller/Scoop/wiki/Criteria-for-including-apps-in-the-main-bucket)
- [OpenAI desktop-only plugin submission](https://developers.openai.com/plugins/deploy/desktop-only)
- [Registry FAQ: immutable published versions](https://modelcontextprotocol.io/registry/faq)
- [GitHub Actions billing and free standard runners](https://docs.github.com/en/billing/concepts/product-billing/github-actions)
- [GitHub private vulnerability reporting](https://docs.github.com/en/code-security/how-tos/report-and-fix-vulnerabilities/configure-vulnerability-reporting/configure-for-a-repository)
- [Microsoft.Data.SqlClient encryption and certificate validation](https://learn.microsoft.com/en-us/sql/connect/ado-net/encryption-and-certificate-validation)

## Working memory: unresolved decisions

- The final publication-channel selection from the previous discussion remains unanswered; the channel table contains recommendations, not implied approvals.
- Each extracted concept remains draft until explicitly approved; extraction does not claim implementation or authorize publication.
- This publication concept remains draft until its own scope is explicitly approved. Personal-website work remains outside scope.
