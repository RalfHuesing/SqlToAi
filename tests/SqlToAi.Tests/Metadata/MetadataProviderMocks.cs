#nullable enable

using System.Data;
using System.Data.Common;

namespace SqlToAi.Tests.Metadata;

#pragma warning disable CS8765 // Nullability of parameter doesn't match overridden member

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

internal sealed class MockMetadataConnection : DbConnection
{
    private readonly string _tableDesc;
    private readonly Dictionary<string, string> _columnDescs;
    private string _connectionString = "";

    public MockMetadataConnection(string tableDesc = "", Dictionary<string, string>? columnDescs = null)
    {
        _tableDesc = tableDesc;
        _columnDescs = columnDescs ?? new Dictionary<string, string>();
    }

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
        return new MockMetadataCommand(_tableDesc, _columnDescs);
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        throw new NotImplementedException();
    }
}

internal sealed class MockMetadataCommand : DbCommand
{
    private readonly string _tableDesc;
    private readonly Dictionary<string, string> _columnDescs;
    private DbConnection? _dbConnection;
    private readonly DbParameterCollection _parameters = new MockParameterCollection();

    public MockMetadataCommand(string tableDesc, Dictionary<string, string> columnDescs)
    {
        _tableDesc = tableDesc;
        _columnDescs = columnDescs;
    }

    public override string CommandText { get; set; } = "";
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    protected override DbConnection? DbConnection
    {
        get => _dbConnection;
        set => _dbConnection = value;
    }

    protected override DbParameterCollection DbParameterCollection => _parameters;
    protected override DbTransaction? DbTransaction { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.None;

    public override void Cancel() { }
    public override int ExecuteNonQuery() => 0;
    public override object? ExecuteScalar() => _tableDesc;

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        foreach (DbParameter param in _parameters)
        {
            if (param.ParameterName == "TableName")
            {
                MetadataProviderTests.LastTableNameParameter = param.Value?.ToString();
            }
        }

        // If the query targets columns
        if (CommandText.Contains("sys.columns") || CommandText.Contains("ColumnName"))
        {
            return new MockColumnDataReader(_columnDescs);
        }

        // Otherwise, it is table description
        return new MockTableDataReader(_tableDesc);
    }

    protected override DbParameter CreateDbParameter()
    {
        return new MockParameter();
    }

    public override void Prepare() { }
}

internal sealed class MockTableDataReader : DbDataReader
{
    private readonly string _value;
    private int _readCount;

    public MockTableDataReader(string value)
    {
        _value = value;
    }

    public override int FieldCount => 1;
    public override int Depth => 0;
    public override bool IsClosed => false;
    public override int RecordsAffected => -1;
    public override bool HasRows => _readCount == 0;

    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read()
    {
        if (_readCount == 0)
        {
            _readCount++;
            return true;
        }
        return false;
    }

    public override Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Read());
    }

    public override bool NextResult() => false;

    public override string GetName(int ordinal) => "Description";
    public override int GetOrdinal(string name) => 0;
    public override object GetValue(int ordinal) => _value;

    public override bool GetBoolean(int ordinal) => false;
    public override byte GetByte(int ordinal) => 0;
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
    public override char GetChar(int ordinal) => '\0';
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
    public override string GetDataTypeName(int ordinal) => "varchar";
    public override DateTime GetDateTime(int ordinal) => DateTime.MinValue;
    public override decimal GetDecimal(int ordinal) => 0;
    public override double GetDouble(int ordinal) => 0;
    public override Type GetFieldType(int ordinal) => typeof(string);
    public override float GetFloat(int ordinal) => 0;
    public override Guid GetGuid(int ordinal) => Guid.Empty;
    public override short GetInt16(int ordinal) => 0;
    public override int GetInt32(int ordinal) => 0;
    public override long GetInt64(int ordinal) => 0;
    public override string GetString(int ordinal) => _value;
    public override int GetValues(object[] values)
    {
        values[0] = _value;
        return 1;
    }
    public override bool IsDBNull(int ordinal) => false;

    public override System.Collections.IEnumerator GetEnumerator()
    {
        throw new NotImplementedException();
    }
}

internal sealed class MockColumnDataReader : DbDataReader
{
    private readonly List<KeyValuePair<string, string>> _items;
    private int _index = -1;

    public MockColumnDataReader(Dictionary<string, string> data)
    {
        _items = new List<KeyValuePair<string, string>>(data);
    }

    public override int FieldCount => 2;
    public override int Depth => 0;
    public override bool IsClosed => false;
    public override int RecordsAffected => -1;
    public override bool HasRows => _items.Count > 0;

    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read()
    {
        if (_index < _items.Count - 1)
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

    public override string GetName(int ordinal) => ordinal == 0 ? "ColumnName" : "Description";
    public override int GetOrdinal(string name) => string.Equals(name, "ColumnName", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    public override object GetValue(int ordinal) => ordinal == 0 ? _items[_index].Key : _items[_index].Value;

    public override bool GetBoolean(int ordinal) => false;
    public override byte GetByte(int ordinal) => 0;
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
    public override char GetChar(int ordinal) => '\0';
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
    public override string GetDataTypeName(int ordinal) => "varchar";
    public override DateTime GetDateTime(int ordinal) => DateTime.MinValue;
    public override decimal GetDecimal(int ordinal) => 0;
    public override double GetDouble(int ordinal) => 0;
    public override Type GetFieldType(int ordinal) => typeof(string);
    public override float GetFloat(int ordinal) => 0;
    public override Guid GetGuid(int ordinal) => Guid.Empty;
    public override short GetInt16(int ordinal) => 0;
    public override int GetInt32(int ordinal) => 0;
    public override long GetInt64(int ordinal) => 0;
    public override string GetString(int ordinal) => GetValue(ordinal).ToString()!;
    public override int GetValues(object[] values)
    {
        values[0] = _items[_index].Key;
        values[1] = _items[_index].Value;
        return 2;
    }
    public override bool IsDBNull(int ordinal) => false;

    public override System.Collections.IEnumerator GetEnumerator()
    {
        throw new NotImplementedException();
    }
}

internal sealed class MockParameterCollection : DbParameterCollection
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
