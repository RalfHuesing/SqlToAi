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
        return _connectionToReturn ?? MockMetadataConnection.Create();
    }
}

/// <summary>
/// Factory for <c>MetadataProvider</c> mock connections, built on the shared
/// <see cref="FakeDbConnection"/>/<see cref="FakeDbCommand"/>/<see cref="FakeDbDataReader"/> plumbing.
/// </summary>
internal static class MockMetadataConnection
{
    public static FakeDbConnection Create(string tableDesc = "", Dictionary<string, string>? columnDescs = null)
    {
        return new FakeDbConnection(conn => new FakeDbCommand(conn, new FakeDbCommandHandlers(
            ExecuteScalar: _ => tableDesc,
            ExecuteReader: cmd => Dispatch(cmd, tableDesc, columnDescs ?? new Dictionary<string, string>()))));
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
