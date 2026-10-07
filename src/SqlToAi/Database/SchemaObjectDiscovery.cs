using System.Data.Common;
using Dapper;

namespace SqlToAi.Database;

internal static class SchemaObjectDiscovery
{
    internal static Task<IEnumerable<ObjectRow>> QueryAsync(
        DbConnection connection, string searchPattern, string? typeFilter, int? limit, bool exportOnly, CancellationToken cancellationToken)
    {
        // Interactive search also includes constraint objects, ranked after the primary kinds.
        // Export selects supported kinds in the same catalog query without a TOP clause.
        string sql = $"""
            SELECT {(limit.HasValue ? "TOP (@Limit)" : "")}
                o.object_id AS ObjectId,
                schema_name(o.schema_id) AS SchemaName,
                o.name AS ObjectName,
                o.type_desc AS TypeDescription,
                parent.object_id AS ParentObjectId,
                schema_name(parent.schema_id) AS ParentSchemaName,
                parent.name AS ParentObjectName,
                parent.type_desc AS ParentTypeDescription
            FROM sys.objects o
            LEFT JOIN sys.objects parent ON parent.object_id = o.parent_object_id
            WHERE o.is_ms_shipped = 0
              AND o.name LIKE @SearchPattern
              AND (@TypeFilter IS NULL OR o.type_desc LIKE @TypeFilter)
              AND (@ExportOnly = 0 OR (
                  o.type IN ('U', 'V', 'P', 'FN', 'IF', 'TF', 'TR')
                  AND (o.type <> 'TR' OR parent.type IN ('U', 'V'))))
            ORDER BY
                CASE o.type_desc
                    WHEN 'USER_TABLE' THEN 0
                    WHEN 'VIEW' THEN 1
                    WHEN 'SQL_STORED_PROCEDURE' THEN 2
                    WHEN 'SQL_SCALAR_FUNCTION' THEN 2
                    WHEN 'SQL_TABLE_VALUED_FUNCTION' THEN 2
                    WHEN 'SQL_INLINE_TABLE_VALUED_FUNCTION' THEN 2
                    WHEN 'SQL_TRIGGER' THEN 3
                    ELSE 9
                END,
                schema_name(o.schema_id), o.name
            """;

        return connection.QueryAsync<ObjectRow>(new CommandDefinition(sql,
            new { Limit = limit, SearchPattern = searchPattern, TypeFilter = typeFilter, ExportOnly = exportOnly }, cancellationToken: cancellationToken));
    }

    internal sealed class ObjectRow
    {
        public int ObjectId { get; init; }
        public string SchemaName { get; init; } = string.Empty;
        public string ObjectName { get; init; } = string.Empty;
        public string TypeDescription { get; init; } = string.Empty;
        public int? ParentObjectId { get; init; }
        public string? ParentSchemaName { get; init; }
        public string? ParentObjectName { get; init; }
        public string? ParentTypeDescription { get; init; }

        public SchemaObject ToSchemaObject()
        {
            var identity = new SchemaObjectIdentity(ObjectId, SchemaName, ObjectName, ParseKind(TypeDescription));
            var parent = identity.Kind == SchemaObjectKind.Trigger && ParentObjectId.HasValue
                ? new SchemaObjectIdentity(ParentObjectId.Value, ParentSchemaName!, ParentObjectName!, ParseKind(ParentTypeDescription!))
                : null;
            return new SchemaObject(identity, parent);
        }

        private static SchemaObjectKind ParseKind(string typeDescription) => typeDescription switch
        {
            "USER_TABLE" => SchemaObjectKind.Table,
            "VIEW" => SchemaObjectKind.View,
            "SQL_STORED_PROCEDURE" => SchemaObjectKind.Procedure,
            "SQL_SCALAR_FUNCTION" => SchemaObjectKind.ScalarFunction,
            "SQL_INLINE_TABLE_VALUED_FUNCTION" => SchemaObjectKind.InlineTableValuedFunction,
            "SQL_TABLE_VALUED_FUNCTION" => SchemaObjectKind.TableValuedFunction,
            "SQL_TRIGGER" => SchemaObjectKind.Trigger,
            _ => throw new InvalidOperationException($"Unsupported schema object type '{typeDescription}'.")
        };
    }
}
