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
