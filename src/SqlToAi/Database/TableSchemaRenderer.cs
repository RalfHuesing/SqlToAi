#nullable enable

using System.Data.Common;
using System.Text;
using Dapper;
using SqlToAi.Anonymization;
using SqlToAi.Metadata;

namespace SqlToAi.Database;

internal sealed class TableSchemaRenderer
{
    private const string DdlUnavailableNote =
        "*Definition not available — either the object is encrypted, or the configured login lacks VIEW DEFINITION permission on it.*";

    /// <summary>SQL Server types whose values are read as .NET strings and are therefore ever subject to anonymization.</summary>
    private static readonly HashSet<string> AnonymizableSqlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "char", "varchar", "text", "nchar", "nvarchar", "ntext"
    };

    private readonly IMetadataProvider _metadataProvider;
    private readonly IAnonymizationPolicyResolver _policyResolver;

    public TableSchemaRenderer(IMetadataProvider metadataProvider, IAnonymizationPolicyResolver policyResolver)
    {
        _metadataProvider = metadataProvider;
        _policyResolver = policyResolver;
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

        string? tableDesc = await _metadataProvider.GetTableDescriptionAsync(databaseName, tableName, cancellationToken);
        var columnDescs = await _metadataProvider.GetColumnDescriptionsAsync(databaseName, tableName, cancellationToken);
        var anonymizedFlags = await ResolveAnonymizedFlagsAsync(databaseName, tableName, columns, cancellationToken);

        var triggers = (await QueryTriggersAsync(connection, tableName, cancellationToken)).ToList();

        var sb = new StringBuilder();
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"# Schema for Table/View: `{tableName}`");
        if (!string.IsNullOrWhiteSpace(tableDesc))
        {
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"*Description:* {tableDesc}").AppendLine();
        }

        AppendColumnsTable(sb, columns, columnDescs, anonymizedFlags);
        AppendTriggersTable(sb, triggers);
        await AppendDiscoveryIndexAsync(sb, connection, tableName, triggers, cancellationToken);

        return sb.ToString();
    }

    /// <summary>How a column's string values are currently handled by the anonymization pipeline.</summary>
    private enum ColumnAnonymizationState
    {
        /// <summary>Not anonymized — either never a string type, or excluded by configuration.</summary>
        None,

        /// <summary>Regular scramble/hash masking.</summary>
        Masked,

        /// <summary>Reversible, searchable tokenization (see <c>Anonymizer.Tokenize</c>).</summary>
        SearchableToken
    }

    /// <summary>
    /// Resolves, once per column, whether its string values would currently be anonymized —
    /// so the schema markdown can tell the caller upfront, before any query is written. Columns
    /// whose SQL type is never read as a string (e.g. int, bit) are never anonymized regardless
    /// of configuration, so they are reported as "No" without even asking the policy resolver.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, ColumnAnonymizationState>> ResolveAnonymizedFlagsAsync(
        string databaseName, string tableName, IEnumerable<ColumnRow> columns, CancellationToken cancellationToken)
    {
        var flags = new Dictionary<string, ColumnAnonymizationState>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in columns)
        {
            flags[col.ColumnName] = await ResolveColumnStateAsync(databaseName, tableName, col, cancellationToken);
        }
        return flags;
    }

    private async Task<ColumnAnonymizationState> ResolveColumnStateAsync(
        string databaseName, string tableName, ColumnRow col, CancellationToken cancellationToken)
    {
        bool willAnonymize = AnonymizableSqlTypes.Contains(col.DataType)
            && await _policyResolver.WillAnonymizeAsync(databaseName, tableName, col.ColumnName, cancellationToken);
        if (!willAnonymize)
        {
            return ColumnAnonymizationState.None;
        }

        bool searchable = await _policyResolver.IsSearchableTokenAsync(databaseName, tableName, col.ColumnName, cancellationToken);
        return searchable ? ColumnAnonymizationState.SearchableToken : ColumnAnonymizationState.Masked;
    }

    private static void AppendColumnsTable(
        StringBuilder sb, IEnumerable<ColumnRow> columns, IReadOnlyDictionary<string, string> columnDescs,
        IReadOnlyDictionary<string, ColumnAnonymizationState> anonymizedFlags)
    {
        var headers = new[] { "Column Name", "Type", "Nullable", "Key/Identity", "Anonymized", "Description" };
        var renderedRows = new List<string[]>();
        foreach (var col in columns)
        {
            string type = FormatTypeString(col.DataType, col.MaxLength, col.Precision, col.Scale);
            string nullable = col.IsNullable ? "Yes" : "No";

            var keyFlags = new List<string>();
            if (col.IsPrimaryKey == 1) keyFlags.Add("PK");
            if (col.IsIdentity) keyFlags.Add("Identity");
            string keyStr = string.Join(", ", keyFlags);

            anonymizedFlags.TryGetValue(col.ColumnName, out var state);
            string anonymized = FormatAnonymizedState(state);

            columnDescs.TryGetValue(col.ColumnName, out string? desc);
            renderedRows.Add([col.ColumnName, type, nullable, keyStr, anonymized, desc ?? ""]);
        }
        sb.AppendLine(RenderMarkdownTable(headers, renderedRows));
    }

    private static string FormatAnonymizedState(ColumnAnonymizationState state) => state switch
    {
        ColumnAnonymizationState.SearchableToken => "Yes (searchable)",
        ColumnAnonymizationState.Masked => "Yes",
        _ => "No"
    };

    private static void AppendTriggersTable(StringBuilder sb, List<TriggerRow> triggers)
    {
        if (triggers.Count == 0) return;

        sb.AppendLine("## Triggers");
        var trigHeaders = new[] { "Trigger Name", "Insert", "Update", "Delete", "Status" };
        var trigRows = triggers.Select(t => new[]
        {
            t.TriggerName,
            t.IsInsert == 1 ? "✓" : "",
            t.IsUpdate == 1 ? "✓" : "",
            t.IsDelete == 1 ? "✓" : "",
            t.IsDisabled == 1 ? "Disabled" : "Active"
        }).ToList();
        sb.AppendLine(RenderMarkdownTable(trigHeaders, trigRows));
    }

    private static async Task AppendDiscoveryIndexAsync(StringBuilder sb, DbConnection connection, string tableName, List<TriggerRow> triggers, CancellationToken cancellationToken)
    {
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

        if (triggers.Count > 0)
        {
            string names = string.Join(", ", triggers.Select(t => $"`{t.TriggerName}`"));
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"- **Triggers:** {triggers.Count} active — use `sql_get_trigger_definition` with: {names}");
        }
    }

    private static async Task<IEnumerable<TriggerRow>> QueryTriggersAsync(DbConnection connection, string tableName, CancellationToken cancellationToken)
    {
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

        return await connection.QueryAsync<TriggerRow>(
            new CommandDefinition(triggersSql, new { TableName = tableName }, cancellationToken: cancellationToken));
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
