---
status: draft
---

# Compact SQL Server and database environment context

## Intention

Give an agent the server version and selected database's compatibility context in
one small metadata call, so it can choose suitable SQL syntax and interpret
diagnostics without issuing ad hoc catalog queries.

## Settled direction

The user has requested this additional concept. The feature remains a small,
independent addition and should not become a broad server-monitoring subsystem.

## Current behavior

- [`sql_list_databases`](../../docs/tools.md#discovery-and-schema) discovers allowed
  database names. It does not report version, edition or compatibility level.
- [`SchemaService`](../../src/SqlToAi/Database/SchemaService.cs) already reads
  `sys.databases` for discovery. The inspected source has no existing
  SERVERPROPERTY/compatibility-level environment operation.
- Ad hoc catalog queries through generic execution may mask strings in protected
  modes. A dedicated bounded metadata contract avoids requiring raw query output
  or broadening database access for environment discovery.

## Scope

### Must

1. Add `sql_get_environment` with one required `database` argument. Resolve
   [qualified targets](../../docs/tools.md) through the shared boundary
   in the final combined feature set; do not add a separate legacy fallback.
2. Check configured access before connecting. SchemaOnly suffices; None and
   excluded targets remain denied. Use the selected endpoint's SqlClient factory,
   timeout, TLS and safe error conventions.
3. Run one fixed read-only metadata query in the selected database. Return a small
   JSON object with target alias/database, product version, edition, engine edition,
   database compatibility level, collation, state and read-only status. These are
   factual values; access-policy mode is separate from SQL Server's read-only flag.
4. Use an allowlisted subset of
   [SERVERPROPERTY](https://learn.microsoft.com/en-us/sql/t-sql/functions/serverproperty-transact-sql)
   and the current entry in
   [sys.databases](https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-databases-transact-sql).
   Missing/not-visible fields stay null with a short availability note. Do not
   equate unavailable metadata with a false flag or version zero. Connection or
   permission failure is an actual error, not an all-null success.
5. Return metadata only, without arbitrary custom SQL, login/user inventories,
   machine/host names, connection strings or secrets. This is explicitly selected
   system metadata, not a claim that ordinary result masking protects all strings.
6. Keep response and query size fixed/bounded and avoid server-wide DMV grants,
   application-data reads, a new cache or startup probes. Register through the
   usual tool/SDK/CLI paths with native AOT-compatible result serialization.

### Out of scope

- Feature-capability promises inferred only from version numbers; actual support
  can depend on compatibility level, edition, configuration and permissions.
- Automated migration advice, changing compatibility/collation/read-only state,
  health monitoring, workload statistics, Query Store or login inspection.
- An offline-database diagnostic guarantee: connecting directly to an unavailable
  database can fail before its state can be queried.

## Verification

- A permitted database returns version/edition and its actual compatibility level,
  collation and read-only flag; SchemaOnly works without application-data access.
- Same-name databases behind two configured server aliases return their own target
  and environment metadata. No database suffix alone determines the connection.
- Unknown/excluded/denied targets fail before metadata SQL. An unavailable database
  returns the existing safe connection error rather than misleading null metadata.
- Fixtures distinguish null/unavailable values from false/zero and SQL errors.
- Results contain no credentials, login names or provider connection strings.
  Ordinary record values and anonymization tokens are not queried.
- Registered MCP calls and the generated CLI agree on argument/result shape;
  repeated calls do not create retained state or change database settings.

## Relative value and dependencies

Low implementation cost and useful before syntax-sensitive work. It can be
implemented independently of named servers, then use their target resolver when
available. It is less urgent than fixing compact result representation or meeting
the confirmed multi-server workflow.

## Decisions awaiting approval

- Additional concept requested. Proposed field list and small metadata-only scope
  await final concept approval.
