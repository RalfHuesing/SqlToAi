#nullable enable

using System.Data.Common;
using SqlToAi.Anonymization;
using SqlToAi.Database;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

internal sealed class DummyConnectionFactory : IDatabaseConnectionFactory
{
    public int ConnectionCreatedCount { get; private set; }

    public DbConnection CreateConnection(string? databaseName = null)
    {
        ConnectionCreatedCount++;
        return BuildConnection();
    }

    private static FakeDbConnection BuildConnection() =>
        new(conn => new FakeDbCommand(conn, new FakeDbCommandHandlers(
            ExecuteScalar: ExecuteScalar,
            ExecuteReader: ExecuteReader)));

    private static object? ExecuteScalar(FakeDbCommand cmd)
    {
        if (cmd.CommandText.Contains("sys.databases")) return TestConstants.DatabaseName;
        if (cmd.CommandText.Contains("sys.objects")) return "U";
        return 1;
    }

    private static readonly (string Term, Func<FakeDbCommand, FakeDbDataReader> Factory)[] MockReaderDispatches = [
        ("COUNT(*)", _ => new FakeDbDataReader(["CountValue"], [[1]])),
        ("sys.databases", _ => new FakeDbDataReader(["name"], [[TestConstants.DatabaseName], ["SalesDb"]])),
        ("sys.dm_sql_referencing_entities", _ => new FakeDbDataReader(["SchemaName", "EntityName", "ClassDescription"], [["dbo", "GetCustomers", "OBJECT_OR_COLUMN"]])),
        ("sys.foreign_keys", _ => new FakeDbDataReader(["ForeignKeyName", "ParentTable", "ParentColumn", "ReferencedTable", "ReferencedColumn"], [["FK_Orders_Customers", "dbo.Orders", "CustomerId", "dbo.Customers", "CustomerId"]])),
        // Matched on "is_identity" (the sys.columns field name), not "sys.columns" itself, and
        // placed before the "sys.indexes" dispatch below: the real column-list query also joins
        // sys.index_columns/sys.indexes for its primary-key lookup, which would otherwise collide
        // with the dedicated "sys.indexes" dispatch. "is_identity" appears only in this query.
        ("is_identity", cmd => {
            bool isWideTable = false;
            foreach (DbParameter p in cmd.Parameters)
            {
                if (p.Value?.ToString()?.Contains("WideTable", StringComparison.OrdinalIgnoreCase) == true) isWideTable = true;
            }
            if (isWideTable)
            {
                var wideRows = Enumerable.Range(1, 250)
                    .Select(i => new object?[] { $"Column{i}", "int", 4, 10, 0, false, false, 0 })
                    .ToList();
                return new FakeDbDataReader(["ColumnName", "DataType", "MaxLength", "Precision", "Scale", "IsNullable", "IsIdentity", "IsPrimaryKey"], wideRows);
            }
            return new FakeDbDataReader(["ColumnName", "DataType", "MaxLength", "Precision", "Scale", "IsNullable", "IsIdentity", "IsPrimaryKey"], [["CustomerId", "int", 4, 10, 0, false, true, 1], ["Email", "varchar", 100, 0, 0, true, false, 0]]);
        }),
        ("sys.indexes", _ => new FakeDbDataReader(["IndexName", "IndexType", "IsUnique", "IsPrimaryKey", "ColumnName", "IsIncluded"], [["PK_Customers", "CLUSTERED", true, true, "CustomerId", false]])),
        ("sys.default_constraints", _ => new FakeDbDataReader(["ConstraintName", "ColumnName", "Definition", "ConstraintType"], [["DF_Customers_Created", "CreatedDate", "(getdate())", "DEFAULT"]])),
        ("sys.check_constraints", _ => new FakeDbDataReader(["ConstraintName", "ColumnName", "Definition", "ConstraintType"], [["DF_Customers_Created", "CreatedDate", "(getdate())", "DEFAULT"]])),
        ("sys.triggers", _ => new FakeDbDataReader(["TriggerName", "IsDisabled", "IsUpdate", "IsDelete", "IsInsert"], [["trg_Audit", 0, 0, 0, 1]])),
        ("sys.parameters", _ => new FakeDbDataReader(["ParameterName", "DataType", "MaxLength", "IsOutput"], [["@CustomerId", "int", 4, false]])),
        ("sys.sql_modules", _ => new FakeDbDataReader(["definition"], [["CREATE PROCEDURE GetCustomers AS SELECT * FROM Customers"]])),
        ("sys.objects", cmd => {
            if (cmd.CommandText.Contains("SELECT TOP"))
            {
                return new FakeDbDataReader(
                    ["SchemaName", "ObjectName", "TypeDescription"],
                    [["dbo", "Customers", "USER_TABLE"]]
                );
            }

            bool isProc = false;
            bool isView = false;
            foreach (DbParameter p in cmd.Parameters)
            {
                string? val = p.Value?.ToString();
                if (val?.Contains("Proc", StringComparison.OrdinalIgnoreCase) == true) isProc = true;
                if (val?.Contains("View", StringComparison.OrdinalIgnoreCase) == true) isView = true;
            }
            string typeCode = isProc ? "P" : isView ? "V" : "U";
            return new FakeDbDataReader(["type"], [[typeCode]]);
        })
    ];

    private static FakeDbDataReader ExecuteReader(FakeDbCommand cmd)
    {
        foreach (var (term, factory) in MockReaderDispatches)
        {
            if (cmd.CommandText.Contains(term))
            {
                return factory(cmd);
            }
        }
        return new FakeDbDataReader(["value"], [[1]]);
    }
}

/// <summary>Stub policy resolver for schema tests that don't exercise anonymization annotation.</summary>
internal sealed class AlwaysAllowPolicyResolver : IAnonymizationPolicyResolver
{
    public Task<bool> WillAnonymizeAsync(string databaseName, string tableName, string columnName, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public bool IsTokenizationActive => false;
}

/// <summary>Reports a single named column as anonymized, so tests can verify the schema markdown annotation.</summary>
internal sealed class SelectiveAnonymizePolicyResolver(string anonymizedColumnName) : IAnonymizationPolicyResolver
{
    public Task<bool> WillAnonymizeAsync(string databaseName, string tableName, string columnName, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Equals(columnName, anonymizedColumnName, StringComparison.OrdinalIgnoreCase));

    public bool IsTokenizationActive => false;
}

/// <summary>Reports a single named column as anonymized, with tokenization globally active, so tests can verify the "Yes (searchable)" annotation.</summary>
internal sealed class SearchableAnonymizePolicyResolver(string searchableColumnName) : IAnonymizationPolicyResolver
{
    public Task<bool> WillAnonymizeAsync(string databaseName, string tableName, string columnName, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Equals(columnName, searchableColumnName, StringComparison.OrdinalIgnoreCase));

    public bool IsTokenizationActive => true;
}
