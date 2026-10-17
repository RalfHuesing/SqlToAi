# Named connections implementation roadmap

## Authority and execution

Implement the approved [concept](Konzept.md) (`status: ready`). Its Must items
and Verification section define the contract; this roadmap assigns implementation
and evidence to ordered slices. It does not change scope or authorize publishing.

Verification uses deterministic automated multi-target isolation coverage,
available single-endpoint SQL integration tests, the Native AOT publish, and
automated registered-MCP/CLI binding plus managed host startup checks. No
additional SQL endpoint or separate Windows profile is required. These are the
user-approved acceptance boundaries in the current concept.

## Resume checkpoint

The independent deep audit started at `cab47d3` on 2026-10-17. Historical
milestone checkmarks below describe prior acceptance, not renewed verification.
Feature closure is reopened until the remediation slices and final audit below
pass. Findings and their source locations are in the [deep audit](audit.md#deep-audit-at-cab47d3).

Baseline gates: `dotnet build SqlToAi.slnx --warnaserror` returned 0 warnings and
0 errors; `dotnet test SqlToAi.slnx --no-build` returned 1,486 passed, 19 skipped,
0 failed. All skipped cases belong to `TlsConnectionIntegrationTests`; external
TLS remains unverified. `git diff --check` was clean. No push, deployment or
personal-settings change is authorized by this work.

## Deep-audit remediation

Each code slice requires a coder, a different independent reviewer, a clean
warning-as-error build, the complete test suite (only the documented external TLS
skips permitted), `git diff --check`, and an atomic commit. The current user request
explicitly authorizes parallel read-only audits and repeated correction/review
loops, superseding the workflow's sequential-audit and one-correction defaults.

- [x] **R1 — Preserve SQL identifier/comment boundaries during token resolution.**
  Correct lexical handling of quoted identifiers, escaped brackets and relevant
  nested comments; retain literal substitution and foreign-token isolation. Add
  regression tests at scanner, resolver and execution boundaries.
  Evidence: [R1 review and gates](audit.md#r1--syntax-aware-token-resolution).
- [x] **R2 — Validate every JSON source occurrence and both DI options paths.**
  Inspect every matching wrapper before merge; reject hidden obsolete sections
  and exact/case-only duplicate aliases. Validate resolved direct and bound options
  equivalently; preserve legitimate overrides, safe stderr and settings bytes.
  Evidence: [R2 review and gates](audit.md#r2--source-boundaries-and-effective-options-validation).
- [x] **R3 — Preserve discovery timeout/TLS failures after connection open.**
  Classify catalog-command failures before static-grant fallback; preserve safe
  owner-specific errors, cancellation and fallback for other catalog errors.
  Include the canonical owner settings path in safe secondary-provider logs.
  A failed SchemaPattern probe must retain default masking; only a successful
  probe proving the column absent may use legacy schema-agnostic rules.
  Evidence: [R3 review and gates](audit.md#r3--safe-discovery-and-failed-provider-loads).
- [x] **R4 — Carry one target through composed services and exploration.**
  Resolve once at export/benchmark entry and retain the context through child
  operations. Derive exploration's manifest rights from its actual canonical
  scenario target. Remove unused parser-only guard entry points that return the
  wrong error for unknown aliases. Verify identity, denial and error paths.
  Evidence: [R4 review and gates](audit.md#r4--one-context-through-export-benchmark-and-exploration).
- [x] **R5 — Test real composition and publish the qualified argument contract.**
  Exercise production rule caches/services behind connection doubles and actual
  SDK/CLI bindings; assert source loads, exemptions, tokens and same-address
  owner separation. Share explicit `alias/database` descriptions and verify help.
  Evidence: [R5 review and gates](audit.md#r5--real-provider-composition-and-public-target-binding).
- [ ] **R2a — Normalize obsolete environment paths at the provider boundary.**
  Reject colon and mixed-delimiter forms of known obsolete product sections;
  retain section boundaries so unrelated names such as SqlServerless remain valid.
  Use safe known-prefix diagnostics and isolated child-process regressions.
- [ ] **R6 — Verify Native AOT honestly and perform independent final audit.**
  Investigate hidden third-party AOT diagnostics without adding suppressions;
  run final build/tests/publish, retain unresolved compatibility findings openly,
  replace unsupported M4.3 claims and supply the missing M3.4 evidence. No
  production-grade or warning-free AOT claim is permitted while these remain open.
  - [ ] **R6a — Remove application-owned dynamic data mapping.** Replace the
    existing Dapper catalog/provider calls with explicit ADO.NET mappings and
    parameters; preserve null handling, outputs, cancellation and timeouts. Use
    independent review and the complete managed gates before committing.
  - [ ] **R6b — Remove hidden diagnostics and execute the Native AOT gate.**
    Remove global IL2104/IL3053 suppression, publish the actual application,
    and distinguish application remediation from unresolved official-driver
    compatibility. Do not introduce a maintained driver fork implicitly.
  - [ ] **R6c — Complete independent final audit and evidence synchronization.**
    Supply M3.4 evidence, replace M4.3 claims, verify links/line and commit
    references, and leave any unmet Native AOT criterion explicitly open.

## Execution contract

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

- [x] **M1 — Aggregate:** M1.1–M1.3 accepted; one identity and policy contract is
  available to all later consumers.

- [x] [**M1.1 — Define immutable targets and server configuration models**](roadmap/m1.1-define-immutable-targets-and-server-configuration-models.md)
  Separate caller target identity, policy comparison and physical database names before any consumer selects an endpoint.

- [x] [**M1.2 — Route primary connections and access policy through targets**](roadmap/m1.2-route-primary-connections-and-access-policy-through-targets.md)
  Make endpoint selection and authorization use the same identity.

- [x] [**M1.3 — Audit target and policy foundations**](roadmap/m1.3-audit-target-and-policy-foundations.md)
  Establish safe contracts before migrating stateful consumers.

## M2 — Provider, token and execution isolation

- [x] **M2 — Aggregate:** M2.1–M2.5 accepted; supporting providers and execution
  paths use scoped state without changing their existing protection boundaries.

- [x] [**M2.1 — Scope metadata, central rules and rule caches**](roadmap/m2.1-scope-metadata-central-rules-and-rule-caches.md)
  Keep descriptions and masking exemptions with their policy owner.

- [x] [**M2.2 — Scope token storage, issuance and existing resolution paths**](roadmap/m2.2-scope-token-storage-issuance-and-existing-resolution-paths.md)
  Prevent tokens from carrying plaintext across selected targets.

- [x] [**M2.3 — Carry targets through query execution and validation**](roadmap/m2.3-carry-targets-through-query-execution-and-validation.md)
  Connect authorization, token processing and query work to one target.

- [x] [**M2.4 — Carry targets through script execution**](roadmap/m2.4-carry-targets-through-script-execution.md)
  Apply the same identity across every batch in a script request.

- [x] [**M2.5 — Audit provider, token and execution isolation**](roadmap/m2.5-audit-provider-token-and-execution-isolation.md)
  Detect foreign exemptions or plaintext reuse before exposing tools.

## M3 — Remaining services and public argument surfaces

- [x] **M3 — Aggregate:** M3.1–M3.4 accepted; every database-facing surface has a
  target-aware implementation ready for host cutover.

- [x] [**M3.1 — Route schema inspection and existing schema exports**](roadmap/m3.1-route-schema-inspection-and-existing-schema-exports.md)
  Preserve database identity through metadata and export composition.

- [x] [**M3.2 — Route comparison, measurements, benchmarks and indexes**](roadmap/m3.2-route-comparison-measurements-benchmarks-and-indexes.md)
  Prevent nested analysis calls from selecting different targets.

- [x] [**M3.3 — Expose server discovery and qualified MCP/CLI arguments**](roadmap/m3.3-expose-server-discovery-and-qualified-mcp-cli-arguments.md)
  Let callers discover aliases and address only explicit targets.

- [x] [**M3.4 — Audit service and transport coverage**](roadmap/m3.4-audit-service-and-transport-coverage.md)
  Rule out a forgotten path before activating the hard cut.

## M4 — Hard cut, operating documentation and final evidence

- [x] **M4 — Aggregate:** M4.1–M4.3 accepted; every concept acceptance criterion
  is verified and the final contract contains no compatibility behavior.

- [x] [**M4.1 — Activate strict startup and remove the single-server contract**](roadmap/m4.1-activate-strict-startup-and-remove-the-single-server-contract.md)
  Make every shipped entry point accept only the approved contract.

- [x] [**M4.2 — Verify composed target isolation and Native AOT compilation**](roadmap/m4.2-verify-composed-target-isolation-and-native-aot-compilation.md)
  Prove the composed process behaves like its unit-level contracts.

- [x] [**M4.3 — Audit the complete concept and close the roadmap**](roadmap/m4.3-audit-the-complete-concept-and-close-the-roadmap.md)
  Verify the finished contract without relying on milestone claims.
