# M1.3 — Independent foundation audit

Reviewed `988b641..070d1bbc7899caf179ede3fca0044950b19a9f3f` and current source/test bodies.
Verdict: no blocking findings within M1.1/M1.2 preparation scope; accept M1.
No production/test changes or correction item. Resume at M2.1, not feature release.

## Contract evidence

Paths below are repository-relative; line numbers refer to the reviewed code.

- Must 1/2/5: `src/SqlToAi/Database/DatabaseTargetParser.cs:9-76` splits only
  the first slash, checks ASCII alias syntax and lowercases ASCII alias characters
  without touching the raw suffix. `DatabaseTargetResolver.cs:15-55` resolves
  configured canonical aliases, rejects case-duplicate effective options and
  returns invalid parameters for unknown aliases. `src/SqlToAi/Domain/DatabaseTargetContext.cs:8-23`
  has an internal constructor and get-only identity; record equality preserves raw
  case while explicit policy comparison ignores database case. Assertions in
  `tests/SqlToAi.Tests/Database/DatabaseTargetResolverTests.cs:12-104` cover
  canonicalization, Unicode/later slashes, explicit default, duplicate physical
  endpoints, invalid/unknown inputs and distinct raw/policy identity.
- Must 1/11: `src/SqlToAi/Configuration/SqlToAiOptions.cs:20-33` owns endpoint,
  authentication, TLS, timeout, exclusions and provider options per alias.
  `DatabaseTargetResolverTests.cs:108-141` exercises actual configuration binding
  and disabled provider defaults. Global query limits were not changed.
- Must 3/7: `src/SqlToAi/Security/TargetAccessPolicy.cs:11-36` applies selected-owner
  raw-name exclusions before restrictive qualified grants, using ordinal alias
  and OrdinalIgnoreCase database matching; unmatched targets deny access.
  `tests/SqlToAi.Tests/Database/NamedPrimaryRoutingTests.cs:99-165` asserts
  restrictive overlaps, local exclusion precedence, same-name owner isolation,
  default deny and no grant from an unqualified entry. Safety rejection has no
  connection dependency; this is not a claim that the factory itself authorizes.
- Must 4/5/11: `src/SqlToAi/Database/SqlConnectionFactory.cs:27-75` selects only
  the configured alias, requires its settings and passes the exact raw catalog;
  explicit catalog discovery alone omits InitialCatalog. The 200 parallel calls
  in `NamedPrimaryRoutingTests.cs:38-81` assert selected endpoint/auth/TLS/timeout,
  unchanged options and retained safety context. Lines 84-97 assert unusual raw
  catalog characters; lines 211-266 assert explicit catalog aliases and no legacy
  endpoint fallback. These build connection objects; they do not contact SQL.
- Must 5/7/11: `src/SqlToAi/Database/QuerySafetyValidator.cs:82-182` carries the
  same context through policy and access checks without changing USE, read-only,
  SchemaOnly or statement-count guards. `NamedPrimaryRoutingTests.cs:168-208,269-282`
  assert successful context retention and rejected mutations/batches/USE.
- Must 4/11: `src/SqlToAi/Database/PrimaryConnection.cs:11-44` names the owner,
  preserves cancellation and timeout priority, maps other open failures to
  SQL-AI-0105 and discards raw exception payloads. `NamedPrimaryRoutingTests.cs:285-327`
  asserts SQL-AI-0105/0106, owner paths, safe exception state, one open attempt
  and unchanged cancellation. Existing `PrimaryConnectionFailureTests.cs:23-163`
  checks safe legacy service/dispatcher/logger/CLI failures after the shared open
  boundary change; it is not named-server transport coverage.

## Evidence correction and boundaries

The previous M1.2 claim that every context boundary returns SQL-AI-0001 for an
unknown alias was unsupported. Input resolution/catalog argument APIs do that;
`NamedPrimaryRoutingTests.cs:240-249` instead passes a context resolved against
another options instance: the guard denies it and the factory throws without
fallback. `PrimaryConnection.OpenTargetAsync` opens an already supplied connection;
it does not resolve or authorize targets. These APIs consume resolved identities
under consistent options, not arbitrary caller strings. No supported same-options
path bypassing resolution was found; hypothetical mismatched internal contexts
are not reported as contract bugs. The roadmap evidence now states this limit.

Required nonempty configuration, grant startup validation, duplicate JSON-source
checks, cross-source overrides, obsolete-contract rejection and host activation
remain M4.1. Provider behavior and remaining consumers belong to M2/M3. Main/demo
configuration, public transport binding and final AOT/composed isolation evidence
remain deferred by the roadmap; this audit does not mark them verified.

## Gates actually used

- Independent AiNetLinter file skeletons and feature contexts for resolver/factory
  relationships; the production factory target method has no current production
  callers, consistent with preparation. Static candidates are not runtime proof.
- `dotnet test tests/SqlToAi.Tests/SqlToAi.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~DatabaseTargetResolverTests|FullyQualifiedName~NamedPrimaryRoutingTests|FullyQualifiedName~PrimaryConnectionFailureTests"`:
  86 passed, 0 skipped, 0 failed (exit 0), using the existing Debug build.
- `git diff --check 988b641..HEAD`: clean at reviewed HEAD. Documentation diff
  and relative audit/concept/roadmap links checked before the audit-only commit.
- AiNetLinter `verify(scope=changes)`: incomplete, `reason: notconfigured`,
  `isGateResult: false`; no rule configuration added and no retries.
- Parent-reported independent gates at the identical `070d1bb`: build 0 warnings/
  errors; full tests 1,322 passed, 19 unavailable external TLS cases skipped,
  0 failed. Not independently repeated here. External TLS remains unverified;
  deterministic exception tests do not establish certificate interoperability.

# M2.5 — Independent provider, token and execution audit

Reviewed `070d1bb..41a4271` and current source/test bodies against concept Must 8–10 and the execution portion of Must 5/11.
Verdict: no blocking findings within M2.1–M2.4 preparation scope; accept M2.
No production/test changes or correction item. Resume at M3.1.

## Contract evidence

Paths below are repository-relative; line numbers refer to the reviewed code.

- Must 8: `src/SqlToAi/Database/SecondaryConnectionBuilder.cs:71-139` resolves provider sources per policy owner: server-less providers use the owner's primary endpoint, authentication, TLS and timeout with the explicit provider database or the target's raw database; dedicated secondary servers require an explicit raw database, use their own credentials/TLS, and never fall back to the primary endpoint. `SecondaryConnectionBuilderOwnerTests.cs:12-306` covers fallback endpoints, explicit provider databases, target database fallback, dedicated credential isolation, and rejected server-less dedicated configs. `MetadataProviderOwnerTests.cs:12-155` covers native descriptions and fallback.
- Must 9: `src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs:23-38,209-230` partitions rule cache keys by `(PolicyOwner, Endpoint, SourceDatabase, RulesTable)`, ensuring identical endpoints under different aliases or different target databases on fallback never share cached rules. `AnonymizationRuleProviderOwnerTests.cs:12-509` covers distinct owners on same/different endpoints, target DB fallback cache partitioning, failed load default-masking retention, and timeout/cancellation semantics.
- Must 10: `src/SqlToAi/Anonymization/TokenVault.cs:23-122` partitions tokens by `DatabaseTargetContext`, preserving raw database casing ordinally. A 64-bit global counter prevents token ID collisions across scopes, and atomic ownership registry prevents cross-scope token rebinding. `TokenVaultScopeTests.cs:12-293`, `AnonymizerScopeTests.cs:12-96`, and `QueryTokenResolverScopeTests.cs:12-120` verify cross-scope isolation, literal foreign tokens, and ordinal raw DB distinctions.
- Must 5/11 (execution): `src/SqlToAi/Database/QueryExecutionService.cs:91-128`, `src/SqlToAi/Database/QueryValidationService.cs:40-80`, and `src/SqlToAi/Database/ScriptExecutionService.cs:60-150` resolve immutable targets before guards or connections, pass raw database names to SqlClient, and maintain isolation under parallel and repeated-batch execution. `QueryExecutionServiceTargetIsolationTests.cs:25-315` and `ScriptExecutionTargetIsolationTests.cs:25-354` verify multi-target concurrency, token egress/ingress boundaries, and batch safety.

## Review leads evaluation

- The three review leads noted at the M2.4 checkpoint were evaluated:
  1. `ScriptExecutionTargetIsolationTests`: foreign token non-resolution is verified directly across scopes where both are anonymized in `QueryExecutionTargetScopeTests`, `QueryExecutionServiceTargetIsolationTests`, and `QueryTokenResolverScopeTests`.
  2. `GrantsAccessLevelProvider`: an offline test fake; production `AccessLevelProvider` remains clean and in-memory.
  3. `QueryExecutionTargetScopeTests.ValidateQueryAsync_ComposedGuard`: each target connection rolls back; the offline factory tracks both targets.
  None of these represent production code defects or violate the concept boundaries.

## Gates actually used

- `dotnet build SqlToAi.slnx`: 0 warnings, 0 errors.
- `dotnet test SqlToAi.slnx`: 1,424 passed, 19 skipped (external TLS), 0 failed.
- `git diff --check`: clean.

# M3.4 — Independent service and transport audit

The original M3.4 acceptance lacked evidence in this audit. Its closure is
re-evaluated by the deep audit and the reviewed remediation, rather than inferred
from old task checkmarks. Reviewed history includes `988b641..cab47d3`, especially
`41a4271..cab47d3`; subsequent service and transport corrections are `0eb44d4`
(R4) and `f4145ed` (R5), each implemented by GPT-6.1-Sol/high and independently
approved by a different GPT-6.1-Sol/high agent.

- Must 5/11: `src/SqlToAi/Cli/SchemaExportService.cs:48–73,117–145` retains one
  context through export discovery and rendering; typed schema overloads retain
  access checks. `Database/OptimizationBenchmarkService.cs:40–82` shares one
  context through comparison and both measurements. The R4 evidence below names
  real-service context-identity tests, child failure/cancellation checks and
  unchanged mutation/USE/statement guards. Exploration rights follow the actual
  canonical `primary/DemoDB`, not the first configured alias.
- Must 6/7: server discovery and raw-name-only database search are exercised by
  `tests/SqlToAi.Tests/Database/ServerDiscoveryTargetIsolationTests.cs`; R3 adds
  actual post-open reader timeout/TLS handling and cancellation. Owner policy,
  exclusions and production connection settings are observed by the composed R5
  tests, including same-address owners and denial before connection creation.
- Public MCP/CLI arguments: `Mcp/ComposedTargetBindingTests.cs:39–249` under the
  test project invokes actual SDK delegates and generated CLI commands. Required
  public schemas, qualified descriptions, all target-bearing tools' invalid input
  forms and unusual raw database names are checked. The managed stdio host test
  at `Mcp/ManagedMcpTargetBindingTests.cs:13–57` is an actual child process.
- The R5 gate passed 1,927 tests with 19 external TLS skips and no failures;
  warning-as-error build and diff checks were clean. This evidence establishes
  managed service/transport acceptance, not Native AOT or strict TLS validation.

# M4.3 — Independent final concept audit

Status: reopened. The previous blanket closure statement and warning-free Native
AOT claim were invalid and have been replaced. The source-level findings and
reviewed remediation are recorded below. Final independent review, actual AOT
gates and exact final source/commit evidence are still pending R6. M4 and the
roadmap remain open until every required acceptance criterion is verified.

# Deep audit at cab47d3

Date: 2026-10-17. Three independent read-only GPT-6.1-Sol/high agents reviewed
foundations/startup/discovery, providers/tokens/execution, and remaining services/
MCP/CLI/exploration. The orchestrator separately checked history, documentation and
runtime gates. Scope: `988b641..cab47d3`, especially `41a4271..cab47d3`, concept
Must 1–11 and all milestone acceptance contracts. No prior checkbox was treated as
proof. AiNetCodeNavigator was used for declarations and relationships; no
AiNetLinter tool was exposed, so no linter pass is claimed.

## Prioritized findings and slices

Locations in this subsection refer to `cab47d3`, before remediation.

| Priority | Finding and evidence | Slice |
| --- | --- | --- |
| P1 security | `SqlCharScanner.cs:92,100` under `src/SqlToAi/Database/` closes escaped `]]` identifiers early and ignores double-quoted identifiers and nested block comments. `QueryTokenResolver.cs:54` can substitute a known token inside a valid output alias; `QueryExecutionService.Anonymization.cs:118` emits the resulting plaintext key. Repro: `SELECT 1 AS [a]]'<token>']` or `SELECT 1 AS "'<token>'"`. Violates Must 10 and the identifier-preservation contract. Predates this feature. | R1 |
| P1 contract | `src/SqlToAi/Configuration/UserConfiguration.cs:143,156,178–184` inspects only the first matching root and Servers property. Existing StartupHarness accepted hidden obsolete `SqlServer` and exact/case-only duplicate aliases in later wrappers, exit 0 and empty stderr. Violates Must 2/4. Introduced in `ec0b3e1`. | R2 |
| P2 validation | `src/SqlToAi/Program.cs:133–142` resolves but does not validate either direct options or the separately bound DI instance. M4.1 expressly requires equivalent effective validation. Normal host startup validates separately; no security bypass of that route is claimed. | R2 |
| P2 error contract | `src/SqlToAi/Database/SchemaService.cs:105–106,122–129` recognizes timeout/TLS only inside connection-open wrappers; a catalog reader timeout after successful open returns static grants as success. Violates discovery Verification; command-failure regression coverage is missing. Introduced in `5f5c648`. | R3 |
| P1 security (found during R3) | `src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs:205–221` treats a failed SchemaPattern probe as an absent column; the subsequent SELECT drops schema restrictions and `RuleRow.SchemaPattern` defaults to `%`. A private-schema exemption can consequently apply elsewhere. Existing `AnonymizationRuleProviderTests.cs:70–88` even expects an exemption after an unrequested probe cancellation. Violates Must 9's failed-load masking; predates this feature (`dbbbc694`, assertion added in `580dcede`). | R3 |
| P2 diagnostics | `src/SqlToAi/Metadata/MetadataProvider.cs:99,152` and `src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs:99,171,220` omit the server owner from provider failure diagnostics. Documented affected settings paths cannot be identified. Masking remains closed on failure. | R3 |
| P2 architecture | `src/SqlToAi/Cli/SchemaExportService.cs:49–63,127–139` and `src/SqlToAi/Database/OptimizationBenchmarkService.cs:41–62` repeatedly pass strings into resolving children rather than retaining one entry context (Must 5). No misrouting under stable startup options was demonstrated. | R4 |
| P2 correctness | `tools/SqlToAi.Exploration/Program.cs:45–50` reports rights for the first configured alias; the scenario targets `primary/DemoDB`. Direct context construction also misses uppercase-alias canonicalization. | R4 |
| P2 guard contract | Unused string overloads in `src/SqlToAi/Database/QuerySafetyValidator.cs:142` parse without configured-alias resolution; unknown aliases yield 0104 rather than 0001. Actual execution services resolve correctly first. | R4 |
| P2 tests | `tests/SqlToAi.Tests/Database/ComposedTargetIsolationTests.cs:34–46,82–94,367` replaces the rule cache and SDK service validation with fakes. Those tests do not prove their claimed real cache/host composition. Existing focused routing/provider tests remain useful. | R5 |
| P2 public contract | `src/SqlToAi/Mcp/ToolRegistry.cs:96` and corresponding SDK parameter descriptions still say only "Target database name. Required." The M4.2 claim of qualified descriptions has no supporting assertion. | R5 |
| P2 transport (runtime-confirmed during R4) | Required SDK delegate parameters reject missing server/database before the dispatcher. Against `dd17ef3`, actual StartupHarness stdio `tools/call` for `sql_list_databases` with `{}` and `sql_execute_query` with only `query` both returned generic "An error occurred invoking…" without required `SQL-AI-0001`. Unknown server did return 0001. No SQL was requested; a temporary settings root was used. Runtime server instructions also incorrectly claim unknown tokens always match nothing and omit the target scope. | R5 |
| P1 acceptance evidence | `src/SqlToAi/SqlToAi.csproj:5` globally suppresses IL2104/IL3053, added by `912701a`. An unmasked detailed AOT publish emitted 103 IL diagnostics, including 17 IL3050 dynamic-code warnings. Exit 0 with suppression is not warning-free AOT compatibility. | R6 |
| P2 configured timeout (found during R6a preparation) | Both `MetadataProvider` commands omit `commandTimeout` despite the existing `MetadataProvider.CommandTimeoutSeconds` setting and its documented behavior in `docs/configuration.md`. The explicit ADO.NET migration must apply that setting, including zero, without changing unrelated catalog defaults. | R6a |
| P2 source contract (confirmed after R5) | `UserConfiguration.cs:106–108` checks raw environment prefixes without provider normalization or section boundaries. Isolated StartupHarness accepts `SqlToAi:SqlServer:Password`, `SqlToAi:MetadataProvider:Password` and `SqlToAi:SqlServer__Password`, although those forms bind configuration; it incorrectly rejects `SqlToAi__SqlServerless__Password`. Existing double-underscore legacy forms fail correctly. | R2a |
| P2 documentation | The resume checkpoint still says M3 has not started, M3.4 evidence is absent here, and historical M4.3 declares complete acceptance without line/commit evidence. | R6 |

No additional cross-owner grant, rule-cache or token-vault leak was demonstrated.
Parser raw-name preservation, selected-owner policy, provider source identity,
ordinal token ownership and request-local execution state were inspected. These
positive observations do not substitute for missing composed runtime coverage.

## Baseline execution evidence

- At clean `cab47d3`, `dotnet build SqlToAi.slnx --warnaserror`: exit 0,
  0 warnings/errors.
- `dotnet test SqlToAi.slnx --no-build --logger 'trx;LogFileName=named-connections-baseline.trx' --results-directory temp/audit-baseline --logger 'console;verbosity=quiet'`:
  exit 0, 1,486 passed, 19 skipped, 0 failed. TRX inspection confirmed all 19
  skips belong to `TlsConnectionIntegrationTests`; no ordinary SQL skips.
- `git diff --check`: clean before changes.
- Diagnostic publish only (not the acceptance gate):
  `dotnet publish src/SqlToAi/SqlToAi.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:NoWarn=NU1510 -p:TrimmerSingleWarn=false -p:TreatWarningsAsErrors=false -o temp/aot-diagnostic`:
  exit 0 with 103 IL warning lines. Categories: IL2026 26, IL2046 8, IL2057 7,
  IL2060 1, IL2067 5, IL2070 21, IL2072 7, IL2075 6, IL2080 2, IL2092 1,
  IL2111 2, IL3050 17. Diagnostics include Dapper DynamicMethod generation and
  SqlClient reflection/dynamic serialization. The Native AOT gate remains open.

Remediation is tracked in [the roadmap](roadmap.md#deep-audit-remediation).

## Reviewed remediation commits

| Slice | Commit | Independent managed gate: passed / TLS skipped / failed |
| --- | --- | --- |
| Findings and reopened acceptance | `7f419fd` | Baseline 1,486 / 19 / 0 |
| R1: syntax-aware token literals | `0bbbe55` | 1,526 / 19 / 0 |
| R2: JSON source and effective options | `7f88de2` | 1,600 / 19 / 0 |
| R3: safe discovery and failed rule loads | `dd17ef3` | 1,610 / 19 / 0 |
| R4: shared composed target contexts | `0eb44d4` | 1,646 / 19 / 0 |
| R5: real composition and SDK/CLI binding | `f4145ed` | 1,927 / 19 / 0 |
| R2a: normalized environment boundaries | `00f5b58` | 1,943 / 19 / 0 |

Every code row had a different independent reviewer, a warning-as-error build
with zero warnings/errors and a clean diff check before commit. Skip names were
compared exactly with the baseline, not inferred from totals. None of these
managed gates establishes Native AOT or strict certificate validation.

## R1 — Syntax-aware token resolution

Coder: GPT-6-luna/high; correction: GPT-6.1-Sol/high. Independent reviewer:
GPT-6.1-Sol/high (`audit_state`), different from both coders. The first review
rejected a lexer-only fix because single-quoted SQL aliases still leaked values.
The second review accepted the complete nine-file diff against `7f419fd`.

`src/SqlToAi/Database/SqlLiteralScanner.cs:11–46` now uses the existing ScriptDom
parser and a static visitor: value expressions are visited while column aliases
are excluded. Parse failures supply no substitution ranges; national literals,
escaped quotes and source ordering are retained. The orphaned SqlCharScanner and
its state-only tests were removed; meaningful lexical cases moved to literal and
resolver regressions. No new token-resolution path was added.

`tests/SqlToAi.Tests/Database/QueryExecutionTargetScopeTests.cs:90–149` uses real
stored tokens and derives result keys from executed command text. It covers
bracketed, double-quoted and all three single-quoted alias forms. Resolver and
literal tests retain real DECLARE/WHERE/IN/LIKE/CASE substitution, foreign scopes,
escaped delimiters, nested comments and malformed input. The security document
describes the resulting syntax boundary.

Parent gates after independent approval: `dotnet build SqlToAi.slnx --warnaserror`
returned 0 warnings/errors; `dotnet test SqlToAi.slnx --no-build` returned 1,526
passed, 19 skipped, 0 failed. `temp/audit-gates/r1.trx` confirmed every skip is an
external `TlsConnectionIntegrationTests` case. `git diff --check` was clean.

## R2 — Source boundaries and effective options validation

Coder: GPT-6.1-Sol/high (`audit_foundations`); independent reviewer:
GPT-6.1-Sol/high (`audit_state`). First review rejected a remaining colon-key
bypass in the JSON provider; second review accepted the amended nine-file diff
against `0bbbe55`. R1 was committed as `0bbbe55`.

`src/SqlToAi/Configuration/UserConfiguration.cs:117–193` recursively inspects
effective provider paths, including repeated wrappers, colon-containing keys and
array indices, before merge. Known obsolete prefixes are rejected with safe
contract-path diagnostics. Every physical effective Servers object rejects exact
or case-only alias duplicates while same-spelling flattened fields and unknown
settings remain supported. Cross-source overrides remain valid.

`ConfigurationResolver.cs:113–166` shares server/grant, logging-retention and
SchemaBatch validation; `src/SqlToAi/Program.cs:133–149` invokes it after resolving
both the direct instance and separately bound DI options. Source validation stays
at the loader. Tests in `UserConfigurationTests.cs:315–496` cover normal and
flattened source attacks, base/active-environment loading, exit 1, empty stdout,
safe stderr and unchanged file bytes. `EffectiveOptionsValidationTests.cs:25–114`
exercises both effective options boundaries and preserved expansion/SQL-file
resolution; previously incomplete DI fixtures now supply valid servers.

Parent gates after approval: `dotnet build SqlToAi.slnx --warnaserror` returned
0 warnings/errors; full `dotnet test SqlToAi.slnx --no-build` returned 1,600 passed,
19 skipped, 0 failed. TRX skip names exactly match the 19 external TLS baseline
cases. `git diff --check` was clean.

## R3 — Safe discovery and failed provider loads

Coder: GPT-6-luna/high (`coder_r1`); independent reviewer: GPT-6.1-Sol/high
(`audit_foundations`). Reviewed against `7f88de2` (the R2 commit); production
approved, then a test-only amendment closed the reader-cancellation evidence gap.

`src/SqlToAi/Database/SchemaService.cs:122–148` distinguishes sanitized open errors
from raw post-open catalog failures. Timeout takes precedence over TLS and both
return safe 0106/0105 errors with the canonical settings path. Ordinary failures
retain owner-filtered static grants without logging raw provider exceptions.
`ServerDiscoveryTargetIsolationTests.cs:174–254` verifies successful open and
actual reader entry, safe responses/logs, fallback, and reader-triggered requested
cancellation. Existing safe-wrapper assertions remain intact.

Provider failures now identify `SqlToAi:Servers:<alias>:MetadataProvider` or
`AnonymizationRules`. During this slice the orchestrator found, and a second
read-only reviewer confirmed, that a failed optional SchemaPattern probe could
broaden a schema-specific exemption. `AnonymizationRuleProvider.cs:157–176,209–213`
now sends that failure to the existing fail-closed loader. The regression at
`AnonymizationRuleProviderOwnerTests.cs:441–483` checks both schemas remain masked
for the failed owner, no active-rule SELECT occurs, the empty cache is reused,
and the healthy owner's private-only exemption stays scoped. The old unsafe
probe-cancellation assertion was strengthened to default masking; successful
absent-column and schema-specific-column tests remain. Provider diagnostic
regressions cover both owners and omit raw exception/credential markers.

Parent gates after approval: warning-as-error build returned 0 warnings/errors;
full no-build tests returned 1,610 passed, 19 skipped, 0 failed. TRX skip names
exactly match the external TLS baseline. `git diff --check` was clean.

## R4 — One context through export, benchmark and exploration

Completed on 2026-10-18. Coder: GPT-6.1-Sol/high (`audit_surfaces`);
independent reviewer: GPT-6.1-Sol/high (`audit_foundations`), read-only approval
against `dd17ef3` (R3). The paused implementation was resumed without treating
its first build as completion evidence.

`src/SqlToAi/Cli/SchemaExportService.cs:48–73,117–145` resolves the entry target
once and shares that instance across discovery, selection, trigger/schema/detail
rendering and the canonical README. The nine typed `ISchemaService` export
overloads reuse existing cores; `SchemaService.VerifyTargetAsync` retains access
checks before SQL. `OptimizationBenchmarkService.cs:40–82` resolves once and
passes that same instance to typed comparison and both measurement calls; each
child retains its guard, rollback and timeout behavior. Unused string safety
entry points and the unused benchmark logger dependency were removed.

`SchemaExportTests.cs:337–354` under `tests/SqlToAi.Tests/Cli/` observes the same
instance through six real schema-service connections, alongside selected/empty
and all-object proxy coverage. `Database/AnalysisTargetIsolationTests.cs:227–243`
observes the same instance through a real comparison and two measurements.
`OptimizationBenchmarkServiceTests.cs:144–201` adds canonical arguments,
cancellation and failure-short-circuit checks. Invalid/unknown/denied targets
remain rejected before SQL/output creation. Existing mutation, USE and statement
guards remain; argument validation tests now exercise real service entry points.

`tools/SqlToAi.Exploration/Program.cs:16–23,54–63` derives manifest target and
rights from the actual shared scenario target. `ExplorationTests.cs:114–153`
covers alias ordering, uppercase PRIMARY, other-owner rights, an absent primary,
and the actual scenario requests. No server is inferred from dictionary order.

Parent gates after independent approval: `dotnet build SqlToAi.slnx --warnaserror`
returned 0 warnings/errors; full `dotnet test SqlToAi.slnx --no-build` returned
1,646 passed, 19 skipped, 0 failed. TRX skip names exactly match the external TLS
baseline. `git diff --check` was clean. These are managed checks; Native AOT
acceptance remains open.

## R5 — Real provider composition and public target binding

Completed on 2026-10-18. Coder: GPT-6.1-Sol/high (`audit_surfaces`);
independent reviewer: GPT-6.1-Sol/high (`audit_foundations`), read-only approval
against `0eb44d4` (R4), including all four new test files.

`src/SqlToAi/Mcp/SqlMcpToolRegistrations.cs:23–28,47–62,103–108` accepts raw nullable
JSON target inputs into the existing dispatcher, retaining catalog errors for
missing, null or incorrectly typed targets. Every public schema is taken from the
canonical registry and still requires string server/database arguments. The shared
description in `McpConstants.cs:60` explicitly states `alias/database` and discovery
tools. Unsupported boxed schema selections now fail with 0001 rather than silently
becoming an absent filter (`ToolDispatcher.cs:361`). Runtime instructions and
outgoing notes explain target-scoped tokens and literal foreign tokens.

`tests/SqlToAi.Tests/Database/ComposedTargetFixture.cs` composes real policy, guard,
rule provider/cache, anonymizer, token vault, resolver and query service. Its
connection decorator inspects unopened connections from the production factory
before replacing only I/O. `ComposedProviderIsolationTests.cs:14–143` forces cold
owner loads to overlap, then alternates/parallelizes warm calls; it checks physical
settings, owner credentials, actual rule-load counts, same-address failure masking,
raw-database cache separation, foreign tokens, cancellation/retry and denial before
connection creation. Earlier fake-only composition tests have narrower descriptions.

`Mcp/ComposedTargetBindingTests.cs:39–249` covers all target-bearing registered SDK
tools and generated CLI arguments, including invalid input matrices, production
routing, unusual raw names, required schemas/help and unsupported selections.
`ManagedMcpTargetBindingTests.cs:13–57` additionally launches the real managed host
and MCP client over stdio in an isolated temporary settings root. No live SQL server
or personal configuration is needed for those binding failures.

Parent gates after independent approval: warning-as-error build returned
0 warnings/errors; full no-build tests returned 1,927 passed, 19 skipped, 0 failed.
`temp/audit-gates/r5.trx` skip names exactly match the external TLS baseline.
`git diff --check` was clean. A separate actual-host JSON-RPC probe confirmed
19 advertised tools, nine catalog-coded failures, canonical server discovery,
unchanged settings bytes and exit 0. These checks do not establish Native AOT or
strict external TLS acceptance.

## R2a — Provider-equivalent environment path rejection

Completed on 2026-10-18. Coder: GPT-6-luna/high (`coder_r1`); independent
reviewer: GPT-6.1-Sol/high (`audit_state`), read-only approval against `f4145ed`
(R5), including the additional opposite mixed-delimiter regression.

The reviewer first reproduced the bypass with ten isolated StartupHarness child
processes: colon paths and one mixed form were accepted, normal double-underscore
legacy paths failed, and a legitimate colon-delimited Servers override affected
both direct/bound options. `SqlServerless` was incorrectly rejected in the
double-underscore form. These runs changed only child environments and temporary
settings stayed byte-identical.

`src/SqlToAi/Configuration/UserConfiguration.cs:106–119` now normalizes `__` to `:`
like the environment provider and reuses exact-or-descendant section matching,
limited to the `SqlToAi` product root. Diagnostics contain only the canonical known
section, never arbitrary key descendants or values. Lookalike names remain valid.
`tests/SqlToAi.Tests/Configuration/UserConfigurationTests.cs:274–335` covers all
three sections, exact names, descendants, case variants and both mixed forms,
safe stderr/exit 1, unchanged base/active files, harmless lookalikes and surviving
Servers overrides. The configuration documentation states these boundaries.

Parent gates after independent approval: warning-as-error build returned
0 warnings/errors; full no-build tests returned 1,943 passed, 19 skipped, 0 failed.
`temp/audit-gates/r2a.trx` skip names exactly match the external TLS baseline.
`git diff --check` was clean. No process-global or personal environment was edited.

## R6a — Explicit ADO.NET catalog and provider mapping

Completed on 2026-10-18. Coder: GPT-6.1-Sol/high (`aot_resolution`);
independent reviewer: GPT-6.1-Sol/high (`audit_foundations`), read-only approval
against `00f5b58` (R2a). The initially assigned reviewer hit a usage limit before
delivering a review; that interruption is not approval. Both new files were
included in the completed independent review.

`src/SqlToAi/Database/DatabaseCommandExecutor.cs:11–79` buffers the first resultset
using explicit static mappings and drains subsequent results. Execute/read/next
operations receive the request token; reader and command are disposed
asynchronously, including configuration, mapping, cancellation and late-result
failures. The caller retains the already-open connection. `DatabaseRow.cs:7–38`
performs case-insensitive SQL-alias lookup and null-aware numeric/string access;
it performs no reflection, dynamic activation or runtime mapper generation.

All 29 former Dapper calls in the rule/metadata providers, discovery, schema
service, both renderers and index suggestions now use these helpers and explicit
parameter types. SQL, ordering, output rendering and trigger ID/schema/parent
checks remain intact. Existing nullable row defaults and SQL byte/short/bit
conversions are retained. Rule-probe failure still enters the fail-closed loader.
Both `MetadataProvider` commands now honor their existing configured timeout,
including zero; unrelated catalog calls retain their provider default.

`tests/SqlToAi.Tests/Database/DatabaseCommandExecutorTests.cs:11–159` exercises
buffering, empty/NULL/defaults, numeric aliases, typed nullable parameters,
default/zero/positive timeouts, token propagation, cancellation during draining,
late result errors and awaited disposal. `Metadata/MetadataProviderTests.cs:29–80`
covers both configured timeouts and custom alias/NULL/last-row semantics. Four
real SQL integration queries also use ADO.NET; existing assertions were retained.
The complete managed suite includes available local SQL integration; external TLS
authentication remains outside that evidence.

Dapper was removed from the production reference and central package pin, not
moved into tests. Restored production/test assets contain no Dapper. The reviewer
independently compared the regenerated notices with the actual runtime graph:
52 matching dependencies, no difference. Only Dapper's attribution was removed;
shared license terms remain. Architecture and deployment comments now reflect the
actual implementation without changing the managed packaging route.

Parent gates after independent approval: `dotnet build SqlToAi.slnx --warnaserror`
returned 0 warnings/errors; full no-build tests returned 1,961 passed, 19 skipped,
0 failed. `temp/audit-gates/r6a.trx` skip names exactly match the external TLS
baseline; `git diff --check` was clean. The independent review and managed checks
do not substitute for R6b's unmasked Native AOT gate.

