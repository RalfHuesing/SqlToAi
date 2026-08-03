#nullable enable

namespace SqlToAi.Mcp;

/// <summary>
/// Builds and returns the canonical list of all MCP tool definitions exposed by this server.
/// This is the single source of truth for <c>tools/list</c> responses.
/// All tool names, argument names, and type information reference <see cref="McpConstants"/>
/// — no string literals appear here.
/// </summary>
public sealed class ToolRegistry
{
    private readonly IReadOnlyList<ToolDefinition> _tools;

    /// <summary>Initializes the registry and pre-builds all tool definitions.</summary>
    public ToolRegistry()
    {
        _tools = BuildTools();
    }

    /// <summary>Returns the full list of registered tool definitions.</summary>
    public IReadOnlyList<ToolDefinition> GetAll() => _tools;

    // -------------------------------------------------------------------------
    // Private builders — one method per tool keeps diffs minimal
    // -------------------------------------------------------------------------

    private static IReadOnlyList<ToolDefinition> BuildTools() =>
    [
        BuildListDatabases(),
        BuildSearchDatabases(),
        BuildValidateQuery(),
        BuildSearchObjects(),
        BuildGetSchema(),
        BuildGetSchemaForeignKeys(),
        BuildGetSchemaIndexes(),
        BuildGetSchemaConstraints(),
        BuildGetTriggerDefinition(),
        BuildGetObjectReferences(),
        BuildGetRoutineParameters(),
        BuildExecuteQuery(),
        BuildCompareQueries(),
        BuildMeasurePerformance(),
        BuildBenchmarkOptimization()
    ];

    private static ToolDefinition BuildListDatabases() => new()
    {
        Name = McpConstants.ToolListDatabases,
        Description = "Lists all databases on the SQL Server that are permitted by the server's whitelist configuration.",
        InputSchema = NoArgs()
    };

    private static ToolDefinition BuildSearchDatabases() => new()
    {
        Name = McpConstants.ToolSearchDatabases,
        Description = "Filters the list of allowed databases by a partial search term.",
        InputSchema = new ToolInputSchema
        {
            Properties = new Dictionary<string, ToolParameterDefinition>
            {
                [McpConstants.ArgSearchTerm] = StringParam("Substring to filter database names by.")
            },
            Required = [McpConstants.ArgSearchTerm]
        }
    };

    private static ToolDefinition BuildValidateQuery() => new()
    {
        Name = McpConstants.ToolValidateQuery,
        Description = "Validates SQL syntax and object references via PARSEONLY without executing the query.",
        InputSchema = new ToolInputSchema
        {
            Properties = new Dictionary<string, ToolParameterDefinition>
            {
                [McpConstants.ArgQuery]      = StringParam("The SQL query to validate."),
                [McpConstants.ArgDatabase]   = StringParam("Target database name. Required."),
                [McpConstants.ArgParameters] = new() { Type = "object", Description = "Optional dictionary of typed SQL parameters (e.g. {\"CustomerId\": 42} or {\"val\": {\"value\": \"123\", \"dbType\": \"AnsiString\"}})." }
            },
            Required = [McpConstants.ArgQuery, McpConstants.ArgDatabase]
        }
    };

    private static ToolDefinition BuildSearchObjects() => new()
    {
        Name = McpConstants.ToolSearchObjects,
        Description = "Searches for database objects (tables, views, procedures, triggers) by name using LIKE.",
        InputSchema = new ToolInputSchema
        {
            Properties = new Dictionary<string, ToolParameterDefinition>
            {
                [McpConstants.ArgSearchTerm] = StringParam("Partial object name to search for."),
                [McpConstants.ArgMaxResults] = new() { Type = "integer", Description = "Maximum number of results to return. Optional." },
                [McpConstants.ArgObjectType] = OptionalStringParam(
                    "Optional filter on SQL Server type_desc. Common values: 'USER_TABLE', 'VIEW', " +
                    "'SQL_STORED_PROCEDURE', 'SQL_TRIGGER', 'SQL_SCALAR_FUNCTION'. Supports LIKE wildcards (e.g. 'SQL_%')."),
                [McpConstants.ArgDatabase]   = StringParam("Target database name. Required.")
            },
            Required = [McpConstants.ArgSearchTerm, McpConstants.ArgDatabase]
        }
    };

    private static ToolDefinition BuildGetSchema() => new()
    {
        Name = McpConstants.ToolGetSchema,
        Description = "Returns the primary schema of a table, view, procedure, or function as a Markdown document, enriched with metadata descriptions.",
        InputSchema = new ToolInputSchema
        {
            Properties = new Dictionary<string, ToolParameterDefinition>
            {
                [McpConstants.ArgObjectName] = StringParam("The name of the database object (table, view, procedure, or function)."),
                [McpConstants.ArgDatabase]   = StringParam("Target database name. Required.")
            },
            Required = [McpConstants.ArgObjectName, McpConstants.ArgDatabase]
        }
    };

    private static ToolDefinition BuildGetSchemaForeignKeys() => new()
    {
        Name = McpConstants.ToolGetSchemaForeignKeys,
        Description = "Returns all inbound and outbound foreign keys for a table as a Markdown table.",
        InputSchema = new ToolInputSchema
        {
            Properties = new Dictionary<string, ToolParameterDefinition>
            {
                [McpConstants.ArgObjectName] = StringParam("The name of the target table."),
                [McpConstants.ArgDatabase]   = StringParam("Target database name. Required.")
            },
            Required = [McpConstants.ArgObjectName, McpConstants.ArgDatabase]
        }
    };

    private static ToolDefinition BuildGetSchemaIndexes() => new()
    {
        Name = McpConstants.ToolGetSchemaIndexes,
        Description = "Returns all indexes (PK, Unique, Non-Clustered) including key and included columns for a table.",
        InputSchema = new ToolInputSchema
        {
            Properties = new Dictionary<string, ToolParameterDefinition>
            {
                [McpConstants.ArgObjectName] = StringParam("The name of the target table."),
                [McpConstants.ArgDatabase]   = StringParam("Target database name. Required.")
            },
            Required = [McpConstants.ArgObjectName, McpConstants.ArgDatabase]
        }
    };

    private static ToolDefinition BuildGetSchemaConstraints() => new()
    {
        Name = McpConstants.ToolGetSchemaConstraints,
        Description = "Returns all Default and Check constraints including their definition texts for a table.",
        InputSchema = new ToolInputSchema
        {
            Properties = new Dictionary<string, ToolParameterDefinition>
            {
                [McpConstants.ArgObjectName] = StringParam("The name of the target table."),
                [McpConstants.ArgDatabase]   = StringParam("Target database name. Required.")
            },
            Required = [McpConstants.ArgObjectName, McpConstants.ArgDatabase]
        }
    };

    private static ToolDefinition BuildGetTriggerDefinition() => new()
    {
        Name = McpConstants.ToolGetTriggerDefinition,
        Description = "Returns the full CREATE TRIGGER DDL definition of a DML trigger.",
        InputSchema = new ToolInputSchema
        {
            Properties = new Dictionary<string, ToolParameterDefinition>
            {
                [McpConstants.ArgObjectName]  = StringParam("The name of the parent table or view."),
                [McpConstants.ArgTriggerName] = StringParam("The name of the trigger."),
                [McpConstants.ArgDatabase]    = StringParam("Target database name. Required.")
            },
            Required = [McpConstants.ArgObjectName, McpConstants.ArgTriggerName, McpConstants.ArgDatabase]
        }
    };

    private static ToolDefinition BuildGetObjectReferences() => new()
    {
        Name = McpConstants.ToolGetObjectReferences,
        Description = "Returns all objects that reference or are referenced by the specified table or view.",
        InputSchema = new ToolInputSchema
        {
            Properties = new Dictionary<string, ToolParameterDefinition>
            {
                [McpConstants.ArgObjectName] = StringParam("The name of the target table or view."),
                [McpConstants.ArgDatabase]   = StringParam("Target database name. Required.")
            },
            Required = [McpConstants.ArgObjectName, McpConstants.ArgDatabase]
        }
    };

    private static ToolDefinition BuildGetRoutineParameters() => new()
    {
        Name = McpConstants.ToolGetRoutineParameters,
        Description = "Returns all parameters (name, type, direction) of a stored procedure or function.",
        InputSchema = new ToolInputSchema
        {
            Properties = new Dictionary<string, ToolParameterDefinition>
            {
                [McpConstants.ArgObjectName] = StringParam("The name of the stored procedure or function."),
                [McpConstants.ArgDatabase]   = StringParam("Target database name. Required.")
            },
            Required = [McpConstants.ArgObjectName, McpConstants.ArgDatabase]
        }
    };

    private static ToolDefinition BuildExecuteQuery() => new()
    {
        Name = McpConstants.ToolExecuteQuery,
        Description = "Executes a single read-only SELECT statement inside a rollback transaction and returns the results as JSON lines. String columns are anonymized when the database access level requires it.",
        InputSchema = new ToolInputSchema
        {
            Properties = new Dictionary<string, ToolParameterDefinition>
            {
                [McpConstants.ArgQuery]             = StringParam("The SQL SELECT query to execute."),
                [McpConstants.ArgDatabase]          = StringParam("Target database name. Required."),
                [McpConstants.ArgRequestedRowLimit] = new() { Type = "integer", Description = "Maximum rows to return. Capped by the server's configured maximum. Optional." },
                [McpConstants.ArgParameters]        = new() { Type = "object", Description = "Optional dictionary of typed SQL parameters (e.g. {\"CustomerId\": 42} or {\"val\": {\"value\": \"123\", \"dbType\": \"AnsiString\"}})." }
            },
            Required = [McpConstants.ArgQuery, McpConstants.ArgDatabase]
        }
    };

    private static ToolDefinition BuildCompareQueries() => new()
    {
        Name = McpConstants.ToolCompareQueries,
        Description = "Compares two SQL queries for semantic equivalence (schema, row counts, and database-side EXCEPT set differences) without transferring full datasets.",
        InputSchema = new ToolInputSchema
        {
            Properties = new Dictionary<string, ToolParameterDefinition>
            {
                [McpConstants.ArgDatabase]    = StringParam("Target database name. Required."),
                [McpConstants.ArgQueryA]       = StringParam("Baseline SQL query (Query A). Required."),
                [McpConstants.ArgQueryB]       = StringParam("Candidate SQL query (Query B). Required."),
                [McpConstants.ArgParametersA]  = new() { Type = "object", Description = "Optional dictionary of parameters for Query A." },
                [McpConstants.ArgParametersB]  = new() { Type = "object", Description = "Optional dictionary of parameters for Query B." },
                [McpConstants.ArgParameters]   = new() { Type = "object", Description = "Optional shared dictionary of parameters for both queries." },
                [McpConstants.ArgMaxDiffRows]  = new() { Type = "integer", Description = "Maximum example diff rows to return when queries differ. Default is 5." }
            },
            Required = [McpConstants.ArgDatabase, McpConstants.ArgQueryA, McpConstants.ArgQueryB]
        }
    };

    private static ToolDefinition BuildMeasurePerformance() => new()
    {
        Name = McpConstants.ToolMeasurePerformance,
        Description = "Measures SQL query execution metrics (CPU time, elapsed time, logical/physical IO reads) and extracts warnings from the actual execution plan XML.",
        InputSchema = new ToolInputSchema
        {
            Properties = new Dictionary<string, ToolParameterDefinition>
            {
                [McpConstants.ArgDatabase]             = StringParam("Target database name. Required."),
                [McpConstants.ArgQuery]                = StringParam("SQL query to measure. Required."),
                [McpConstants.ArgParameters]           = new() { Type = "object", Description = "Optional dictionary of typed parameters for the query." },
                [McpConstants.ArgWarmupRuns]           = new() { Type = "integer", Description = "Number of initial unmeasured warmup runs (default 1)." },
                [McpConstants.ArgExecutionRuns]        = new() { Type = "integer", Description = "Number of measured execution runs to average (default 1)." },
                [McpConstants.ArgIncludePlanAnalysis]  = new() { Type = "boolean", Description = "Whether to attempt actual execution plan XML analysis (default true)." }
            },
            Required = [McpConstants.ArgDatabase, McpConstants.ArgQuery]
        }
    };

    private static ToolDefinition BuildBenchmarkOptimization() => new()
    {
        Name = McpConstants.ToolBenchmarkOptimization,
        Description = "Runs a full optimization benchmark comparing baseline (Query A) vs candidate (Query B), evaluating result set equivalence, performance deltas (CPU, IO), and returning an actionable recommendation verdict.",
        InputSchema = new ToolInputSchema
        {
            Properties = new Dictionary<string, ToolParameterDefinition>
            {
                [McpConstants.ArgDatabase]     = StringParam("Target database name. Required."),
                [McpConstants.ArgQueryA]        = StringParam("Baseline SQL query (Query A). Required."),
                [McpConstants.ArgQueryB]        = StringParam("Candidate SQL query (Query B). Required."),
                [McpConstants.ArgParametersA]   = new() { Type = "object", Description = "Optional dictionary of parameters for Query A." },
                [McpConstants.ArgParametersB]   = new() { Type = "object", Description = "Optional dictionary of parameters for Query B." },
                [McpConstants.ArgParameters]    = new() { Type = "object", Description = "Optional shared dictionary of parameters for both queries." },
                [McpConstants.ArgWarmupRuns]    = new() { Type = "integer", Description = "Number of initial unmeasured warmup runs (default 1)." },
                [McpConstants.ArgExecutionRuns] = new() { Type = "integer", Description = "Number of measured execution runs to average (default 1)." }
            },
            Required = [McpConstants.ArgDatabase, McpConstants.ArgQueryA, McpConstants.ArgQueryB]
        }
    };

    // -------------------------------------------------------------------------
    // Shared parameter builders
    // -------------------------------------------------------------------------

    private static ToolInputSchema NoArgs() => new()
    {
        Properties = [],
        Required = []
    };

    private static ToolParameterDefinition StringParam(string description) => new()
    {
        Type = "string",
        Description = description
    };

    private static ToolParameterDefinition OptionalStringParam(string description) => new()
    {
        Type = "string",
        Description = description
    };
}
