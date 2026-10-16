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

- [x] **6. Expose the schema export tool and update its consumers.**

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

  **Completion evidence (2026-10-16):**

  - Registered and dispatched `sql_export_schema` through the existing singleton
    export coordinator, SDK collection and registry-generated query CLI. Required
    database/scope/output_path and nullable scalar/list selections share the
    canonical registry schema and description. The SDK binds raw nullable JSON
    internally so missing/null/wrong-type arguments reach the same validation and
    compact catalog JSON response as dispatcher calls. Neither missing scope nor
    empty selected input can activate full export. The original `export-schema`
    command remains unchanged, including its relative-path/full-export behavior.
  - Added MCP-only Windows drive-rooted/UNC qualification, rejecting relative,
    drive/root-relative, URL and device-namespace paths after slash normalization.
    Resolved valid destinations survive later argument failures. Existing core
    empty/absent-directory checks, exclusive writes and access checks are reused.
    Responses contain exact snake_case status/path/partial_output/error/reason
    fields, omit absent optional fields, return no documents and mark failures or
    cancellation as MCP errors. Persistent server filesystem output, large dumps,
    substantial time, filesystem access, scopes, partial output and retry behavior
    are prominent in the tool description and canonical docs.
  - Added 61 cases in `tests/SqlToAi.Tests/Mcp/ExportToolSurfaceTests.cs`, including
    actual registered SDK invocation plus dispatcher and query CLI coverage for
    scalar/list/combined selection, duplicate aliases, harmless empty full inputs,
    invalid selections, missing/invalid scope and required-field JSON shapes,
    valid/rejected Windows paths, existing files/child directories/empty roots,
    schema-only selection, denied access, completed/failed/cancelled outcomes and
    partial output. Tests reuse catalog/connection and export filesystem fakes;
    UNC qualification does not require or claim a reachable share. Injected writes
    verify retained child directories; item 5 already covers incomplete files.
  - Review corrected two boundary findings: preserve a resolved destination on
    later parameter failures, and use raw nullable SDK arguments while retaining
    required canonical schema so SDK binding cannot replace the compact failure
    contract. Matching SDK/registry schema and description are asserted.
  - Updated tool/development/security/architecture/exploration docs and current
    registry/model/package-smoke inventories to 18. The permanent exploration
    baseline explicitly covers the original 17 tools; no scenario was changed,
    no packaged smoke was forced, and practical export review remains item 7.
    No user settings, permissions, credentials or temporary scenarios changed.
  - Validation: focused export-surface tests passed; final
    `dotnet build SqlToAi.slnx` passed with zero warnings/errors.
    `dotnet test SqlToAi.slnx --logger 'console;verbosity=quiet'` passed: 1102
    successful, 19 existing TLS endpoint skips, zero failures, 1121 total.
    Certificate cases still require `SQLTOAI_TLS_TEST_PROFILE` and remain unverified
    by this run. `git diff --check` passed. Actual delegated setting:
    `gpt-6.1-sol`, reasoning effort `medium`. Atomic commit reference: direct child
    of `183b7dd` with subject `feat: Stelle den Schemaexport über MCP bereit`
    (this slice). Items 7–8 remain open and were not implemented.

- [x] **7. Review export calls through real exploration.**

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

  **Pre-run expectations (2026-10-16):** Selected scalar, list and combined
  scalar/list requests should return `completed`, `partial_output=false` and the
  exact fully qualified destination; generated files should cover only resolved
  objects, deduplicating aliases. Full scope should work at an existing empty
  directory and report its actual path. Empty or invalid selections, unknown
  objects, wrong selection types, relative paths, an existing file, a non-empty
  directory, omitted scope and a selection supplied with `all` should return
  `isError=true` with a failed compact JSON outcome and no unwanted writes.
  Failure metadata should identify the invalid input through an actionable catalog
  error and preserve `partial_output=false` when the destination remains absent or
  empty. The registered description should prominently warn about persistent
  server-filesystem output, potentially large/many-file/slow dumps, deliberate
  use, required server filesystem access, destination constraints and retained
  partial output/retry behavior. These are expectations only until the recorded
  dispatcher run is inspected.

  **Completion evidence (2026-10-16):**

  - Invoked the temporary `ExploreSchemaExportReview` scenario with the documented
    PowerShell runner override and configured `DemoDB` (`ReadWrite`) using the real
    dispatcher and production service registrations. The retained runs are
    `temp/exploration/ExploreSchemaExportReview/20261016-210037-8028868/` and the
    final complete run `temp/exploration/ExploreSchemaExportReview/20261016-210055-2407085/`.
    The second run completed technically with exit 0 and no `error.txt`; all 14
    calls have a `request.json`, a `response.json` with every content block and
    `isError`, and a matching `response.txt`. The run manifest records the
    configuration directory, DemoDB access and timeout without credentials.
  - Expected selected scalar, list and combined requests to create UTF-8 Markdown
    under the supplied path, with exact returned path and duplicate resolution;
    expected full scope to populate absent/empty roots; expected invalid requests
    to fail with no unrequested output and actionable catalog errors. Observed
    selected scalar at `outputs/selected-scalar/` with `README.md` and only
    `tables/dbo.FakeProjects.md`; list scope at `outputs/selected-list/` with
    `FakeProjects` and `FakeAddresses`; and combined scalar/list at
    `outputs/selected-combined/` with those same two files despite the
    repeated `FakeProjects`, null and whitespace inputs. All returned `completed`,
    `partial_output=false`, `isError=false`, and the exact destination path.
  - Full scope at the pre-created empty `outputs/all-empty/` root returned
    the exact path and created `README.md`, nine table documents, one view and one
    procedure. Full scope with scalar whitespace and null/empty/whitespace array
    entries also completed at `outputs/all-empty-selection/`, confirming those
    normalized empty selections are harmless. Representative `README.md` files
    link to the actual selected/full documents; selected export excludes unrelated
    objects and deduplicates the repeated selection.
  - The eight invalid cases returned one compact JSON text block each with
    `isError=true`, `status=failed` and specific errors: empty selected list
    (`SQL-AI-0001`, at least one name required), valid-plus-missing object
    (`SQL-AI-0103`, missing name identified), numeric list entry (`SQL-AI-0001`,
    string-or-null required), relative path (`SQL-AI-0001`, fully qualified
    Windows path required, no resolved output path), existing file and non-empty
    directory (`SQL-AI-0001`, absent-or-empty requirement), missing scope
    (`SQL-AI-0001`, required argument), and selection with `all` (`SQL-AI-0001`,
    selection rejected). The existing file content stayed `preserve`; invalid
    absent roots stayed absent. The non-empty root stayed intact and reported
    `partial_output=true` because its child directory remained, consistent with
    the documented remaining-content definition. The second run also confirmed an
    invalid scope value returns `SQL-AI-0001`, while `all` with empty normalized
    selection succeeds. No call threw; each observed error was in the JSON result.
  - Inspected the registered description in `src/SqlToAi/Mcp/ToolRegistry.cs`:
    its opening sentences explicitly say the export persistently writes to the
    server filesystem, may create a large dump with many files and take substantial
    time, requires agent filesystem access, and should be called deliberately. It
    also explains scope, selection, absolute-path and empty-root rules, status,
    partial output, retained files and retry. Tool docs agree with these observed
    cases. No usability or contract defect was found; failures identify the
    correction required, and returned paths match the files actually written.
  - **Usability verdict: usable for deliberate agent-directed schema export.**
    The selection forms are straightforward, normalization/deduplication is
    visible in the resulting files, full scope is explicit, and each outcome is
    easy to associate with its destination. Invalid input errors provide a
    corrective next step. No product finding required a fix or rerun. Removed the
    temporary scenario source; retained only runner observations and generated
    output under the git-ignored `temp/exploration/` tree. No UNC destination was
    exercised, as deterministic path validation is covered by unit tests and the
    task excludes inventing inaccessible destinations.
  - Actual delegated setting: `gpt-6-luna`, reasoning effort `high`. This review
    changed only this roadmap evidence. Atomic commit reference: `9600838`
    (`docs: Prüfe Schemaexport über MCP`).

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

  **Independent audit evidence (2026-10-16; correction required):**

  - Audited the ready concept's complete Verification section against the final
    source/test/documentation changes from `11957de` through `9600838`, items 1–7
    and their retained practical evidence. Actual delegated audit setting:
    `gpt-6.1-sol`, reasoning effort `high`. Production code, tests and canonical
    documentation remained read-only. The audit found one unresolved contract
    defect below; item 8 remains open until the single permitted correction and
    focused verification have completed.
  - Confirmed scalar-versus-array routing, scalar-first tolerant normalization,
    incorrect-type rejection, ordered duplicate associations, SQL-quoted and
    database-catalog-aware resolution, ambiguity/unsupported-object errors and
    fetch reuse by object ID. Reviewed the access checks before discovery and
    writes, default-deny/SchemaOnly tests, native/custom metadata identity handling
    and explicit masked/unmasked enrichment assertions. Real SQL fixture tests
    executed, while separate identifier collations remain represented by focused
    fakes rather than claims of additional configured databases.
  - Inspected exact UTF-8 serialization, metadata reservation before database
    work, count-before-deduplication, whole-schema/error omission, oversized first
    schemas, mixed successful siblings and cancellation. Sampled the exact-byte
    boundary and Unicode/error assertions, real registered-SDK `InvokeAsync`
    binding, matching advertised schemas/descriptions, and actual CLI parser
    invocations, including malformed/missing/multiple array tokens.
  - Checked selected/all export resolution before writes, supported kinds,
    deduplication before path mapping, no dependency expansion, empty full catalogs,
    offline references and excluded enrichment. Reviewed drive/UNC qualification,
    invalid destinations, exclusive creation, preserved relative CLI behavior,
    service/write failures, incomplete files, cancellation after awaited operations
    and final disposal, retained output and empty-root retry tests. UNC reachability
    and new packaged smoke execution were not claimed; qualification and the
    updated 18-tool package inventory were inspected and tested at their scope.
  - Independently inspected all 15 recorded schema call shapes and the separate
    response-limit call in the resumed runs named under item 4, plus all 14 calls
    in the final export run named under item 7. Every request had one response
    content block, matching `response.txt`, the expected `isError`, and no
    `error.txt`. Sampled complete schema bodies, mixed errors and diagnostics;
    scalar/batch bodies agree apart from their qualified heading, null-array
    scalar output is identical, and the limit envelope is exactly 201 UTF-8 bytes
    with both inputs identified as `not_returned`/`response_limit`.
  - Sampled selected scalar/list/combined and full exported files, README indexes,
    the offline foreign-key presentation and routine definition. File sets match
    selections, aliases do not add files, unexported references remain readable,
    and full export contains nine tables, one view and one procedure. All 43
    relative document links across the retained output resolve. The existing
    file still contains `preserve`; invalid absent destinations remain absent.
    These observations support the practical usability verdicts in items 4 and 7,
    subject to the additional early-failure metadata defect identified here.
  - Canonical tool/configuration/development/architecture/security/exploration
    documentation agrees with implemented boundaries, examples and options,
    except for that defect. Defaults remain 10 inputs and 65,536 UTF-8 bytes in
    options and the template. Current registry/SDK/model/package consumers agree
    on 18 tools; the permanent exploration baseline accurately retains its
    original 17-tool coverage. Checked all 94 repository-local links and heading
    fragments in the six changed canonical pages and both task artifacts; none
    is missing. The README's introduction/setup/examples need no change.
  - **Open finding (P2): early export parameter failures report incorrect retained
    output.** `src/SqlToAi/Mcp/ToolDispatcher.cs:327–329` constructs every caught
    argument failure with `partial_output=false`, even after `output_path` was
    resolved to a non-empty directory. Reproduced through the actual query CLI:
    `dotnet src/SqlToAi/bin/Debug/net10.0/SqlToAi.dll query sql_export_schema
    --database DemoDB --scope invalid --output_path
    C:\Daten\Entwicklung\Ralf\SqlToAi\temp\exploration\ExploreSchemaExportReview\20261016-210055-2407085\outputs\selected-scalar`.
    It exits 1 with `status=failed`, the resolved path and `partial_output=false`,
    although that directory retains `README.md` and its `tables` child. Missing
    scope/database has the same catch path. The concept defines remaining files
    or child directories as partial output for failed outcomes, and the core
    coordinator already reports a non-empty destination as true. Reuse consistent
    retained-output inspection for these boundary failures and cover absent,
    empty and non-empty destinations through SDK/dispatcher entry points. No
    writes or database work occurred in this reproduction.
  - Final `dotnet build SqlToAi.slnx` passed with zero warnings/errors;
    `dotnet test SqlToAi.slnx --logger 'console;verbosity=quiet'` passed 1102 tests,
    with 19 existing TLS endpoint skips, zero failures, 1121 total. Those
    certificate scenarios still require `SQLTOAI_TLS_TEST_PROFILE` and remain
    unverified. The earlier blocked schema run is historical only; successful
    resumed artifacts supply practical evidence. No settings, permissions or
    credentials were changed by the audit. Temporary scenario source and the
    parent-owned credential wrapper are absent. The initial working tree was
    clean; the focused tracked diff contains no new credential assignments and
    only the four intended batch-option template lines. `git diff --check` passed.
    This audit commit changes only this roadmap; no user changes were touched.

- [ ] **9. Correct retained-output metadata for early export parameter failures.**

  **Intention:** Make `partial_output` truthful when MCP argument validation fails
  after resolving a valid destination, fulfilling the open finding from item 8.

  **Scope:** This is the single permitted audit correction, delegated to the
  implementation role. Reuse shared remaining-output inspection at the dispatcher's
  early failure boundary. For a valid resolved directory, remaining files or child
  directories mean true; absent/empty roots, file destinations and invalid paths
  mean false. Preserve compact catalog JSON, resolved path metadata and error flags.
  Resolve the existing audit finding without creating output or changing content
  during validation. Record implementation and focused verification here.

  **Exclusions:** No new export features, automatic cleanup, overwrite behavior,
  broader audit loop, release/publish or changes to database/configuration policy.

  **Acceptance:** Registered SDK, dispatcher and query CLI tests exercise invalid
  or missing scope and missing database against non-empty, empty and absent
  destinations. Cover files/child directories, preserve existing contents, and
  retain correct catalog errors, MCP/CLI error state and valid resolved paths.
  File and invalid-path failures remain false. Full repository build/tests pass
  with genuine environment limits recorded. Focused post-correction verification
  replays item 8's reproduced CLI failure against the retained selected output
  and checks affected entry points and empty/absent cases. Close items 8 and 9
  only after this finding and the already documented audit acceptance are
  demonstrably satisfied; no repeated general audit/correction loop is required.
