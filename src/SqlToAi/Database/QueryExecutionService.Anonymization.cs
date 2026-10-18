using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using SqlToAi.Anonymization;
using SqlToAi.Domain;
using SqlToAi.Mcp;

namespace SqlToAi.Database;

/// <summary>
/// Partial-class half of <see cref="QueryExecutionService"/> holding the row-serialization and
/// PII-anonymization helpers. Split purely to stay within the project's per-file line-count
/// budget (<c>MaxLineCount</c> 500).
/// </summary>
public sealed partial class QueryExecutionService
{
    /// <summary>Accumulates per-row anonymization outcomes while serializing a result set.</summary>
    private sealed class RowAnonymizationTracker
    {
        public bool WasAnonymized { get; set; }
        public List<string> AnonymizedColumns { get; } = [];
        public List<string> SearchableTokenColumns { get; } = [];

        public void RecordAnonymizedColumn(string qualifiedName, bool searchable)
        {
            WasAnonymized = true;
            if (!AnonymizedColumns.Contains(qualifiedName))
            {
                AnonymizedColumns.Add(qualifiedName);
            }
            if (searchable && !SearchableTokenColumns.Contains(qualifiedName))
            {
                SearchableTokenColumns.Add(qualifiedName);
            }
        }
    }

    /// <summary>
    /// The real source of an output column, resolved once per ordinal via the reader's schema
    /// table (<c>BaseSchemaName</c>/<c>BaseTableName</c>/<c>BaseColumnName</c>) — never the query's
    /// output alias. Any part is null when the provider can't resolve it (e.g. unsupported
    /// provider, or a computed/literal/aggregate expression with no traceable source column).
    /// </summary>
    private sealed record ColumnOrigin(string? TableName, string? ColumnName, string? SchemaName);

    private sealed record ResultColumn(int Ordinal, string Name);

    /// <summary>
    /// Bundles per-query anonymization context for passing between internal helpers.
    /// <see cref="Target"/> is the resolved execution target carried by every batch execution.
    /// </summary>
    private sealed record AnonymizationContext(
        bool Anonymize,
        ColumnOrigin?[]? ColumnOrigins,
        bool[]? CentralExclusions,
        bool UseTokenization,
        DatabaseTargetContext Target);

    private async Task<AnonymizationContext> ResolveAnonymizationContextAsync(
        DbDataReader reader,
        ResultColumn[] columns,
        bool anonymize,
        DatabaseTargetContext target,
        CancellationToken cancellationToken)
    {
        if (!anonymize)
        {
            return new AnonymizationContext(false, null, null, false, target);
        }

        var columnOrigins = GetColumnOrigins(reader);
        bool[]? centralExclusions = _anonymizationRuleProvider != null
            ? await ResolveCentralExclusionsAsync(target, columns, columnOrigins, cancellationToken)
            : null;

        return new AnonymizationContext(true, columnOrigins, centralExclusions, _tokenizationOptions.IsUsable, target);
    }

    /// <summary>
    /// Resolves the central rule provider's exclusion decision once per column ordinal (not per
    /// row), so a 1000-row result only pays for N rule lookups instead of N × rowCount. Passes the
    /// resolved base schema alongside the base table, so a same-named table in a different schema
    /// never inherits a rule scoped to another schema.
    /// </summary>
    private async Task<bool[]> ResolveCentralExclusionsAsync(
        DatabaseTargetContext target, ResultColumn[] columns, ColumnOrigin?[] columnOrigins, CancellationToken cancellationToken)
    {
        var result = new bool[columnOrigins.Length];
        foreach (var column in columns)
        {
            ColumnOrigin? origin = columnOrigins[column.Ordinal];
            if (origin is null
                || string.IsNullOrWhiteSpace(origin.SchemaName)
                || string.IsNullOrWhiteSpace(origin.TableName)
                || string.IsNullOrWhiteSpace(origin.ColumnName))
            {
                continue;
            }

            result[column.Ordinal] = await _anonymizationRuleProvider!.IsExcludedAsync(
                target, origin.SchemaName, origin.TableName, origin.ColumnName, cancellationToken);
        }
        return result;
    }

    private void AppendSerializedRow(
        StringBuilder sb,
        DbDataReader reader,
        ResultColumn[] columns,
        AnonymizationContext anonCtx,
        RowAnonymizationTracker tracker)
    {
        var rowDict = new Dictionary<string, object?>(columns.Length, StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            object? raw = reader.IsDBNull(column.Ordinal) ? null : reader.GetValue(column.Ordinal);
            raw = AnonymizeCell(column.Name, raw, anonCtx, column.Ordinal, tracker);
            rowDict[column.Name] = raw;
        }

        sb.AppendLine(JsonSerializer.Serialize(rowDict, typeof(Dictionary<string, object?>), McpJsonContext.Default));
    }

    private object? AnonymizeCell(
        string columnName,
        object? raw,
        AnonymizationContext anonCtx,
        int columnIndex,
        RowAnonymizationTracker tracker)
    {
        if (!anonCtx.Anonymize || raw is not string strVal)
        {
            return raw;
        }

        if (IsFlagSet(anonCtx.CentralExclusions, columnIndex))
        {
            return raw;
        }

        ColumnOrigin? origin = anonCtx.ColumnOrigins != null && columnIndex < anonCtx.ColumnOrigins.Length ? anonCtx.ColumnOrigins[columnIndex] : null;
        string? tableName = origin?.TableName;
        var columnContext = new AnonymizationColumnContext(tableName, origin?.ColumnName, origin?.SchemaName);
        string anonymizedValue = anonCtx.UseTokenization
            ? _anonymizer.Tokenize(strVal, columnContext, anonCtx.Target)
            : _anonymizer.Anonymize(strVal, columnContext);

        if (anonymizedValue != strVal)
        {
            // Qualify with the resolved base table when known, so the LLM (and the human it
            // reports to) can act on a concrete "TableName.ColumnName" instead of a bare alias.
            // Deliberately reports the alias (columnName), not the resolved origin column, since
            // the alias is the JSON key the AI actually sees in the result — only the exclusion
            // *decision* above is origin-based, not this display name.
            string qualifiedName = string.IsNullOrEmpty(tableName) ? columnName : $"{tableName}.{columnName}";
            tracker.RecordAnonymizedColumn(qualifiedName, anonCtx.UseTokenization);
        }

        return anonymizedValue;
    }

    private static bool IsFlagSet(bool[]? flags, int index) =>
        flags != null && index < flags.Length && flags[index];

    /// <summary>
    /// Resolves each output column's real source (base schema + base table + base column) via the
    /// reader's schema table, so the anonymization exclusion decision can be based on where a value
    /// actually comes from instead of the query's output alias (e.g. <c>SELECT SSN AS RecordId</c>
    /// must never be judged by the alias <c>RecordId</c>). The resolved schema lets two same-named
    /// tables in different schemas be told apart, so an exclusion/rule scoped to one schema never
    /// silently applies to the other. Tolerates providers where <see cref="DbDataReader.GetSchemaTable"/>
    /// is unavailable or incomplete — any column whose origin can't be determined simply gets a null
    /// <see cref="ColumnOrigin"/>, which the anonymizer then treats fail-safe (never excluded via
    /// the plain pattern list).
    /// </summary>
    private static ColumnOrigin?[] GetColumnOrigins(DbDataReader reader)
    {
        var origins = new ColumnOrigin?[reader.FieldCount];
        try
        {
            var schemaTable = reader.GetSchemaTable();
            if (schemaTable != null)
            {
                PopulateColumnOrigins(schemaTable, origins);
            }
        }
        catch (Exception ignored)
        {
            _ = ignored; // Safe fallback: schema table not available for this provider
        }
        return origins;
    }

    private static void PopulateColumnOrigins(DataTable schemaTable, ColumnOrigin?[] origins)
    {
        bool hasOrdinal = schemaTable.Columns.Contains("ColumnOrdinal");
        bool hasBaseTable = schemaTable.Columns.Contains("BaseTableName");
        bool hasBaseColumn = schemaTable.Columns.Contains("BaseColumnName");
        bool hasBaseSchema = schemaTable.Columns.Contains("BaseSchemaName");

        for (int i = 0; i < schemaTable.Rows.Count; i++)
        {
            var row = schemaTable.Rows[i];
            int ordinal = hasOrdinal ? Convert.ToInt32(row["ColumnOrdinal"], System.Globalization.CultureInfo.InvariantCulture) : i;
            if (ordinal < 0 || ordinal >= origins.Length)
            {
                continue;
            }

            origins[ordinal] = new ColumnOrigin(
                ReadOriginValue(row, "BaseTableName", hasBaseTable),
                ReadOriginValue(row, "BaseColumnName", hasBaseColumn),
                ReadOriginValue(row, "BaseSchemaName", hasBaseSchema));
        }
    }

    /// <summary>Reads a normalized (null-if-empty) schema table value, or null when the column itself is unavailable.</summary>
    private static string? ReadOriginValue(DataRow row, string columnName, bool columnAvailable)
    {
        if (!columnAvailable)
        {
            return null;
        }
        string? value = row[columnName]?.ToString();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static ResultColumn[] GetColumnNames(DbDataReader reader)
    {
        var hiddenOrdinals = new HashSet<int>();
        try
        {
            var schemaTable = reader.GetSchemaTable();
            if (schemaTable?.Columns.Contains("IsHidden") == true)
            {
                bool hasOrdinal = schemaTable.Columns.Contains("ColumnOrdinal");
                for (int i = 0; i < schemaTable.Rows.Count; i++)
                {
                    var row = schemaTable.Rows[i];
                    if (row["IsHidden"] is true)
                    {
                        hiddenOrdinals.Add(hasOrdinal
                            ? Convert.ToInt32(row["ColumnOrdinal"], System.Globalization.CultureInfo.InvariantCulture)
                            : i);
                    }
                }
            }
        }
        catch (Exception ignored)
        {
            _ = ignored; // Preserve all fields when schema metadata is unavailable.
            hiddenOrdinals.Clear();
        }

        var columns = new List<ResultColumn>(reader.FieldCount);
        for (int i = 0; i < reader.FieldCount; i++)
        {
            if (!hiddenOrdinals.Contains(i))
            {
                columns.Add(new ResultColumn(i, reader.GetName(i)));
            }
        }
        return columns.ToArray();
    }
}
