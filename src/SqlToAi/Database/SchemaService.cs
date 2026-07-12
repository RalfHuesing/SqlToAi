#nullable enable

using System.Data.Common;
using System.Text;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Metadata;
using SqlToAi.Security;

namespace SqlToAi.Database;

#pragma warning disable CA1848 // Use the LoggerMessage delegates

/// <summary>
/// Implements database schema and object exploration queries, returning cleanly formatted Markdown.
/// </summary>
public sealed class SchemaService : ISchemaService
{
    /// <summary>
    /// <c>sys.sql_modules.definition</c> returns NULL both for genuinely encrypted modules and
    /// for callers lacking <c>VIEW DEFINITION</c> on the object — SQL Server does not
    /// distinguish the two cases in this DMV, so the message names both possible causes.
    /// </summary>
    private const string DdlUnavailableNote =
        "*Definition not available — either the object is encrypted, or the configured login lacks VIEW DEFINITION permission on it.*";

    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly ISecurityGuard _securityGuard;
    private readonly IAccessLevelProvider _accessLevelProvider;
    private readonly IMetadataProvider _metadataProvider;
    private readonly SqlToAiOptions _options;
    private readonly ILogger<SchemaService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaService"/> class.
    /// </summary>
    public SchemaService(
        IDatabaseConnectionFactory connectionFactory,
        ISecurityGuard securityGuard,
        IAccessLevelProvider accessLevelProvider,
        IMetadataProvider metadataProvider,
        IOptions<SqlToAiOptions> options,
        ILogger<SchemaService> logger)
    {
        _connectionFactory = connectionFactory;
        _securityGuard = securityGuard;
        _accessLevelProvider = accessLevelProvider;
        _metadataProvider = metadataProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Checks static whitelisting and dynamic access level checks for the database.
    /// </summary>
    private async Task<Result> VerifyDatabaseAccessAsync(string databaseName, CancellationToken cancellationToken)
    {
        if (!_securityGuard.IsDatabaseAllowed(databaseName))
        {
            return SqlToAiError.SafetyCheckFailed($"Database '{databaseName}' is blocked by security policies (static whitelist).");
        }

        var accessLevel = await _accessLevelProvider.GetAccessLevelAsync(databaseName, cancellationToken);
        if (accessLevel == AccessLevel.None)
        {
            return SqlToAiError.SafetyCheckFailed($"Database '{databaseName}' access was denied (AccessLevel: None).");
        }

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<string>>> ListDatabasesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            var databases = await connection.QueryAsync<string>(
                new CommandDefinition("SELECT name FROM sys.databases WHERE state = 0", cancellationToken: cancellationToken));

            var allowedDbs = databases
                .Where(db => _securityGuard.IsDatabaseAllowed(db))
                .OrderBy(db => db)
                .ToList();

            return allowedDbs;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query sys.databases catalog. Falling back to configured static Allowed list.");
            var staticAllowed = _options.Databases.Allowed
                .Where(pattern => !pattern.Contains('*') && !pattern.Contains('?'))
                .OrderBy(db => db)
                .ToList();
            return staticAllowed;
        }
    }

    public async Task<Result<IReadOnlyList<string>>> SearchDatabasesAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        var dbsResult = await ListDatabasesAsync(cancellationToken);
        if (dbsResult.IsFailure)
        {
            return dbsResult.Error;
        }

        var filtered = dbsResult.Value
            .Where(db => db.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return filtered;
    }

    public async Task<Result<string>> SearchObjectsAsync(string databaseName, string searchTerm, int? maxResults = null, CancellationToken cancellationToken = default)
    {
        var accessCheck = await VerifyDatabaseAccessAsync(databaseName, cancellationToken);
        if (accessCheck.IsFailure)
        {
            return accessCheck.Error;
        }

        int limit = maxResults ?? 100;
        string sql = $"""
            SELECT TOP (@Limit)
                schema_name(schema_id) AS SchemaName,
                name AS ObjectName,
                type_desc AS TypeDescription
            FROM sys.objects
            WHERE is_ms_shipped = 0
              AND name LIKE @SearchPattern
            ORDER BY type_desc, schema_name(schema_id), name
            """;

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            var rows = await connection.QueryAsync<ObjectRow>(
                new CommandDefinition(sql, new { Limit = limit, SearchPattern = $"%{searchTerm}%" }, cancellationToken: cancellationToken));

            var renderedRows = new List<string[]>();
            foreach (var r in rows)
            {
                renderedRows.Add([ r.SchemaName, r.ObjectName, r.TypeDescription ]);
            }

            if (renderedRows.Count == 0)
            {
                return $"No objects found matching '{searchTerm}' in database '{databaseName}'.";
            }

            return RenderMarkdownTable(["Schema", "Name", "Type"], renderedRows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search objects in database {DatabaseName}.", databaseName);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    public async Task<Result<string>> GetSchemaAsync(string databaseName, string objectName, CancellationToken cancellationToken = default)
    {
        var accessCheck = await VerifyDatabaseAccessAsync(databaseName, cancellationToken);
        if (accessCheck.IsFailure)
        {
            return accessCheck.Error;
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            // 1. Identify object type
            var typeCode = await connection.QueryFirstOrDefaultAsync<string>(
                new CommandDefinition("SELECT RTRIM(type) FROM sys.objects WHERE object_id = OBJECT_ID(@ObjectName)", new { ObjectName = objectName }, cancellationToken: cancellationToken));

            if (string.IsNullOrEmpty(typeCode))
            {
                return SqlToAiError.ObjectNotFound(objectName);
            }

            // 2. Query schema depending on type code
            if (typeCode == "U")
            {
                return await GetTableSchemaMarkdownAsync(connection, databaseName, objectName, cancellationToken);
            }
            if (typeCode == "V")
            {
                string tableMarkdown = await GetTableSchemaMarkdownAsync(connection, databaseName, objectName, cancellationToken);
                string definitionMarkdown = await GetViewDefinitionMarkdownAsync(connection, objectName, cancellationToken);
                return tableMarkdown + definitionMarkdown;
            }
            return await GetRoutineSchemaMarkdownAsync(connection, objectName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve schema for {ObjectName} in database {DatabaseName}.", objectName, databaseName);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    private async Task<string> GetTableSchemaMarkdownAsync(DbConnection connection, string databaseName, string tableName, CancellationToken cancellationToken)
    {
        // Query columns list
        string columnsSql = """
            SELECT 
                c.name AS ColumnName,
                t.name AS DataType,
                c.max_length AS MaxLength,
                c.precision AS Precision,
                c.scale AS Scale,
                c.is_nullable AS IsNullable,
                c.is_identity AS IsIdentity,
                ISNULL(pk.is_primary_key, 0) AS IsPrimaryKey
            FROM sys.columns c
            INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
            LEFT JOIN (
                SELECT ic.object_id, ic.column_id, 1 AS is_primary_key
                FROM sys.index_columns ic
                INNER JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                WHERE i.is_primary_key = 1
            ) pk ON c.object_id = pk.object_id AND c.column_id = pk.column_id
            WHERE c.object_id = OBJECT_ID(@TableName)
            ORDER BY c.column_id
            """;

        var columns = await connection.QueryAsync<ColumnRow>(
            new CommandDefinition(columnsSql, new { TableName = tableName }, cancellationToken: cancellationToken));

        // Get extended metadata descriptions
        string? tableDesc = await _metadataProvider.GetTableDescriptionAsync(databaseName, tableName, cancellationToken);
        var columnDescs = await _metadataProvider.GetColumnDescriptionsAsync(databaseName, tableName, cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"# Schema for Table/View: `{tableName}`");
        if (!string.IsNullOrWhiteSpace(tableDesc))
        {
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"*Description:* {tableDesc}").AppendLine();
        }

        // Render Columns table
        var headers = new[] { "Column Name", "Type", "Nullable", "Key/Identity", "Description" };
        var renderedRows = new List<string[]>();
        foreach (var col in columns)
        {
            string colName = col.ColumnName;
            string type = FormatTypeString(col.DataType, col.MaxLength, col.Precision, col.Scale);
            string nullable = col.IsNullable ? "Yes" : "No";
            
            var keyFlags = new List<string>();
            if (col.IsPrimaryKey == 1) keyFlags.Add("PK");
            if (col.IsIdentity) keyFlags.Add("Identity");
            string keyStr = string.Join(", ", keyFlags);

            columnDescs.TryGetValue(colName, out string? desc);
            renderedRows.Add([colName, type, nullable, keyStr, desc ?? ""]);
        }
        sb.AppendLine(RenderMarkdownTable(headers, renderedRows));

        // Query Triggers summary
        string triggersSql = """
            SELECT 
                name AS TriggerName,
                OBJECTPROPERTY(object_id, 'ExecIsTriggerDisabled') AS IsDisabled,
                OBJECTPROPERTY(object_id, 'ExecIsUpdateTrigger') AS IsUpdate,
                OBJECTPROPERTY(object_id, 'ExecIsDeleteTrigger') AS IsDelete,
                OBJECTPROPERTY(object_id, 'ExecIsInsertTrigger') AS IsInsert
            FROM sys.triggers
            WHERE parent_id = OBJECT_ID(@TableName)
            """;

        var triggers = await connection.QueryAsync<TriggerRow>(
            new CommandDefinition(triggersSql, new { TableName = tableName }, cancellationToken: cancellationToken));

        if (triggers.Any())
        {
            sb.AppendLine("## Triggers");
            var trigHeaders = new[] { "Trigger Name", "Insert", "Update", "Delete", "Status" };
            var trigRows = new List<string[]>();
            foreach (var t in triggers)
            {
                trigRows.Add([
                    t.TriggerName,
                    t.IsInsert == 1 ? "✓" : "",
                    t.IsUpdate == 1 ? "✓" : "",
                    t.IsDelete == 1 ? "✓" : "",
                    t.IsDisabled == 1 ? "Disabled" : "Active"
                ]);
            }
            sb.AppendLine(RenderMarkdownTable(trigHeaders, trigRows));
        }

        // Query counts for Discovery Index
        int fksCount = await connection.QueryFirstOrDefaultAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(@TableName) OR referenced_object_id = OBJECT_ID(@TableName)", new { TableName = tableName }, cancellationToken: cancellationToken));

        int indexesCount = await connection.QueryFirstOrDefaultAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(@TableName) AND is_hypothetical = 0", new { TableName = tableName }, cancellationToken: cancellationToken));

        int constraintsCount = await connection.QueryFirstOrDefaultAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM (SELECT object_id FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID(@TableName) UNION ALL SELECT object_id FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(@TableName)) c", new { TableName = tableName }, cancellationToken: cancellationToken));

        sb.AppendLine("## Discovery Index");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- **Foreign Keys:** {fksCount} (run `sql_get_schema_foreign_keys` to view details)");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- **Indexes:** {indexesCount} (run `sql_get_schema_indexes` to view details)");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- **Constraints:** {constraintsCount} (run `sql_get_schema_constraints` to view details)");

        return sb.ToString();
    }

    /// <summary>
    /// Fetches the underlying <c>CREATE VIEW ... AS SELECT ...</c> definition for a view.
    /// Views only ever surface their column list via <see cref="GetTableSchemaMarkdownAsync"/>
    /// (identical treatment to tables); the SQL body is queried separately here so a caller
    /// can see the joins/computed columns behind the view, same as for procedures/functions.
    /// </summary>
    private static async Task<string> GetViewDefinitionMarkdownAsync(DbConnection connection, string viewName, CancellationToken cancellationToken)
    {
        string? ddl = await connection.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition("SELECT definition FROM sys.sql_modules WHERE object_id = OBJECT_ID(@ViewName)", new { ViewName = viewName }, cancellationToken: cancellationToken));

        var sb = new StringBuilder();
        sb.AppendLine("## View Definition");
        if (!string.IsNullOrWhiteSpace(ddl))
        {
            sb.AppendLine("```sql");
            sb.AppendLine(ddl.Trim());
            sb.AppendLine("```");
        }
        else
        {
            sb.AppendLine(DdlUnavailableNote);
        }
        return sb.ToString();
    }

    private static async Task<string> GetRoutineSchemaMarkdownAsync(DbConnection connection, string routineName, CancellationToken cancellationToken)
    {
        // Query DDL Definition
        string? ddl = await connection.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition("SELECT definition FROM sys.sql_modules WHERE object_id = OBJECT_ID(@RoutineName)", new { RoutineName = routineName }, cancellationToken: cancellationToken));

        int paramCount = await connection.QueryFirstOrDefaultAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM sys.parameters WHERE object_id = OBJECT_ID(@RoutineName)", new { RoutineName = routineName }, cancellationToken: cancellationToken));

        var sb = new StringBuilder();
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"# DDL Definition for Stored Procedure/Function: `{routineName}`");
        sb.AppendLine();
        if (paramCount > 0)
        {
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"*Discovery:* This routine accepts `{paramCount}` parameter(s). Run `sql_get_routine_parameters` to view them.").AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(ddl))
        {
            sb.AppendLine("```sql");
            sb.AppendLine(ddl.Trim());
            sb.AppendLine("```");
        }
        else
        {
            sb.AppendLine(DdlUnavailableNote);
        }

        return sb.ToString();
    }

    public async Task<Result<string>> GetSchemaForeignKeysAsync(string databaseName, string tableName, CancellationToken cancellationToken = default)
    {
        var accessCheck = await VerifyDatabaseAccessAsync(databaseName, cancellationToken);
        if (accessCheck.IsFailure)
        {
            return accessCheck.Error;
        }

        string sql = """
            SELECT 
                fk.name AS ForeignKeyName,
                schema_name(fk.schema_id) + '.' + object_name(fk.parent_object_id) AS ParentTable,
                col.name AS ParentColumn,
                schema_name(fk.schema_id) + '.' + object_name(fk.referenced_object_id) AS ReferencedTable,
                refcol.name AS ReferencedColumn
            FROM sys.foreign_keys fk
            INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            INNER JOIN sys.columns col ON fkc.parent_object_id = col.object_id AND fkc.parent_column_id = col.column_id
            INNER JOIN sys.columns refcol ON fkc.referenced_object_id = refcol.object_id AND fkc.referenced_column_id = refcol.column_id
            WHERE fk.parent_object_id = OBJECT_ID(@TableName)
               OR fk.referenced_object_id = OBJECT_ID(@TableName)
            ORDER BY ParentTable, ForeignKeyName
            """;

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            var rows = await connection.QueryAsync<ForeignKeyRow>(
                new CommandDefinition(sql, new { TableName = tableName }, cancellationToken: cancellationToken));

            var renderedRows = new List<string[]>();
            foreach (var r in rows)
            {
                renderedRows.Add([
                    r.ForeignKeyName,
                    r.ParentTable + "." + r.ParentColumn,
                    "→",
                    r.ReferencedTable + "." + r.ReferencedColumn
                ]);
            }

            if (renderedRows.Count == 0)
            {
                return $"No foreign keys found for table '{tableName}' in database '{databaseName}'.";
            }

            return $"# Foreign Keys for `{tableName}`\n\n" + RenderMarkdownTable(["FK Name", "Source Column", "Dir", "Reference Column"], renderedRows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve foreign keys for table {TableName} in database {DatabaseName}.", tableName, databaseName);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    public async Task<Result<string>> GetSchemaIndexesAsync(string databaseName, string tableName, CancellationToken cancellationToken = default)
    {
        var accessCheck = await VerifyDatabaseAccessAsync(databaseName, cancellationToken);
        if (accessCheck.IsFailure)
        {
            return accessCheck.Error;
        }

        string sql = """
            SELECT 
                i.name AS IndexName,
                i.type_desc AS IndexType,
                i.is_unique AS IsUnique,
                i.is_primary_key AS IsPrimaryKey,
                c.name AS ColumnName,
                ic.is_included_column AS IsIncluded
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE i.object_id = OBJECT_ID(@TableName)
              AND i.is_hypothetical = 0
            ORDER BY i.index_id, ic.key_ordinal, ic.is_included_column
            """;

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            var rows = await connection.QueryAsync<IndexRow>(
                new CommandDefinition(sql, new { TableName = tableName }, cancellationToken: cancellationToken));

            var indexGroups = rows.GroupBy(r => new { IndexName = r.IndexName, IndexType = r.IndexType, IsUnique = r.IsUnique, IsPrimaryKey = r.IsPrimaryKey });

            var renderedRows = new List<string[]>();
            foreach (var g in indexGroups)
            {
                var keys = new List<string>();
                var includes = new List<string>();

                foreach (var col in g)
                {
                    if (col.IsIncluded)
                    {
                        includes.Add(col.ColumnName);
                    }
                    else
                    {
                        keys.Add(col.ColumnName);
                    }
                }

                string properties = g.Key.IsPrimaryKey ? "Primary Key" : (g.Key.IsUnique ? "Unique" : "Standard");
                renderedRows.Add([
                    g.Key.IndexName ?? "HEAP",
                    g.Key.IndexType,
                    properties,
                    string.Join(", ", keys),
                    string.Join(", ", includes)
                ]);
            }

            if (renderedRows.Count == 0)
            {
                return $"No indexes found for table '{tableName}' in database '{databaseName}'.";
            }

            return $"# Indexes for `{tableName}`\n\n" + RenderMarkdownTable(["Index Name", "Type", "Property", "Keys", "Included Columns"], renderedRows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve indexes for table {TableName} in database {DatabaseName}.", tableName, databaseName);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    public async Task<Result<string>> GetSchemaConstraintsAsync(string databaseName, string tableName, CancellationToken cancellationToken = default)
    {
        var accessCheck = await VerifyDatabaseAccessAsync(databaseName, cancellationToken);
        if (accessCheck.IsFailure)
        {
            return accessCheck.Error;
        }

        string defaultSql = """
            SELECT 
                dc.name AS ConstraintName,
                c.name AS ColumnName,
                dc.definition AS Definition,
                'DEFAULT' AS ConstraintType
            FROM sys.default_constraints dc
            INNER JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE dc.parent_object_id = OBJECT_ID(@TableName)
            """;

        string checkSql = """
            SELECT 
                cc.name AS ConstraintName,
                c.name AS ColumnName,
                cc.definition AS Definition,
                'CHECK' AS ConstraintType
            FROM sys.check_constraints cc
            LEFT JOIN sys.columns c ON cc.parent_object_id = c.object_id AND cc.parent_column_id = c.column_id
            WHERE cc.parent_object_id = OBJECT_ID(@TableName)
            """;

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            var defaultConstraints = await connection.QueryAsync<ConstraintRow>(
                new CommandDefinition(defaultSql, new { TableName = tableName }, cancellationToken: cancellationToken));

            var checkConstraints = await connection.QueryAsync<ConstraintRow>(
                new CommandDefinition(checkSql, new { TableName = tableName }, cancellationToken: cancellationToken));

            var renderedRows = new List<string[]>();
            foreach (var dc in defaultConstraints)
            {
                renderedRows.Add([ dc.ConstraintName, dc.ColumnName ?? "", "DEFAULT", dc.Definition ]);
            }
            foreach (var cc in checkConstraints)
            {
                renderedRows.Add([ cc.ConstraintName, cc.ColumnName ?? "", "CHECK", cc.Definition ]);
            }

            if (renderedRows.Count == 0)
            {
                return $"No default or check constraints found for table '{tableName}' in database '{databaseName}'.";
            }

            return $"# Constraints for `{tableName}`\n\n" + RenderMarkdownTable(["Constraint Name", "Column", "Type", "Definition"], renderedRows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve constraints for table {TableName} in database {DatabaseName}.", tableName, databaseName);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    public async Task<Result<string>> GetTriggerDefinitionAsync(string databaseName, string tableName, string triggerName, CancellationToken cancellationToken = default)
    {
        var accessCheck = await VerifyDatabaseAccessAsync(databaseName, cancellationToken);
        if (accessCheck.IsFailure)
        {
            return accessCheck.Error;
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            // Verify trigger belongs to the table
            var tName = await connection.QueryFirstOrDefaultAsync<string>(
                new CommandDefinition("SELECT name FROM sys.triggers WHERE name = @TriggerName AND parent_id = OBJECT_ID(@TableName)", new { TriggerName = triggerName, TableName = tableName }, cancellationToken: cancellationToken));

            if (string.IsNullOrEmpty(tName))
            {
                return SqlToAiError.ObjectNotFound(triggerName);
            }

            string? definition = await connection.QueryFirstOrDefaultAsync<string>(
                new CommandDefinition("SELECT definition FROM sys.sql_modules WHERE object_id = OBJECT_ID(@TriggerName)", new { TriggerName = triggerName }, cancellationToken: cancellationToken));

            if (string.IsNullOrWhiteSpace(definition))
            {
                return $"*Definition for trigger '{triggerName}' not available.* {DdlUnavailableNote}";
            }

            return $"# Trigger Definition: `{triggerName}` (on table `{tableName}`)\n\n```sql\n{definition.Trim()}\n```";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve trigger DDL for {TriggerName} in database {DatabaseName}.", triggerName, databaseName);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    public async Task<Result<string>> GetObjectReferencesAsync(string databaseName, string objectName, CancellationToken cancellationToken = default)
    {
        var accessCheck = await VerifyDatabaseAccessAsync(databaseName, cancellationToken);
        if (accessCheck.IsFailure)
        {
            return accessCheck.Error;
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            // Check if object is table or view
            string? objectType = await connection.QueryFirstOrDefaultAsync<string>(
                new CommandDefinition("SELECT RTRIM(type) FROM sys.objects WHERE object_id = OBJECT_ID(@ObjectName)", new { ObjectName = objectName }, cancellationToken: cancellationToken));

            if (string.IsNullOrEmpty(objectType))
            {
                return SqlToAiError.ObjectNotFound(objectName);
            }

            if (objectType != "U" && objectType != "V")
            {
                return SqlToAiError.InvalidReferenceType(objectName);
            }

            string sql = """
                SELECT 
                    referencing_schema_name AS SchemaName,
                    referencing_entity_name AS EntityName,
                    referencing_class_desc AS ClassDescription
                FROM sys.dm_sql_referencing_entities(@ObjectName, 'OBJECT')
                ORDER BY referencing_schema_name, referencing_entity_name
                """;

            var rows = await connection.QueryAsync<ReferenceRow>(
                new CommandDefinition(sql, new { ObjectName = objectName }, cancellationToken: cancellationToken));

            var renderedRows = new List<string[]>();
            foreach (var r in rows)
            {
                renderedRows.Add([ r.SchemaName, r.EntityName, r.ClassDescription ]);
            }

            if (renderedRows.Count == 0)
            {
                return $"No objects reference '{objectName}' in database '{databaseName}'.";
            }

            return $"# Referencing Entities for `{objectName}`\n\n" + RenderMarkdownTable(["Schema", "Entity Name", "Type"], renderedRows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve referencing entities for {ObjectName} in database {DatabaseName}.", objectName, databaseName);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    public async Task<Result<string>> GetRoutineParametersAsync(string databaseName, string routineName, CancellationToken cancellationToken = default)
    {
        var accessCheck = await VerifyDatabaseAccessAsync(databaseName, cancellationToken);
        if (accessCheck.IsFailure)
        {
            return accessCheck.Error;
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            // Check if object is procedure or function
            string? objectType = await connection.QueryFirstOrDefaultAsync<string>(
                new CommandDefinition("SELECT RTRIM(type) FROM sys.objects WHERE object_id = OBJECT_ID(@RoutineName)", new { RoutineName = routineName }, cancellationToken: cancellationToken));

            if (string.IsNullOrEmpty(objectType))
            {
                return SqlToAiError.ObjectNotFound(routineName);
            }

            if (objectType != "P" && objectType != "FN" && objectType != "TF" && objectType != "IF")
            {
                return SqlToAiError.InvalidParameterType(routineName);
            }

            string sql = """
                SELECT 
                    p.name AS ParameterName,
                    t.name AS DataType,
                    p.max_length AS MaxLength,
                    p.is_output AS IsOutput
                FROM sys.parameters p
                INNER JOIN sys.types t ON p.user_type_id = t.user_type_id
                WHERE p.object_id = OBJECT_ID(@RoutineName)
                ORDER BY p.parameter_id
                """;

            var rows = await connection.QueryAsync<ParameterRow>(
                new CommandDefinition(sql, new { RoutineName = routineName }, cancellationToken: cancellationToken));

            var renderedRows = new List<string[]>();
            foreach (var r in rows)
            {
                string pName = string.IsNullOrWhiteSpace(r.ParameterName) ? "(ReturnValue)" : r.ParameterName;
                renderedRows.Add([
                    pName,
                    r.DataType,
                    r.MaxLength == -1 ? "MAX" : r.MaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    r.IsOutput ? "Yes" : "No"
                ]);
            }

            if (renderedRows.Count == 0)
            {
                return $"Routine '{routineName}' accepts no parameters.";
            }

            return $"# Parameters for Routine `{routineName}`\n\n" + RenderMarkdownTable(["Parameter Name", "Type", "Length", "Output"], renderedRows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve routine parameters for {RoutineName} in database {DatabaseName}.", routineName, databaseName);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    private static string FormatTypeString(string type, int length, int precision, int scale)
    {
        if (type == "varchar" || type == "nvarchar" || type == "char" || type == "nchar")
        {
            return length == -1 ? $"{type}(max)" : $"{type}({length})";
        }
        if (type == "decimal" || type == "numeric")
        {
            return $"{type}({precision},{scale})";
        }
        return type;
    }

    private static string RenderMarkdownTable(string[] headers, List<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append("| ").Append(string.Join(" | ", headers)).AppendLine(" |");
        sb.Append("| ").Append(string.Join(" | ", headers.Select(_ => "---"))).AppendLine(" |");
        foreach (var row in rows)
        {
            sb.Append("| ").Append(string.Join(" | ", row.Select(r => r?.Replace("|", "\\|") ?? ""))).AppendLine(" |");
        }
        return sb.ToString();
    }

    private sealed class ColumnRow
    {
        public string ColumnName { get; init; } = string.Empty;
        public string DataType { get; init; } = string.Empty;
        public int MaxLength { get; init; }
        public int Precision { get; init; }
        public int Scale { get; init; }
        public bool IsNullable { get; init; }
        public bool IsIdentity { get; init; }
        public int IsPrimaryKey { get; init; }
    }

    private sealed class TriggerRow
    {
        public string TriggerName { get; init; } = string.Empty;
        public int? IsDisabled { get; init; }
        public int? IsUpdate { get; init; }
        public int? IsDelete { get; init; }
        public int? IsInsert { get; init; }
    }

    private sealed class ObjectRow
    {
        public string SchemaName { get; init; } = string.Empty;
        public string ObjectName { get; init; } = string.Empty;
        public string TypeDescription { get; init; } = string.Empty;
    }

    private sealed class ForeignKeyRow
    {
        public string ForeignKeyName { get; init; } = string.Empty;
        public string ParentTable { get; init; } = string.Empty;
        public string ParentColumn { get; init; } = string.Empty;
        public string ReferencedTable { get; init; } = string.Empty;
        public string ReferencedColumn { get; init; } = string.Empty;
    }

    private sealed class IndexRow
    {
        public string? IndexName { get; init; }
        public string IndexType { get; init; } = string.Empty;
        public bool IsUnique { get; init; }
        public bool IsPrimaryKey { get; init; }
        public string ColumnName { get; init; } = string.Empty;
        public bool IsIncluded { get; init; }
    }

    private sealed class ConstraintRow
    {
        public string ConstraintName { get; init; } = string.Empty;
        public string? ColumnName { get; init; }
        public string Definition { get; init; } = string.Empty;
        public string ConstraintType { get; init; } = string.Empty;
    }

    private sealed class ReferenceRow
    {
        public string SchemaName { get; init; } = string.Empty;
        public string EntityName { get; init; } = string.Empty;
        public string ClassDescription { get; init; } = string.Empty;
    }

    private sealed class ParameterRow
    {
        public string? ParameterName { get; init; }
        public string DataType { get; init; } = string.Empty;
        public int MaxLength { get; init; }
        public bool IsOutput { get; init; }
    }
}

