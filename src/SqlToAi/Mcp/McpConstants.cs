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
