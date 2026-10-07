using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Metadata;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

internal sealed class SchemaPresentationFixture : IDatabaseConnectionFactory, IMetadataProvider, IAnonymizationRuleProvider
{
    public string? ObjectType { get; set; } = "U";
    public string? Definition { get; set; } = "-- SQL comment: run sql_get_schema\nCREATE VIEW sales.Overview AS SELECT Email FROM sales.Customers;";
    public bool FailQuery { get; set; }
    public string? FailQueryTerm { get; set; }
    public string ReferenceSchema { get; set; } = "sales";
    public string ReferenceName { get; set; } = "Customers";
    public bool RejectEnrichment { get; set; }
    public bool ExportDiscovery { get; set; }
    public IReadOnlyList<SchemaObject>? ExportObjects { get; set; }
    public string? StructuredName { get; set; }
    public bool EmptyDetails { get; set; }
    public List<string> QueriedObjectNames { get; } = [];
    public int MetadataCalls { get; private set; }
    public int RuleCalls { get; private set; }
    public int Connections { get; private set; }
    public int PolicyCalls { get; private set; }

    public SchemaService CreateService(bool allowed = true)
    {
        var options = new SqlToAiOptions();
        if (allowed) options.Databases.SchemaOnly = ["DemoDB"];
        options.Anonymizer.Enabled = true;
        options.AnonymizationRules.Enabled = true;
        var bound = Options.Create(options);
        var policy = new TrackingPolicy(this, new AnonymizationPolicyResolver(bound, this));
        return new SchemaService(this, new SecurityGuard(bound),
            new AccessLevelProvider(this, bound, NullLogger<AccessLevelProvider>.Instance),
            this, policy, bound, NullLogger<SchemaService>.Instance);
    }

    public DbConnection CreateConnection(string? databaseName = null)
    {
        Connections++;
        return new FakeDbConnection(connection => new FakeDbCommand(connection,
            new FakeDbCommandHandlers(ExecuteReader: Read)));
    }

    private FakeDbDataReader Read(FakeDbCommand command)
    {
        if (FailQuery || (FailQueryTerm is not null && command.CommandText.Contains(FailQueryTerm, StringComparison.Ordinal)))
            throw new InvalidOperationException("fixed schema failure");
        string sql = command.CommandText;
        foreach (DbParameter parameter in command.Parameters)
        {
            if (parameter.ParameterName is "ObjectName" or "TableName" or "RoutineName" or "ViewName")
                QueriedObjectNames.Add((string)parameter.Value!);
        }
        if (ExportDiscovery && sql.Contains("ParentObjectId", StringComparison.Ordinal) && !sql.Contains("SELECT t.object_id", StringComparison.Ordinal))
        {
            string description = ObjectType switch { "U" => "USER_TABLE", "V" => "VIEW", _ => "SQL_STORED_PROCEDURE" };
            if (ExportObjects is null)
                return new FakeDbDataReader(["ObjectId", "SchemaName", "ObjectName", "TypeDescription"], [[501, "sales", "Exported", description]]);
            return new FakeDbDataReader(["ObjectId", "SchemaName", "ObjectName", "TypeDescription", "ParentObjectId", "ParentSchemaName", "ParentObjectName", "ParentTypeDescription"],
                ExportObjects.Select(item => new object?[] { item.Identity.ObjectId, item.Identity.SchemaName, item.Identity.ObjectName,
                    item.Identity.Kind switch
                    {
                        SchemaObjectKind.Table => "USER_TABLE", SchemaObjectKind.View => "VIEW", SchemaObjectKind.Procedure => "SQL_STORED_PROCEDURE",
                        SchemaObjectKind.ScalarFunction => "SQL_SCALAR_FUNCTION", SchemaObjectKind.InlineTableValuedFunction => "SQL_INLINE_TABLE_VALUED_FUNCTION",
                        SchemaObjectKind.TableValuedFunction => "SQL_TABLE_VALUED_FUNCTION", _ => "SQL_TRIGGER"
                    }, item.Parent?.ObjectId, item.Parent?.SchemaName, item.Parent?.ObjectName, item.Parent?.Kind == SchemaObjectKind.View ? "VIEW" : "USER_TABLE" }).ToArray());
        }
        if (sql.Contains("i.name AS IndexName", StringComparison.Ordinal))
            return new FakeDbDataReader(["IndexName", "IndexType", "IsUnique", "IsPrimaryKey", "ColumnName", "IsIncluded"], EmptyDetails || StructuredName is null ? [] : [[StructuredName, "CLUSTERED", false, true, StructuredName, false], [StructuredName, "CLUSTERED", false, true, StructuredName, true]]);
        if (sql.Contains("AS ConstraintName", StringComparison.Ordinal))
            return new FakeDbDataReader(["ConstraintName", "ColumnName", "Definition", "ConstraintType"], EmptyDetails || StructuredName is null ? [] : [[StructuredName, StructuredName, "(N'A&amp;B`C')", "DEFAULT"]]);
        if (sql.Contains("p.name AS ParameterName", StringComparison.Ordinal))
            return new FakeDbDataReader(["ParameterName", "DataType", "MaxLength", "IsOutput"], EmptyDetails ? [] : [[StructuredName ?? "@Id", "int", 4, false]]);
        if (sql.Contains("is_identity", StringComparison.Ordinal))
            return new FakeDbDataReader(["ColumnName", "DataType", "MaxLength", "Precision", "Scale", "IsNullable", "IsIdentity", "IsPrimaryKey"],
                [[StructuredName ?? "Id", "int", 4, 10, 0, false, true, 1], ["Email", "nvarchar", 80, 0, 0, true, false, 0]]);
        if (sql.Contains("COUNT(*)", StringComparison.Ordinal))
            return new FakeDbDataReader(["Count"], [[2]]);
        if (sql.Contains("sys.sql_modules", StringComparison.Ordinal))
            return new FakeDbDataReader(["definition"], [[Definition]]);
        if (sql.Contains("sys.triggers", StringComparison.Ordinal))
        {
            if (sql.Contains("SELECT t.object_id", StringComparison.Ordinal))
                return new FakeDbDataReader(["object_id"], [[301]]);
            if (sql.Contains("SELECT name", StringComparison.Ordinal))
                return new FakeDbDataReader(["name"], [[StructuredName ?? "Changed"]]);
            return new FakeDbDataReader(["ObjectId", "SchemaName", "TriggerName", "IsDisabled", "IsUpdate", "IsDelete", "IsInsert"],
                [[301, "audit", StructuredName ?? "Changed", 1, 1, 0, 1]]);
        }
        if (sql.Contains("sys.foreign_keys", StringComparison.Ordinal))
            return new FakeDbDataReader(["ForeignKeyName", "ParentTable", "ParentColumn", "ReferencedTable", "ReferencedColumn", "ParentSchemaName", "ParentObjectName", "ReferencedSchemaName", "ReferencedObjectName"],
                EmptyDetails ? [] : [[StructuredName ?? "FK_Customer", "sales.Orders", StructuredName ?? "CustomerId", "sales.Customers", StructuredName ?? "Id", "sales", "Orders", ReferenceSchema, ReferenceName]]);
        if (sql.Contains("sys.dm_sql_referencing_entities", StringComparison.Ordinal))
            return new FakeDbDataReader(["SchemaName", "EntityName", "ClassDescription"], EmptyDetails ? [] : [[StructuredName ?? "sales", StructuredName ?? "Overview", "OBJECT_OR_COLUMN"]]);
        if (sql.Contains("sys.objects", StringComparison.Ordinal))
            return new FakeDbDataReader(["type"], ObjectType is null ? [] : [[ObjectType]]);
        throw new InvalidOperationException($"Unexpected query: {sql}");
    }

    public Task<string?> GetTableDescriptionAsync(string databaseName, string tableName, CancellationToken cancellationToken = default)
    {
        MetadataCalls++;
        if (RejectEnrichment) throw new InvalidOperationException("Metadata must not be called");
        return Task.FromResult<string?>("Fixed table description");
    }

    public Task<IReadOnlyDictionary<string, string>> GetColumnDescriptionsAsync(string databaseName, string tableName, CancellationToken cancellationToken = default)
    {
        MetadataCalls++;
        if (RejectEnrichment) throw new InvalidOperationException("Metadata must not be called");
        return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(StringComparer.Ordinal) { ["Email"] = "Fixed column description" });
    }

    public Task<bool> IsExcludedAsync(string databaseName, string schemaName, string tableName, string columnName, CancellationToken cancellationToken = default)
    {
        RuleCalls++;
        if (RejectEnrichment) throw new InvalidOperationException("Rules must not be called");
        return Task.FromResult(false);
    }

    private sealed class TrackingPolicy(SchemaPresentationFixture fixture, IAnonymizationPolicyResolver inner) : IAnonymizationPolicyResolver
    {
        public bool IsTokenizationActive
        {
            get
            {
                if (fixture.RejectEnrichment) throw new InvalidOperationException("Policy must not be called");
                return inner.IsTokenizationActive;
            }
        }

        public Task<bool> WillAnonymizeAsync(string databaseName, string tableName, string columnName, CancellationToken cancellationToken = default)
        {
            fixture.PolicyCalls++;
            if (fixture.RejectEnrichment) throw new InvalidOperationException("Policy must not be called");
            return inner.WillAnonymizeAsync(databaseName, tableName, columnName, cancellationToken);
        }
    }
}
