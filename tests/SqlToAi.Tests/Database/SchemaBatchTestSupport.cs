using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Metadata;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

internal sealed class SchemaBatchTestFactory : IDatabaseConnectionFactory
{
    public DbConnection CreateTargetConnection(SqlToAi.Domain.DatabaseTargetContext target) => CreateConnection(target.DatabaseName);
    public SqlToAi.Domain.Result<System.Data.Common.DbConnection> CreateCatalogConnection(string? serverAlias) => throw new NotSupportedException();

    internal SqlToAiOptions Options { get; } = new SqlToAiOptions { Databases = new DatabasesOptions { SchemaOnly = [TestConstants.QualifiedDatabaseName] } }.WithNamedServers(TestTargets.ServerAlias);
    internal List<string> SchemaFetches { get; } = [];
    internal List<string> Commands { get; } = [];
    internal int ConnectionCount { get; private set; }
    internal bool FailCatalog { get; init; }
    internal string? FailingSchema { get; init; }
    internal Exception? OpenFailure { get; init; }
    internal RecordingMetadata Metadata { get; } = new();
    internal List<string> MetadataQueryNames { get; } = [];
    internal string CatalogSchema { get; init; } = "dbo";

    internal SchemaService Service(IMetadataProvider? metadata = null, SqlToAi.Anonymization.IAnonymizationPolicyResolver? policy = null)
    {
        var bound = Microsoft.Extensions.Options.Options.Create(Options);
        return new SchemaService(this, new SecurityGuard(bound), new AccessLevelProvider(bound),
            metadata ?? Metadata, policy ?? new SearchableAnonymizePolicyResolver("Secret"), bound, NullLogger<SchemaService>.Instance);
    }

    public DbConnection CreateConnection(string? databaseName = null)
    {
        ConnectionCount++;
        return new FakeDbConnection(connection => new FakeDbCommand(connection, new FakeDbCommandHandlers(ExecuteReader: Read)),
            new FakeDbConnectionOptions(OpenFailure: OpenFailure));
    }

    private FakeDbDataReader Read(FakeDbCommand command)
    {
        string sql = command.CommandText;
        Commands.Add(sql);
        var parameters = command.Parameters.Cast<DbParameter>().ToDictionary(item => item.ParameterName, item => item.Value, StringComparer.Ordinal);
        if (sql is "CUSTOM_TABLE_DESCRIPTION" or "CUSTOM_COLUMN_DESCRIPTION" || sql.Contains("MS_Description", StringComparison.Ordinal))
        {
            MetadataQueryNames.Add((string)parameters["TableName"]!);
            return sql == "CUSTOM_COLUMN_DESCRIPTION" || sql.Contains("c.name AS ColumnName", StringComparison.Ordinal)
                ? new FakeDbDataReader(["ColumnName", "Description"], [["Secret", "custom column description"]])
                : new FakeDbDataReader(["Description"], [["custom table description"]]);
        }
        if (sql.Contains("o.type_desc AS TypeDescription", StringComparison.Ordinal))
        {
            if (FailCatalog) throw new InvalidOperationException("fixed catalog failure");
            string name = (string)parameters["ObjectName"]!;
            string? schema = parameters["SchemaName"] as string;
            string type = name switch
            {
                "View" => "VIEW", "Procedure" => "SQL_STORED_PROCEDURE", "Scalar" => "SQL_SCALAR_FUNCTION",
                "Inline" => "SQL_INLINE_TABLE_VALUED_FUNCTION", "Function" => "SQL_TABLE_VALUED_FUNCTION",
                "Unsupported" => "CHECK_CONSTRAINT", _ => "USER_TABLE"
            };
            int id = name switch { "Orders" => 1, "View" => 2, "Procedure" => 3, "Scalar" => 4, "Inline" => 5, "Function" => 6, _ => 7 };
            object?[][] rows = name == "missing" || (schema is not null && schema != CatalogSchema) ? [] : [[id, CatalogSchema, name, type]];
            if (name == "Ambiguous") rows = [[7, "dbo", name, type], [8, "sales", name, type]];
            return new FakeDbDataReader(["ObjectId", "SchemaName", "ObjectName", "TypeDescription"], rows);
        }
        string? objectName = parameters.Values.OfType<string>().FirstOrDefault();
        if (sql.Contains("FROM sys.columns c", StringComparison.Ordinal))
        {
            SchemaFetches.Add(objectName!);
            if (objectName == FailingSchema) throw new InvalidOperationException("fixed schema failure");
            return new FakeDbDataReader(["ColumnName", "DataType", "MaxLength", "IsNullable"],
                [["Id", "int", 4, false], ["Secret", "varchar", 50, true], ["Exempt", "varchar", 50, true]]);
        }
        if (sql.Contains("SELECT definition", StringComparison.Ordinal))
        {
            SchemaFetches.Add(objectName!);
            return new FakeDbDataReader(["definition"], [["CREATE ... -- full definition"]]);
        }
        if (sql.Contains("FROM sys.triggers", StringComparison.Ordinal))
            return new FakeDbDataReader(["ObjectId", "TriggerName", "IsInsert"], [[10, "Changed", 1]]);
        if (sql.Contains("COUNT(*)", StringComparison.Ordinal)) return new FakeDbDataReader(["Count"], [[1]]);
        if (sql.Contains("SELECT RTRIM(type)", StringComparison.Ordinal))
            return new FakeDbDataReader(["type"], [["U"]]);
        throw new InvalidOperationException($"Unexpected test query: {sql}");
    }

    internal sealed class RecordingMetadata : IMetadataProvider
    {
        internal List<string> Calls { get; } = [];

        public Task<string?> GetTableDescriptionAsync(string databaseName, string tableName, CancellationToken cancellationToken = default)
        {
            Calls.Add(tableName);
            return Task.FromResult<string?>("enriched table description");
        }

        public Task<IReadOnlyDictionary<string, string>> GetColumnDescriptionsAsync(string databaseName, string tableName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { ["Secret"] = "enriched column description" });

        public Task<string?> GetTableDescriptionAsync(SqlToAi.Domain.DatabaseTargetContext target, string tableName, CancellationToken cancellationToken = default)
            => GetTableDescriptionAsync(target.DatabaseName, tableName, cancellationToken);

        public Task<IReadOnlyDictionary<string, string>> GetColumnDescriptionsAsync(SqlToAi.Domain.DatabaseTargetContext target, string tableName, CancellationToken cancellationToken = default)
            => GetColumnDescriptionsAsync(target.DatabaseName, tableName, cancellationToken);

        public Task<string?> GetTableDescriptionAsync(SqlToAi.Domain.DatabaseTargetContext target, SqlToAi.Database.SchemaObjectIdentity identity, CancellationToken cancellationToken = default)
            => GetTableDescriptionAsync(target, identity.DisplayName, cancellationToken);

        public Task<IReadOnlyDictionary<string, string>> GetColumnDescriptionsAsync(SqlToAi.Domain.DatabaseTargetContext target, SqlToAi.Database.SchemaObjectIdentity identity, CancellationToken cancellationToken = default)
            => GetColumnDescriptionsAsync(target, identity.DisplayName, cancellationToken);
    }
}
