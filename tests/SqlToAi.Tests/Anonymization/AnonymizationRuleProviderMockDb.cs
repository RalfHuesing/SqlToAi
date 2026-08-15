#nullable enable

using System.Data.Common;
using SqlToAi.Database;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Anonymization;

internal sealed record RuleRowData(string DatabasePattern, string TablePattern, string ColumnPattern, bool Anonymize, string SchemaPattern = "%");

/// <summary>Bundles the mock connection's two independent boolean behaviors into one parameter object.</summary>
internal sealed record MockConnectionFlags(bool ThrowException = false, bool HasSchemaPatternColumn = false);

internal sealed class DummyConnectionFactory : IDatabaseConnectionFactory
{
    private readonly DbConnection? _connectionToReturn;
    public int ConnectionCreatedCount { get; private set; }

    public DummyConnectionFactory(DbConnection? connectionToReturn = null)
    {
        _connectionToReturn = connectionToReturn;
    }

    public DbConnection CreateConnection(string? databaseName = null)
    {
        ConnectionCreatedCount++;
        return _connectionToReturn ?? MockConnection.Create([]);
    }
}

/// <summary>
/// Factory for <c>AnonymizationRuleProvider</c> mock connections, built on the shared
/// <see cref="FakeDbConnection"/>/<see cref="FakeDbCommand"/>/<see cref="FakeDbDataReader"/> plumbing.
/// </summary>
internal static class MockConnection
{
    public static FakeDbConnection Create(
        List<RuleRowData> rows,
        MockConnectionFlags? flags = null,
        string? simulatedTableName = "dbo.AnonymizationRules")
    {
        return new FakeDbConnection(conn => new FakeDbCommand(conn, new FakeDbCommandHandlers(
            ExecuteReader: cmd => Dispatch(cmd, rows, flags ?? new MockConnectionFlags(), simulatedTableName))));
    }

    private static FakeDbDataReader Dispatch(FakeDbCommand cmd, List<RuleRowData> rows, MockConnectionFlags flags, string? simulatedTableName)
    {
        if (flags.ThrowException)
        {
            throw new InvalidOperationException("Connection failed simulated.");
        }
        if (cmd.CommandText.Contains("OBJECT_ID", StringComparison.Ordinal))
        {
            return new FakeDbDataReader(["Value"], simulatedTableName is null ? [] : [[simulatedTableName]]);
        }
        if (cmd.CommandText.Contains("COL_LENGTH", StringComparison.Ordinal))
        {
            return new FakeDbDataReader(["Value"], [[flags.HasSchemaPatternColumn]]);
        }
        bool includeSchemaPattern = cmd.CommandText.Contains("SchemaPattern", StringComparison.Ordinal);
        return BuildRuleReader(rows, includeSchemaPattern);
    }

    private static FakeDbDataReader BuildRuleReader(List<RuleRowData> rows, bool includeSchemaPattern)
    {
        string[] columns = includeSchemaPattern
            ? ["DatabasePattern", "SchemaPattern", "TablePattern", "ColumnPattern", "Anonymize"]
            : ["DatabasePattern", "TablePattern", "ColumnPattern", "Anonymize"];
        List<object?[]> dataRows = rows.Select(r => includeSchemaPattern
            ? new object?[] { r.DatabasePattern, r.SchemaPattern, r.TablePattern, r.ColumnPattern, r.Anonymize }
            : new object?[] { r.DatabasePattern, r.TablePattern, r.ColumnPattern, r.Anonymize })
            .ToList();
        return new FakeDbDataReader(columns, dataRows);
    }
}
