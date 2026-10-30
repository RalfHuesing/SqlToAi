using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace SqlToAi.Mcp;

/// <summary>
/// Builds the MCP <see cref="ToolCollection"/> exposing all SQL Server tools for the ModelContextProtocol SDK.
/// </summary>
public static class SqlMcpToolRegistrations
{
    /// <summary>
    /// Builds and returns the collection of all 20 SQL tools, wired to the provided <paramref name="dispatcher"/>.
    /// </summary>
    public static McpServerPrimitiveCollection<McpServerTool> BuildToolCollection(IToolDispatcher dispatcher)
    {
        var tools = new McpServerPrimitiveCollection<McpServerTool>();
        RegisterDatabaseDiscoveryTools(tools, dispatcher);
        RegisterSchemaInspectionTools(tools, dispatcher);
        RegisterQueryExecutionTools(tools, dispatcher);
        RegisterPerformanceAndAnalysisTools(tools, dispatcher);
        var definitions = new ToolRegistry().GetAll().ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            // Raw target binding reaches catalog error handling; discovery still advertises required inputs.
            tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(definitions[tool.ProtocolTool.Name].InputSchema,
                McpJsonContext.Default.ToolInputSchema);
        }
        return tools;
    }

    private static void RegisterDatabaseDiscoveryTools(
        McpServerPrimitiveCollection<McpServerTool> tools,
        IToolDispatcher dispatcher)
    {
        tools.Add(McpServerTool.Create(
            (CancellationToken ct = default) =>
                ExecuteAsync(dispatcher, McpConstants.ToolListServers, new Dictionary<string, object?>(StringComparer.Ordinal), ct),
            new McpServerToolCreateOptions
            {
                Name = McpConstants.ToolListServers,
                Description = "Lists all configured named server aliases available for database routing."
            }));

        tools.Add(McpServerTool.Create(
            ([Description("The named server alias to list databases for.")] JsonElement? server = null,
             CancellationToken ct = default) =>
                ExecuteAsync(dispatcher, McpConstants.ToolListDatabases, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [McpConstants.ArgServer] = server
                }, ct),
            new McpServerToolCreateOptions
            {
                Name = McpConstants.ToolListDatabases,
                Description = "Lists all databases on the specified server alias that are permitted by security configuration."
            }));

        tools.Add(McpServerTool.Create(
            ([Description("Substring to filter database names by.")] string search_term,
             [Description("The named server alias to search databases on.")] JsonElement? server = null,
             CancellationToken ct = default) =>
                ExecuteAsync(dispatcher, McpConstants.ToolSearchDatabases, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [McpConstants.ArgServer] = server,
                    [McpConstants.ArgSearchTerm] = search_term
                }, ct),
            new McpServerToolCreateOptions
            {
                Name = McpConstants.ToolSearchDatabases,
                Description = "Filters the list of allowed databases on the specified server alias by a partial search term."
            }));

        tools.Add(McpServerTool.Create(
            ([Description("Partial object name to search for.")] string search_term,
             [Description(McpConstants.DatabaseTargetDescription)] JsonElement? database = null,
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
        ToolDefinition definition = new ToolRegistry().GetAll().Single(tool => tool.Name == McpConstants.ToolGetSchema);
        McpServerTool schemaTool = McpServerTool.Create(
            (JsonElement? database = null, JsonElement? object_name = null, JsonElement? object_names = null, CancellationToken ct = default) =>
                ExecuteAsync(dispatcher, McpConstants.ToolGetSchema, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [McpConstants.ArgDatabase] = database,
                    [McpConstants.ArgObjectName] = object_name,
                    [McpConstants.ArgObjectNames] = object_names
                }, ct),
            new McpServerToolCreateOptions { Name = definition.Name, Description = definition.Description });
        tools.Add(schemaTool);
        ToolDefinition exportDefinition = new ToolRegistry().GetAll().Single(tool => tool.Name == McpConstants.ToolExportSchema);
        McpServerTool exportTool = McpServerTool.Create(
            (JsonElement? database = null, JsonElement? scope = null, JsonElement? output_path = null, JsonElement? object_name = null,
                JsonElement? object_names = null, CancellationToken ct = default) =>
                ExecuteAsync(dispatcher, McpConstants.ToolExportSchema, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [McpConstants.ArgDatabase] = database,
                    [McpConstants.ArgScope] = scope,
                    [McpConstants.ArgOutputPath] = output_path,
                    [McpConstants.ArgObjectName] = object_name,
                    [McpConstants.ArgObjectNames] = object_names
                }, ct),
            new McpServerToolCreateOptions { Name = exportDefinition.Name, Description = exportDefinition.Description });
        tools.Add(exportTool);
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
             [Description(McpConstants.DatabaseTargetDescription)] JsonElement? database = null,
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
             [Description(McpConstants.DatabaseTargetDescription)] JsonElement? database = null,
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
             [Description(McpConstants.DatabaseTargetDescription)] JsonElement? database = null,
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
                Description = ExecutionToolDescriptions.ValidateQuery
            }));

        tools.Add(McpServerTool.Create(
            ([Description("The SQL query to execute.")] string query,
             [Description(McpConstants.DatabaseTargetDescription)] JsonElement? database = null,
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
                Description = "Executes a single SQL statement and returns results as JSON lines with execution metrics (rows, latency, CPU, logical reads). " +
                    "ReadOnly and ReadOnlyAnonymized databases enforce rollback protection and block writes; ReadWrite and ReadWriteAnonymized databases commit changes permanently. " +
                    "String columns are anonymized when required by access level."
            }));

        RegisterExportDataTool(tools, dispatcher);
        RegisterExecuteFileTool(tools, dispatcher);
    }

    private static void RegisterExportDataTool(
        McpServerPrimitiveCollection<McpServerTool> tools,
        IToolDispatcher dispatcher)
    {
        tools.Add(McpServerTool.Create(
            ([Description("The read-only SQL query to execute and export.")] string query,
             [Description("Required absolute Windows drive-rooted or UNC file path for the exported file.")] string output_path,
             [Description(McpConstants.DatabaseTargetDescription)] JsonElement? database = null,
             [Description("Optional export format ('csv' or 'jsonl'). Defaults to auto-detection from the output_path extension (fallback 'csv').")] string? format = null,
             [Description("Whether to overwrite an existing file at output_path. Default is false.")] bool? overwrite = null,
             [Description("Maximum rows to export. Capped by the server's configured export maximum. Optional.")] int? requested_row_limit = null,
             [Description("Optional dictionary of typed SQL parameters (e.g. {\"CustomerId\": 42} or {\"val\": {\"value\": \"123\", \"dbType\": \"AnsiString\"}}).")] object? parameters = null,
             CancellationToken ct = default) =>
            {
                var args = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [McpConstants.ArgQuery] = query,
                    [McpConstants.ArgOutputPath] = output_path,
                    [McpConstants.ArgDatabase] = database
                };
                if (format != null) args[McpConstants.ArgFormat] = format;
                if (overwrite.HasValue) args[McpConstants.ArgOverwrite] = overwrite.Value;
                if (requested_row_limit.HasValue) args[McpConstants.ArgRequestedRowLimit] = requested_row_limit.Value;
                if (parameters != null) args[McpConstants.ArgParameters] = parameters;
                return ExecuteAsync(dispatcher, McpConstants.ToolExportData, args, ct);
            },
            new McpServerToolCreateOptions
            {
                Name = McpConstants.ToolExportData,
                Description = ExecutionToolDescriptions.ExportData
            }));
    }

    private static void RegisterExecuteFileTool(
        McpServerPrimitiveCollection<McpServerTool> tools,
        IToolDispatcher dispatcher)
    {
        tools.Add(McpServerTool.Create(
            ([Description("Local .sql file path, absolute or relative to the server working directory. Required.")] string file_path,
             [Description(McpConstants.DatabaseTargetDescription)] JsonElement? database = null,
             [Description(ExecutionToolDescriptions.UseTransaction)] bool? use_transaction = null,
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
                Description = ExecutionToolDescriptions.ExecuteFile
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
            ([Description("Baseline SQL query (Query A). Required.")] string query_a,
             [Description("Candidate SQL query (Query B). Required.")] string query_b,
             [Description(McpConstants.DatabaseTargetDescription)] JsonElement? database = null,
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
            ([Description("SQL query to measure. Required.")] string query,
             [Description(McpConstants.DatabaseTargetDescription)] JsonElement? database = null,
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
            ([Description("Baseline SQL query (Query A). Required.")] string query_a,
             [Description("Candidate SQL query (Query B). Required.")] string query_b,
             [Description(McpConstants.DatabaseTargetDescription)] JsonElement? database = null,
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
            ([Description(McpConstants.DatabaseTargetDescription)] JsonElement? database = null,
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
