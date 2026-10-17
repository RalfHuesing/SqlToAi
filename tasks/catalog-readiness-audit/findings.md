# Catalog readiness audit

## Reassessment and remediation — 2026-10-17

This section supersedes the original snapshot's release verdict below. The
reassessment started at `0250868` (version **1.0.33**, clean working tree).
F01, F02, F03 and F05 were still present; the claimed F04 and F08 fixes were
confirmed against the current implementation. Remediation is committed as
`746c5b2` (execution controls) and `bdad267` (masking and comparison parameters).
The original audit remains below as historical evidence, not a description of
the corrected source.

### Current decision

**The demonstrated F01–F05 blockers are addressed for the documented execution
contract.** Proceed to release preparation with the residual limitations below
explicitly retained. This is a product-readiness recommendation, not Registry
acceptance, namespace verification or authorization to publish. No release,
push, version bump or catalog submission was performed during this work.

| Finding | Current status | Risk and implemented decision |
| --- | --- | --- |
| F01 | Fixed in `bdad267` | An alias could expose protected strings. Central exclusions now require resolved base schema, table and column and use the base column instead of the output alias. Incomplete/unavailable origins keep masking or tokenization active; genuine source exemptions still apply through aliases. |
| F02 | Fixed in `746c5b2` | A validation call could reactivate execution. AST preflight rejects caller `NOEXEC`, `PARSEONLY` and `FMTONLY` settings, including nested and combined settings, before opening the validation connection. Normal permitted SELECT/DML validation remains supported. Live metadata now describes NOEXEC and deferred name resolution. |
| F03 | Direct controls fixed; opaque-code limit documented in `746c5b2` | An early COMMIT could persist writes before a later failure. All batches are preflighted before execution; server-managed transactions reject explicit BEGIN/COMMIT/ROLLBACK/SAVE and IMPLICIT_TRANSACTIONS settings, including nested controls. ReadWrite autocommit remains explicit. |
| F04 | Previously fixed in `588608b`; confirmed | The effective clamp is passed through to both difference readers; zero/negative requests retain at least one difference sample, and the configured maximum is enforced. |
| F05 | Fixed by rejection in `bdad267` | Independent same-named parameters could cause false equivalence. Their normalized names are checked case-insensitively before safety/connection work; optional `@` prefixes do not evade detection. Overlap returns `SQL-AI-0001`, even for equal values. Distinct names and one shared fallback set remain supported. Benchmark propagates rejection before measurement. |
| F08 | Previously fixed in `bb4d750`; confirmed | Comparison schema/count/difference commands and performance commands now use the configured command timeout. |

F03 does **not** establish unconditional atomicity for procedures, dynamic SQL
or triggers. Such code can change transactions outside the submitted AST;
post-execution transaction-count checks cannot undo an earlier commit or detect
every restored count. These paths remain supported in ReadWrite. The security
contract, both tool-description surfaces and the standalone report label now
state the server-managed-transaction scope. A stronger guarantee would require
additional restrictions on opaque execution; it is not claimed by this fix.
See the [security contract](../../docs/security.md#query-guards-and-transactions)
and [tool contracts](../../docs/tools.md).

### Remaining P2 decisions

These findings remain open. They have no demonstrated mandatory catalog-format
blocker, but materially affect agent correctness. Prioritize F06/F07/F09 as
follow-up fixes; do not infer that catalog acceptance validates their behavior.

| Finding | Current evidence | Consequence and smallest follow-up |
| --- | --- | --- |
| F06 | Query/script serialization in [QueryExecutionService.Anonymization.cs](../../src/SqlToAi/Database/QueryExecutionService.Anonymization.cs) and comparison samples in [QueryComparisonService.cs](../../src/SqlToAi/Database/QueryComparisonService.cs) still use case-insensitive dictionaries keyed by output name. | Duplicate names silently lose values. Reject ambiguous names and ask for unique SQL aliases; unique aliases are the current workaround. |
| F07 | [SqlParameterBinder.cs](../../src/SqlToAi/Database/SqlParameterBinder.cs) still infers a JSON value before applying explicit DbType. | Date/Guid-looking identifiers can change even with an explicit string type. Resolve explicit types before inference and preserve exact string values. The F05 enumeration refactor does not fix this conversion. |
| F09 | [QueryExecutionService.cs](../../src/SqlToAi/Database/QueryExecutionService.cs) still only upper-clamps requested row limits. | Zero can execute SQL and return successful empty data. Reject non-positive requests before execution; use positive limits until corrected. |
| F10 | Search in [SchemaService.cs](../../src/SqlToAi/Database/SchemaService.cs) and query output in [ToolDispatcher.cs](../../src/SqlToAi/Mcp/ToolDispatcher.cs) still omit effective-limit/saturation information. | Agents can mistake partial data for complete evidence. Report effective bounds and a conservative limit-reached notice; narrow searches or count explicitly meanwhile. Saturation alone does not prove truncation. |
| F11 | [McpConstants.cs](../../src/SqlToAi/Mcp/McpConstants.cs) and [SqlMcpToolRegistrations.cs](../../src/SqlToAi/Mcp/SqlMcpToolRegistrations.cs) still imply outgoing lineage from the incoming-reference tool. | Agents can investigate dependencies in the wrong direction. Correct live guidance to incoming references, use available definitions for source investigation and acknowledge unresolved column lineage. Canonical tools documentation already describes the incoming direction correctly. |

### Verification and orchestration

C# declarations were inspected with AiNetCodeNavigator, followed by bounded
source reads. P2 conclusions are source verification, not newly executed live
reproductions. Actual delegation used GPT-6.1-Sol / High for F02/F03 and an
independent F01/F05 review, GPT-6-Luna / High for F01/F05 and F04 verification,
and GPT-6-Luna / High for P2 verification and an independent F02/F03 review.
Implementation writers ran sequentially in the shared checkout; read-only
reviews ran independently. Both reviewed slices had no remaining P1/P2 finding
within their assigned change scope.

- F02/F03: **103 focused tests passed**, including live fictional-fixture
  validation/script integration, metadata parity and report rendering. The
  early-COMMIT regression verifies preflight rejection and no fictional marker
  persisted; a separate valid runtime-error case still exercises rollback.
- F01/F05: **66 focused tests passed**, including both alias directions,
  incomplete/unavailable origin, tokenization, the real shared script executor,
  dictionary/JSON/nested JSON-string containers, parameter name/type collisions,
  shared fallback, malformed/disposed JSON error mapping, existing binder
  behavior and benchmark error propagation.
- Final `dotnet build SqlToAi.slnx`: **exit 0, 0 warnings, 0 errors**.
- Final `dotnet test SqlToAi.slnx`: **exit 0; 1,247 passed, 19 skipped,
  1,266 total, 0 failed**. The skipped cases are dedicated TLS tests without a
  configured endpoint profile; they remain unverified. Ordinary SQL integration
  uses the existing explicit fictional-fixture trust overlay and does not prove
  secure-default certificate identity validation.
- `git diff --check`: clean. Personal settings were not read or changed.

### Publication handoff

No new packaging-format defect was established in this bounded reassessment.
[package.ps1](../../scripts/package.ps1) already derives
`io.github.ralfhuesing/SqlToAi`, the versioned release download URL and the final
MCPB hash into `server.json`; do not put a speculative hash in the template.
The current official [MCPB publication requirements](https://github.com/modelcontextprotocol/registry/blob/main/docs/modelcontextprotocol-io/package-types.mdx)
and [GitHub authentication rules](https://github.com/modelcontextprotocol/registry/blob/main/docs/modelcontextprotocol-io/authentication.mdx)
were checked on 2026-10-17: MCPB metadata needs the release URL and SHA-256, and
the GitHub namespace depends on successful publisher authentication. The
briefing's verified-namespace claim was not independently established here.

Follow the existing [release and Registry procedure](../../docs/development.md#prepare-registry-metadata-locally)
for the eventual release: review residual P2/TLS evidence, validate the final
package, publish the checked release deliberately, compare its publicly served
MCPB bytes with `server.json`, then authenticate and submit the exact metadata.
Installation, ownership, official acceptance and inclusion in GitHub/community
catalogs remain unverified; local source/build tests do not establish them.

## Audit basis and verdict

Audited on **2026-10-16 (Europe/Berlin)**, against commit
`0d4352704f991d4a0e43588f10827248e1e9c055`, project version **1.0.32**.
The initial working tree was clean. The only repository change made for this
audit is this report. No fixes, deployment, publication or infrastructure changes
were performed; personal SqlToAi settings were not read or modified.

**Verdict: withhold catalog submission and distribution of this snapshot until
F01–F05 are resolved.** These are concrete security/correctness blockers for
consumers of the intended GitHub Releases, MCPB and official Registry routes.
They are product readiness findings, not claims that a catalog automatically
rejects these behaviors. No separate mandatory Registry/MCPB metadata or package
format blocker was demonstrated. Namespace verification, public release assets,
actual client installation and accepted Registry publication remain unverified.
This is a bounded audit, not certification or a comprehensive security guarantee.

### Environment and assignments

Windows x64, PowerShell 7, .NET SDK **10.0.400**, MSBuild **18.9.6**,
host/runtime **10.0.11**, target `net10.0`. Resolved production dependencies include
`ModelContextProtocol`/`ModelContextProtocol.Core` **2.2.0**,
`Microsoft.Data.SqlClient` **7.0.2**, SNI runtime **6.0.2**, Dapper **2.1.79**,
ScriptDom **170.3.0**, `System.Security.Cryptography.Pkcs` **10.0.13** and
`Microsoft.Bcl.Cryptography` **9.0.13**. Packaging uses PowerShell/.NET ZIP APIs
and `dotnet publish`; no Node/npm MCPB dependency is required. Versions were
checked in project/package metadata and the resolved package listing.

The root reviewer consolidated findings, ran shared validation and comparison
probes, and checked cross-cutting evidence. Actual delegated assignments:

| Agent | Model / reasoning | Bounded responsibility |
| --- | --- | --- |
| `security` | GPT-6.1-Sol / High | Access, masking, SQL guards, transactions, TLS and consequential runtime behavior |
| `contracts` | GPT-6.1-Sol / Medium | MCP wire contracts, agent scenarios, representation and relevant documentation |
| `packages` | GPT-6-Luna / High | Packages, release handoff, catalog metadata and distribution documentation |

Subagents did not repeat the full test suite. C# declarations were discovered
with AiNetCodeNavigator before focused reads of known source ranges. Static
relationships, fake-provider execution, real stdio and live SQL evidence are
distinguished below.

### Standards and primary sources

All online sources below were accessed **2026-10-16**. Released versions were
used as requirements; drafts and optional features were not treated as blockers.

| Source | Resolved basis and audit use |
| --- | --- |
| [MCP latest](https://modelcontextprotocol.io/specification/latest) | Resolves to released **2026-07-28**. [Versioning](https://modelcontextprotocol.io/specification/2026-07-28/basic/versioning) and [stdio binding](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/stdio) distinguish modern per-request metadata/discovery from legacy initialization. |
| [MCP 2025-11-25 tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools), [lifecycle](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle), [transport](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports) | Applicable to the tested legacy handshake. Checked tool capability, schemas, tool-versus-protocol errors, stdio framing and shutdown. Optional annotations/structured output are not mandatory. |
| [Official C# SDK v2.2.0](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.2.0) | Pinned release, commit `6fa3825`; release describes support for both 2025-11-25 and 2026-07-28. Runtime probes below establish the application's actual behavior. |
| [MCPB manifest](https://github.com/modelcontextprotocol/mcpb/blob/70fe3b34cd6dff1b3bba046638edc72a6467a4fb/MANIFEST.md), [v0.3 schema](https://github.com/modelcontextprotocol/mcpb/blob/70fe3b34cd6dff1b3bba046638edc72a6467a4fb/schemas/mcpb-manifest-v0.3.schema.json) | Current checked revision `70fe3b34cd6dff1b3bba046638edc72a6467a4fb`, manifest v0.3; repository pins this schema. |
| [Registry](https://github.com/modelcontextprotocol/registry), [server schema 2025-12-11](https://static.modelcontextprotocol.io/schemas/2025-12-11/server.schema.json) | Checked `main` revision `11b7b0e274d518b6f449c5ee3518a6151d9848b9`; latest dated released schema is 2025-12-11, newer draft changes are unreleased. Checked MCPB asset URL/hash rules and intended GitHub namespace. Schema validity does not establish ownership or publication. |
| [Microsoft NOEXEC](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-noexec-transact-sql?view=sql-server-ver17) | ON compiles without execution; OFF permits execution. Supports F02's observed distinction. |
| [SqlClient command timeout](https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlcommand.commandtimeout?view=sqlclient-dotnet-core-7.0), [encryption/certificates](https://learn.microsoft.com/en-us/sql/connect/ado-net/encryption-and-certificate-validation?view=sql-server-ver17) | Default command timeout is 30 seconds; endpoint trust bypass and required encryption are distinct. Used with source inspection, not as transport proof. |
| [NuGet audit/advisory sources](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages), [.NET versioning](https://learn.microsoft.com/en-us/dotnet/core/versions/) | Interpreted the actual resolved dependency audit and SDK/runtime evidence. Advisory coverage is limited to the source's available data at audit time. |

### Observed MCP surface

The configured server exposes **stdio only**, with no HTTP listener. Safe real
stdio probes used the existing startup harness with an owned temporary settings
root, deny-all lists, disabled file logging/metadata and inherited `SqlToAi__*`
overrides removed. No packaged executable or normal user profile was used.

- Legacy initialization succeeded for **2024-11-05, 2025-03-26, 2025-06-18 and
  2025-11-25**.
- Modern `server/discover` succeeded with per-request metadata for **2026-07-28**;
  discovery reported that modern version. Modern calls returned
  `resultType: "complete"` and server version `1.0.32.0`.
- Both eras advertised `logging: {}` and `tools: { listChanged: true }`, with no
  prompts/resources. Tool inventory was **17** in both eras; tools/list responses
  measured **9,615 / 9,760 UTF-8 bytes** respectively. These measurements alone do
  not establish excessive output.
- No tool annotations or output schemas were declared; results use text blocks.
  This absence is not a standards violation. Runtime execution descriptions do
  warn that ReadWrite query calls can persist changes.
- Observed stdout contained only parseable newline-delimited JSON-RPC; probe
  stderr was empty. EOF shut down every probe with exit 0 within five seconds.
- Unknown tools returned JSON-RPC `-32602`; a denied execution returned
  `SQL-AI-0104` with `isError: true`. Extra unknown arguments were accepted/ignored,
  consistent with permissive schemas. SDK binding failures were tool errors but
  gave generic messages before the application error mapper.

An `initialize` request for 2026-07-28 returned `-32022`: this revision uses
modern discovery rather than the legacy handshake. Successful modern probes
prevent misclassifying that response as lack of modern protocol support.
Neither older negotiated support nor absent optional MCP features is a finding.

Inventory: `sql_list_databases`, `sql_search_databases`, `sql_search_objects`,
`sql_validate_query`, `sql_get_schema`, `sql_get_schema_foreign_keys`,
`sql_get_schema_indexes`, `sql_get_schema_constraints`,
`sql_get_trigger_definition`, `sql_get_object_references`,
`sql_get_routine_parameters`, `sql_execute_query`, `sql_execute_file`,
`sql_compare_queries`, `sql_measure_performance`, `sql_benchmark_optimization`,
`sql_suggest_indexes`.

## Finding summary

| ID | Priority | Confidence | Area | Consequence |
| --- | --- | --- | --- | --- |
| F01 | P1 | High | Masking | An output alias can exempt a protected source column. |
| F02 | P1 | High; live non-destructive proof | Validation | A validation call can execute supplied SQL. |
| F03 | P1 | High; static trace | Atomic scripts | Caller COMMIT can persist changes before integrity detection. |
| F04 | P1 | High; fake-provider execution | Comparison bounds | Zero samples can produce false equality; maximum cap is ignored. |
| F05 | P1 | High; live read-only proof | Comparison parameters | Same-named per-query parameters collapse into one value, producing false equality. |
| F06 | P2 | High | Serialization | Duplicate column names silently lose result values. |
| F07 | P2 | High | Typed parameters | Explicit string values are changed by date/Guid inference. |
| F08 | P2 | High | Operational limits | Comparison/performance ignore configured command timeout. |
| F09 | P2 | High | Query limit validation | A zero row limit runs SQL but reports successful empty data. |
| F10 | P2 | High | Bounded evidence | Search/query output does not disclose saturation/effective limit. |
| F11 | P2 | High; static trace | Agent instructions | Source-tracing guidance follows dependencies in the wrong direction. |

## Confirmed findings

### F01 — Aliases bypass central column masking

**P1 / high confidence.** Affects query execution and script batches through the
shared executor registered in [Program.cs](../../src/SqlToAi/Program.cs), lines
161–164. [QueryExecutionService.Anonymization.cs](../../src/SqlToAi/Database/QueryExecutionService.Anonymization.cs):85–88
resolves the base column but passes the output alias to `IsExcludedAsync`.
Lines 124–126 return raw values for exclusions; the downstream anonymizer does
not correct the policy decision.

Expected: exemptions apply to the source column described by the central policy
in [security.md](../../docs/security.md), lines 49–53. Executed synthetic proof:
the real query service and guards, a reader with base `dbo.FakeConsultants.SSN`
and alias `FullName`, and a provider exempting `FullName`, returned
`{"FullName":"synthetic-secret-123"}` for
`SELECT SSN AS FullName FROM dbo.FakeConsultants`. The provider received the alias.
This ordinary SELECT exposes a protected value when a different column is exempt.

**Recommendation:** match resolved base schema/table/column; keep aliases for
display. Preserve default masking when origin cannot be resolved. **Effort:
small.** Validate protected-to-exempt and exempt-to-other aliases, unresolved
origins, query/script paths and tokenization. Existing always-excluding mocks do
not establish alias safety. **Submission blocker: yes**, because configured
masking can be bypassed without changing access grants.

### F02 — Validation can turn execution back on

**P1 / high confidence.** [QueryValidationService.cs](../../src/SqlToAi/Database/QueryValidationService.cs):102–115
sets `NOEXEC ON` and then submits caller SQL.
[SqlMultiStatementDetector.cs](../../src/SqlToAi/Database/SqlMultiStatementDetector.cs):50–57
accepts SET as preamble; [QuerySafetyValidator.cs](../../src/SqlToAi/Database/QuerySafetyValidator.cs):139–140
skips mutation filtering for ReadWrite. Thus `SET NOEXEC OFF; <one main statement>`
reaches SQL Server. [tools.md](../../docs/tools.md):11 promises validation without
execution; live registration at
[SqlMcpToolRegistrations.cs](../../src/SqlToAi/Mcp/SqlMcpToolRegistrations.cs):152
also promises no execution and incorrectly names PARSEONLY.

Executed against only the explicit fictional DemoDB fixture, with a five-second
bound: `THROW 51000,'fictional-validation-executed',1;` validated successfully;
prefixing `SET NOEXEC OFF;` returned `SQL-AI-0102` containing that fictional
marker. This proves execution. No writes or COMMIT were executed. The
[Microsoft NOEXEC contract](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-noexec-transact-sql?view=sql-server-ver17)
explains why the prefix changes behavior. Under ReadWrite, executable SQL can
have cost/side effects inappropriate for a validation-only call; rollback alone
cannot protect against all executable code.

**Recommendation:** reject caller execution-control statements capable of
changing the validation mode before submission, retaining rollback as defense
in depth. Correct live metadata to NOEXEC and its deferred-resolution limits.
**Effort: small/medium.** Validate that the prefixed THROW is rejected before
execution, with normal SELECT/DML validation preserved. **Submission blocker:
yes**, because the validation contract unexpectedly permits execution.

### F03 — Atomic script protection detects COMMIT too late

**P1 / high confidence from static trace; persistence probe intentionally not
executed.** [security.md](../../docs/security.md):31 and
[SqlMcpToolRegistrations.cs](../../src/SqlToAi/Mcp/SqlMcpToolRegistrations.cs):189
promise an atomic transaction for `use_transaction=true`.
[ScriptExecutionService.cs](../../src/SqlToAi/Database/ScriptExecutionService.cs):160–168
preflights with batch safety; [QuerySafetyValidator.cs](../../src/SqlToAi/Database/QuerySafetyValidator.cs):90,
139–148 permits multiple statements and bypasses mutation filtering in ReadWrite.
There is no pre-execution transaction-control rejection.

An executed guard-only check accepted the synthetic batch
`INSERT INTO dbo.FictionalAuditTable(Value) VALUES(1); COMMIT; THROW 51000,'after-commit',1;`.
That batch was **not sent to SQL Server**. The precise runtime trace is:
ScriptExecutionService starts the transaction at 187–189, executes the entire
batch at 293–295, then checks its count at 302–306. An embedded COMMIT can persist
ordinary transactional DML before detection. ROWCOUNT reset at
[QueryExecutionService.cs](../../src/SqlToAi/Database/QueryExecutionService.cs):250
can fail on the completed transaction; subsequent rollback attempts cannot undo
the commit. Existing changed-count fake tests prove detection, not atomicity.

**Recommendation:** reject explicit transaction control before atomic execution.
For EXEC/procedures/dynamic SQL, constrain the guaranteed subset or explicitly
qualify the atomic contract; a keyword-only prohibition cannot guarantee what
called procedures do. Retain post-execution checks as detection. **Effort:
medium.** Validate preflight rejection plus an isolated fictional-table test
showing attempted early COMMIT cannot leave writes after failure. **Submission
blocker: yes**: the advertised atomic mode can leave committed changes despite
failure. This is distinct from the documented external-side-effect limitation.

### F04 — Comparison discards its normalized sample limit

**P1 / high confidence.** [QueryComparisonService.cs](../../src/SqlToAi/Database/QueryComparisonService.cs):77–89
computes/passes a clamp of 1 through MaxRowLimit. `PerformComparisonAsync`
ignores that parameter at 152–153; 283–284 passes original `args.MaxDiffRows`.
The loop at 319 reads no rows for zero/negative values; equality at 155 treats
the resulting empty samples as proof of no differences.

Executed the full comparison service with fake connection/transaction/readers,
equal schemas/counts and non-empty difference readers: `max_diff_rows=0`
returned success with `IsEqual=true`. With MaxRowLimit=2 and requested samples=5,
five difference rows were returned. This is not a duplicate-multiplicity or
ReadCommitted limitation: actual differences are hidden by a permitted argument.
The public dispatcher forwards this value and the input schema has no lower bound.

**Recommendation:** use the normalized limit throughout and validate/document
its range; separate difference existence from sample formatting if zero samples
are intentionally supported. Preserve default behavior for ordinary positive
values. **Effort: small.** Test zero/negative/default/above-maximum values with
equal counts but actual differences, and verify output never exceeds the cap.
**Submission blocker: yes**, because the core comparison tool can declare
different results equal. Benchmark does not expose max_diff_rows directly, but
also inherits the ignored configured sample cap through default comparison.

### F05 — Per-query parameters are overwritten in combined comparisons

**P1 / high confidence; live non-destructive proof.**
[QueryComparisonService.cs](../../src/SqlToAi/Database/QueryComparisonService.cs):280–305
combines both SELECTs into one EXCEPT command and binds primary then secondary
parameters under their original names.
[SqlParameterBinder.cs](../../src/SqlToAi/Database/SqlParameterBinder.cs):191–199
overwrites an existing parameter. Separate schema/count calls use the intended
individual values, while both sides of each difference use one shared value.

Executed against the explicit fictional DemoDB fixture, bounded to five seconds:
both query texts were `SELECT @p AS Value`, with parameters_a `{p:1}` and
parameters_b `{p:2}`. Actual result: `is_equal=true`, both counts 1, both
differences `[]`. Expected: unequal, with values 1 versus 2. No database rows
were modified. This affects `sql_compare_queries` and benchmark's embedded
comparison, which can support an invalid optimization verdict.

**Recommendation:** isolate query parameter namespaces when composing SQL, with
parser-aware variable rewriting, or compare independently materialized results.
Do not use raw text replacement that changes literals/comments. Rejecting
conflicting names safely is a smaller interim compatibility restriction, but
must be actionable and documented. **Effort: medium.** Test same-name/different
values and types, same values, disjoint names, shared fallback and benchmark
propagation. **Submission blocker: yes**, because documented independent
parameters can produce a false equivalence result.

### F06 — Duplicate result names silently discard values

**P2 / high confidence.**
[QueryExecutionService.Anonymization.cs](../../src/SqlToAi/Database/QueryExecutionService.Anonymization.cs):100–109
serializes through a case-insensitive dictionary. Later duplicate keys overwrite
earlier columns. The same shared executor serves query and script calls;
comparison sample serialization also uses this representation at
[QueryComparisonService.cs](../../src/SqlToAi/Database/QueryComparisonService.cs):321–326.

Executed production batch serialization with existing fake-provider plumbing:
columns `["Id","Id"]`, row `[1,2]` returned successful `{"Id":2}`; case-only
`Id`/`ID` and two unnamed columns also lost the first value. A normal join
`SELECT a.Id,b.Id ...` can therefore return plausible incomplete evidence.

**Recommendation:** detect collisions/ambiguous empty names before returning
success and ask for unique SQL aliases, or explicitly introduce an ordinal
representation. Detection is the smaller compatibility change. **Effort:
small.** Test duplicate, case-only, unnamed and unique names through query,
script and difference-sample paths. **Submission blocker: no mandatory catalog
blocker established**; callers can use unique aliases as a workaround.

### F07 — Explicit string types do not preserve literal strings

**P2 / high confidence.**
[SqlParameterBinder.cs](../../src/SqlToAi/Database/SqlParameterBinder.cs):119–134
infers the value before applying explicit DbType; 174–181 converts date/Guid
strings. Setting DbType at 208 does not restore the original value assigned at
204. All execution/validation/analysis paths sharing this binder are affected.

Executed the production binder with a real SqlCommand, without opening a
connection: `{"p":{"value":"2025-01-02","dbType":"String"}}` produced CLR
DateTime and SqlValue text `02.01.2025 00:00:00` under the audit culture, not the
input identifier. AnsiString behaved similarly; an offset-bearing explicit
string lost its original representation/offset. Exact formatting depends on
culture, but conversion and loss of the original string do not. Unicode, null
and a 28-digit decimal were positive controls.

**Recommendation:** determine explicit type before inference and preserve raw
strings for explicit string types. Retain existing inferred behavior when no
type is supplied. **Effort: small/medium.** Test exact CLR value and SqlValue for
date/Guid/offset-looking strings, property-order variants, null and explicit
numeric/date types. **Submission blocker: no mandatory listing blocker**;
material parameter correctness, with no reliable explicit-string escape hatch
for affected JSON callers.

### F08 — Comparison and measurement ignore configured timeout

**P2 / high confidence.** Commands at
[QueryComparisonService.cs](../../src/SqlToAi/Database/QueryComparisonService.cs):213–216,
246–249, 298–305 and
[PerformanceMeasurementService.cs](../../src/SqlToAi/Database/PerformanceMeasurementService.cs):241–248
do not assign QueryExecution.CommandTimeoutSeconds. Query/validation commands
do assign it. Both analysis paths feed `sql_benchmark_optimization`.

Executed comparison with configured timeout 7 and fake commands initialized to
SqlClient's documented default 30: all twelve commands across two calls retained
30. The performance path is a precise static trace. Actual wall-clock timeout
was not measured. Configured lower limits therefore fail to constrain these
commands as intended, while higher limits still expire at the provider default.
Cancellation support does not implement the configured command timeout.

**Recommendation:** pass options into all analysis query command creation and
set the timeout consistently, including relevant control commands. **Effort:
small.** Capture non-default timeout on every schema/count/diff/warmup/measured
command; add one bounded fictional-fixture timeout check. **Submission blocker:
no mandatory catalog blocker**, but operators cannot rely on the documented
limit across tools.

### F09 — Zero query limit executes SQL and reports empty success

**P2 / high confidence.**
[QueryExecutionService.cs](../../src/SqlToAi/Database/QueryExecutionService.cs):100–103
applies only an upper bound to requested_row_limit. Lines 201, 225–240 issue
`SET ROWCOUNT 0`, execute the command, then read no rows because the client loop
requires `rowCount < args.RowLimit`; the successful result at 259 is `[]`. For a
zero limit the initial comparison is `0 < 0`, which is false.

Executed the production batch path with a reader containing three synthetic
rows and limit zero: success with `[]`. Public execution passes zero unchanged;
this public-path consequence is traced, not a separate live SQL probe. The
argument schema supplies no positive bound. SQL executes even though the agent
gets a result indistinguishable from no matches; ROWCOUNT zero also disables
SQL-side limiting. Negative values are likewise unvalidated, though their live
provider outcome was not tested.

**Recommendation:** reject non-positive requested limits as SQL-AI-0001, or
define/document a safe explicit zero meaning without running SQL. Validate
configured defaults/maxima as well. **Effort: small.** Verify zero/negative
requests fail before opening/executing, positive values are clamped, and a truly
empty query remains successful. Script's own normalization must remain
consistent. **Submission blocker: no mandatory catalog blocker**; positive
limits are a workaround.

### F10 — Bounded results conceal saturation and effective limits

**P2 / high confidence.**
[SchemaService.cs](../../src/SqlToAi/Database/SchemaService.cs):126–145 defaults
search to 100 and returns only a table;
[SchemaObjectDiscovery.cs](../../src/SqlToAi/Database/SchemaObjectDiscovery.cs):14
applies TOP without a completeness probe. Query execution at
[QueryExecutionService.cs](../../src/SqlToAi/Database/QueryExecutionService.cs):100–103,
240–264 caps results, but
[ToolDispatcher.cs](../../src/SqlToAi/Mcp/ToolDispatcher.cs):142 reports only the
returned count, not the effective limit or saturation.

Executed a three-row fake reader with limit two: success and two rows, with no
completeness signal. A complete two-row source has the same result shape. For
search, exactly 100 matches and a larger capped set are similarly indistinguishable
by static query/rendering trace. Public docs mention caps, but connecting agents
cannot discover configured defaults/maxima from those results. This can turn a
partial object list or dataset into an unsupported completeness assumption.

**Recommendation:** include effective limit and a conservative “limit reached;
more rows/matches may exist” notice with narrowing/count guidance. For search,
TOP(limit+1) can establish truncation if desired. Do not claim definite query
truncation from saturation alone, or mandate pagination. **Effort: small/medium.**
Test below-cap, exact-cap, over-cap, default and clamped output. **Submission
blocker: no mandatory catalog blocker**; narrower searches and explicit COUNT
queries are workarounds.

### F11 — Source-tracing instructions use incoming dependencies

**P2 / high confidence, static proof.**
[McpConstants.cs](../../src/SqlToAi/Mcp/McpConstants.cs):34 instructs agents to
trace view/computed/aggregate source columns using sql_get_object_references.
[SqlMcpToolRegistrations.cs](../../src/SqlToAi/Mcp/SqlMcpToolRegistrations.cs):87
claims incoming and outgoing references. Actual
[DetailSchemaRenderer.cs](../../src/SqlToAi/Database/DetailSchemaRenderer.cs):319
uses only `sys.dm_sql_referencing_entities`; 334–338 reports incoming references.
Canonical [tools.md](../../docs/tools.md) already describes that direction correctly.

Concrete static scenario, not executed against SQL: sales.CustomerView reads
sales.Customer, but no object references the view. The prescribed call returns
“No objects reference ...” and never supplies Customer. The agent cannot complete
the instructed source/exemption workflow from that result and can be misdirected.

**Recommendation:** correct live description and server instructions to incoming
references, use available view-definition inspection for source investigation,
and acknowledge unresolved column lineage. Adding outgoing/column lineage is a
larger optional product change. **Effort: small.** Assert actual tools/list and
initialize/discover guidance, plus a synthetic outgoing-only view scenario.
**Submission blocker: no mandatory catalog blocker**; manual definition
inspection provides a workaround.

## Verification and coverage

| Area | Inspected/exercised evidence | Result and limits |
| --- | --- | --- |
| 1. MCP contracts | Program, SDK registrations, registry, dispatcher; real legacy/modern stdio discovery, list, denied calls, unknown names/arguments, binding failures and EOF | 17 tools; capabilities/framing/error distinction observed. No HTTP/OAuth requirement imposed. No universal client claim. |
| 2. Agent usability | Database/object discovery contract, schema detail selection, references, parameterized validation/comparison, row serialization, bounds, typed values | F04–F07/F09–F11. Successful live SQL schema-discovery chain was not run; discovery/selection was inspected statically and through existing tests. |
| 3. Security/trust | Default deny/overlap/exclusions across schema/query/script/analysis paths; read-only guards, masking/rules/tokens, script reader, primary/secondary connections, validation and atomicity | F01–F03. Default-deny and restrictive overlap checks are present; no distinct grant bypass established. Accepted TLS gap retained. |
| 4. Behavior/maintainability | Singleton DI, per-call connections/transactions, token vault/cache, cancellation/error mapping, CLI shared dispatcher, focused existing tests | F03–F09. No extra confirmed concurrency/cache defect. Cancelled-token cleanup is not claimed safe under every provider failure. |
| 5. Operational truth | README, SETUP, configuration/tools/security/development/architecture, templates, SECURITY, generated package README and versioned links | Live metadata contradictions recorded in F02/F11. Manual settings, conservative defaults, unsigned Windows binaries and self-contained runtime documented. Private-reporting setting not independently confirmed. |
| 6. Distribution/catalog | Package/smoke/release/draft scripts and workflow, pinned manifest/Registry schemas, actual local package, copied-script failure checks | Payload/hash/schema checks passed. No package start, hosted workflow, actual draft upload, namespace verification, client install or submission performed. |

### Commands and observed results

Commands ran from the repository root unless a temporary probe project is named.
`<audit-temp>` is the owned temporary evidence directory recorded below.

| Check / exact command or bounded procedure | Exit/result |
| --- | --- |
| `git --no-pager rev-parse HEAD`; `git --no-pager status --short`; `dotnet --info` | 0; commit/environment above, initial clean tree |
| `dotnet build SqlToAi.slnx`; subsequent `dotnet build SqlToAi.slnx --no-restore` | Build success; verified latter exit 0, 0 warnings/errors |
| `pwsh -NoProfile -File scripts/validate.ps1 -ResultsDirectory <audit-temp>` | 0; forced uncached restore with audit all/low and warnings as errors; build clean; **834 offline passed, 0 failed/skipped** |
| `dotnet test SqlToAi.slnx --no-build --no-restore --logger 'trx;LogFileName=full.trx' --results-directory <audit-temp>` | 0; **898 total: 879 passed, 0 failed, 19 TLS skipped**; ordinary SQL tests use the explicit fictional DemoDB fixture/overlay |
| `dotnet list src/SqlToAi/SqlToAi.csproj package --include-transitive --format json`; focused search for audit suppressions/warning exclusions | 0; resolved versions recorded; no advisory suppressions or NU1900–NU1905 exclusions found |
| `dotnet run --project <audit-temp>\comparison\Probe.csproj` | 0; F04 and F08 fake-provider evidence. Temporary project explicitly aligned Pkcs to production's 10.0.13 before final run. |
| `& <contracts-temp>\probe.ps1 -Work <contracts-temp>`; `& <contracts-temp>\modern.ps1 -Work <contracts-temp>` | 0; legacy/modern stdio evidence above, no stderr, EOF exit 0 |
| `dotnet run --project <contracts-temp>\probe\Probe.csproj --no-restore --verbosity quiet` | 0; synthetic serialization/binder controls. Its separate older transitive Pkcs advisory warnings do not describe production's pinned graph. |
| `dotnet run --project <security-temp>\probe.csproj` | 0 on corrected final harness; F01 synthetic, F03 guard-only, F02/F05 bounded live non-destructive fixture probes. Initial platform-neutral SqlClient harness binding failed and was corrected to the Windows runtime assembly; those failures were not server findings. |
| `& .\scripts\package.ps1 -OutputDirectory <package-temp>` | 0; one self-contained win-x64 publish, actual ZIP/MCPB generated and schema-validated |
| ZIP APIs/Get-FileHash/Test-Json checks on generated files | Pass: eight common entries with identical extracted bytes; MCPB has only one additional manifest.json; both checksum sidecars valid; Registry fileSha256 matches final MCPB |
| Copied package script with native publish stub returning 47 | Wrapper exit 1, “Package publish failed with exit code 47”; no ZIP/result object; no real publish/release endpoint invoked |
| Copied draft script in owned temporary Git repository/tag with tampered ZIP and unchanged expected hash | Exit 1, mismatch against smoke-checked SHA-256; gh stub marker absent, no upload attempted |
| `git ls-remote` of official MCPB/Registry main | 0; revisions recorded above |
| `gh api repos/RalfHuesing/SqlToAi --jq '.security_and_analysis.private_vulnerability_reporting.enabled | tojson'` | 0, `null`; does not confirm enabled/disabled. Public SECURITY.md accessible; direct private-report form fetch unavailable. |

Temporary native release stubs were synthetic local scripts, not real release
actions. No production/deploy scripts or hosted workflows were triggered.

The dependency audit returned no advisories for the actual resolved solution
graph from the configured nuget.org source. No suppression rationale was needed.
This does not establish absence of unknown vulnerabilities. Different disposable
probe graphs initially resolved older Pkcs versions; those warnings are isolated
from the production dependency conclusion and were not promoted to findings.

### Package evidence

Generated version 1.0.32, identity `io.github.ralfhuesing/SqlToAi`, transport
`stdio`, manifest binary entry `${__dirname}/SqlToAi.exe`, Windows/x64 route and
versioned GitHub MCPB asset URL. Manifest and Registry metadata passed the pinned
official schemas. LICENSE, third-party and runtime notices were included; the
copied SETUP README links were rewritten to versioned repository URLs.

| Artifact | Bytes | SHA-256 |
| --- | --- | --- |
| SqlToAi-win-x64.zip | 39,347,078 | `4c16e827a91013ede94a106e1e56cd5887a5823208c6dd296922b6af4d6fddf9` |
| SqlToAi-win-x64.mcpb | 39,347,534 | `7b7198e5a3a505e425537ed8955c0a0fadc4a8b7815f7a3f4f522e0d6b8ed5f5` |

These hashes identify audit-generated local bytes, not a published release.
Static release/draft workflow review showed expected-hash checking before
handoff; the tampered-archive failure check exercised that boundary. A real
package/client launch was not inferred from archive integrity or schema validity.

Disposable evidence locations (local, not required distribution artifacts):

- `<audit-temp>`: `C:\Users\Ralf\AppData\Local\Temp\SqlToAi-readiness-audit-1654408dda5b4daaa2101cb68b7186d3` — TRX files and comparison probe.
- `<contracts-temp>`: `C:\Users\Ralf\AppData\Local\Temp\sqltoai-contracts-e0832a8dc04440bab1bf163a46bbdfde` — stdio scripts, JSONL/list responses, stderr and synthetic C# probe.
- `<security-temp>`: `C:\Users\Ralf\AppData\Local\Temp\SqlToAi-security-audit-15a320ba356c4b3cbef0a9b19ce9af3c` — synthetic/guard/fixture probe source.
- `<package-temp>`: `C:\Users\Ralf\AppData\Local\Temp\SqlToAi-audit-package-b9970d6b84f345b390e063308e94c384` — archives, manifests and checksums.
- Negative checks: `C:\Users\Ralf\AppData\Local\Temp\SqlToAi-audit-negative-697c13a0b07447d8803ec0fbbfbd59ad` — copied scripts, synthetic local repository and stubs.

## Remaining gaps and accepted limitations

- **Accepted TLS environment limitation remains in force.** The
  [recorded decision](../sql-connection-security/Konzept.md#accepted-environment-limitation-2026-10-16)
  accepts unavailable trusted/untrusted TCP infrastructure. No test profile was
  supplied in this run; all 19 dedicated TLS cases skipped. Passing ordinary SQL
  tests with their explicit fixture-only trust exception proves neither default
  certificate authentication nor the missing TCP matrix. No new infrastructure
  was requested or provisioned.
- No already disposable OS profile was available for actual packaged executable
  starts. smoke-package guards were inspected; package startup, native extraction,
  fresh-profile installation and actual MCPB client installation remain unverified.
  Startup-harness stdio is narrower evidence and is labelled accordingly.
- Registry namespace ownership, public versioned-asset availability and eventual
  Registry acceptance were not established. None is inferred from valid server.json.
  No release/draft/Registry operation or GitHub setting was changed. Private
  reporting remains a read-only verification gap because the API returned null
  and the direct form was unavailable; the policy claim alone is not confirmation.
- Atomic persistence behavior in F03 is a precise static trace, not an executed
  destructive probe. F02's execution and F05's false equality were demonstrated
  only with bounded non-destructive fixture SQL.
- Live SQL timeout/cancellation, every unusual SQL name/type, successful complete
  discovery/schema sequences and token reuse across process restart were not all
  exercised. Existing unit coverage/static wiring is narrower than those scenarios.
- Cross-database references/synonyms/linked servers are constrained primarily by
  SQL permissions, not by requested-database allow lists. Raw DDL/comments,
  comparison samples, plans, errors/logs and non-string values are disclosed
  masking limits, not rediscovered protection claims. Tokens remain process-local;
  resolution is not promised for parameters/every analysis tool.
- SQL names, metadata descriptions, comments and results can carry untrusted text
  into agent context. Raw schema/definition paths are present; no downstream model
  takeover or host-action authorization was demonstrated. Treat those contents as
  data. Local script access is deliberately not restricted to one directory;
  direct URL/UNC rejection is not a host-consent mechanism.
- No extra confirmed singleton/cache/concurrency defect was established. Unbounded
  warmup/repetition inputs and cleanup under cancellation merit focused future
  testing if needed; no resource-exhaustion probe was run and no new finding is
  based solely on that suspicion.

Small unprioritized improvements: make safe SDK argument-binding errors name the
bad argument without exposing provider exceptions; describe typed-parameter shapes
in live metadata; carry documented comparison duplicate/order/snapshot and raw
sample limitations into its tool description. Structured output may help clients,
but absence alone is not a defect or reason for a mandatory SDK upgrade.

## Recommended correction order

1. Close F01 and F02's exposure/execution paths, then constrain atomic mode in
   F03. Keep SQL permissions as the underlying boundary and existing accepted TLS
   limitations unchanged.
2. Correct F04/F05 before relying on comparison or optimization verdicts. Parameter
   namespace isolation needs its own regression cases; fixing the sample clamp
   does not fix parameter overwrite.
3. Correct F06/F07 data representation, then F08/F09 limit behavior. Expose
   reliable effective-bound information for F10 after normalization is settled;
   fix F11's metadata/instructions independently.

Use the focused validation criteria in each finding, then the shared build/audit/
offline checks and applicable fictional-fixture tests. Publication/client/ownership
checks remain separate evidence steps when publication is authorized. This report
does not implement fixes or create a remediation roadmap.
