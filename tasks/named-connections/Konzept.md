---
status: draft
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
  implicit default server, legacy lookup or fallback is wanted. Configuration JSON,
  examples, environment overrides and caller arguments must match the new contract.
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

## Proposed configuration and calls

This illustrates the intended new configuration, not the current implementation.
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
`sql_list_databases` uses `{"server":"test"}` and returns qualified permitted
targets. The server receives raw InitialCatalog `AppDb`, not `test/AppDb`.

## Scope

### Must

1. Add required `SqlToAi.Servers`, with at least one named endpoint. Each entry
   owns address, authentication, TLS, connect timeout, local excluded-database
   patterns and metadata/rule-provider settings. New endpoints grant no access.
2. Use `alias/database` as the proposed syntax. Aliases match
   `[A-Za-z][A-Za-z0-9_-]*`, with case-insensitive lookup and duplicate rejection.
   Split only the first `/`; retain the remaining raw database name, including
   later slashes and Unicode. Reject empty parts and unknown aliases before SQL.
   Do not trim significant DB-name characters, Unicode-normalize or parse SQL
   multipart names. Alias `default` may be an ordinary explicitly configured name;
   it has no special behavior.
3. Qualify every entry in the existing global `Databases` access lists. Validate
   malformed, unqualified and unknown-server grants at startup. Compare alias plus
   database, never only the suffix. Preserve default deny, restrictive overlapping
   access levels and current case-insensitive database-policy matching. SQL Server
   applies its own identifier comparison; case-only distinct databases on a
   case-sensitive server retain the existing access-policy limitation.
4. Remove the old top-level `SqlServer`, `MetadataProvider` and `AnonymizationRules`
   configuration contracts. Reject obsolete sections and old single-server
   environment override paths with actionable configuration diagnostics. Reject
   unqualified database arguments; never infer a server. Do not import, rewrite,
   back up or repair settings. Keep required user-settings loading and safe stderr.
5. Resolve one immutable target context in every database-facing path: schema,
   exports, validation, queries, scripts, comparison, measurement, benchmarks,
   indexes, CLI, exploration and direct services. Use the raw name as SqlClient
   InitialCatalog. Where a response already identifies its target database, use
   the canonical qualified target in that existing field/header. Do not add a new
   row envelope or rewrite SQL/DDL identifiers. Never mutate singleton options or
   a global current-server variable. Exports here mean the existing schema-export
   tool/CLI, which must route correctly; deferred CSV/data export is not included.
6. Add `sql_list_servers`, returning configured aliases without credentials,
   hostnames or connection probes. Database list/search require a `server` alias,
   contact that server alone and return permitted qualified database targets.
   Existing `database` arguments remain strings but now require qualification.
7. Scope exclusions to their server and match local raw DB-name glob patterns.
   Preserve precedence over grants and all SQL guards. SQL Server login permissions
   remain essential; this does not sandbox every cross-database reference, synonym
   or linked server. Granting `test/AppDb` never grants `prod/AppDb`.
8. Put optional MetadataProvider and AnonymizationRules settings under each server.
   Default to native descriptions and no central-rule exemptions. A missing
   secondary server uses the selected primary endpoint and any explicit secondary
   database; dedicated providers keep their own auth/TLS and never fail over.
   Configure settings directly, without provider aliases or implicit inheritance.
9. Partition rule caches by policy owner and effective source. Rule database
   patterns still match the raw name inside that server's context. Reusing one
   dedicated source requires explicitly configuring it for both owners. Failed
   loads retain default masking; no foreign cached exemptions may be borrowed.
10. Scope token issuance/reuse/resolution by server plus raw database identity.
    Prevent token-ID collisions across scopes. Identical plaintext across targets
    must not yield interchangeable tokens. Preserve unknown-token literal behavior:
    a foreign token cannot resolve through the vault but remains ordinary SQL text.
    Keep raw DB identifiers distinct for token scope, even when policy comparison
    intentionally ignores casing; conservative token non-reuse is safe.
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
- All database-facing tools and CLI/exploration paths, including existing schema
  export, use the same parser, policy identity and endpoint. SqlClient receives
  only the raw physical database name. Existing target fields/headers identify the
  qualified target without a new row format; deferred data export remains absent.
- Legacy sections/overrides and unqualified or unknown targets fail clearly; no
  fallback endpoint is contacted and no settings file is changed or created.
- Parse first-slash targets, later slashes in database names, Unicode, invalid/empty
  aliases and case-variant duplicate aliases. Startup rejects invalid grants.
- Listing aliases performs no SQL work; database discovery contacts only the
  explicitly selected server and returns only its allowed qualified names.
- Grants/exclusions, SchemaOnly/data-access restrictions, provider fallback,
  central-rule caches and tokens remain isolated, including under parallel calls.
- Safe failures and discovery disclose no credentials or full connection strings.
  TLS exceptions apply only to the selected primary/secondary endpoint.
- New main/demo JSON and documented environment examples load with the intended
  new contract; the main template still denies all databases. Native AOT and
  registered MCP/CLI binding cover the changed argument/configuration surface.

## Decisions awaiting approval

- Hard cut and qualified grants/targets are settled user decisions.
- Proposed delimiter: `/`, with first-separator parsing. No special default server.
- The fully elaborated concept and implementation limits await final approval.
