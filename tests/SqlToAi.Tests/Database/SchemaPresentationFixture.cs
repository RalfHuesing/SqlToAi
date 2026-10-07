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
        if (sql.Contains("is_identity", StringComparison.Ordinal))
            return new FakeDbDataReader(["ColumnName", "DataType", "MaxLength", "Precision", "Scale", "IsNullable", "IsIdentity", "IsPrimaryKey"],
                [["Id", "int", 4, 10, 0, false, true, 1], ["Email", "nvarchar", 80, 0, 0, true, false, 0]]);
        if (sql.Contains("COUNT(*)", StringComparison.Ordinal))
            return new FakeDbDataReader(["Count"], [[2]]);
        if (sql.Contains("sys.sql_modules", StringComparison.Ordinal))
            return new FakeDbDataReader(["definition"], [[Definition]]);
        if (sql.Contains("sys.triggers", StringComparison.Ordinal))
            return new FakeDbDataReader(["ObjectId", "SchemaName", "TriggerName", "IsDisabled", "IsUpdate", "IsDelete", "IsInsert"],
                [[301, "audit", "Changed", 1, 1, 0, 1]]);
        if (sql.Contains("sys.foreign_keys", StringComparison.Ordinal))
            return new FakeDbDataReader(["ForeignKeyName", "ParentTable", "ParentColumn", "ReferencedTable", "ReferencedColumn", "ParentSchemaName", "ParentObjectName", "ReferencedSchemaName", "ReferencedObjectName"],
                [["FK_Customer", "sales.Orders", "CustomerId", "sales.Customers", "Id", "sales", "Orders", ReferenceSchema, ReferenceName]]);
        if (sql.Contains("sys.dm_sql_referencing_entities", StringComparison.Ordinal))
            return new FakeDbDataReader(["SchemaName", "EntityName", "ClassDescription"], [["sales", "Overview", "OBJECT_OR_COLUMN"]]);
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
