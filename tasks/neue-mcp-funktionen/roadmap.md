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

- [x] **2. Implement bounded primary-schema batch composition.**

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


  **Completion evidence (2026-10-16):**

  - Added internal `SchemaService.GetSchemaBatchAsync` and
    `src/SqlToAi/Database/SchemaBatchComposer.cs`. Scalar-first normalization is
    reused from item 1, count limits apply before deduplication, and every normalized
    input retains its own ordered result. Resolved object IDs share primary-schema
    fetches, including fetch failures. Interactive enrichment and discovery hints
    remain enabled; detail sections, export, MCP registration and legacy scalar
    validation/rendering remain on their existing paths.
  - The composer retains the exact JSON text measured in UTF-8, including envelope,
    names, status, escaping, full catalog errors and omission reasons. It reserves
    omission metadata for all pending inputs, rejecting an insufficient metadata
    budget before opening a database connection. If a whole schema or object-error
    entry cannot fit, that input and all subsequent inputs become
    `not_returned`/`response_limit`; earlier results remain intact. An oversized first
    entry therefore produces metadata only. `complete` is true only for all-`ok`
    results. Validation, access and catalog/connection failures fail the request;
    cancellation propagates, including cancellation during the final fetch.
  - Review identified that passing always-quoted names to existing string-based
    enrichment would change custom metadata parameters and rule matching. Focused
    identity overloads now let the batch path use qualified SQL names for native
    descriptions, raw object names for custom descriptions, and resolved schema/
    object names for anonymization rules. Existing string-based scalar and offline
    paths are preserved; default interface implementations retain compatibility
    with existing custom providers. Tests cover explicit unmasked values alongside
    masked/searchable strings, non-string values and quoted dot/bracket names.
  - Added 45 unit/integration cases in `SchemaBatchComposerTests.cs`,
    `SchemaServiceBatchTests.cs`, `UserConfigurationTests.cs` and
    `SchemaServiceIntegrationTests.cs`, using `SchemaBatchTestSupport.cs` and existing
    test support. Coverage includes duplicate/alias fetch reuse, mixed object and
    query errors with successful siblings, count-before-deduplication, exact
    serialized limits with Unicode/escaping and catalog errors, whole omission,
    oversized first entries, metadata-only rejection, access denial, connection
    timeout, invalid inputs and cancellation. The real SQL fixture batch executed
    for a table, view, quoted alias and missing name; its table Markdown matched the
    equivalent scalar call. This service test does not claim MCP/SDK binding or
    practical exploration; those remain assigned to items 3 and 4.
  - Added `SchemaBatch.MaxInputCount=10` and `MaxResponseBytes=65536` to bound
    options and the main template, with positive-value startup validation and
    binding/default/template tests. Updated `docs/configuration.md` and
    `docs/architecture.md` for the internal boundary, exact budgets and enrichment.
  - Validation: `dotnet build SqlToAi.slnx` passed with zero warnings/errors.
    `dotnet test SqlToAi.slnx` passed; the final rerun with the quiet console logger
    passed 975 cases, with 19 existing TLS endpoint skips, zero failures, 994 total.
    These skips still require `SQLTOAI_TLS_TEST_PROFILE` and do not verify its
    certificate scenarios. `git diff --check` passed. No user configuration or
    database permissions were changed.
  - Actual delegated implementation setting: `gpt-6.1-sol`, reasoning effort
    `high`. Atomic commit reference: the direct child of `6dc3c5e` with subject
    `feat: Begrenze und komponiere Schema-Stapel` (this slice). Items 3–8 remain open.

- [x] **3. Expose scalar and batch schema calls consistently.**

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


  **Completion evidence (2026-10-16):**

  - Exposed `GetSchemaBatchAsync` through `ISchemaService` with its retained
    `SchemaBatchResponse`; the existing singleton DI registration therefore
    supplies both paths. `ToolDispatcher` preserves legacy scalar validation and
    Markdown for absent/null `object_names`, routes supplied arrays (including
    `[]`) through batch normalization, and returns the original measured JSON
    with `isError=!Complete`. Request failures keep catalog error responses.
    Schema detail tools and export behavior are unchanged; inventory remains 17.
  - Registry parameters now require only `database`, advertise nullable scalar,
    array and array-entry forms, and explain normalization, budgets, ordered
    results and failure/omission flags. SDK registration reuses the registry's
    description and exact input schema. Nullable JSON-element delegate arguments
    preserve shapes for validation rather than coercing invalid names. Review
    caught that `JsonElement=default` fails SDK schema creation; nullable defaults
    fix this and the registered invocation tests verify actual binding.
  - The generated CLI accepts `--object_names` as exactly one JSON array/null
    token, clones parsed JSON for dispatch, and reports invalid JSON/non-array
    tokens as parse errors. Explicit arity and token-count handling prevent
    missing/multiple tokens from throwing. Existing unrelated parsers retain their
    behavior. Updated `docs/tools.md` and `docs/development.md` with scalar, list,
    combined and empty-array examples, and removed stale internal-only batch
    integration wording from architecture/configuration docs.
  - Added 37 cases in `tests/SqlToAi.Tests/Mcp/SchemaToolSurfaceTests.cs` using the
    existing database test factory. They invoke the tools returned by
    `SqlMcpToolRegistrations.BuildToolCollection` through SDK `InvokeAsync` with
    JSON arguments and an in-memory server context; no running MCP transport or
    external database is needed. Coverage includes omitted/null arrays and scalar
    Markdown equality, one/multiple/combined/empty arrays, normalization and
    duplicates, invalid scalar/list/item types, empty selection, ordered mixed
    success/error siblings, full response-limit omission, count-before-dedup and
    denied access. Complete batches have `isError=false`; mixed/omitted and
    request-wide failures have `isError=true`. Registry/SDK schemas are compared
    including nullable item shapes. Native dispatcher string arrays also work.
    The documented CLI forms run through the actual parser and dispatcher into
    `SchemaService`; malformed/non-array, missing and multiple tokens are checked.
  - Validation: all 37 focused cases passed. `dotnet build SqlToAi.slnx` passed
    with zero warnings/errors. `dotnet test SqlToAi.slnx --logger
    'console;verbosity=quiet'` passed: 1011 successful, 19 existing TLS endpoint
    skips, zero failures, 1030 total. The skipped certificate scenarios still
    require `SQLTOAI_TLS_TEST_PROFILE`. `git diff --check` passed. This evidence
    proves binding/parser behavior, not the practical usability review or access
    through the maintainer's configuration; item 4 remains open. No user settings,
    database permissions or temporary exploration scenarios were changed.
  - Actual delegated implementation setting: `gpt-6.1-sol`, reasoning effort
    `medium`. Atomic commit reference: the direct child of `21edecc` with subject
    `feat: Integriere Schema-Stapel in MCP und CLI` (this slice). Items 4–8 remain
    open and were not implemented.

- [x] **4. Review schema parameter usability through real exploration.**

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

  **Pre-run schema expectations (2026-10-16; recorded before the first execution):**

  - `object_names` absent or null should preserve scalar Markdown. A supplied
    array, including one element or `[]`, selects batch mode; a null list with a
    scalar stays on the scalar path. Combined values appear scalar-first, then array
    order, with duplicates retained as separate ordered results. Empty/null/
    whitespace entries disappear; a wholly empty selection, wrong scalar/list/item
    types and 11 normalized entries should return identifiable `SQL-AI-0001` errors.
  - `dbo.FakeProjects` should yield equivalent primary Markdown in scalar and
    one-entry batch mode. `dbo.FakeAddresses` and
    `dbo.FakeAddressCommunications` form a realistic related-table pair and should
    each be identifiable in the ordered envelope. Duplicate aliases should keep
    separate entries. A mixed existing/missing/existing request should keep both
    successful schemas, label the missing object with a catalog error, and set
    `complete=false`/`isError=true`.
  - The 11-entry request should fail before schema retrieval. Every recorded call
    should have request arguments plus either all response blocks, joined text and
    `isError`, or a recorded dispatcher exception; actual outputs must be judged
    for usable object association and actionable omission/error information.
  - Registry input schema/description should advertise the accepted shapes, while
    item 3 SDK invocation tests provide binding evidence for the same JSON forms,
    since exploration dispatch bypasses SDK binding.

  **Initial blocked attempt (2026-10-16; superseded by resumed review below):**

  - The first real runner attempt is retained at
    `temp/exploration/ExploreSchemaParameters/20261016-203146-6723304/`.
    The 14-call scenario could not open DemoDB; all database-backed calls returned
    `SQL-AI-0105`, and the separate SQL diagnosis recorded `HAS_DBACCESS=0`. Its
    validation observations were inspected, but it did not establish practical
    schema usability. The temporary source from that attempt was removed; its
    retained artifact is historical and the resumed run below supplies the review.

  **Pre-run resumed review expectation (2026-10-16; before resumed execution):**

  - Run the 15-case matrix again with the production default batch budget. In a
    separate real-dispatcher run, temporarily set only the process option
    `SqlToAi__SchemaBatch__MaxResponseBytes=512` and request the two known tables.
    Their full schemas should exceed the budget while omission metadata should fit,
    yielding a retained JSON envelope with `complete=false`, `isError=true`, and
    identifiable `not_returned` entries whose reason is `response_limit`. If the
    metadata itself cannot fit or the schemas unexpectedly fit, record that actual
    behavior and do not claim an observed response-limit omission.
  - Compare scalar Markdown to the one-entry batch schema; verify both related
    tables and scalar-first combined ordering, including the `object_names:null`
    scalar-path distinction. Inspect each call's full response and text, then
    compare advertised schema/description and the recorded registered-SDK tests.

  **Completion evidence (2026-10-16):**

  - Before the resumed runs, recorded expectations for scalar/null preservation,
    supplied-array batch selection, order, duplicate aliases, related tables, mixed
    valid/missing siblings, invalid inputs, count limits and a separate response-byte
    limit case. Ran the temporary `ExploreSchemaParameters` scenario through the
    production dispatcher against DemoDB with the authorized task-specific process
    credentials; runner exit was 0. Artifact directory:
    `temp/exploration/ExploreSchemaParameters/20261016-204513-8535321/`.
  - Inspected `run.json` and all 15 request/response/text triples. Every call had
    exactly one text content block, matching `response.txt`, with the recorded
    `isError`; no `error.txt` exceptions occurred. Scalar call 01 returned primary
    Markdown. One-element batch call 02 returned `complete=true`, `isError=false`
    and one `ok` entry. Its schema matched the scalar after normalizing the allowed
    heading difference (`dbo.FakeProjects` versus `[dbo].[FakeProjects]`); columns,
    anonymization labels and discovery index were identical. Null `object_names`
    (call 06) preserved scalar Markdown, while `object_names:[]` with a valid scalar
    (call 05) selected batch and retained that scalar as its sole result. A null
    scalar plus a supplied valid array (call 07) selected batch.
  - Related-table batch call 03 returned `dbo.FakeAddresses` and
    `dbo.FakeAddressCommunications` as separately named `ok` entries with their
    complete primary schemas and discovery index counts. Combined call 04 placed
    the scalar before array entries and retained the repeated `dbo.FakeProjects`.
    Alias call 10 retained `dbo.FakeProjects` and `[dbo].[FakeProjects]` as separate
    entries with their original `object_name` values, plus the following address
    entry. Each complete response was a single JSON text block with all input/schema
    associations intact.
  - Mixed call 11 preserved the valid schemas on both sides of the missing object;
    that entry reported `SQL-AI-0103` and an actionable object-not-found message.
    The envelope was `complete=false`, `isError=true`, with ordered statuses
    `ok/error/ok`. Empty selections (08-09), legacy wrong scalar type (12), wrong
    list type (13), wrong item type (14), and 11 duplicate inputs (15) all returned
    `SQL-AI-0001` with one text block and `isError=true`. The 11-entry diagnostic
    explicitly states the limit applies before deduplication. The legacy scalar
    error retains its established required/non-empty wording.
  - The separate byte-budget run used only the process-level option override
    `SqlToAi__SchemaBatch__MaxResponseBytes=512`; no configuration file, credentials,
    permissions or trust settings were changed. Artifact directory:
    `temp/exploration/ExploreSchemaResponseLimit/20261016-204533-6165410/`.
    Its real two-table request returned a 201-byte JSON text block, with both input
    names identified as `not_returned`, reason `response_limit`, `complete=false`
    and `isError=true`. Thus the whole-schema omission is visible and the response
    remains within the configured budget.
  - Reviewed the advertised registry schema/description in
    `src/SqlToAi/Mcp/ToolRegistry.cs` against the calls and `docs/tools.md`: nullable
    scalar/list/item shapes, null-list scalar behavior, supplied-array batch mode,
    ordering, limits, statuses and separate detail calls match observed behavior.
    Item 3's `SchemaToolSurfaceTests` invoke the registered SDK tools with the same
    JSON shapes and compare SDK schema/description to the registry; its recorded
    run passed all 37 cases. This complements the dispatcher exploration, which
    does not perform SDK binding.
  - Actual review setting: `gpt-6-luna`, reasoning effort `high`. Runner results and
    artifact completeness were checked; `git diff --check` passed. The temporary
    scenario source was removed. No contract defect required code or test changes.
    **Usability verdict: usable.** Agents can retain scalar behavior, deliberately
    choose batch mode with an array, associate schemas and errors with each original
    input, and recover from count/response omissions using the advertised limits.
    No review finding remains open.
- [x] **5. Extend existing export composition for selected objects and outcomes.**

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

  **Completion evidence (2026-10-16):**

  - Added the selected scope and typed completed/failed/cancelled outcome to
    `SchemaExportService`, retaining its existing full-export `Result` adapter
    for the CLI. `ISchemaService.GetSelectedExportObjectsAsync` reuses
    `ResolveObjectsAsync` and database-aware SQL identifier resolution. The whole
    normalized selection resolves before mapping/output; any object error rejects
    it, and aliases/duplicates export once by object ID in first-input order.
    Selected mappings contain only selected objects, so unrelated path collisions
    do not reject the request and unselected references/trigger parents stay
    readable without links. No dependency expansion or batch limits were added.
  - Outcomes retain a successfully resolved output path, catalog failures or a
    cancellation reason, and truthful partial-output state. Failed/cancelled
    output retains child directories and files, including incomplete files; an
    empty root alone is false, while completed results always set it false.
    Sequential composition/writing stops at its first failure or cancellation.
    Token checks after awaited discovery/rendering catch downstream services that
    turn cancellation into a failed `Result`; the final check prevents completion
    after cancellation during the last file's disposal. Exclusive creation,
    existing unavailable-definition notes, offline presentation and the CLI's
    full-export/relative-path behavior remain covered by regression tests.
  - Added 30 cases in `SchemaExportOutcomeTests`, reusing the existing export
    proxy, database catalog fixture and temporary-directory helper. Coverage
    includes all seven object kinds and both trigger-parent kinds, scalar/list
    normalization, aliases, invalid/ambiguous/unsupported selections before writes,
    denied/excluded access, empty full catalogs, unselected offline references,
    16 selected entries without the batch count limit, and actual relative CLI
    invocation/exit codes. Injected stream failures/cancellation retain a genuinely
    incomplete README; child-directory-only and empty-root cases are distinct.
    Tests cover failures/cancellation before/after writes, empty-root retry,
    cancellation returned as a service failure and cancellation after the final
    file closes. A narrow stream factory supports deterministic filesystem faults
    without replacing production exclusive file creation. Architecture and
    development documentation describe the shared behavior and current exposure.
  - Validation: `dotnet test SqlToAi.slnx --filter
    'FullyQualifiedName~SchemaExport' --logger 'console;verbosity=quiet'` passed
    all 65 cases. `dotnet build SqlToAi.slnx` passed with zero warnings/errors.
    `dotnet test SqlToAi.slnx --logger 'console;verbosity=quiet'` passed: 1041
    successful, 19 existing TLS endpoint skips, zero failures, 1060 total.
    The skipped certificate scenarios still require `SQLTOAI_TLS_TEST_PROFILE`;
    they are not verified here. `git diff --check` passed. No user configuration,
    database permissions, credentials or practical exploration artifacts changed.
  - Actual delegated implementation setting: `gpt-6.1-sol`, reasoning effort
    `high`. Atomic commit reference: the direct child of `b49fdb4` with subject
    `feat: Erweitere den Schemaexport um Auswahl und Ergebnisse` (this slice).
    Items 6–8 remain open; MCP registration, MCP-only path validation and practical
    export exploration were not implemented or claimed by this item.

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
