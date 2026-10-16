# Schema batching and schema export — roadmap

Implement the [ready concept](Konzept.md) in the order below. The concept owns all
public contracts and exclusions; this roadmap assigns executable slices without
redefining them. Each item includes analysis, implementation, validation, related
canonical documentation and a focused commit, and must fit one agent session.
No implementation or practical verification has been performed by creating this file.

## Execution rules

- Follow the repository rules, including MCP-first C# navigation. Inspect current
  consumers and tests before editing; reuse existing services and test support.
- For each code slice, add meaningful xUnit tests for success and failure paths,
  then run `dotnet build SqlToAi.slnx` and `dotnet test SqlToAi.slnx` from the root.
  Record actual results and any unverified prerequisites; do not equate static
  inspection or a skipped database case with successful execution.
- Update the documentation assigned to the slice in the same commit. Apply the
  concept's documentation requirements throughout, rather than deferring them all
  to the final audit. Keep new option defaults in the main configuration template.
- Store completion evidence directly below the completed item: changed behavior,
  validation commands/results, relevant artifact paths, review findings and fixes,
  and commit reference. Do not add tracking/report files. Check an item only when
  all its acceptance criteria are verified; do not close blocked practical reviews.
- Practical reviews use temporary scenario source and retained runner artifacts
  under `temp/exploration/`, as specified by the concept. State expectations before
  execution, inspect every recorded response/error, then remove temporary source.
  Do not commit artifacts or change user configuration/permissions to manufacture
  a successful review. If required database access is unavailable, report it.

## Ordered work

- [x] **1. Share input normalization and database object resolution.**

  **Intention:** Give schema batches and selected export the same tolerant input
  handling and unambiguous object identities.

  **Scope:** Add the internal normalization/selection boundary defined by the
  concept. Reuse permitted object discovery and SQL identifier handling, resolving
  qualified/quoted names and unqualified names under database comparison rules.
  Preserve ordered input associations while reusing resolved-object work. Cover
  unknown, ambiguous and unsupported identities with existing catalog errors.
  Starting points: `SchemaService`, `SchemaObjectDiscovery`, `SchemaObjectIdentity`
  and existing schema discovery tests. Update architecture documentation if this
  introduces a shared responsibility worth describing.

  **Exclusions:** No public MCP/CLI behavior change, case-insensitive SQL-name
  assumption, relationship expansion or replacement of legacy scalar resolution.

  **Acceptance:** Focused tests verify scalar/list combination order, null/empty/
  whitespace handling, invalid types, duplicate names and aliases, quoted names,
  schema ambiguity and database-sensitive name comparison. Access checks precede
  catalog exposure. The existing scalar path remains unchanged; build/tests pass.

  **Completion evidence (2026-10-16):**

  - Implemented the internal boundary in
    `src/SqlToAi/Database/SchemaObjectSelection.cs` and
    `SchemaService.ResolveObjectsAsync`. JSON normalization retains scalar-first
    order and duplicate associations, ignores null/empty/whitespace entries,
    rejects invalid types, and supports explicit empty-selection validation for
    future callers. No MCP registration, CLI contract or scalar lookup changed.
  - SQL identifiers are parsed with the existing ScriptDom parser. Shared
    discovery compares schema/object parameters against SQL Server catalog names,
    with no forced collation or case folding. Exact inputs and equivalent quoted
    identifiers reuse lookups; aliases reuse the same resolved object by ID.
    Unknown names return `SQL-AI-0103`; ambiguous, invalid and unsupported
    identities return identifiable `SQL-AI-0001` errors. Database policy and
    access-level checks precede connections/catalog queries; `SchemaOnly` works.
  - Added 50 focused unit cases in
    `tests/SqlToAi.Tests/Database/SchemaObjectSelectionTests.cs`, covering order,
    empty/null values, wrong types, duplicates/aliases, escaping, literal wildcard
    names, ambiguity, unsupported kinds, simulated case-sensitive/insensitive
    catalog matches, denied access, query failure and cancellation. The added
    integration case in `SchemaServiceIntegrationTests.cs` executed against the
    existing SQL fixture and checked quoted aliases and actual database catalog
    comparison. It does not claim execution against separate databases with every
    collation. Existing scalar/search/export regression tests remain green.
  - Review found that ScriptDom accepts trailing SQL comments in parsed names;
    selection now rejects comment tokens, and the focused regression passes.
    Updated `docs/architecture.md` for the internal responsibility. No temporary
    scenario source or database/configuration changes were needed for this item;
    practical MCP reviews remain assigned to items 4 and 7.
  - Validation: the focused unit/integration command passed all 51 cases.
    `dotnet build SqlToAi.slnx` passed with zero warnings/errors.
    `dotnet test SqlToAi.slnx` passed: 930 successful, 19 skipped, zero failures,
    949 total. The pre-existing TLS endpoint tests skip without an explicitly
    configured `SQLTOAI_TLS_TEST_PROFILE`; their certificate scenarios remain
    unverified by this run. `git diff --check` passed.
  - Actual delegated implementation setting: `gpt-6.1-sol`, reasoning effort
    `high`. Atomic commit reference: the direct child of `11957de` with subject
    `refactor: Teile Normalisierung und Auflösung der Objektauswahl` (this slice).
    Items 2–8 remain open and were not implemented.

- [ ] **2. Implement bounded primary-schema batch composition.**

  **Intention:** Produce complete, attributable results within the agreed budgets.

  **Scope:** Build on item 1 to fetch primary schemas with existing interactive
  enrichment, reuse resolved-object fetches and compose the exact JSON envelope
  and completeness/error semantics from the concept. Add bound options for input
  count and aggregate UTF-8 response bytes, with the specified defaults, option
  validation and template values. Update configuration documentation and describe
  new service responsibilities in architecture documentation as applicable.

  **Exclusions:** No MCP registration yet, batch detail sections, export budgets,
  cursor state, automatic dependency discovery or change to scalar responses.

  **Acceptance:** Tests prove one result per normalized input, fetch reuse,
  successful siblings after object errors, count-before-dedup behavior, exact
  serialized-byte accounting including metadata, whole-schema omission and the
  oversized-first-schema/metadata-only cases. Enrichment and access policy remain
  consistent with existing inspection. Options/docs/template agree; build/tests pass.

- [ ] **3. Expose scalar and batch schema calls consistently.**

  **Intention:** Make the parameter forms agents see actually callable through all
  existing entry points.

  **Scope:** Integrate item 2 through `ToolRegistry`, parameter-schema models and
  JSON serialization metadata as needed, `SqlMcpToolRegistrations`, `ToolDispatcher`,
  DI and `ToolCommandFactory`. Advertise optional `object_name`/`object_names`
  truthfully, including nullable inputs allowed by the concept. Document actual CLI
  array syntax and scalar/list/combined examples in tool and development docs.
  Add in-process registered-SDK invocations with the same JSON input shapes; direct
  dispatcher tests alone do not cover SDK argument binding.

  **Exclusions:** No plural schema tool, transport infrastructure, batching other
  tools, public changes to unrelated command parsing or schema detail calls.

  **Acceptance:** Registry/SDK contracts and dispatcher/CLI parsing agree. Tests
  cover absent/null arrays preserving legacy validation and Markdown, supplied
  arrays selecting the envelope, combined inputs and invalid types, and correct
  `isError` propagation. Existing command tests remain valid. Documented examples
  are exercised with the actual parsers/binders; build/tests pass.

- [ ] **4. Review schema parameter usability through real exploration.**

  **Intention:** Detect impractical calls and confusing responses before building
  export on the same selection contract.

  **Scope:** Run the concept's schema-review matrix using a temporary
  `SqlToAi.Exploration` scenario and the real configured database. Include a
  realistic related-table lookup, legacy scalar comparison, combined/empty/duplicate
  forms, invalid types, mixed valid/missing names and exceeded limits. Inspect
  requests, all response blocks, joined text, error state and recorded exceptions.
  Compare exposed descriptions/schema and item 3's SDK-binding evidence to the
  same calls. Assess whether another agent can choose parameters, associate results
  and recover from errors or omissions without implementation knowledge.

  **Exclusions:** No new permanent scenario, snapshot assertions, CI requirement,
  synthetic success substituted for missing database access or feature expansion.

  **Acceptance:** Record expectations, run/artifact paths, observed behavior and
  an explicit parameter/output usability verdict here. Correct contract/usability
  findings within the approved scope, update affected docs/tests and rerun affected
  cases. Runner exit zero alone is insufficient. Temporary source is removed;
  any code fixes pass build/tests. All required observations are actually available.

- [ ] **5. Extend existing export composition for selected objects and outcomes.**

  **Intention:** Reuse the accepted CLI dump while enabling precise selection and
  truthful completion/failure reporting.

  **Scope:** Extend `SchemaExportService` using item 1's selection logic. Resolve
  selected input fully before output creation, filter before path mapping and
  reuse existing renderers, document layout and exclusive creation. Provide the
  internal outcome information needed for the concept's MCP response, including
  cancellation and partial-output state, while preserving the CLI's existing exit,
  path and output behavior. Update architecture/development docs for shared
  composition changes; use existing export tests/support for regression coverage.

  **Exclusions:** No content redesign, recursive selection, staging/cleanup,
  export-root configuration, new artifact format or MCP-only path restrictions
  applied to the CLI.

  **Acceptance:** Tests cover selected/all object coverage, aliases/deduplication,
  invalid/ambiguous/unsupported selection before writes, unexported references,
  empty full-export catalog, unchanged CLI full-export behavior and successful
  unavailable-definition notes. Inject failures/cancellation before and after
  writes: retained files/child directories and an empty root yield the specified
  outcome, and a still-empty root can be retried. Build/tests pass.

- [ ] **6. Expose the schema export tool and update its consumers.**

  **Intention:** Let agents deliberately trigger the existing dump through a
  discoverable, accurately described MCP call.

  **Scope:** Register and dispatch `sql_export_schema`, wire shared export services
  and return the concept's compact status/path/partial-output response. Enforce
  explicit scope and MCP-only fully qualified Windows destination validation.
  Update registry-generated CLI support where the new registration requires it,
  without altering the existing `export-schema` command. Update tool, development,
  security and architecture docs, including the potentially large server-side
  dump and failure/retry semantics. Adjust current inventory assumptions, including
  `scripts/smoke-package.ps1` and relevant test expectations, from 17 to 18 tools.
  Update exploration documentation truthfully: its existing baseline does not
  automatically cover the new tool; task-specific review is covered by item 7.

  **Exclusions:** No new transport/client, package release/publish, forced packaged
  smoke run in the maintainer's profile or permanent baseline mutation just to
  satisfy an obsolete all-tools claim.

  **Acceptance:** In-process SDK, dispatcher, registry and applicable CLI tests
  exercise selected scalar/list/combined forms, missing/invalid scope, harmless
  empty selection on `all`, prohibited non-empty selection on `all`, and an empty
  `selected` error. Path tests cover drive/UNC qualification, rejected path forms,
  files and non-empty targets; tests use existing filesystem/connection support
  rather than assuming a reachable UNC share. Access, status and error flags match
  the concept. Inventory consumers and documented examples agree; build/tests pass.

- [ ] **7. Review export calls through real exploration.**

  **Intention:** Verify the new agent-facing export workflow without reopening the
  already accepted CLI dump design.

  **Scope:** Invoke selected and full export through a temporary real-dispatcher
  scenario, using fresh absent/empty absolute destination directories and the
  configured database. Exercise selection parameter forms and representative
  invalid selection/path/destination cases. Inspect request/response artifacts,
  returned status/location, actual directory creation and representative files.
  Assess the tool description's scope/large-dump warning and the usability of
  selection, destination requirements and actionable failure responses.

  **Exclusions:** No comprehensive repeat of the CLI dump-content audit, automatic
  cleanup of failed output, extra permanent scenarios or invented runtime evidence
  for inaccessible destinations. Deterministic fault injection remains in tests.

  **Acceptance:** Record expectations, artifact/output paths, observations and an
  explicit export-call usability verdict here. Confirm the returned paths identify
  the actual output and selected/full operations follow the advertised contract.
  Fix findings and rerun affected cases, updating docs/tests as needed. Remove
  temporary source and retain observations per runner conventions. No required
  practical verification remains blocked; code fixes pass build/tests.

- [ ] **8. Audit implementation, documentation and completion evidence.**

  **Intention:** Establish that the delivered behavior satisfies the ready concept
  and works for an agent in practice.

  **Scope:** Audit the concept's full Verification section against items 1–7,
  source/test changes and recorded practical artifacts. Verify canonical docs,
  configuration defaults, tool inventory, parameter examples and CLI/MCP boundary
  descriptions against the implementation. Check security/exposure wording,
  retained output/retry behavior and all links. Run final repository build/tests
  on the completed code and review the focused diff and working tree.

  **Exclusions:** No new scope, release/deployment, historical task rewrites or
  completion based only on unit tests when practical evidence is missing.

  **Acceptance:** Every concept criterion has concrete evidence, all earlier items
  are verified, and practical reviews have explicit usable verdicts with no open
  findings. Documentation describes implemented behavior and invocations, not
  planned behavior; README is changed only if affected. Final build/tests pass
  with any genuine environment limits reported accurately. All task changes are
  committed, unrelated changes are preserved and no temporary scenario source
  remains. Record the audit result here before closing this item.
