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

Date: 2026-10-18. A fresh GPT-6.1-Sol/high agent (`final_audit`) independently
reviewed the complete contract, current wiring, history and recorded execution
evidence. It did not implement any correction or rerun the parent's gates.
History scope: `988b641..b7b2eaa`, especially `41a4271..b7b2eaa`; remediation:
`cab47d3..b7b2eaa`. It used semantic C# navigation and actual source/diff reads,
not prior checkbox claims. No AiNetLinter tool was exposed.

**Verdict: no additional blocking application finding in the reviewed scope;
full feature acceptance remains open because Native AOT failed.** The former
blanket closure and warning-free publish claims are withdrawn. M4.2, M4.3 and
the M4/roadmap aggregates remain open. External TLS stays unverified rather than
being counted as passed.

The following source/test locations are physical lines at `b7b2eaa`. The final
five-file correction changes only README/SETUP/tool documentation and XML
comments in IAnonymizer/SqlToAiOptions; its independent review confirmed no
non-comment C# differences. QueryExecutionOptions moves from line 183 to 180 in
that comment-only delta. Historical slice sections below cite their own reviewed
snapshot; they are not substituted for this final matrix.

## Concept requirement verification

| Must | Final implementation evidence and meaningful tests |
| --- | --- |
| 1 | Named ownership and required configuration: `src/SqlToAi/Configuration/SqlToAiOptions.cs:9,18`, `src/SqlToAi/Configuration/ConfigurationResolver.cs:110`. Binding/default tests: `tests/SqlToAi.Tests/Database/DatabaseTargetResolverTests.cs:108`; owner-specific connection construction: `tests/SqlToAi.Tests/Database/NamedPrimaryRoutingTests.cs:38`. These inspect real SqlClient construction without contacting multiple live endpoints. |
| 2 | First-slash/raw-name parsing: `src/SqlToAi/Database/DatabaseTargetParser.cs:8`; configured resolution: `src/SqlToAi/Database/DatabaseTargetResolver.cs:37`; every JSON source occurrence: `src/SqlToAi/Configuration/UserConfiguration.cs:135`. Tests: `tests/SqlToAi.Tests/Database/DatabaseTargetResolverTests.cs:12,69,95` and `tests/SqlToAi.Tests/Configuration/UserConfigurationTests.cs:363`. Only aliases are ASCII-canonicalized; raw database characters and ordinal identity are retained. |
| 3 | Qualified grants, restrictive overlap and default deny: `src/SqlToAi/Security/TargetAccessPolicy.cs:11`; startup grant validation: `src/SqlToAi/Configuration/ConfigurationResolver.cs:144,167`. Tests: `tests/SqlToAi.Tests/Database/NamedPrimaryRoutingTests.cs:103,157`. |
| 4 | Source loading, safe hard-cut rejection and equivalent DI validation: `src/SqlToAi/Configuration/UserConfiguration.cs:51,99,135`, `src/SqlToAi/Program.cs:133,144`; target binding and required public schemas: `src/SqlToAi/Mcp/SqlMcpToolRegistrations.cs:27,47`. Tests: `tests/SqlToAi.Tests/Configuration/UserConfigurationTests.cs:274`, `tests/SqlToAi.Tests/Configuration/EffectiveOptionsValidationTests.cs:26,42`, `tests/SqlToAi.Tests/Mcp/ComposedTargetBindingTests.cs:39`, `tests/SqlToAi.Tests/Mcp/ManagedMcpTargetBindingTests.cs:13`. The SDK multi-error boundary below is explicit. |
| 5 | Request-local targets: `src/SqlToAi/Database/QueryExecutionService.cs:93`, `src/SqlToAi/Database/ScriptExecutionService.cs:47`, `src/SqlToAi/Database/SchemaService.cs:244,379,503`, `src/SqlToAi/Cli/SchemaExportService.cs:52,117`, `src/SqlToAi/Database/OptimizationBenchmarkService.cs:42`, `tools/SqlToAi.Exploration/Program.cs:19`. Actual child-service context identity: `tests/SqlToAi.Tests/Cli/SchemaExportTests.cs:337`, `tests/SqlToAi.Tests/Database/AnalysisTargetIsolationTests.cs:227`; scripts/exploration: `tests/SqlToAi.Tests/Database/ScriptExecutionTargetIsolationTests.cs:118`, `tests/SqlToAi.Tests/Exploration/ExplorationTests.cs:114`. |
| 6 | Pure server listing, selected catalog and raw-name search: `src/SqlToAi/Database/SchemaService.cs:79,84,179,197`. Tests: `tests/SqlToAi.Tests/Database/ServerDiscoveryTargetIsolationTests.cs:92,134,180,272`, `tests/SqlToAi.Tests/Mcp/ComposedTargetBindingTests.cs:62`. Reader timeout/TLS/cancellation cases enter after successful connection open. |
| 7 | Owner-local exclusions and existing SQL guards: `src/SqlToAi/Security/TargetAccessPolicy.cs:15`, `src/SqlToAi/Database/QuerySafetyValidator.cs:80,85,90,101,108`. Tests: `tests/SqlToAi.Tests/Database/NamedPrimaryRoutingTests.cs:126,175,195,270`, `tests/SqlToAi.Tests/Database/ComposedProviderIsolationTests.cs:103`. Cross-database references, synonyms and linked servers do not acquire a new sandbox guarantee. |
| 8 | Explicit source fallback and independent dedicated-provider credentials/TLS: `src/SqlToAi/Database/SecondaryConnectionBuilder.cs:48,82`, `src/SqlToAi/Metadata/MetadataProvider.cs:67,116`. Tests: `tests/SqlToAi.Tests/Database/SecondaryConnectionBuilderOwnerTests.cs:55,175,189,257`, `tests/SqlToAi.Tests/Metadata/MetadataProviderOwnerTests.cs:67`, `tests/SqlToAi.Tests/Anonymization/AnonymizationRuleProviderOwnerTests.cs:187,327`. Both metadata timeouts are applied; no dedicated-endpoint failover is added. |
| 9 | Owner/source cache key and fail-closed probes: `src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs:30,124,180,209`. Tests: `tests/SqlToAi.Tests/Anonymization/AnonymizationRuleProviderOwnerTests.cs:266,385,441`, `tests/SqlToAi.Tests/Database/ComposedProviderIsolationTests.cs:14,61,84`. Real providers/cache/vault share a production-factory decorator replacing only I/O; cold owner loads are forced to overlap. |
| 10 | Ordinal token scope, global ownership and value-only SQL substitution: `src/SqlToAi/Anonymization/TokenVault.cs:25,58,86`, `src/SqlToAi/Database/QueryTokenResolver.cs:39`, `src/SqlToAi/Database/SqlLiteralScanner.cs:12,30,36`. Tests: `tests/SqlToAi.Tests/Anonymization/TokenVaultScopeTests.cs:12`, `tests/SqlToAi.Tests/Database/QueryExecutionTargetScopeTests.cs:90`, `tests/SqlToAi.Tests/Database/ComposedProviderIsolationTests.cs:14`. Foreign tokens remain ordinary SQL text; no new parameter/tool resolution paths are added. |
| 11 | Global limits and mandatory encrypted connections: `src/SqlToAi/Configuration/SqlToAiOptions.cs:183` (180 after comment delta), `src/SqlToAi/appsettings.json:41,55`, `src/SqlToAi/Database/SqlConnectionFactory.cs:52`. Tests: `tests/SqlToAi.Tests/Database/QueryExecutionServiceOptionsTests.cs:28,41,97`, `tests/SqlToAi.Tests/Database/NamedPrimaryRoutingTests.cs:38`. README/SETUP now state the hard cut and provide a complete minimal new settings document before replacement instructions; main grants stay empty. Native AOT verification is still unmet. |

The auditor's complete semantic factory-reference result contained 15 production
CreateTargetConnection calls in eight files with no analysis omissions. Relevant
service cores, DI/host wiring and outputs were also read; static references alone
were not treated as runtime evidence. The 29 former Dapper calls and 29 explicit
executor replacements, mappings, parameters, NULL/defaults, numeric conversions,
timeouts, cancellation and awaited disposal were checked. Reviewed test changes
did not weaken assertions; the unsafe rule-probe expectation was strengthened.

## Actual final gates and scope boundaries

- `dotnet build SqlToAi.slnx --warnaserror`: 0 warnings/errors.
- `dotnet test SqlToAi.slnx --no-build`: 1,961 passed, 19 skipped, 0 failed;
  `temp/audit-gates/final.trx`. The final suite ran after independent approval of
  the documentation/comment delta. The auditor separately parsed `r6b.trx` and
  sampled important alias-leak, repeated-JSON, environment-normalization, reader
  timeout/TLS, shared-context, real cold-cache and managed-stdio regressions as
  Passed, rather than trusting test names.
- `git diff --check`: clean. Related Markdown links, JSON examples and claims are
  checked during evidence synchronization. The complete SETUP JSON was also run
  through the actual managed StartupHarness CLI in an isolated temporary root:
  exit 0, canonical `["primary"]`, empty stderr and unchanged settings bytes;
  no SQL connection was needed.
- Exact required Native AOT command: exit 1, recorded in
  `temp/aot-final.log`; the separate relaxed diagnostic publish has 57 IL warnings
  including three IL3050. The auditor independently counted diagnostics and
  checked both recorded log hashes. See [R6b](#r6b--unmasked-native-aot-gate-and-official-driver-boundary)
  for commands, origins and upstream evidence. Generated AOT directories are
  absent; logs/TRX are retained.

All 19 skips exactly match the baseline and belong to
`tests/SqlToAi.Tests/Integration/TlsConnectionIntegrationTests.cs`:

| Test | Exact skipped combinations | Count |
| --- | --- | --- |
| EndpointPolicy_RejectsOrObservesEncryptedSession | TrustedTcp × Primary/Metadata/Rules × trust=false | 3 |
| EndpointPolicy_RejectsOrObservesEncryptedSession | UntrustedTcp/LocalUntrusted × Primary/Metadata/Rules × trust=false/true | 12 |
| PrimaryDefault_RealCertificateFailureReachesServiceError | LocalUntrusted/UntrustedTcp | 2 |
| LocalFallback_UsesPrimaryPolicyDespiteOppositeSecondarySetting | primaryTrust=false/true | 2 |

Available ordinary SQL integration tests pass using the explicitly documented
fixture-only certificate-trust overlay. They do not prove strict certificate
authentication. No second live endpoint or separate Windows profile is claimed.

Target-only missing/null/malformed/unknown SDK arguments return 0001 when other
arguments are bindable. Actual isolated stdio calls `sql_execute_query {}` and
`sql_search_databases {}` still return generic SDK errors because query/search_term
are also absent. The independent auditor classified this as an existing binding
boundary, not an added routing/security blocker: the concept specifies no error
precedence for multiply invalid requests. `docs/tools.md` now states this limit;
the audit does not promise catalog codes for every malformed multi-error request.

No personal settings, global environment, deployment, release or push was changed.
The remaining required decision is obtaining a warning-free supported driver or
explicitly maintaining a driver change; this audit does not authorize that fork
or silently waive the Native AOT criterion.

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
| R6a: explicit ADO.NET mapping | `fe52819` | 1,961 / 19 / 0 |
| R6b: visible AOT diagnostics | `b7b2eaa` | 1,961 / 19 / 0; Native AOT failed |

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

## R6b — Unmasked Native AOT gate and official driver boundary

Executed on 2026-10-18. Coder: GPT-6-luna/high (`coder_r1`); independent
reviewer: GPT-6.1-Sol/high (`audit_foundations`), read-only approval against
`fe52819`. `src/SqlToAi/SqlToAi.csproj:5` removes IL2104/IL3053 suppression;
the pre-existing NU1510 setting and TreatWarningsAsErrors remain. No descriptor,
feature removal, driver fork, package downgrade or replacement suppression was
introduced. `docs/development.md` documents the exact gate and separates it from
managed packaging.

After approval, parent `dotnet build SqlToAi.slnx --warnaserror` returned
0 warnings/errors. Full no-build tests returned 1,961 passed, 19 skipped, 0 failed;
`temp/audit-gates/r6b.trx` skip names exactly match the external TLS baseline.
The diff check was clean.

Actual required command, without property overrides:

```powershell
dotnet publish src/SqlToAi/SqlToAi.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -o temp/aot
```

**Exit 1; acceptance failed.** `temp/aot-final.log` records three IL2104 errors
for Microsoft.Data.SqlClient, System.Configuration.ConfigurationManager and
Microsoft.Data.SqlClient.Internal.Logging, plus IL3053 for SqlClient. The SDK
reports MSB3077 after the native compiler emits those diagnostics. They are
visible failures, not a clean publish.

Diagnostic-only command:

```powershell
dotnet publish src/SqlToAi/SqlToAi.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:TrimmerSingleWarn=false -p:TreatWarningsAsErrors=false -o temp/aot-diagnostic-after
```

Exit 0 with **57 IL warning lines**, recorded in `temp/aot-diagnostic-after.log`:
IL2026 26, IL2057 7, IL2067 4, IL2070 6, IL2072 7, IL2075 2, IL2111 2, IL3050 3.
Diagnostic-origin namespaces: SqlClient 50, SqlTypes 2, System.Configuration 5.
No diagnostic originates in SqlToAi or Dapper. Relative to the baseline, the
46 Dapper warnings and its 14 IL3050 warnings are gone. This relaxed diagnostic
run is not an accepted gate or runtime correctness proof.

An independent GPT-6.1-Sol/high investigation inspected the installed 7.0.2 and
official 7.1.1 code and linker substitutions; upgrading alone does not remove
the reachable reflection/JSON/authentication/UDT paths. Upstream status checked
2026-10-18: [7.1.1 release](https://github.com/dotnet/SqlClient/releases/tag/v7.1.1)
predates the [partial JSON source-generation correction](https://github.com/dotnet/SqlClient/pull/4683)
merged on October 1. [Zero-warning AOT support](https://github.com/dotnet/SqlClient/issues/1947)
and [trimming-compatible UDT deserialization](https://github.com/dotnet/SqlClient/issues/4413)
remain open; [AOT-safe authentication registration](https://github.com/dotnet/SqlClient/pull/4806)
is separate upstream work. The [7.1.1 linker substitutions](https://github.com/dotnet/SqlClient/blob/v7.1.1/src/Microsoft.Data.SqlClient/src/Resources/ILLink.Substitutions.xml)
do not offer a supported removal of those paths.

A warning-free official dependency or an explicitly maintained driver change is
still required. Introducing such a fork silently would add maintenance and
support obligations beyond the application remediation. R6b/M4.2 and aggregate
feature acceptance remain open; no production-grade or warning-free AOT claim is
made. Independent final audit must preserve this boundary.

Generated `temp/aot`, `temp/aot-diagnostic` and `temp/aot-diagnostic-after`
directories are absent after cleanup. Paths were verified inside this checkout's
temp directory and contained no tracked files. Diagnostic logs and TRX files are
retained. SHA-256 of `temp/aot-final.log`:
`F1105CB7B91CEEB6A65F1382CEE07CCB7E1CD75A80857DE331571D5B541EC1A7`;
of `temp/aot-diagnostic-after.log`:
`06FE2F8CE323BC155E9082B3DF19738D83EF057DE8B8CDBC839F194B647A1D0B`.

## R6c — Final independent audit and public documentation correction

Completed on 2026-10-18. A fresh GPT-6.1-Sol/high auditor (`final_audit`) reviewed
clean `b7b2eaa`, all concept requirements and the actual TRX/AOT logs. The final
matrix above replaces earlier inaccurate closure claims and intermediate line
references. M3.4 evidence is supplied; M4.2/M4.3 and the feature aggregate remain
open because the required Native AOT gate fails.

The audit found public-documentation and XML-comment corrections, implemented by
GPT-6-luna/high (`coder_r1`) and independently approved by `final_audit`:
README/SETUP now warn explicitly about the breaking configuration cut before
startup and include a complete minimal new JSON document; obsolete environment
paths are described with their real section boundaries; tool docs distinguish
target errors from SDK binding failures for other invalid arguments. Three stale
XML comments no longer cite removed APIs or inline removal history. The reviewer
compared both C# files outside XML comments and confirmed zero code differences.

After that approval, parent warning-as-error build returned 0 warnings/errors;
the complete no-build test run returned 1,961 passed, 19 exact baseline TLS skips,
0 failed (`temp/audit-gates/final.trx`). The new SETUP JSON also loaded through the
actual managed host in an isolated temporary root: exit 0, canonical server list,
empty stderr and unchanged settings. No SQL or personal configuration was used.
`git diff --check` is clean. The final documentation check verified 93 local links
and 33 heading anchors across all eight changed Markdown files; it also corrected
the ADO.NET heading anchor rather than leaving a broken roadmap link. No additional
runtime code or AOT-success claim is introduced by this evidence synchronization.
The fresh independent auditor approved the synchronized task documentation and
reproduced the final TRX and link/anchor counts. R6c records audit execution, not
closure of the failed R6b/M4.2/M4.3 acceptance.

## R7 — Obsolete optional logging removal

User-approved follow-up on 2026-10-18; reviewed against `84dc1be`. The user
explicitly deferred AOT/package work and requested complete removal of the unused
optional logging fragment. Coder: GPT-6-luna/high (`cleanup_logging`);
independent read-only reviewer: GPT-6.1-Sol/high (`final_audit`).

Removed the unused option/property contract, template section, path-resolution
branch, entire startup cleanup service and its DI/startup calls. No disabled stub,
compatibility branch, migration, replacement warning or deletion of existing user
settings/logs was introduced. Searches across the tracked working tree, existing
filenames and the current semantic snapshot found no obsolete identifiers or
references. Unrelated line-delimited query-result formats remain supported.

`src/SqlToAi/Configuration/LoggingOptions.cs:9,12,20,28` retains the log root,
application/error sinks and root resolution. `ConfigurationResolver.cs:41,148`
retains path resolution and sink-retention validation. `src/SqlToAi/Program.cs:34,37,197`
goes directly from normal logger/service setup to command construction and MCP
registration; `Program.cs:244,258` still passes the same retained-file counts to
Serilog. The reviewer parsed the template and verified that its remaining JSON is
structurally identical to `84dc1be` after removal of the obsolete section.

Four tests of the removed service and one feature-only path test were deleted.
Surviving real application/error log probes, default/relative/absolute path tests,
user-file preservation, DI/startup and stdio tests remain. In particular,
`tests/SqlToAi.Tests/Configuration/UserPathTests.cs:24,102,157` exercises real
options/DI and log destinations; `SqlToAiOptionsTests.cs:39,40,48` retains explicit
sink defaults and shipped-template binding. SETUP and canonical configuration/
architecture documentation no longer describe the removed fragment. The concept
and roadmap record the approved scope without reintroducing retired names.

After independent approval, parent `dotnet build SqlToAi.slnx --warnaserror`
returned 0 warnings/errors; `dotnet test SqlToAi.slnx --no-build` returned
1,956 passed, 19 skipped, 0 failed (`temp/audit-gates/r7.trx`). All 19 skip names
exactly match the documented external TLS baseline. The five-test reduction from
1,961 is entirely the deleted feature-only cases, reviewed independently.
`git diff --check`, JSON parsing and the obsolete-reference scan passed. No Native
AOT publish, package upgrade, deployment or personal-configuration change was run.

## R8 — Current stable NuGet declarations

User-approved follow-up on 2026-10-18; reviewed against `5be0da2`. Coder:
GPT-6-luna/high (`upgrade_packages`); different independent read-only reviewer:
GPT-6-luna/high (`review_packages`). The slice updates dependencies; the separate
Native AOT acceptance remains deferred and open.

All 27 central declarations, including both global analyzers and the explicit
PKCS transitive pin, match the latest **listed stable** releases in official
NuGet V3 registration metadata. Twenty declarations changed; seven were already
current. No preview, framework/SDK change, downgrade or additional warning/audit
suppression was introduced. Updated versions:

| Declaration | Previous | Current |
|---|---|---|
| Meziantou.Analyzer | 3.0.290 | 3.0.307 |
| Microsoft.CodeAnalysis.NetAnalyzers | 10.0.401 | 10.0.402 |
| Microsoft.Data.SqlClient | 7.0.2 | 7.1.1 |
| Microsoft.SqlServer.TransactSql.ScriptDom | 170.3.0 | 180.140.0 |
| System.Text.Encoding.CodePages | 10.0.10 | 10.0.13 |
| All ten declared Microsoft.Extensions packages | 10.0.10 | 10.0.13 |
| System.CommandLine | 2.0.9 | 2.0.13 |
| coverlet.collector | 10.0.1 | 10.1.0 |
| Microsoft.NET.Test.Sdk | 18.7.0 | 18.11.0 |
| xunit.runner.visualstudio | 3.1.5 | 4.0.1 |
| xunit.v3 → xunit.v3.mtp-off | 3.2.2 | 4.0.2 |

The unchanged current entries are ModelContextProtocol 2.2.0,
System.Security.Cryptography.Pkcs 10.0.13, Serilog 4.4.0,
Serilog.Extensions.Hosting 10.0.0, Serilog.Settings.Configuration 10.0.1,
Serilog.Sinks.Console 6.1.1 and Serilog.Sinks.File 7.0.0.

The xUnit package variant deliberately preserves the existing VSTest runner,
category filters, TRX output and Coverlet collector. This is the
[official xUnit v3 variant with MTP integration disabled](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform#choosing-the-microsoft-testing-platform-version),
using the current framework and adapter versions. No test assertions, source
code, CI/release scripts or test configuration were changed; no `global.json`
was added. The reviewer verified the variant against official documentation and
independently checked important listed/latest feed entries.

### Resolved graph boundary

`dotnet package list --project SqlToAi.slnx --outdated --include-transitive
--format json` reports **zero outdated top-level packages**, but 19 unique
transitive-only differences across the four projects remain:

| Transitive packages | Resolved | Latest stable |
|---|---|---|
| Microsoft.Bcl.AsyncInterfaces | 6.0.0 | 10.0.13 |
| Microsoft.Extensions.AI.Abstractions | 10.8.3 | 10.11.0 |
| Microsoft.Extensions.Caching.Abstractions, Microsoft.Extensions.Diagnostics.Abstractions, Microsoft.Extensions.Hosting.Abstractions | 10.0.10 | 10.0.13 |
| Microsoft.Extensions.DependencyModel | 10.0.0 | 10.0.13 |
| Microsoft.Bcl.Cryptography, Microsoft.Extensions.Caching.Memory, System.Configuration.ConfigurationManager, System.Diagnostics.EventLog, System.Security.Cryptography.ProtectedData, System.Threading.RateLimiting | 9.0.18 | 10.0.13 |
| Microsoft.IdentityModel.Abstractions, Microsoft.IdentityModel.JsonWebTokens, Microsoft.IdentityModel.Logging, Microsoft.IdentityModel.Protocols, Microsoft.IdentityModel.Protocols.OpenIdConnect, Microsoft.IdentityModel.Tokens, System.IdentityModel.Tokens.Jwt | 8.16.0 | 8.23.0 |

Some projects resolve the Extensions/EventLog entries to 10.0.13 already; the
table records the older resolutions still reported elsewhere. The restored
assets show these dependency floors in current upstream parents: xunit.v3.common
for AsyncInterfaces; ModelContextProtocol/Core for AI, caching and hosting;
SqlClient for the 9.0.18 and IdentityModel dependencies; the IdentityModel chain
for its own family; Serilog.Settings.Configuration for DependencyModel. This
slice updates repository declarations and uses their resolved graph; it adds no
independent override pins for these upstream dependencies. These are disclosed
version differences, not proven incompatibilities or a claim that every resolved
package is latest.

### Verification

After independent approval, parent forced a fresh restore with
`--force --force-evaluate --no-http-cache -p:NuGetAudit=true
-p:NuGetAuditMode=all -p:NuGetAuditLevel=low -p:TreatWarningsAsErrors=true`:
exit 0 for all four projects, no vulnerability or audit-source diagnostics.
`dotnet build SqlToAi.slnx --no-restore --warnaserror` returned 0 warnings/errors.
The complete `dotnet test SqlToAi.slnx --no-build` run returned 1,956 passed,
19 skipped, 0 failed (`temp/audit-gates/r8.trx`); all skip names exactly match the
original external TLS baseline. TLS acceptance remains unverified.

The unchanged documented offline category filter with
`RunConfiguration.TreatNoTestsAsError=true` and `--collect 'XPlat Code Coverage'`
also passed: 1,907 passed, 0 skipped/failed. Coverlet produced a nonempty Cobertura
report in `temp/r8/offline-coverage/`; this verifies the retained filter and
collector with the updated test packages.

Notices were regenerated using the already installed license tool. Parent and
reviewer independently compared actual production assets against every notice
package/version: all 53 runtime dependencies match, including native assets;
no test/analyzer dependencies are included. `git diff --check` and the tracked
obsolete-reference scan pass. No publish, deployment, user-wide tool installation
or personal-settings change was performed.

Ignored supporting reports are in `temp/r8/`: `version-matrix.csv`,
`package-list-outdated.json`, `outdated-transitives.csv`,
`transitive-parent-ranges.txt`, `parent-restore-audit.log`, `parent-build.log` and
`parent-tests.log`. The committed declarations, notices and this explicit graph
boundary retain the reviewable result without depending on temporary reports.

