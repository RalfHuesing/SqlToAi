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

# M4.3 — Final concept audit and roadmap closure

> Superseded by the independent deep audit below. These historical claims are
> not current acceptance evidence; in particular, the Native AOT result hid
> third-party diagnostics, and the composed tests did not exercise a real rule cache.

Reviewed full repository state against `tasks/named-connections/Konzept.md` (Must 1–11, Out of scope, and Verification criteria).
Verdict: all concept requirements and acceptance criteria are completely satisfied; no open findings; accept M4 and close the named-connections roadmap.

## Final contract verification against Konzept.md

- **Must 1 (Named Servers Configuration):** `SqlToAi.Servers` requires at least one named server. Each entry holds server address, authentication, TLS, connect timeout, local excluded DB patterns, and secondary provider options. New endpoints grant no access by default. Distinct aliases pointing to the same physical host maintain completely independent policy contexts (`SamePhysicalEndpointUnderTwoAliases_RetainsIndependentOwners`).
- **Must 2 (Target Syntax and Canonicalization):** `alias/database` syntax validated by `DatabaseTargetParser.cs`. Aliases match `[A-Za-z][A-Za-z0-9_-]*`, with canonical invariant ASCII lowercase. Duplicate alias keys inside a single JSON `Servers` object are rejected at startup before merge. Only the first `/` is split, preserving all subsequent characters including dots, spaces, slashes, and Unicode. Empty parts, malformed aliases, and unknown aliases are rejected before any database operation.
- **Must 3 (Qualified Access Grants):** All entries in `Databases` access lists (`SchemaOnly`, `ReadOnlyAnonymized`, `ReadOnly`, `ReadWrite`) are qualified with `alias/database`. Startup validation rejects malformed, unqualified, or unknown-server grants. Policy evaluation matches alias plus database using `OrdinalIgnoreCase` for database names.
- **Must 4 (Hard Cut & Startup Diagnostics):** Obsolete top-level sections (`SqlServer`, `MetadataProvider`, `AnonymizationRules`) and old environment prefixes (`SqlToAi__SqlServer__`, etc.) are actively detected and rejected at startup with exit code 1 and clean stderr diagnostics without leaking sensitive configuration or credentials. Unqualified database arguments return `SQL-AI-0001`; unauthorized targets return `SQL-AI-0104`.
- **Must 5 (Target Context & Database Routing):** Every database-facing path resolves an immutable `DatabaseTargetContext`. SqlClient InitialCatalog receives exclusively the raw physical database name. Tool responses carry canonical qualified target names. No singleton option mutation or global current-server variables exist.
- **Must 6 (Server Discovery & Target Discovery):** `sql_list_servers` returns configured canonical server aliases with zero SQL probes or network connections. `sql_list_databases` and `sql_search_databases` require a valid `server` alias and return only permitted qualified targets. Search filters only on the raw database name substring.
- **Must 7 (Scoped Exclusions):** Local database exclusions match raw DB glob patterns scoped strictly to the owning server alias, taking precedence over grants and safety guards.
- **Must 8 (Scoped Supporting Providers):** MetadataProvider and AnonymizationRules are configured per server. Server-less providers inherit the primary endpoint, auth, TLS, and timeout with explicit or target database fallback; dedicated providers require their own raw database and never fall back to primary.
- **Must 9 (Partitioned Rule Caches):** Anonymization rule caches are strictly partitioned by `(PolicyOwner, Endpoint, SourceDatabase, RulesTable)`. Fallback target database rules are never reused across different databases.
- **Must 10 (Scoped Token Vault & Resolution):** Token issuance, storage, and resolution are scoped by `DatabaseTargetContext` with 64-bit global collision avoidance. Identical plaintext on different targets yields non-interchangeable tokens. Foreign tokens stay literal SQL text. Complete hard cut achieved: all unscoped methods and transitional `_legacyScope` state on `ITokenVault`, `TokenVault`, `IAnonymizer`, `Anonymizer`, `IQueryTokenResolver`, and `QueryTokenResolver` were completely deleted. Production code (`src/`), unit tests (`tests/`), and documentation (`docs/`) contain zero legacy methods, fallback artifacts, or transitional legacy comments (`rg -i legacy` returns zero hits across `src/`, `tests/`, and `docs/`).
- **Must 11 (Limits, Formats & Operational Documentation):** Global row and command limits remain unchanged. Main and demo settings files, README, SETUP, and documentation across `docs/` have been updated to the qualified multi-server schema with manual hard-cut migration instructions.

## Verification gates passed

- `dotnet build SqlToAi.slnx --warnaserror`: 0 warnings, 0 errors.
- `dotnet test SqlToAi.slnx`: 1,486 passed, 0 failed, 19 skipped (external TLS endpoints without live test server).
- `dotnet publish src/SqlToAi/SqlToAi.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -o temp/aot`: Clean Native AOT publish with 0 warnings and 0 errors.
- `ComposedTargetIsolationTests`: 12/12 passed (multi-target concurrency, same physical endpoint under distinct aliases, unusual raw database names, server discovery, and MCP SDK bindings).
- `git grep -i legacy src/ tests/ docs/`: 0 hits across all source, test, and documentation files.
- Clean working directory, all changes committed.

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
| P2 diagnostics | `src/SqlToAi/Metadata/MetadataProvider.cs:99,152` and `src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs:99,171,220` omit the server owner from provider failure diagnostics. Documented affected settings paths cannot be identified. Masking remains closed on failure. | R3 |
| P2 architecture | `src/SqlToAi/Cli/SchemaExportService.cs:49–63,127–139` and `src/SqlToAi/Database/OptimizationBenchmarkService.cs:41–62` repeatedly pass strings into resolving children rather than retaining one entry context (Must 5). No misrouting under stable startup options was demonstrated. | R4 |
| P2 correctness | `tools/SqlToAi.Exploration/Program.cs:45–50` reports rights for the first configured alias; the scenario targets `primary/DemoDB`. Direct context construction also misses uppercase-alias canonicalization. | R4 |
| P2 guard contract | Unused string overloads in `src/SqlToAi/Database/QuerySafetyValidator.cs:142` parse without configured-alias resolution; unknown aliases yield 0104 rather than 0001. Actual execution services resolve correctly first. | R4 |
| P2 tests | `tests/SqlToAi.Tests/Database/ComposedTargetIsolationTests.cs:34–46,82–94,367` replaces the rule cache and SDK service validation with fakes. Those tests do not prove their claimed real cache/host composition. Existing focused routing/provider tests remain useful. | R5 |
| P2 public contract | `src/SqlToAi/Mcp/ToolRegistry.cs:96` and corresponding SDK parameter descriptions still say only "Target database name. Required." The M4.2 claim of qualified descriptions has no supporting assertion. | R5 |
| P1 acceptance evidence | `src/SqlToAi/SqlToAi.csproj:5` globally suppresses IL2104/IL3053, added by `912701a`. An unmasked detailed AOT publish emitted 103 IL diagnostics, including 17 IL3050 dynamic-code warnings. Exit 0 with suppression is not warning-free AOT compatibility. | R6 |
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

