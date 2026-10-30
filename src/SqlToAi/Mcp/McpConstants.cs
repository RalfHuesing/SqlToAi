namespace SqlToAi.Mcp;

/// <summary>
/// Central repository of constant values used across the MCP layer.
/// All string literals that would otherwise be scattered across source files live here.
/// </summary>
internal static class McpConstants
{
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

        Some anonymized columns are marked "Yes (searchable)" instead of plain "Yes" (schema) or listed separately as searchable in the `sql_execute_query` note. These values are reversible tokens, not the underlying data. Identical plaintext reuses a token across tables/columns only within the same canonical server alias and exact raw database name, compared ordinally. Copy a complete token verbatim into a SQL string literal on the same target; supported query paths resolve it before execution. A token from another target, or an unknown token, remains ordinary literal SQL text and can match actual stored text. Never edit, truncate, concatenate, or guess tokens; their fragments and ordering do not preserve the underlying value relationship.
        """;

    // -------------------------------------------------------------------------
    // Tool names (documented in docs/tools.md)
    // -------------------------------------------------------------------------

    internal const string ToolListServers = "sql_list_servers";
    internal const string ToolListDatabases = "sql_list_databases";
    internal const string ToolSearchDatabases = "sql_search_databases";
    internal const string ToolGetEnvironment = "sql_get_environment";
    internal const string ToolValidateQuery = "sql_validate_query";
    internal const string ToolSearchObjects = "sql_search_objects";
    internal const string ToolGetSchema = "sql_get_schema";
    internal const string ToolExportSchema = "sql_export_schema";
    internal const string ToolGetSchemaForeignKeys = "sql_get_schema_foreign_keys";
    internal const string ToolGetSchemaIndexes = "sql_get_schema_indexes";
    internal const string ToolGetSchemaConstraints = "sql_get_schema_constraints";
    internal const string ToolGetTriggerDefinition = "sql_get_trigger_definition";
    internal const string ToolGetObjectReferences = "sql_get_object_references";
    internal const string ToolGetRoutineParameters = "sql_get_routine_parameters";
    internal const string ToolExecuteQuery = "sql_execute_query";
    internal const string ToolExportData = "sql_export_data";
    internal const string ToolExecuteFile = "sql_execute_file";
    internal const string ToolCompareQueries = "sql_compare_queries";
    internal const string ToolMeasurePerformance = "sql_measure_performance";
    internal const string ToolBenchmarkOptimization = "sql_benchmark_optimization";
    internal const string ToolSuggestIndexes = "sql_suggest_indexes";

    // -------------------------------------------------------------------------
    // Tool argument keys
    // -------------------------------------------------------------------------

    internal const string DatabaseTargetDescription = "Required qualified target in alias/database form, for example test/AppDb. Discover aliases with sql_list_servers and permitted targets with sql_list_databases.";

    internal const string ArgServer = "server";
    internal const string ArgDatabase = "database";
    internal const string ArgScope = "scope";
    internal const string ArgOutputPath = "output_path";
    internal const string ArgFormat = "format";
    internal const string ArgOverwrite = "overwrite";
    internal const string ArgFilePath = "file_path";
    internal const string ArgUseTransaction = "use_transaction";
    internal const string ArgSearchTerm = "search_term";
    internal const string ArgMaxResults = "max_results";
    internal const string ArgObjectNames = "object_names";
    internal const string ArgObjectName = "object_name";
    internal const string ArgObjectType = "object_type";
    internal const string ArgTriggerName = "trigger_name";
    internal const string ArgQuery = "query";
    internal const string ArgRequestedRowLimit = "requested_row_limit";
    internal const string ArgParameters = "parameters";
    internal const string ArgQueryA = "query_a";
    internal const string ArgQueryB = "query_b";
    internal const string ArgParametersA = "parameters_a";
    internal const string ArgParametersB = "parameters_b";
    internal const string ArgMaxDiffRows = "max_diff_rows";
    internal const string ArgMinScore = "min_score";
    internal const string ArgTableName = "table_name";
    internal const string ArgTop = "top";
    internal const string ArgWarmupRuns = "warmup_runs";
    internal const string ArgExecutionRuns = "execution_runs";
    internal const string ArgIncludePlanAnalysis = "include_plan_analysis";
}
