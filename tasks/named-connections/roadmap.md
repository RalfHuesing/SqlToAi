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

M1 and M2 are accepted; [M1.3 audit evidence](audit.md#m13--independent-foundation-audit) covers
preparation contracts at `070d1bb`, and [M2.5 audit evidence](audit.md#m25--independent-provider-token-and-execution-audit)
covers provider, token and execution isolation across `070d1bb..41a4271`.
Resume execution at **M3.1** (Route schema inspection and existing schema exports).
M3 and M4 items have not started. This is an intermediate, non-releasable state:
schema/export, analysis, discovery/MCP/CLI arguments, strict host cutover, legacy-API removal,
final composed checks and Native AOT evidence belong to the remaining slices.

Latest implementation commits: M2.1 `2913a11` + `67049c7`; M2.2 `d488565` +
`208624d` + `337beb4`; M2.3 `7adeb6a` + `53aa63c`; M2.4 `41a4271`. No push,
deployment or personal-settings change.

Parent verification after M2.5: `dotnet build SqlToAi.slnx` succeeded with
0 warnings/errors; `dotnet test SqlToAi.slnx --no-build --logger
'console;verbosity=quiet'` returned 1,424 passed, 19 skipped, 0 failed.
`git diff --check` was clean.

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

- [ ] **M3 — Aggregate:** M3.1–M3.4 accepted; every database-facing surface has a
  target-aware implementation ready for host cutover.

- [x] [**M3.1 — Route schema inspection and existing schema exports**](roadmap/m3.1-route-schema-inspection-and-existing-schema-exports.md)
  Preserve database identity through metadata and export composition.

- [x] [**M3.2 — Route comparison, measurements, benchmarks and indexes**](roadmap/m3.2-route-comparison-measurements-benchmarks-and-indexes.md)
  Prevent nested analysis calls from selecting different targets.

- [ ] [**M3.3 — Expose server discovery and qualified MCP/CLI arguments**](roadmap/m3.3-expose-server-discovery-and-qualified-mcp-cli-arguments.md)
  Let callers discover aliases and address only explicit targets.

- [ ] [**M3.4 — Audit service and transport coverage**](roadmap/m3.4-audit-service-and-transport-coverage.md)
  Rule out a forgotten path before activating the hard cut.

## M4 — Hard cut, operating documentation and final evidence

- [ ] **M4 — Aggregate:** M4.1–M4.3 accepted; every concept acceptance criterion
  is verified and the final contract contains no compatibility behavior.

- [ ] [**M4.1 — Activate strict startup and remove the single-server contract**](roadmap/m4.1-activate-strict-startup-and-remove-the-single-server-contract.md)
  Make every shipped entry point accept only the approved contract.

- [ ] [**M4.2 — Verify composed target isolation and Native AOT compilation**](roadmap/m4.2-verify-composed-target-isolation-and-native-aot-compilation.md)
  Prove the composed process behaves like its unit-level contracts.

- [ ] [**M4.3 — Audit the complete concept and close the roadmap**](roadmap/m4.3-audit-the-complete-concept-and-close-the-roadmap.md)
  Verify the finished contract without relying on milestone claims.
