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
