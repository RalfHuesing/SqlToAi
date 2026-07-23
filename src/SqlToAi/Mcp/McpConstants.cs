#nullable enable

namespace SqlToAi.Mcp;

/// <summary>
/// Central repository of constant values used across the MCP layer.
/// All string literals that would otherwise be scattered across source files live here.
/// </summary>
internal static class McpConstants
{
    // -------------------------------------------------------------------------
    // Protocol
    // -------------------------------------------------------------------------

    /// <summary>MCP protocol version this server implements.</summary>
    internal const string ProtocolVersion = "2024-11-05";

    // -------------------------------------------------------------------------
    // Server identity
    // -------------------------------------------------------------------------

    /// <summary>Human-readable server name reported to the MCP client.</summary>
    internal const string ServerName = "SqlToAi";

    /// <summary>Server version string reported to the MCP client.</summary>
    internal const string ServerVersion = "1.0.0";

    /// <summary>
    /// Behavioral guidance sent once to the connecting MCP client via the <c>initialize</c>
    /// response's <c>instructions</c> field, so agents don't need this repeated in every
    /// tool description or query result.
    /// </summary>
    internal const string ServerInstructions = """
        This server may anonymize (scramble or hash) string column values to protect PII. `sql_get_schema` proactively marks each column "Anonymized: Yes/No" so you can see this before writing a query; `sql_execute_query` also reports it after the fact via an accompanying note listing the affected `Table.Column` names.

        If a task needs a column marked anonymized, tell the user explicitly which `Table.Column` is affected instead of treating the scrambled value as real data. For a plain table/view column, propose to the user that they add an exclusion rule (do not attempt to modify any exclusion or rule configuration yourself). For a view, computed column, or aggregation where the underlying source is unclear, first call `sql_get_object_references` (and inspect the base table's schema) to trace the real source column before proposing anything — only suggest an exclusion once you are confident which concrete table and column it maps to.

        Some anonymized columns are marked "Yes (searchable)" instead of plain "Yes" (schema) or listed separately as searchable in the `sql_execute_query` note. Their values are still not the real data, but they are stable, reusable tokens: the same underlying value always yields the same token, including across different tables/columns, so you can copy a token verbatim (unmodified, whole) into a later query's `WHERE`/`JOIN`/`LIKE`/`IN`/range predicate against that same column, and the server transparently resolves it back to the real value before executing — you still never see or need the real value to correlate rows. Never edit, truncate, concatenate, or guess a token; an unrecognized token simply matches nothing.
        """;

    // -------------------------------------------------------------------------
    // JSON-RPC methods
    // -------------------------------------------------------------------------

    internal const string MethodInitialize = "initialize";
    internal const string MethodInitialized = "notifications/initialized";
    internal const string MethodRootsListChanged = "notifications/roots/list_changed";
    internal const string MethodToolsList = "tools/list";
    internal const string MethodToolsCall = "tools/call";
    internal const string MethodPing = "ping";

    // -------------------------------------------------------------------------
    // Tool names (must match mcp-specification.md exactly)
    // -------------------------------------------------------------------------

    internal const string ToolListDatabases = "sql_list_databases";
    internal const string ToolSearchDatabases = "sql_search_databases";
    internal const string ToolValidateQuery = "sql_validate_query";
    internal const string ToolSearchObjects = "sql_search_objects";
    internal const string ToolGetSchema = "sql_get_schema";
    internal const string ToolGetSchemaForeignKeys = "sql_get_schema_foreign_keys";
    internal const string ToolGetSchemaIndexes = "sql_get_schema_indexes";
    internal const string ToolGetSchemaConstraints = "sql_get_schema_constraints";
    internal const string ToolGetTriggerDefinition = "sql_get_trigger_definition";
    internal const string ToolGetObjectReferences = "sql_get_object_references";
    internal const string ToolGetRoutineParameters = "sql_get_routine_parameters";
    internal const string ToolExecuteQuery = "sql_execute_query";

    // -------------------------------------------------------------------------
    // Tool argument keys
    // -------------------------------------------------------------------------

    internal const string ArgDatabase = "database";
    internal const string ArgSearchTerm = "search_term";
    internal const string ArgMaxResults = "max_results";
    internal const string ArgObjectName = "object_name";
    internal const string ArgObjectType = "object_type";
    internal const string ArgTriggerName = "trigger_name";
    internal const string ArgQuery = "query";
    internal const string ArgRequestedRowLimit = "requested_row_limit";
}
