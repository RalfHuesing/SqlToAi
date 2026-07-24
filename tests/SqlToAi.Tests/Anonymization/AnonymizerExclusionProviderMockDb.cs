#nullable enable

using System.Data.Common;
using SqlToAi.Database;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Anonymization;

/// <summary>One row of mock exclusion data: table/column, and an optional 3rd schema column.</summary>
internal sealed record ExclusionRow(string Table, string Column, string? Schema = null);

/// <summary>Bundles the mock connection's two independent boolean behaviors into one parameter object.</summary>
internal sealed record ExclusionMockFlags(bool ThrowException = false, bool HasSchemaColumn = false);

/// <summary>Bundles the dispatch inputs for <see cref="ExclusionMockConnection"/> into one parameter object (see AiNetLinter <c>MaxMethodParameterCount</c>).</summary>
internal sealed record ExclusionDispatchConfig(List<ExclusionRow> Rows, ExclusionMockFlags Flags, string? SimulatedTableName, int FieldCount);

internal sealed class ExclusionDummyConnectionFactory : IDatabaseConnectionFactory
{
    private readonly DbConnection? _connectionToReturn;
    public int ConnectionCreatedCount { get; private set; }

    public ExclusionDummyConnectionFactory(DbConnection? connectionToReturn = null)
    {
        _connectionToReturn = connectionToReturn;
    }

    public DbConnection CreateConnection(string? databaseName = null)
    {
        ConnectionCreatedCount++;
        return _connectionToReturn ?? new ExclusionMockConnection([]);
    }
}

/// <summary>
/// Mock connection for <c>AnonymizerExclusionProvider</c>, built on the shared
/// <see cref="FakeDbConnection"/>/<see cref="FakeDbCommand"/>/<see cref="FakeDbDataReader"/>
/// plumbing. Kept as a named subclass (rather than a plain <see cref="FakeDbConnection"/>
/// instance) because other test files construct it directly by name with these exact optional
/// parameters.
/// </summary>
internal sealed class ExclusionMockConnection : FakeDbConnection
{
    public ExclusionMockConnection(
        List<ExclusionRow> rows,
        ExclusionMockFlags? flags = null,
        string? simulatedTableName = "dbo.MyExclusions",
        int fieldCount = 2)
        : base(conn => new FakeDbCommand(conn, new FakeDbCommandHandlers(
            ExecuteReader: cmd => Dispatch(cmd, new ExclusionDispatchConfig(rows, flags ?? new ExclusionMockFlags(), simulatedTableName, fieldCount)))))
    {
    }

    private static FakeDbDataReader Dispatch(FakeDbCommand cmd, ExclusionDispatchConfig config)
    {
        if (config.Flags.ThrowException)
        {
            throw new InvalidOperationException("Connection failed simulated.");
        }
        if (cmd.CommandText.Contains("OBJECT_ID", StringComparison.Ordinal))
        {
            return new FakeDbDataReader(["Value"], config.SimulatedTableName is null ? [] : [[config.SimulatedTableName]]);
        }
        if (cmd.CommandText.Contains("COL_LENGTH", StringComparison.Ordinal))
        {
            return new FakeDbDataReader(["Value"], [[config.Flags.HasSchemaColumn]]);
        }
        // A 3-column row set is only ever returned when the caller actually asked for the
        // SchemaName column (either the custom SQL text names it, or the table path detected
        // it via COL_LENGTH) — mirrors the real provider's positional column-count contract.
        bool includeSchemaColumn = config.FieldCount >= 3 || cmd.CommandText.Contains("SchemaName", StringComparison.Ordinal);
        return BuildExclusionReader(config.Rows, includeSchemaColumn);
    }

    private static FakeDbDataReader BuildExclusionReader(List<ExclusionRow> rows, bool includeSchemaColumn)
    {
        string[] columns = includeSchemaColumn ? ["TableName", "ColumnName", "SchemaName"] : ["TableName", "ColumnName"];
        List<object?[]> dataRows = rows.Select(r => includeSchemaColumn
            ? new object?[] { r.Table, r.Column, r.Schema }
            : new object?[] { r.Table, r.Column })
            .ToList();
        return new FakeDbDataReader(columns, dataRows);
    }
}
