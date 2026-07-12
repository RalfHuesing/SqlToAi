#nullable enable

using System.Data.Common;
using System.Text;
using Dapper;
using SqlToAi.Metadata;

namespace SqlToAi.Database;

internal sealed class TableSchemaRenderer
{
    private const string DdlUnavailableNote =
        "*Definition not available — either the object is encrypted, or the configured login lacks VIEW DEFINITION permission on it.*";

    private readonly IMetadataProvider _metadataProvider;

    public TableSchemaRenderer(IMetadataProvider metadataProvider)
    {
        _metadataProvider = metadataProvider;
    }

    public async Task<string> GetTableSchemaMarkdownAsync(DbConnection connection, string databaseName, string tableName, CancellationToken cancellationToken)
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

        // Emit trigger names explicitly so the agent can call sql_get_trigger_definition
        // directly without parsing the trigger summary table above.
        var triggerList = triggers.ToList();
        if (triggerList.Count > 0)
        {
            string names = string.Join(", ", triggerList.Select(t => $"`{t.TriggerName}`"));
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"- **Triggers:** {triggerList.Count} active — use `sql_get_trigger_definition` with: {names}");
        }

        return sb.ToString();
    }

    public static async Task<string> GetViewDefinitionMarkdownAsync(DbConnection connection, string viewName, CancellationToken cancellationToken)
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

    public static async Task<string> GetRoutineSchemaMarkdownAsync(DbConnection connection, string routineName, CancellationToken cancellationToken)
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

    private static string FormatTypeString(string type, int length, int precision, int scale)
    {
        if (type is "varchar" or "char")
        {
            return length == -1 ? $"{type}(max)" : $"{type}({length})";
        }
        if (type is "nvarchar" or "nchar")
        {
            // sys.columns.max_length stores byte length; nvarchar/nchar use 2 bytes per character.
            return length == -1 ? $"{type}(max)" : $"{type}({length / 2})";
        }
        if (type is "decimal" or "numeric")
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
}
