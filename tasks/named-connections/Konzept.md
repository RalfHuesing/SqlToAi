---
status: ready
---

# Named SQL Servers and qualified database targets

## Intention

Let agents address configured SQL Server instances with unambiguous targets such
as `test/AppDb` and `prod/AppDb` in one running Windows stdio process. Equal database
names on different servers must have independent policy, rules and token scopes.

## Settled decisions

- The administrator chooses server aliases; tool/database-grant identifiers carry
  the alias as a prefix. Keep the existing `database` argument as the selection
  point, rather than introduce a second per-query connection selector.
- This is a hard configuration/contract cut. No migration, compatibility layer,
  implicit default server, legacy lookup or primary-endpoint failover is wanted.
  The explicitly defined secondary-provider fallback is described below.
  Configuration JSON, examples, environment overrides and caller arguments must
  match the new contract.
- Switching among already loaded targets needs no restart. Editing server settings
  remains a restart operation; hot reload is outside this concept.

## Current behavior

[`SqlConnectionFactory`](../../src/SqlToAi/Database/SqlConnectionFactory.cs) currently
gets its endpoint from one top-level `SqlServer` section; `database` selects its
initial catalog. Global grants compare database names without an endpoint identity.
The singleton rule cache and token vault also lack server/database scope. Existing
metadata and rule servers are supporting providers, not selectable primary targets.
See [configuration](../../docs/configuration.md), [architecture](../../docs/architecture.md)
and [security](../../docs/security.md) for current behavior.

## Configuration and calls

This defines the required new contract, not the current implementation.
The `.invalid` production hostname is deliberately fictional. Both entries use the
Windows process identity; passwords are not embedded. TLS certificate validation
remains enabled, including for development unless deliberately configured otherwise.

```json
{
  "SqlToAi": {
    "Servers": {
      "test": {
        "Server": ".\\SQLTEST",
        "IntegratedSecurity": true,
        "TrustServerCertificate": false,
        "ConnectTimeoutSeconds": 30,
        "ExcludedDatabases": ["master", "model", "msdb", "tempdb"],
        "AnonymizationRules": { "Enabled": false }
      },
      "prod": {
        "Server": "sqlprod.example.invalid",
        "IntegratedSecurity": true,
        "TrustServerCertificate": false,
        "ConnectTimeoutSeconds": 30,
        "ExcludedDatabases": ["master", "model", "msdb", "tempdb"],
        "AnonymizationRules": { "Enabled": false }
      }
    },
    "Databases": {
      "SchemaOnly": [],
      "ReadOnlyAnonymized": [],
      "ReadOnly": ["prod/AppDb"],
      "ReadWrite": ["test/AppDb"]
    },
    "QueryExecution": {
      "DefaultRowLimit": 100,
      "MaxRowLimit": 1000,
      "CommandTimeoutSeconds": 30,
      "MaxScriptFileSizeBytes": 10485760
    }
  }
}
```

The different grants are deliberate examples, not recommended production access.
The shipped main template must keep every grant list empty. SQL authentication
uses per-server `IntegratedSecurity=false`, `UserId` and `Password`; examples must
show environment overrides rather than working credentials. For example,
`SqlToAi__Servers__test__Password` replaces the old single-server override path.

```json
{"database":"test/AppDb","query":"SELECT COUNT_BIG(*) AS Total FROM dbo.Orders"}
{"database":"prod/AppDb","object_name":"dbo.Orders"}
```

The first object is for `sql_execute_query`, the second for `sql_get_schema`.
`sql_list_servers` takes no arguments and returns a JSON array of canonical alias
strings in the existing text-content format; it performs no SQL work. Database
list and search require `server`; list takes `{"server":"test"}`, and search also
requires the unchanged `search_term`. They return the existing database-list format
with qualified permitted targets. The server receives raw InitialCatalog `AppDb`,
not `test/AppDb`.

## Scope

### Must

1. Add required `SqlToAi.Servers`, with at least one named endpoint. Each entry
   owns address, authentication, TLS, connect timeout, local excluded-database
   patterns and metadata/rule-provider settings. New endpoints grant no access.
   Aliases remain distinct policy owners even when they name the same physical
   endpoint; do not deduplicate endpoints by address.
2. Use `alias/database` syntax. Aliases match `[A-Za-z][A-Za-z0-9_-]*`, are looked
   up case-insensitively and have a canonical spelling formed by invariant ASCII
   lowercase, independent of their configuration-key casing. Reject exact and
   case-only duplicate keys within one JSON `Servers` object before configuration
   sources merge; the same logical alias may be overridden by the active
   environment-specific file or environment variables. Split only the first `/`;
   retain the remaining raw database name, including later slashes and Unicode.
   Reject empty parts, malformed aliases and unknown aliases before SQL. Do not trim
   significant database-name characters, Unicode-normalize or parse SQL multipart
   names. Alias `default` may be an ordinary explicitly configured name; it has no
   special behavior. Canonical qualified targets use the canonical alias and the
   exact raw database name.
3. Qualify every entry in the existing global `Databases` access lists. Validate
   malformed, unqualified and unknown-server grants at startup. Compare alias plus
   database, never only the suffix. Preserve default deny, restrictive overlapping
   access levels and `OrdinalIgnoreCase` database-policy matching. SQL Server
   applies its own identifier comparison; case-only distinct databases on a
   case-sensitive server retain the existing access-policy limitation.
4. Remove the old top-level `SqlServer`, `MetadataProvider` and `AnonymizationRules`
   configuration contracts. Reject obsolete sections and old environment prefixes
   (`SqlToAi__SqlServer__`, `SqlToAi__MetadataProvider__` and
   `SqlToAi__AnonymizationRules__`) with actionable configuration diagnostics.
   Check the required base file, the active environment-specific file and process
   environment keys even when a later source overrides the obsolete value. Startup
   failures exit with code 1 before SQL work or MCP transport. Safe stderr
   diagnostics identify the setting key/path, reason and correction guidance,
   never credential values. Preserve existing configuration expansion, provider
   SQL-file resolution and relative-path semantics for the surviving settings. Reject unqualified
   database arguments; never infer a server. Missing, malformed,
   unqualified or unknown database targets and missing, malformed or unknown server
   arguments return `SQL-AI-0001`; valid targets denied by grants or exclusions
   return `SQL-AI-0104`.
   Preserve existing MCP `isError` and CLI failure forms, primary connection errors
   `SQL-AI-0105`/`SQL-AI-0106`, required user-settings loading and safe stderr. Do
   not import, rewrite, back up or repair settings.
5. Resolve one immutable target context in every database-facing path: schema,
   exports, validation, queries, scripts, comparison, measurement, benchmarks,
   indexes, CLI, exploration and direct services. Use the raw name as SqlClient
   InitialCatalog. Where a response already identifies its target database, use
   the canonical qualified target in that existing field/header. Do not add a new
   row envelope or rewrite SQL/DDL identifiers. Never mutate singleton options or
   a global current-server variable. Exports here mean the existing schema-export
   tool/CLI, which must route correctly; deferred CSV/data export is not included.
6. Add `sql_list_servers`, returning canonical configured aliases without
   credentials, hostnames or SQL probes. Database list/search require a valid
   `server` alias, contact that server alone and return permitted qualified targets.
   Search retains `OrdinalIgnoreCase` substring matching on the raw database name
   only, not on the alias. Preserve the existing database-list output format.
   Discovery's catalog connection may use the login default database only to query
   `sys.databases`; it never creates an implicit tool target. Existing `database`
   arguments remain strings but now require qualification.
7. Scope exclusions to their server and match local raw DB-name glob patterns.
   Preserve precedence over grants and all SQL guards. SQL Server login permissions
   remain essential; this does not sandbox every cross-database reference, synonym
   or linked server. Granting `test/AppDb` never grants `prod/AppDb`.
8. Put optional MetadataProvider and AnonymizationRules settings under each server.
   Disabled providers make no connections. Metadata defaults to native descriptions;
   central rules default to no exemptions. Without a secondary server, providers use
   the selected primary endpoint, authentication, TLS and connect timeout. An
   explicit secondary `Database` is the raw provider database; if omitted, use the
   selected target's raw database. With a dedicated secondary server, require a raw
   `Database` and use that provider's own authentication and TLS settings; do not
   inherit primary credentials/TLS or fail over. Preserve the existing provider
   timeout semantics. This fallback is explicit contract behavior, not implicit
   provider inheritance.
9. Partition rule caches by policy owner (canonical server alias) and effective
   source, including the physical source database and rules table. In particular,
   primary-endpoint fallback without an explicit rule database must not reuse rules
   loaded for another target database. Rule database patterns match the target's raw
   database name inside that owner's context. Reusing one dedicated source requires
   explicitly configuring it for both owners. Failed loads retain default masking;
   no foreign cached exemptions may be borrowed.
10. Scope existing token issuance, reuse and resolution paths by canonical server
    alias plus raw database identity, preserving database characters ordinally in
    token scope even though grant/policy matching ignores database-name casing.
    Prevent token-ID collisions across scopes. Identical plaintext across targets
    must not yield interchangeable tokens. A foreign token cannot resolve through
    the vault but remains ordinary SQL text. Keep token resolution limited to the
    existing supported query paths; do not add parameter substitution or coverage
    for new tools.
11. Keep query row/command limits global and existing output content intact. Use
    options binding/validation, safe connection errors and endpoint-specific trust
    defaults. Update main/demo settings, configuration/security/tool/CLI docs,
    environment examples, tests and exploration fixtures in the implementing slice.
    Explain the hard cut in the public setup/update instructions and provide the
    complete new example before users replace settings manually.

### Out of scope

- Migration commands, legacy `SqlServer` compatibility, implicit defaults, automatic
  server switching/failover, per-call credentials or a new credential store.
- Hot reload, configuration editing tools, cross-server joins/transactions,
  other database engines, HTTP transport or a server-management UI.
- Changing query formatting, row limits or token storage lifetime beyond isolation.

## Verification

- Configure same-name databases on test and production with different records and
  rights; alternate and parallelize calls in one process without target/state leaks.
  `TEST/AppDb` resolves to canonical `test/AppDb`; `test/App/Db` resolves with raw
  database name `App/Db` and SqlClient receives exactly that InitialCatalog.
- All database-facing tools and CLI/exploration paths, including existing schema
  export, use the same parser, policy identity and endpoint. SqlClient receives
  only the raw physical database name. Existing target fields/headers identify the
  qualified target without a new row format; deferred data export remains absent.
- Legacy sections and environment prefixes fail clearly even when their values are
  overridden by an active later configuration source; no settings file is changed
  or created, and no alternate primary server is contacted. Missing, unqualified,
  malformed and unknown targets/aliases return
  `SQL-AI-0001`; grant/exclusion denials return `SQL-AI-0104` before database work.
- Parse first-slash targets, later slashes in database names and Unicode; reject
  invalid/empty aliases and case-variant or exact duplicate keys in one JSON
  `Servers` object. Verify canonical output `TEST/AppDb` -> `test/AppDb`. Overrides
  of one logical alias across configuration sources remain valid. Startup rejects
  malformed, unqualified and unknown-server grants.
- Listing aliases performs no SQL work and returns a JSON array of canonical alias
  strings in text content. List/search require `server`; search also requires
  `search_term` and matches only the raw suffix using existing `Contains` semantics.
  Discovery contacts only the selected server and returns allowed qualified names.
  Preserve static-grant fallback for existing catalog errors other than TLS failures
  and timeouts; deduplicate grants and apply that server's exclusions and access
  policy. Results make no claim that each database exists or is reachable. Requested
  cancellation propagates rather than triggering fallback.
- Grants/exclusions, SchemaOnly/data-access restrictions, provider fallback,
  central-rule caches and tokens remain isolated, including under parallel calls.
  A rule provider with neither secondary server nor database uses the selected
  target database, and cache entries for `AppDb` and `OtherDb` remain separate.
  A foreign token is not resolved in another target and stays literal SQL text.
- Provider-disabled paths make no connection. A server-less provider with a raw
  explicit database uses primary authentication/TLS/connect timeout; a dedicated
  provider requires its own raw database and uses its own authentication/TLS. A
  failed dedicated connection never retries against the primary endpoint.
- Safe failures and discovery disclose no credentials or full connection strings.
  TLS exceptions apply only to the selected primary/secondary endpoint.
- New main/demo JSON and documented environment examples load with the intended
  new contract; the main template still denies all databases. Native AOT and
  registered MCP/CLI binding cover the changed argument/configuration surface.
