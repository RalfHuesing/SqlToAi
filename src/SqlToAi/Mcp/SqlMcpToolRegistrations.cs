#nullable enable

using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace SqlToAi.Mcp;

/// <summary>
/// Builds the MCP <see cref="ToolCollection"/> exposing all SQL Server tools for the ModelContextProtocol SDK.
/// </summary>
public static class SqlMcpToolRegistrations
{
    /// <summary>
    /// Builds and returns the collection of all 17 SQL tools, wired to the provided <paramref name="dispatcher"/>.
    /// </summary>
    public static McpServerPrimitiveCollection<McpServerTool> BuildToolCollection(IToolDispatcher dispatcher)
    {
        var tools = new McpServerPrimitiveCollection<McpServerTool>();
        RegisterDatabaseDiscoveryTools(tools, dispatcher);
        RegisterSchemaInspectionTools(tools, dispatcher);
        RegisterQueryExecutionTools(tools, dispatcher);
        RegisterPerformanceAndAnalysisTools(tools, dispatcher);
        return tools;
    }

    private static void RegisterDatabaseDiscoveryTools(
        McpServerPrimitiveCollection<McpServerTool> tools,
        IToolDispatcher dispatcher)
    {
        tools.Add(McpServerTool.Create(
            (CancellationToken ct = default) =>
                ExecuteAsync(dispatcher, McpConstants.ToolListDatabases, new Dictionary<string, object?>(StringComparer.Ordinal), ct),
            new McpServerToolCreateOptions
            {
                Name = McpConstants.ToolListDatabases,
                Description = "Lists all databases on the SQL Server that are permitted by the server's whitelist configuration."
            }));

        tools.Add(McpServerTool.Create(
            ([Description("Substring to filter database names by.")] string search_term,
             CancellationToken ct = default) =>
                ExecuteAsync(dispatcher, McpConstants.ToolSearchDatabases, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [McpConstants.ArgSearchTerm] = search_term
                }, ct),
            new McpServerToolCreateOptions
            {
                Name = McpConstants.ToolSearchDatabases,
                Description = "Filters the list of allowed databases by a partial search term."
            }));

        tools.Add(McpServerTool.Create(
            ([Description("Partial object name to search for.")] string search_term,
             [Description("Target database name. Required.")] string database,
             [Description("Maximum number of results to return. Optional.")] int? max_results = null,
             [Description("Optional filter on SQL Server type_desc.")] string? object_type = null,
             CancellationToken ct = default) =>
            {
                var args = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [McpConstants.ArgSearchTerm] = search_term,
                    [McpConstants.ArgDatabase] = database
                };
                if (max_results.HasValue) args[McpConstants.ArgMaxResults] = max_results.Value;
                if (!string.IsNullOrEmpty(object_type)) args[McpConstants.ArgObjectType] = object_type;
                return ExecuteAsync(dispatcher, McpConstants.ToolSearchObjects, args, ct);
            },
            new McpServerToolCreateOptions
            {
                Name = McpConstants.ToolSearchObjects,
                Description = "Searches for database objects (tables, views, procedures, triggers) by name using LIKE."
            }));
    }

    private static void RegisterSchemaInspectionTools(
        McpServerPrimitiveCollection<McpServerTool> tools,
        IToolDispatcher dispatcher)
    {
        RegisterDetailTool(tools, dispatcher, McpConstants.ToolGetSchema,
            "Returns the primary schema of a table, view, procedure, or function as a Markdown document, enriched with metadata descriptions.");
        RegisterDetailTool(tools, dispatcher, McpConstants.ToolGetSchemaForeignKeys,
            "Returns all inbound and outbound foreign keys for a table as a Markdown table.");
        RegisterDetailTool(tools, dispatcher, McpConstants.ToolGetSchemaIndexes,
            "Returns all indexes (PK, Unique, Non-Clustered) including key and included columns for a table.");
        RegisterDetailTool(tools, dispatcher, McpConstants.ToolGetSchemaConstraints,
            "Returns all Default and Check constraints including their definition texts for a table.");
        RegisterDetailTool(tools, dispatcher, McpConstants.ToolGetObjectReferences,
            "Returns all objects that reference or are referenced by the specified table or view.");
        RegisterDetailTool(tools, dispatcher, McpConstants.ToolGetRoutineParameters,
            "Returns all parameters (name, type, direction) of a stored procedure or function.");

        tools.Add(McpServerTool.Create(
            ([Description("The name of the parent table or view.")] string object_name,
             [Description("The name of the trigger.")] string trigger_name,
             [Description("Target database name. Required.")] string database,
             CancellationToken ct = default) =>
                ExecuteAsync(dispatcher, McpConstants.ToolGetTriggerDefinition, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [McpConstants.ArgObjectName] = object_name,
                    [McpConstants.ArgTriggerName] = trigger_name,
                    [McpConstants.ArgDatabase] = database
                }, ct),
            new McpServerToolCreateOptions
            {
                Name = McpConstants.ToolGetTriggerDefinition,
                Description = "Returns the full CREATE TRIGGER DDL definition of a DML trigger."
            }));
    }

    private static void RegisterDetailTool(
        McpServerPrimitiveCollection<McpServerTool> tools,
        IToolDispatcher dispatcher,
        string toolName,
        string description)
    {
        tools.Add(McpServerTool.Create(
            ([Description("The name of the database object.")] string object_name,
             [Description("Target database name. Required.")] string database,
             CancellationToken ct = default) =>
                ExecuteAsync(dispatcher, toolName, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [McpConstants.ArgObjectName] = object_name,
                    [McpConstants.ArgDatabase] = database
                }, ct),
            new McpServerToolCreateOptions
            {
                Name = toolName,
                Description = description
            }));
    }

    private static void RegisterQueryExecutionTools(
        McpServerPrimitiveCollection<McpServerTool> tools,
        IToolDispatcher dispatcher)
    {
        tools.Add(McpServerTool.Create(
            ([Description("The SQL query to validate.")] string query,
             [Description("Target database name. Required.")] string database,
             [Description("Optional dictionary of typed SQL parameters.")] object? parameters = null,
             CancellationToken ct = default) =>
            {
                var args = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [McpConstants.ArgQuery] = query,
                    [McpConstants.ArgDatabase] = database
                };
                if (parameters != null) args[McpConstants.ArgParameters] = parameters;
                return ExecuteAsync(dispatcher, McpConstants.ToolValidateQuery, args, ct);
            },
            new McpServerToolCreateOptions
            {
                Name = McpConstants.ToolValidateQuery,
                Description = "Validates SQL syntax and object references via PARSEONLY without executing the query."
            }));

        tools.Add(McpServerTool.Create(
            ([Description("The SQL SELECT query to execute.")] string query,
             [Description("Target database name. Required.")] string database,
             [Description("Maximum rows to return. Capped by the server's configured maximum. Optional.")] int? requested_row_limit = null,
             [Description("Optional dictionary of typed SQL parameters.")] object? parameters = null,
             CancellationToken ct = default) =>
            {
                var args = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [McpConstants.ArgQuery] = query,
                    [McpConstants.ArgDatabase] = database
                };
                if (requested_row_limit.HasValue) args[McpConstants.ArgRequestedRowLimit] = requested_row_limit.Value;
                if (parameters != null) args[McpConstants.ArgParameters] = parameters;
                return ExecuteAsync(dispatcher, McpConstants.ToolExecuteQuery, args, ct);
            },
            new McpServerToolCreateOptions
            {
                Name = McpConstants.ToolExecuteQuery,
                Description = "Executes a single read-only SELECT statement inside a rollback transaction and returns the results as JSON lines."
            }));

        RegisterExecuteFileTool(tools, dispatcher);
    }

    private static void RegisterExecuteFileTool(
        McpServerPrimitiveCollection<McpServerTool> tools,
        IToolDispatcher dispatcher)
    {
        tools.Add(McpServerTool.Create(
            ([Description("Local .sql file path, absolute or relative to the server working directory. Required.")] string file_path,
             [Description("Target database name. Required.")] string database,
             [Description("Whether ReadWrite batches use one atomic transaction. Defaults to true; protected read-only modes always roll back.")] bool? use_transaction = null,
             [Description("Maximum rows returned per SELECT batch. Capped by the server's configured maximum. Optional.")] int? requested_row_limit = null,
             [Description("Optional dictionary of typed SQL parameters for all batches.")] object? parameters = null,
             CancellationToken ct = default) =>
            {
                var args = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [McpConstants.ArgFilePath] = file_path,
                    [McpConstants.ArgDatabase] = database
                };
                if (use_transaction.HasValue) args[McpConstants.ArgUseTransaction] = use_transaction.Value;
                if (requested_row_limit.HasValue) args[McpConstants.ArgRequestedRowLimit] = requested_row_limit.Value;
                if (parameters != null) args[McpConstants.ArgParameters] = parameters;
                return ExecuteAsync(dispatcher, McpConstants.ToolExecuteFile, args, ct);
            },
            new McpServerToolCreateOptions
            {
                Name = McpConstants.ToolExecuteFile,
                Description = "Executes a local .sql file with multi-batch support and returns a structured Markdown report with transaction mode, metrics, results, and diagnostics."
            }));
    }

    private static void RegisterPerformanceAndAnalysisTools(
        McpServerPrimitiveCollection<McpServerTool> tools,
        IToolDispatcher dispatcher)
    {
        RegisterCompareQueriesTool(tools, dispatcher);
        RegisterMeasurePerformanceTool(tools, dispatcher);
        RegisterBenchmarkOptimizationTool(tools, dispatcher);
        RegisterSuggestIndexesTool(tools, dispatcher);
    }

    private static void RegisterCompareQueriesTool(
        McpServerPrimitiveCollection<McpServerTool> tools,
        IToolDispatcher dispatcher)
    {
        tools.Add(McpServerTool.Create(
            ([Description("Target database name. Required.")] string database,
             [Description("Baseline SQL query (Query A). Required.")] string query_a,
             [Description("Candidate SQL query (Query B). Required.")] string query_b,
             [Description("Optional dictionary of parameters for Query A.")] object? parameters_a = null,
             [Description("Optional dictionary of parameters for Query B.")] object? parameters_b = null,
             [Description("Optional shared dictionary of parameters for both queries.")] object? parameters = null,
             [Description("Maximum example diff rows to return when queries differ. Default is 5.")] int? max_diff_rows = null,
             CancellationToken ct = default) =>
            {
                var args = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [McpConstants.ArgDatabase] = database,
                    [McpConstants.ArgQueryA] = query_a,
                    [McpConstants.ArgQueryB] = query_b
                };
                if (parameters_a != null) args[McpConstants.ArgParametersA] = parameters_a;
                if (parameters_b != null) args[McpConstants.ArgParametersB] = parameters_b;
                if (parameters != null) args[McpConstants.ArgParameters] = parameters;
                if (max_diff_rows.HasValue) args[McpConstants.ArgMaxDiffRows] = max_diff_rows.Value;
                return ExecuteAsync(dispatcher, McpConstants.ToolCompareQueries, args, ct);
            },
            new McpServerToolCreateOptions
            {
                Name = McpConstants.ToolCompareQueries,
                Description = "Compares two SQL queries for semantic equivalence without transferring full datasets."
            }));
    }

    private static void RegisterMeasurePerformanceTool(
        McpServerPrimitiveCollection<McpServerTool> tools,
        IToolDispatcher dispatcher)
    {
        tools.Add(McpServerTool.Create(
            ([Description("Target database name. Required.")] string database,
             [Description("SQL query to measure. Required.")] string query,
             [Description("Optional dictionary of typed parameters for the query.")] object? parameters = null,
             [Description("Number of initial unmeasured warmup runs (default 1).")] int? warmup_runs = null,
             [Description("Number of measured execution runs (default 1).")] int? execution_runs = null,
             [Description("Whether to attempt actual execution plan XML analysis (default true).")] bool? include_plan_analysis = null,
             CancellationToken ct = default) =>
            {
                var args = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [McpConstants.ArgDatabase] = database,
                    [McpConstants.ArgQuery] = query
                };
                if (parameters != null) args[McpConstants.ArgParameters] = parameters;
                if (warmup_runs.HasValue) args[McpConstants.ArgWarmupRuns] = warmup_runs.Value;
                if (execution_runs.HasValue) args[McpConstants.ArgExecutionRuns] = execution_runs.Value;
                if (include_plan_analysis.HasValue) args[McpConstants.ArgIncludePlanAnalysis] = include_plan_analysis.Value;
                return ExecuteAsync(dispatcher, McpConstants.ToolMeasurePerformance, args, ct);
            },
            new McpServerToolCreateOptions
            {
                Name = McpConstants.ToolMeasurePerformance,
                Description = "Measures SQL query performance via SET STATISTICS IO/TIME on the actual execution."
            }));
    }

    private static void RegisterBenchmarkOptimizationTool(
        McpServerPrimitiveCollection<McpServerTool> tools,
        IToolDispatcher dispatcher)
    {
        tools.Add(McpServerTool.Create(
            ([Description("Target database name. Required.")] string database,
             [Description("Baseline SQL query (Query A). Required.")] string query_a,
             [Description("Candidate SQL query (Query B). Required.")] string query_b,
             [Description("Optional dictionary of parameters for Query A.")] object? parameters_a = null,
             [Description("Optional dictionary of parameters for Query B.")] object? parameters_b = null,
             [Description("Optional shared dictionary of parameters for both queries.")] object? parameters = null,
             [Description("Number of initial unmeasured warmup runs (default 1).")] int? warmup_runs = null,
             [Description("Number of measured execution runs to average (default 1).")] int? execution_runs = null,
             CancellationToken ct = default) =>
            {
                var args = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [McpConstants.ArgDatabase] = database,
                    [McpConstants.ArgQueryA] = query_a,
                    [McpConstants.ArgQueryB] = query_b
                };
                if (parameters_a != null) args[McpConstants.ArgParametersA] = parameters_a;
                if (parameters_b != null) args[McpConstants.ArgParametersB] = parameters_b;
                if (parameters != null) args[McpConstants.ArgParameters] = parameters;
                if (warmup_runs.HasValue) args[McpConstants.ArgWarmupRuns] = warmup_runs.Value;
                if (execution_runs.HasValue) args[McpConstants.ArgExecutionRuns] = execution_runs.Value;
                return ExecuteAsync(dispatcher, McpConstants.ToolBenchmarkOptimization, args, ct);
            },
            new McpServerToolCreateOptions
            {
                Name = McpConstants.ToolBenchmarkOptimization,
                Description = "Runs a full optimization benchmark comparing baseline vs candidate queries."
            }));
    }

    private static void RegisterSuggestIndexesTool(
        McpServerPrimitiveCollection<McpServerTool> tools,
        IToolDispatcher dispatcher)
    {
        tools.Add(McpServerTool.Create(
            ([Description("Target database name. Required.")] string database,
             [Description("Optional LIKE filter on the DMV 'statement' column.")] string? table_name = null,
             [Description("Optional minimum improvement_score threshold. Default 0.")] double? min_score = null,
             [Description("Maximum number of recommendations to return. Default 10.")] int? top = null,
             CancellationToken ct = default) =>
            {
                var args = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [McpConstants.ArgDatabase] = database
                };
                if (!string.IsNullOrEmpty(table_name)) args[McpConstants.ArgTableName] = table_name;
                if (min_score.HasValue) args[McpConstants.ArgMinScore] = min_score.Value;
                if (top.HasValue) args[McpConstants.ArgTop] = top.Value;
                return ExecuteAsync(dispatcher, McpConstants.ToolSuggestIndexes, args, ct);
            },
            new McpServerToolCreateOptions
            {
                Name = McpConstants.ToolSuggestIndexes,
                Description = "Returns server-wide cumulative missing-index recommendations sourced from SQL Server DMVs."
            }));
    }

    private static async Task<CallToolResult> ExecuteAsync(
        IToolDispatcher dispatcher,
        string toolName,
        Dictionary<string, object?> arguments,
        CancellationToken ct)
    {
        var callParams = new ToolCallParams
        {
            Name = toolName,
            Arguments = arguments
        };

        ToolCallResult result = await dispatcher.DispatchAsync(callParams, ct).ConfigureAwait(false);

        var contentBlocks = new List<ContentBlock>(result.Content.Count);
        foreach (ToolContent c in result.Content)
        {
            contentBlocks.Add(new TextContentBlock { Text = c.Text ?? string.Empty });
        }

        return new CallToolResult
        {
            Content = contentBlocks,
            IsError = result.IsError
        };
    }
}
