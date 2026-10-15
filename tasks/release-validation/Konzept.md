---
status: draft
---

# Database-independent validation and reliable release gates

## Intention

Make SqlToAi changes and distributed binaries verifiable before publication through automatic database-independent checks and an explicit local SQL integration result. This is a prerequisite extracted from the [publication concept](../mcp-server-dictionary/Konzept.md), not authorization to publish a release.

## Current behavior and evidence

- [Development documentation](../../docs/development.md) describes a single solution-wide build/test path and SQL integration fixtures that load JSON directly. There is no documented database-independent contributor/CI command.
- The [release workflow](../../.github/workflows/release.yml) publishes a self-contained Windows x64 application and ZIP after a version tag, without running tests or inspecting the running packaged process.
- [release.ps1](../../scripts/release.ps1) runs local validation but allows `-SkipTests`; manually pushing a tag does not depend on this script.
- [deploy.ps1](../../scripts/deploy.ps1) runs native `dotnet` commands without explicitly checking their exit codes at each step. PowerShell completion/success text is not reliable evidence of a successful test or publish.
- Existing build analyzers, warnings-as-errors and xUnit tests are useful foundations. The existence of tests is not proof that the current checkout or a packaged binary passed them.

These observations describe inspected files; no build, test or package execution was performed while extracting this concept.

## Scope

### Must

- Define explicit test selection for database-independent tests versus SQL-dependent integration tests. In-memory MCP protocol tests remain in the database-independent suite even if their names contain `Integration`.
- Provide documented commands that a contributor and CI can run without a SQL instance, production configuration, private credentials or a network route to the maintainer's database.
- Run build, database-independent tests and dependency-vulnerability checks automatically for relevant pull requests and default-branch changes using standard GitHub-hosted runners and free features.
- Add reusable local checks for the actual self-contained Windows x64 output: extract the ZIP, launch the packaged process, complete MCP initialization and tool discovery, and verify that stdout contains protocol messages rather than diagnostic text.
- Run startup/configuration checks in an isolated Windows user-data environment. Existing maintainer configuration, logs and processes must not be used or modified as a side effect of validation.
- Automate candidate creation and validation for an exact source revision and package bytes. Prepare a public-release activation boundary that additionally requires local SQL evidence and the maintainer's release decision; a manually pushed tag must not activate the public release on CI results alone. The artifact later published must be the artifact that was checked.
- Keep SQL integration local against the fictional schema and a dedicated test database. Record the source revision, commands, relevant environment and sanitized result for the release candidate; distinguish not run, passed and failed.
- Establish an explicit maintainer release decision accounting for the local SQL result. Do not present a green database-independent workflow or a confirmation flag as proof that SQL tests ran.
- Handle native command exit codes correctly in validation/deployment helpers touched by this work; fail before reporting success or continuing release operations after a failed prerequisite.
- Give dependency findings a visible disposition. Fix release-blocking findings or document an explicit reviewed exception; a successful vulnerability command with findings is not equivalent to a clean report.
- Update the canonical development/release documentation and affected contributor instructions to explain test selection, prerequisites, failures, local SQL evidence and publication gates.

### Out of scope

- SQL Server provisioning in GitHub, paid runners, paid scanners or dependence on an accessible private SQL instance in public workflows.
- A new Claude/Cursor/Codex client matrix, application benchmarks, coverage-percentage targets or rewriting existing functional tests merely to increase their count.
- New runtime behavior, SQL permissions, configuration migration or certificate-policy implementation; those changes belong to their own concepts.
- MCPB generation, registry manifests, package-manager manifests, external directory submissions or an actual release/push. These remain in the publication task.
- Unrelated changes to process termination/deployment behavior or a general rewrite of the release tooling.

## Validation contract

- Every test group has an unambiguous documented selection. Database-dependent tests are not silently skipped and reported as passed when their prerequisites are unavailable.
- The automated required path is Windows x64, database-independent, repeatable from a clean checkout and unsuccessful when build/tests/package checks fail. Keep workflow permissions limited to each job's needs.
- First-run tests must exercise the [user-directory contract](../user-configuration/Konzept.md), including an install path containing spaces, a different working directory and preservation of an existing user settings file. Test hooks may redirect to temporary storage without introducing a production portable/configuration mode.
- Packaged stdio checks must not require a SQL connection or a separately installed .NET runtime/SDK for the server process. The harness may use the build toolchain; run the server in a controlled environment that cannot silently use that SDK/runtime to hide missing bundled dependencies.
- Keep checks and package creation reusable by the publication workflow. Validate the ZIP produced by this task's current build; when the publication task adds MCPB, it must extend this check to the final bundle's version, content, executable, configuration mapping and SHA-256. Do not add a permanently skipped MCPB check and call it coverage.
- Local SQL integration evidence must refer to the exact candidate source, using the same functional configuration and dependencies as the candidate artifact. If automated artifact creation cannot be reproduced locally byte-for-byte, state that limit instead of claiming binary equivalence.
- Require successful local SQL validation before the release owner activates a public release. The later publication design must keep the public-release step separate from creating/validating a candidate, so an automatic tag build cannot accidentally substitute for the local decision.
- Store evidence in the existing PR/release workflow or release description, without committing secrets, customer data or a new task execution-log inventory. Never publish unsanitized integration output.

## Dependencies and boundaries

- [User configuration and conservative defaults](../user-configuration/Konzept.md) owns the new user-directory behavior, demo separation and first-run/fixture adaptation. Validation infrastructure can be developed independently; its final smoke checks must run against that completed behavior.
- [SQL connection security](../sql-connection-security/Konzept.md) owns encryption/trust behavior and its specific tests. This task makes their database-independent tests part of CI and their real SQL scenarios part of the local release result.
- The [publication concept](../mcp-server-dictionary/Konzept.md) owns ZIP/MCPB distribution, metadata, public release execution and external acceptance. It consumes the verified validation gates rather than implementing them again.
- Configuration and TLS work can proceed separately before this concept's final combined candidate verification. There is no requirement to complete public MCPB distribution before the basic validation infrastructure is finished.

## Verification

- A documented database-independent build/test command succeeds without SQL Server or personal configuration. A failing selected test produces a failing workflow/helper exit status.
- SQL-dependent tests run through a distinct local command against the fictional setup. Missing SQL prerequisites are clearly reported as unavailable/failure, never as executed passes.
- PR/default-branch validation includes the intended non-SQL tests and reports dependency-check results. No paid execution resource or private SQL credential is required.
- The unpacked candidate executable initializes over stdio and exposes tools from an isolated user-data environment. It works without an installed runtime, with spaces in paths and from another working directory.
- A failed build, test, dependency gate or package check prevents public release activation. A tag creates/checks a candidate; CI success alone cannot activate a public release or establish completion of the local SQL requirement. The separate maintainer decision must account for that local result.
- The exact source revision, checked artifact identity and local SQL result can be associated with a release decision. No artifact is rebuilt/replaced after validation without rerunning the applicable checks.
- Existing helpers no longer report successful validation/publish after a relevant native process returns failure; preview/dry-run remains free of release side effects.
- Development documentation accurately separates automated checks, local SQL checks, candidate creation and actual publication. No new client certification is claimed.

This concept remains draft pending explicit concept approval. It creates no roadmap and executes no build, tests, configuration changes or publication.
