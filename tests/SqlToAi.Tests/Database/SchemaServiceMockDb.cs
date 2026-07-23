#nullable enable

using System.Data;
using System.Data.Common;
using SqlToAi.Anonymization;
using SqlToAi.Database;

namespace SqlToAi.Tests.Database;

internal sealed class DummyConnectionFactory : IDatabaseConnectionFactory
{
    public int ConnectionCreatedCount { get; private set; }

    public DbConnection CreateConnection(string? databaseName = null)
    {
        ConnectionCreatedCount++;
        return new MockSchemaConnection();
    }
}

/// <summary>Stub policy resolver for schema tests that don't exercise anonymization annotation.</summary>
internal sealed class AlwaysAllowPolicyResolver : IAnonymizationPolicyResolver
{
    public Task<bool> WillAnonymizeAsync(string databaseName, string tableName, string columnName, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<bool> IsSearchableTokenAsync(string databaseName, string tableName, string columnName, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}

/// <summary>Reports a single named column as anonymized, so tests can verify the schema markdown annotation.</summary>
internal sealed class SelectiveAnonymizePolicyResolver(string anonymizedColumnName) : IAnonymizationPolicyResolver
{
    public Task<bool> WillAnonymizeAsync(string databaseName, string tableName, string columnName, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Equals(columnName, anonymizedColumnName, StringComparison.OrdinalIgnoreCase));

    public Task<bool> IsSearchableTokenAsync(string databaseName, string tableName, string columnName, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}

/// <summary>Reports a single named column as anonymized AND searchable, so tests can verify the "Yes (searchable)" annotation.</summary>
internal sealed class SearchableAnonymizePolicyResolver(string searchableColumnName) : IAnonymizationPolicyResolver
{
    public Task<bool> WillAnonymizeAsync(string databaseName, string tableName, string columnName, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Equals(columnName, searchableColumnName, StringComparison.OrdinalIgnoreCase));

    public Task<bool> IsSearchableTokenAsync(string databaseName, string tableName, string columnName, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Equals(columnName, searchableColumnName, StringComparison.OrdinalIgnoreCase));
}

internal sealed class MockSchemaConnection : DbConnection
{
    private string _connectionString = "";

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ConnectionString
    {
        get => _connectionString;
        set => _connectionString = value ?? string.Empty;
    }

    public override string Database => "MockDb";
    public override string DataSource => "MockServer";
    public override string ServerVersion => "1.0";
    public override ConnectionState State => ConnectionState.Open;

    public override void ChangeDatabase(string databaseName) { }
    public override void Close() { }
    public override void Open() { }
    public override Task OpenAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected override DbCommand CreateDbCommand()
    {
        return new MockSchemaCommand(this);
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        throw new NotImplementedException();
    }
}

internal sealed class MockSchemaCommand : DbCommand
{
    internal readonly DbParameterCollection _parameters = new MockParameterCollectionAdapter();
    private DbConnection? _connection;

    public MockSchemaCommand() { }

    public MockSchemaCommand(DbConnection connection)
    {
        _connection = connection;
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string CommandText { get; set; } = "";
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    protected override DbConnection? DbConnection
    {
        get => _connection;
        set => _connection = value;
    }

    protected override DbParameterCollection DbParameterCollection => _parameters;
    protected override DbTransaction? DbTransaction { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.None;

    public override void Cancel() { }
    public override int ExecuteNonQuery() => 0;
    
    public override object? ExecuteScalar()
    {
        if (CommandText.Contains("sys.databases")) return TestConstants.DatabaseName;
        if (CommandText.Contains("sys.objects")) return "U";
        return 1;
    }

    private static readonly (string Term, Func<MockSchemaCommand, DbDataReader> Factory)[] MockReaderDispatches = [
        ("COUNT(*)", _ => new MockDataTableReader(["CountValue"], [[1]])),
        ("sys.databases", _ => new MockDataTableReader(["name"], [[TestConstants.DatabaseName], ["SalesDb"]])),
        ("sys.dm_sql_referencing_entities", _ => new MockDataTableReader(["SchemaName", "EntityName", "ClassDescription"], [["dbo", "GetCustomers", "OBJECT_OR_COLUMN"]])),
        ("sys.foreign_keys", _ => new MockDataTableReader(["ForeignKeyName", "ParentTable", "ParentColumn", "ReferencedTable", "ReferencedColumn"], [["FK_Orders_Customers", "dbo.Orders", "CustomerId", "dbo.Customers", "CustomerId"]])),
        // Matched on "is_identity" (the sys.columns field name), not "sys.columns" itself, and
        // placed before the "sys.indexes" dispatch below: the real column-list query also joins
        // sys.index_columns/sys.indexes for its primary-key lookup, which would otherwise collide
        // with the dedicated "sys.indexes" dispatch. "is_identity" appears only in this query.
        ("is_identity", cmd => {
            bool isWideTable = false;
            foreach (DbParameter p in cmd._parameters)
            {
                if (p.Value?.ToString()?.Contains("WideTable", StringComparison.OrdinalIgnoreCase) == true) isWideTable = true;
            }
            if (isWideTable)
            {
                var wideRows = Enumerable.Range(1, 250)
                    .Select(i => new object[] { $"Column{i}", "int", 4, 10, 0, false, false, 0 })
                    .ToList();
                return new MockDataTableReader(["ColumnName", "DataType", "MaxLength", "Precision", "Scale", "IsNullable", "IsIdentity", "IsPrimaryKey"], wideRows);
            }
            return new MockDataTableReader(["ColumnName", "DataType", "MaxLength", "Precision", "Scale", "IsNullable", "IsIdentity", "IsPrimaryKey"], [["CustomerId", "int", 4, 10, 0, false, true, 1], ["Email", "varchar", 100, 0, 0, true, false, 0]]);
        }),
        ("sys.indexes", _ => new MockDataTableReader(["IndexName", "IndexType", "IsUnique", "IsPrimaryKey", "ColumnName", "IsIncluded"], [["PK_Customers", "CLUSTERED", true, true, "CustomerId", false]])),
        ("sys.default_constraints", _ => new MockDataTableReader(["ConstraintName", "ColumnName", "Definition", "ConstraintType"], [["DF_Customers_Created", "CreatedDate", "(getdate())", "DEFAULT"]])),
        ("sys.check_constraints", _ => new MockDataTableReader(["ConstraintName", "ColumnName", "Definition", "ConstraintType"], [["DF_Customers_Created", "CreatedDate", "(getdate())", "DEFAULT"]])),
        ("sys.triggers", _ => new MockDataTableReader(["TriggerName", "IsDisabled", "IsUpdate", "IsDelete", "IsInsert"], [["trg_Audit", 0, 0, 0, 1]])),
        ("sys.parameters", _ => new MockDataTableReader(["ParameterName", "DataType", "MaxLength", "IsOutput"], [["@CustomerId", "int", 4, false]])),
        ("sys.sql_modules", _ => new MockDataTableReader(["definition"], [["CREATE PROCEDURE GetCustomers AS SELECT * FROM Customers"]])),
        ("sys.objects", cmd => {
            if (cmd.CommandText.Contains("SELECT TOP"))
            {
                return new MockDataTableReader(
                    ["SchemaName", "ObjectName", "TypeDescription"],
                    [["dbo", "Customers", "USER_TABLE"]]
                );
            }
            
            bool isProc = false;
            bool isView = false;
            foreach (DbParameter p in cmd._parameters)
            {
                string? val = p.Value?.ToString();
                if (val?.Contains("Proc", StringComparison.OrdinalIgnoreCase) == true) isProc = true;
                if (val?.Contains("View", StringComparison.OrdinalIgnoreCase) == true) isView = true;
            }
            string typeCode = isProc ? "P" : isView ? "V" : "U";
            return new MockDataTableReader(["type"], [[typeCode]]);
        })
    ];

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        foreach (var (term, factory) in MockReaderDispatches)
        {
            if (CommandText.Contains(term))
            {
                return factory(this);
            }
        }
        return new MockDataTableReader(["value"], [[1]]);
    }

    protected override DbParameter CreateDbParameter() => new MockParameter();
    public override void Prepare() { }
}

internal sealed class MockParameterCollectionAdapter : DbParameterCollection
{
    private readonly List<DbParameter> _parameters = new();

    public override int Count => _parameters.Count;
    public override object SyncRoot => this;
    public override bool IsReadOnly => false;
    public override bool IsFixedSize => false;

    public override int Add(object value)
    {
        _parameters.Add((DbParameter)value);
        return _parameters.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (var val in values)
        {
            Add(val!);
        }
    }

    public override void Clear() => _parameters.Clear();

    public override bool Contains(object value) => _parameters.Contains((DbParameter)value);

    public override int IndexOf(object value) => _parameters.IndexOf((DbParameter)value);

    public override void Insert(int index, object value) => _parameters.Insert(index, (DbParameter)value);

    public override void Remove(object value) => _parameters.Remove((DbParameter)value);

    public override void RemoveAt(int index) => _parameters.RemoveAt(index);

    protected override DbParameter GetParameter(int index) => _parameters[index];

    protected override DbParameter GetParameter(string parameterName) =>
        _parameters.FirstOrDefault(p => p.ParameterName == parameterName) 
        ?? throw new KeyNotFoundException(parameterName);

    protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;

    protected override void SetParameter(string parameterName, DbParameter value)
    {
        int idx = _parameters.FindIndex(p => p.ParameterName == parameterName);
        if (idx >= 0) _parameters[idx] = value;
        else _parameters.Add(value);
    }

    public override bool Contains(string value) => _parameters.Any(p => p.ParameterName == value);

    public override int IndexOf(string parameterName) => _parameters.FindIndex(p => p.ParameterName == parameterName);

    public override void RemoveAt(string parameterName)
    {
        int idx = _parameters.FindIndex(p => p.ParameterName == parameterName);
        if (idx >= 0) _parameters.RemoveAt(idx);
    }

    public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_parameters).CopyTo(array, index);

    public override System.Collections.IEnumerator GetEnumerator() => _parameters.GetEnumerator();
}

internal sealed class MockParameter : DbParameter
{
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ParameterName { get; set; } = "";
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override object Value { get; set; } = DBNull.Value;
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }
    public override int Size { get; set; }
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string SourceColumn { get; set; } = "";
    public override bool SourceColumnNullMapping { get; set; }
    public override void ResetDbType() { }
}

internal sealed class MockDataTableReader : DbDataReader
{
    private readonly string[] _columns;
    private readonly List<object[]> _rows;
    private int _index = -1;

    public MockDataTableReader(string[] columns, List<object[]> rows)
    {
        _columns = columns;
        _rows = rows;
    }

    public override int FieldCount => _columns.Length;
    public override int Depth => 0;
    public override bool IsClosed => false;
    public override int RecordsAffected => -1;
    public override bool HasRows => _rows.Count > 0;

    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read()
    {
        if (_index < _rows.Count - 1)
        {
            _index++;
            return true;
        }
        return false;
    }

    public override Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Read());
    }

    public override bool NextResult() => false;

    public override string GetName(int ordinal) => _columns[ordinal];
    public override int GetOrdinal(string name)
    {
        int idx = Array.FindIndex(_columns, c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) return idx;
#pragma warning disable CA2201 // Do not throw reserved exception types
        throw new IndexOutOfRangeException(name);
#pragma warning restore CA2201
    }

    public override object GetValue(int ordinal)
    {
        if (_index < 0 || _index >= _rows.Count)
        {
            return DBNull.Value;
        }
        return _rows[_index][ordinal];
    }

    public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
    public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
    public override char GetChar(int ordinal) => (char)GetValue(ordinal);
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
    public override string GetDataTypeName(int ordinal) => "varchar";
    public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
    public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
    public override Type GetFieldType(int ordinal)
    {
        if (_rows.Count > 0 && ordinal < _columns.Length)
        {
            var val = _rows[0][ordinal];
            return val?.GetType() ?? typeof(string);
        }
        return typeof(string);
    }
    public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
    public override short GetInt16(int ordinal) => Convert.ToInt16(GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
    public override int GetInt32(int ordinal) => Convert.ToInt32(GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
    public override long GetInt64(int ordinal) => Convert.ToInt64(GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
    public override string GetString(int ordinal) => GetValue(ordinal)?.ToString() ?? "";
    public override int GetValues(object[] values)
    {
        int count = Math.Min(FieldCount, values.Length);
        for (int i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }
        return count;
    }
    public override bool IsDBNull(int ordinal) => GetValue(ordinal) == null || GetValue(ordinal) == DBNull.Value;

    public override System.Collections.IEnumerator GetEnumerator() => _rows.GetEnumerator();
}
