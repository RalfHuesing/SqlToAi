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
        BuildExecuteQuery()
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
                [McpConstants.ArgQuery]    = StringParam("The SQL query to validate."),
                [McpConstants.ArgDatabase] = OptionalStringParam("Target database name. Defaults to the configured default database.")
            },
            Required = [McpConstants.ArgQuery]
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
                [McpConstants.ArgDatabase]   = OptionalStringParam("Target database name.")
            },
            Required = [McpConstants.ArgSearchTerm]
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
                [McpConstants.ArgDatabase]   = OptionalStringParam("Target database name.")
            },
            Required = [McpConstants.ArgObjectName]
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
                [McpConstants.ArgDatabase]   = OptionalStringParam("Target database name.")
            },
            Required = [McpConstants.ArgObjectName]
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
                [McpConstants.ArgDatabase]   = OptionalStringParam("Target database name.")
            },
            Required = [McpConstants.ArgObjectName]
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
                [McpConstants.ArgDatabase]   = OptionalStringParam("Target database name.")
            },
            Required = [McpConstants.ArgObjectName]
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
                [McpConstants.ArgDatabase]    = OptionalStringParam("Target database name.")
            },
            Required = [McpConstants.ArgObjectName, McpConstants.ArgTriggerName]
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
                [McpConstants.ArgDatabase]   = OptionalStringParam("Target database name.")
            },
            Required = [McpConstants.ArgObjectName]
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
                [McpConstants.ArgDatabase]   = OptionalStringParam("Target database name.")
            },
            Required = [McpConstants.ArgObjectName]
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
                [McpConstants.ArgDatabase]          = OptionalStringParam("Target database name."),
                [McpConstants.ArgRequestedRowLimit] = new() { Type = "integer", Description = "Maximum rows to return. Capped by the server's configured maximum. Optional." }
            },
            Required = [McpConstants.ArgQuery]
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
