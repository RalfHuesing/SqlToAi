#nullable enable

using System.Data.Common;
using Dapper;
using SqlToAi.Domain;

namespace SqlToAi.Database;

internal static class DetailSchemaRenderer
{
    internal const string DdlUnavailableNote =
        "*Definition not available — either the object is encrypted, or the configured login lacks VIEW DEFINITION permission on it.*";

    /// <summary>
    /// Verifies the object exists and is a table or view. Used by the foreign-key/index/constraint
    /// detail queries, which only make sense for tables (and indexed views) — without this check,
    /// calling them on a procedure or function silently returns an empty "not found" result instead
    /// of signalling that the object type itself is wrong.
    /// </summary>
    private static async Task<Result<string>?> ValidateTableOrViewAsync(DbConnection connection, string objectName, CancellationToken cancellationToken)
    {
        string? objectType = await connection.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition("SELECT RTRIM(type) FROM sys.objects WHERE object_id = OBJECT_ID(@ObjectName)", new { ObjectName = objectName }, cancellationToken: cancellationToken));

        if (string.IsNullOrEmpty(objectType))
        {
            return SqlToAiError.ObjectNotFound(objectName);
        }

        if (objectType != SqlServerObjectType.UserTable && objectType != SqlServerObjectType.View)
        {
            return SqlToAiError.InvalidDetailQueryType(objectName);
        }

        return null;
    }

    public static async Task<Result<string>> GetSchemaForeignKeysAsync(DbConnection connection, string tableName, string databaseName, CancellationToken cancellationToken)
    {
        var typeCheck = await ValidateTableOrViewAsync(connection, tableName, cancellationToken);
        if (typeCheck is not null)
        {
            return typeCheck;
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
            ORDER BY ParentTable, ForeignKeyName, fkc.constraint_column_id
            """;

        var rows = await connection.QueryAsync<ForeignKeyRow>(
            new CommandDefinition(sql, new { TableName = tableName }, cancellationToken: cancellationToken));

        // Composite-key FKs produce one row per column pair, all sharing the same FK name. Group
        // them back into a single table row so a 2-column FK reads as one entry instead of two,
        // which otherwise makes the result look twice as large as the Discovery Index's FK count.
        var fkGroups = rows.GroupBy(r => new { r.ForeignKeyName, r.ParentTable, r.ReferencedTable });

        var renderedRows = new List<string[]>();
        foreach (var g in fkGroups)
        {
            var parentColumns = g.Select(r => r.ParentColumn).ToList();
            var referencedColumns = g.Select(r => r.ReferencedColumn).ToList();
            renderedRows.Add([
                g.Key.ForeignKeyName,
                FormatColumnReference(g.Key.ParentTable, parentColumns),
                "→",
                FormatColumnReference(g.Key.ReferencedTable, referencedColumns)
            ]);
        }

        if (renderedRows.Count == 0)
        {
            return $"No foreign keys found for table '{tableName}' in database '{databaseName}'.";
        }

        return $"# Foreign Keys for `{tableName}`\n\n" + MarkdownTableRenderer.Render(["FK Name", "Source Column", "Dir", "Reference Column"], renderedRows);
    }

    /// <summary>
    /// Formats a table + column-list reference. A single column keeps the familiar
    /// <c>table.column</c> form; a composite key is rendered as <c>table (col1, col2)</c>
    /// so multi-column foreign keys stay readable as one entry.
    /// </summary>
    private static string FormatColumnReference(string table, List<string> columns) =>
        columns.Count == 1 ? $"{table}.{columns[0]}" : $"{table} ({string.Join(", ", columns)})";

    public static async Task<Result<string>> GetSchemaIndexesAsync(DbConnection connection, string tableName, string databaseName, CancellationToken cancellationToken)
    {
        var typeCheck = await ValidateTableOrViewAsync(connection, tableName, cancellationToken);
        if (typeCheck is not null)
        {
            return typeCheck;
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

        return $"# Indexes for `{tableName}`\n\n" + MarkdownTableRenderer.Render(["Index Name", "Type", "Property", "Keys", "Included Columns"], renderedRows);
    }

    public static async Task<Result<string>> GetSchemaConstraintsAsync(DbConnection connection, string tableName, string databaseName, CancellationToken cancellationToken)
    {
        var typeCheck = await ValidateTableOrViewAsync(connection, tableName, cancellationToken);
        if (typeCheck is not null)
        {
            return typeCheck;
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

        return $"# Constraints for `{tableName}`\n\n" + MarkdownTableRenderer.Render(["Constraint Name", "Column", "Type", "Definition"], renderedRows);
    }

    public static async Task<Result<string>> GetTriggerDefinitionAsync(DbConnection connection, string tableName, string triggerName, string databaseName, CancellationToken cancellationToken)
    {
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

    public static async Task<Result<string>> GetObjectReferencesAsync(DbConnection connection, string objectName, string databaseName, CancellationToken cancellationToken)
    {
        // Check if object is table or view
        string? objectType = await connection.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition("SELECT RTRIM(type) FROM sys.objects WHERE object_id = OBJECT_ID(@ObjectName)", new { ObjectName = objectName }, cancellationToken: cancellationToken));

        if (string.IsNullOrEmpty(objectType))
        {
            return SqlToAiError.ObjectNotFound(objectName);
        }

        if (objectType != SqlServerObjectType.UserTable && objectType != SqlServerObjectType.View)
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

        return $"# Referencing Entities for `{objectName}`\n\n" + MarkdownTableRenderer.Render(["Schema", "Entity Name", "Type"], renderedRows);
    }

    public static async Task<Result<string>> GetRoutineParametersAsync(DbConnection connection, string routineName, string databaseName, CancellationToken cancellationToken)
    {
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

        return $"# Parameters for Routine `{routineName}`\n\n" + MarkdownTableRenderer.Render(["Parameter Name", "Type", "Length", "Output"], renderedRows);
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
