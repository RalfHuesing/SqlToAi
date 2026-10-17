# Named connections implementation roadmap

## Authority and execution

Implement the approved [concept](Konzept.md) (`status: ready`). Its Must items
and Verification section define the contract; this roadmap assigns implementation
and evidence to ordered slices. It does not change scope or authorize publishing.

Execute the first open task in order. Each task includes inspecting its current
consumers, updating affected tests and canonical documentation, running the
[required validation](../../.agents/rules/system-specs.mdc), recording concise
evidence beneath that task, and making an atomic commit. Use xUnit v3; test success
and failure paths. Check a task only after its acceptance is verified. Audit items
are executable tasks too; milestone checkboxes aggregate their tasks and audit.

The preparation slices build reusable context-aware contracts before the final
host cutover. Keep the solution buildable and migrate affected callers and test
fakes with their owning slice. Existing, unmigrated code may remain during
preparation; do not introduce a public compatibility mode, inferred server or
legacy fallback. M4.1 removes every obsolete entry point and configuration member.
Intermediate milestones are not a completed or releasable named-connections feature.
If a slice cannot fit one agent session including validation, split its remaining
work here before executing it, preserving its dependencies and acceptance.

For code changes run `dotnet build SqlToAi.slnx` and `dotnet test SqlToAi.slnx`
from the repository root; warnings are errors. Use the documented
[test categories and infrastructure](../../docs/development.md#build-and-test)
to distinguish offline, SQL and TLS evidence. An unavailable endpoint or skipped
TLS case is unverified, not a pass; retain the corresponding acceptance as open.
Do not create real credentials, change personal settings or weaken shipped TLS
defaults to make tests pass. Documentation-only audits check links, examples and
claims; rerun code checks only when changes or unresolved concerns warrant it.

## M1 — Target identity and primary routing

- [ ] **M1 — Aggregate:** M1.1–M1.3 accepted; one identity and policy contract is
  available to all later consumers.

- [ ] **M1.1 — Define immutable targets and server configuration models.**
  **Depends on:** approved concept.
  **Intention:** separate caller target identity, policy comparison and physical
  database names before any consumer selects an endpoint.
  **Scope:** shared parser/resolver, immutable target context and per-server options
  in the existing Configuration/Domain/Database boundaries; prepare named-server
  binding for M4.1. Inspect [options](../../src/SqlToAi/Configuration/SqlToAiOptions.cs)
  and the [connection contract](../../src/SqlToAi/Database/IDatabaseConnectionFactory.cs).
  **Exclusions:** activating the new host configuration, migration, hot reload,
  connection probes and changes to SQL object-name parsing.
  **Acceptance:** `TEST/AppDb` canonicalizes to `test/AppDb`; `test/App/Db` retains
  `App/Db`; Unicode and significant database characters survive unchanged.
  Missing/empty/malformed/unqualified targets and unknown aliases map to
  `SQL-AI-0001`. Alias identity uses invariant ASCII lowercase, policy database
  comparison uses `OrdinalIgnoreCase`, and raw database identity remains ordinal.
  Explicit `default` and two aliases for one physical endpoint gain no special
  behavior. Contexts contain no mutable current-server state.

- [ ] **M1.2 — Route primary connections and access policy through targets.**
  **Depends on:** M1.1.
  **Intention:** make endpoint selection and authorization use the same identity.
  **Scope:** [SqlConnectionFactory](../../src/SqlToAi/Database/SqlConnectionFactory.cs),
  [SecurityGuard](../../src/SqlToAi/Security/SecurityGuard.cs),
  [AccessLevelProvider](../../src/SqlToAi/Security/AccessLevelProvider.cs),
  [QuerySafetyValidator](../../src/SqlToAi/Database/QuerySafetyValidator.cs), primary
  connection diagnostics and directly affected dependencies/fakes. Provide an
  explicit selected-server catalog-connection path for M3.3, distinct from a
  database target. Preserve a resolved context through downstream calls.
  **Exclusions:** discovery formatting, secondary-provider policy and changes to
  SQL guards, login permissions, transaction semantics or global query limits.
  **Acceptance:** independent same-name grants, restrictive overlapping levels,
  default deny and server-local raw-name exclusions are verified. Valid denied or
  excluded targets return `SQL-AI-0104` before SQL; SqlClient receives only the raw
  InitialCatalog and selected endpoint/auth/TLS/connect timeout. Catalog access
  cannot infer a target for ordinary calls. Primary TLS/infrastructure and timeout
  errors retain `SQL-AI-0105`/`SQL-AI-0106` and identify the owning settings path
  without raw exception secrets. Parallel routing does not mutate options.

- [ ] **M1.3 — Audit target and policy foundations.**
  **Depends on:** M1.1–M1.2.
  **Intention:** establish safe contracts before migrating stateful consumers.
  **Scope:** inspect the preceding diff and tests against concept Must 1–5, 7 and
  11 as applicable; verify reusable context APIs and primary error handling.
  **Exclusions:** declaring public startup/discovery or the whole feature complete.
  **Acceptance:** parser, casing, endpoint, exclusion and error cases above have
  actual test evidence; no context can silently select a default endpoint. Fix
  findings in their owning slice before closing M1.

## M2 — Provider, token and execution isolation

- [ ] **M2 — Aggregate:** M2.1–M2.5 accepted; supporting providers and execution
  paths use scoped state without changing their existing protection boundaries.

- [ ] **M2.1 — Scope metadata, central rules and rule caches.**
  **Depends on:** M1.
  **Intention:** keep descriptions and masking exemptions with their policy owner.
  **Scope:** [MetadataProvider](../../src/SqlToAi/Metadata/MetadataProvider.cs),
  [SecondaryConnectionBuilder](../../src/SqlToAi/Database/SecondaryConnectionBuilder.cs),
  [AnonymizationRuleProvider](../../src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs)
  and policy resolution; use each owner's provider settings and effective source
  identity. Extend environment expansion and provider SQL-file resolution to
  per-server settings, preserving the user-root path base.
  **Exclusions:** provider aliases, new inheritance rules, failover and changing
  rule specificity, matching or timeout semantics.
  **Acceptance:** disabled providers make no connection; defaults retain native
  descriptions and no central-rule exemptions. No secondary Server/Database uses
  the selected target database; an explicit raw provider Database on the same
  server uses primary auth/TLS/connect timeout. A dedicated server requires its
  own raw database/auth/TLS and never retries on primary. Cache keys distinguish
  owner and effective source, including physical database and rules table; test
  both shared dedicated sources and per-target fallback sources. Failed loads
  retain default masking; cancellation propagates without caching a fallback.
  Masked and explicitly exempt values remain correct under parallel requests.

- [ ] **M2.2 — Scope token storage, issuance and existing resolution paths.**
  **Depends on:** M1.
  **Intention:** prevent tokens from carrying plaintext across selected targets.
  **Scope:** [TokenVault](../../src/SqlToAi/Anonymization/TokenVault.cs), anonymizer
  contracts, [QueryTokenResolver](../../src/SqlToAi/Database/QueryTokenResolver.cs)
  and affected callers; pass the target explicitly rather than ambient state.
  **Exclusions:** parameter substitution, resolution in additional tools, changes
  to process-local lifetime, masking algorithms or output envelopes.
  **Acceptance:** token IDs cannot collide across scopes; identical plaintext in
  different aliases or ordinally distinct raw database names is not interchangeable.
  Same-scope reuse and SQL-literal escaping still work. Foreign/unknown tokens stay
  literal text; comments and identifiers remain untouched. Verify concurrent
  issuance/resolution and both masked and exempt values without exposing plaintext.

- [ ] **M2.3 — Carry targets through query execution and validation.**
  **Depends on:** M2.1–M2.2.
  **Intention:** connect authorization, token processing and query work to one target.
  **Scope:** query execution, anonymization partials, query validation and
  [QueryExecutionDependencies](../../src/SqlToAi/Database/QueryExecutionDependencies.cs).
  Resolve at entry and carry the immutable context through guards, connections,
  provider calls, token operations and existing database-identifying output.
  **Exclusions:** script batches, analysis tools, new token coverage and SQL rewrites.
  **Acceptance:** alternate and concurrent same-name calls use the right endpoint,
  access level, masking rules and token scope. Direct-service calls reject invalid
  targets too. Existing target fields/headers are canonical, while rows, catalog
  hints, limits, `NOEXEC` safeguards, read-only rollback, ReadWrite commit and
  cancellation-independent cleanup retain their behavior and error contracts.

- [ ] **M2.4 — Carry targets through script execution.**
  **Depends on:** M2.3.
  **Intention:** apply the same identity across every batch in a script request.
  **Scope:** [ScriptExecutionService](../../src/SqlToAi/Database/ScriptExecutionService.cs),
  its report composition and shared execution/token/provider dependencies.
  **Exclusions:** changing script paths, size limits, GO parsing, transaction-control
  preflight or autocommit behavior.
  **Acceptance:** batches cannot change the selected target context; per-server
  access and state isolation hold in parallel requests. Canonical target labels
  preserve report structure. Existing protected rollback, managed-transaction
  rejection, ReadWrite autocommit partial failure, timeout and cancellation tests
  remain valid with qualified targets.

- [ ] **M2.5 — Audit provider, token and execution isolation.**
  **Depends on:** M2.1–M2.4.
  **Intention:** detect foreign exemptions or plaintext reuse before exposing tools.
  **Scope:** review concept Must 8–10 and the execution portion of Must 5/11 against
  implementations, fakes and tests, including parallel and failed-load cases.
  **Exclusions:** claiming schema/analysis or SDK coverage from service tests.
  **Acceptance:** cache ownership, provider fallback, ordinal token scope and
  unchanged protection limits have verified success/failure evidence; no shared
  mutable target state or newly introduced legacy fallback remains. Resolve all
  findings before closing M2.

## M3 — Remaining services and public argument surfaces

- [ ] **M3 — Aggregate:** M3.1–M3.4 accepted; every database-facing surface has a
  target-aware implementation ready for host cutover.

- [ ] **M3.1 — Route schema inspection and existing schema exports.**
  **Depends on:** M2.
  **Intention:** preserve database identity through metadata and export composition.
  **Scope:** [SchemaService](../../src/SqlToAi/Database/SchemaService.cs), scalar/batch
  schema, object search and all separate detail/definition operations; export
  discovery, [SchemaExportService](../../src/SqlToAi/Cli/SchemaExportService.cs) and
  its typed identities/rendering contexts. Pass one resolved target through helpers.
  **Exclusions:** database list/search (M3.3), data/CSV export, changes to selection,
  batch budgets, dump layout, file safety or offline provider exclusions.
  **Acceptance:** same-name targets route correctly for scalar/batch schema, every
  detail tool, selected/all MCP export and legacy full CLI export. SchemaOnly and
  pre-SQL access checks remain effective. Existing database headings use canonical
  targets, but SQL/DDL/object identifiers stay raw. Batch envelopes/limits, offline
  rendering without provider calls, partial-output and cancellation behavior hold.

- [ ] **M3.2 — Route comparison, measurements, benchmarks and indexes.**
  **Depends on:** M2.
  **Intention:** prevent nested analysis calls from selecting different targets.
  **Scope:** comparison, performance measurement, optimization benchmark, index
  suggestions and [DatabaseAnalysisServices](../../src/SqlToAi/Database/DatabaseAnalysisServices.cs).
  Carry context into nested validation, measurements, plans and connection calls.
  **Exclusions:** new analysis features, masking raw difference samples, new token
  resolution, altered metrics, verdicts, permissions or query limits.
  **Acceptance:** each operation and benchmark sub-operation uses one endpoint
  and policy identity; existing database fields are canonical. Verify SchemaOnly
  rejection where required, rollback/cleanup, safe connection failures and parallel
  same-name routing. Existing parameter isolation and raw-output limitations remain.

- [ ] **M3.3 — Expose server discovery and qualified MCP/CLI arguments.**
  **Depends on:** M3.1–M3.2.
  **Intention:** let callers discover aliases and address only explicit targets.
  **Scope:** database discovery services; [ToolRegistry](../../src/SqlToAi/Mcp/ToolRegistry.cs),
  dispatcher, SDK registrations, source-generated JSON metadata, registry-generated
  CLI and `export-schema` binding; update exploration scenarios, owning fixtures
  and the expected tool inventory in [package smoke checks](../../scripts/smoke-package.ps1).
  **Exclusions:** host configuration activation, credential/hostname discovery,
  connection probes for listing aliases and implicit server selection.
  **Acceptance:** `sql_list_servers` has no arguments and returns canonical aliases
  as one JSON array in text content without SQL calls. List/search require `server`;
  search additionally requires unchanged `search_term` and matches only raw names
  with `OrdinalIgnoreCase` substring semantics. Catalog work contacts one selected
  server. Existing static fallback deduplicates only that owner's grants, applies
  exclusions/policy and claims no existence guarantee; TLS/timeouts return their
  errors and cancellation propagates. Registered SDK invocation, dispatcher and
  generated CLI tests verify argument failures, qualified targets, result shapes
  and MCP `isError`/CLI failure behavior; update the SQL tool count to 19.

- [ ] **M3.4 — Audit service and transport coverage.**
  **Depends on:** M3.1–M3.3.
  **Intention:** rule out a forgotten path before activating the hard cut.
  **Scope:** trace current factory, policy, provider and vault consumers plus
  registry/SDK/CLI/exploration wiring. Compare them with concept Must 5–7/11.
  **Exclusions:** treating static references or test names as runtime proof.
  **Acceptance:** all existing SQL tools, direct services, both export entry
  surfaces and exploration use the intended parser/context; every target-bearing
  output follows the canonical contract without new row formats. Record evidence
  here, not in a separate code map. Fix omissions before closing M3.

## M4 — Hard cut, operating documentation and final evidence

- [ ] **M4 — Aggregate:** M4.1–M4.3 accepted; every concept acceptance criterion
  is verified and the final contract contains no compatibility behavior.

- [ ] **M4.1 — Activate strict startup and remove the single-server contract.**
  **Depends on:** M3.
  **Intention:** make every shipped entry point accept only the approved contract.
  **Scope:** [UserConfiguration](../../src/SqlToAi/Configuration/UserConfiguration.cs),
  ConfigurationResolver, options validation, Program/DI, StartupHarness, main/demo
  JSON, integration/test-only TLS overlays, package-smoke configuration inputs,
  remaining fixtures and environment examples. Remove obsolete top-level options,
  old overloads and unused preparation scaffolding. Update [configuration](../../docs/configuration.md),
  [security](../../docs/security.md), [architecture](../../docs/architecture.md),
  [tools](../../docs/tools.md), [development](../../docs/development.md),
  [exploration](../../docs/exploration.md), [SETUP](../../SETUP.md) and affected
  public [README](../../README.md) setup examples in this cutover slice.
  **Exclusions:** importing/rewriting/backing up user settings, migration commands,
  release/version/publishing changes and weakening test or application TLS defaults.
  **Acceptance:** require at least one valid server; validate grants after binding
  and supported expansion. Reject exact/case-only aliases inside each JSON Servers
  object before lossy merge, but allow normal cross-source overrides. Inspect the
  required base file, active environment file and process keys for obsolete
  sections/prefixes even when overridden. Missing base settings still fail;
  invalid configuration exits 1 before SQL/MCP with safe actionable stderr and no
  diagnostic stdout or settings writes. Direct options/DI use equivalent effective
  validation; source checks stay at the loading boundary. Provider SQL paths and
  other surviving expansion/path rules remain intact. Main grants stay empty;
  demo grants are qualified and examples use environment-based SQL credentials.
  Public setup/update guidance includes a complete new example and manual hard-cut
  instructions. No runtime consumer can still bind or infer the old contract.

- [ ] **M4.2 — Verify cross-target behavior through the real host and Native AOT.**
  **Depends on:** M4.1.
  **Intention:** prove the composed process behaves like its unit-level contracts.
  **Scope:** add focused multi-target integration coverage and finalize exploration
  fixtures; use two configured SQL endpoints with same-name databases, deliberately
  distinct fictional rows/rights/rules and explicit test authentication settings.
  Run the documented build/test commands. Separately validate Native AOT with
  `dotnet publish src/SqlToAi/SqlToAi.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -o <fresh-temporary-directory>`.
  The existing packaging/deploy route is not Native AOT; do not silently replace it.
  Exercise published stdio registration/arguments, not only dispatcher calls,
  using an already disposable Windows profile when the production executable
  needs user settings. The normal profile must remain untouched; the existing
  temporary-root StartupHarness can verify managed host startup separately.
  **Exclusions:** remote provisioning, personal configuration changes, release
  creation, deployment, pushing and unrequested benchmark/coverage projects.
  **Acceptance:** alternating and parallel calls in one process never mix endpoint,
  rights, rules, cache or tokens. Same physical endpoint under two aliases still
  has independent owners. Service tests and real-host checks cover query/script,
  validation, schema/details/export, analysis and discovery with qualified targets.
  Startup failures precede database work; server listing performs no SQL probes;
  SQL/TLS failures and per-endpoint trust remain safe. Distinguish offline checks
  for syntactically unusual raw database names from live same-name database tests.
  Native AOT publishes without warnings/errors; published MCP discovery exposes
  19 tools and actual binding accepts/rejects the changed arguments correctly.
  Record commands, outcomes and any unavailable infrastructure under this item;
  unresolved required live/AOT evidence keeps it open.

- [ ] **M4.3 — Audit the complete concept and close the roadmap.**
  **Depends on:** M4.1–M4.2 and all preceding milestone acceptance.
  **Intention:** verify the finished contract without relying on milestone claims.
  **Scope:** review the final diff, current code/wiring, examples and recorded test
  evidence against every concept Must and Verification item; inspect remaining
  legacy identifiers and classify legitimate rejection tests/historical references.
  **Exclusions:** starting another workflow stage or claiming skipped tests passed.
  **Acceptance:** no unresolved findings, unverified concept criteria, public
  legacy configuration, implicit defaults or foreign state remain. Docs and main/
  demo/test profiles agree with implementation; secrets and raw connection strings
  are absent from new diagnostics/examples. Links and examples are valid, required
  validation evidence is complete and related code/tests/docs are committed. Close
  M4 and the roadmap only after this audit passes.
