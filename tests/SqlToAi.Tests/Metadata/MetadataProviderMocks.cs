#nullable enable

using System.Data.Common;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Metadata;

internal sealed class DummyConnectionFactory : SqlToAi.Database.IDatabaseConnectionFactory
{
    private readonly DbConnection? _connectionToReturn;
    public int ConnectionCreatedCount { get; private set; }
    public string? LastDatabaseName { get; private set; }

    public DummyConnectionFactory(DbConnection? connectionToReturn = null)
    {
        _connectionToReturn = connectionToReturn;
    }

    public DbConnection CreateConnection(string? databaseName = null)
    {
        ConnectionCreatedCount++;
        LastDatabaseName = databaseName;
        return _connectionToReturn ?? new MockMetadataConnection();
    }
}

/// <summary>
/// Mock connection for <c>MetadataProvider</c>, built on the shared
/// <see cref="FakeDbConnection"/>/<see cref="FakeDbCommand"/>/<see cref="FakeDbDataReader"/>
/// plumbing. Kept as a named subclass (rather than a plain <see cref="FakeDbConnection"/>
/// instance) because other test files construct it directly by name with these exact optional
/// parameters.
/// </summary>
internal sealed class MockMetadataConnection : FakeDbConnection
{
    public MockMetadataConnection(string tableDesc = "", Dictionary<string, string>? columnDescs = null)
        : base(conn => new FakeDbCommand(conn, new FakeDbCommandHandlers(
            ExecuteScalar: _ => tableDesc,
            ExecuteReader: cmd => Dispatch(cmd, tableDesc, columnDescs ?? new Dictionary<string, string>()))))
    {
    }

    private static FakeDbDataReader Dispatch(FakeDbCommand cmd, string tableDesc, Dictionary<string, string> columnDescs)
    {
        foreach (DbParameter param in cmd.Parameters)
        {
            if (param.ParameterName == "TableName")
            {
                MetadataProviderTests.LastTableNameParameter = param.Value?.ToString();
            }
        }

        // If the query targets columns
        if (cmd.CommandText.Contains("sys.columns") || cmd.CommandText.Contains("ColumnName"))
        {
            List<object?[]> columnRows = columnDescs.Select(kv => new object?[] { kv.Key, kv.Value }).ToList();
            return new FakeDbDataReader(["ColumnName", "Description"], columnRows);
        }

        // Otherwise, it is table description
        return new FakeDbDataReader(["Description"], [[tableDesc]]);
    }
}
