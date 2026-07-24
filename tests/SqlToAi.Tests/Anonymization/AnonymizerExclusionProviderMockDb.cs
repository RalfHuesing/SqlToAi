#nullable enable

using System.Data;
using System.Data.Common;
using SqlToAi.Database;

namespace SqlToAi.Tests.Anonymization;

#pragma warning disable CS8765 // Nullability of parameter doesn't match overridden member

/// <summary>One row of mock exclusion data: table/column, and an optional 3rd schema column.</summary>
internal sealed record ExclusionRow(string Table, string Column, string? Schema = null);

/// <summary>Bundles the mock connection's two independent boolean behaviors into one parameter object.</summary>
internal sealed record ExclusionMockFlags(bool ThrowException = false, bool HasSchemaColumn = false);

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

internal sealed class ExclusionMockConnection : DbConnection
{
    private readonly List<ExclusionRow> _rows;
    private readonly ExclusionMockFlags _flags;
    private readonly string? _simulatedTableName;
    private readonly int _fieldCount;
    private string _connectionString = "";

    public ExclusionMockConnection(
        List<ExclusionRow> rows,
        ExclusionMockFlags? flags = null,
        string? simulatedTableName = "dbo.MyExclusions",
        int fieldCount = 2)
    {
        _rows = rows;
        _flags = flags ?? new ExclusionMockFlags();
        _simulatedTableName = simulatedTableName;
        _fieldCount = fieldCount;
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
        return new ExclusionMockCommand(_rows, _flags, _simulatedTableName, _fieldCount);
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        throw new NotImplementedException();
    }
}

internal sealed class ExclusionMockCommand : DbCommand
{
    private readonly List<ExclusionRow> _rows;
    private readonly ExclusionMockFlags _flags;
    private readonly string? _simulatedTableName;
    private readonly int _fieldCount;
    private DbConnection? _dbConnection;

    public ExclusionMockCommand(List<ExclusionRow> rows, ExclusionMockFlags flags, string? simulatedTableName, int fieldCount)
    {
        _rows = rows;
        _flags = flags;
        _simulatedTableName = simulatedTableName;
        _fieldCount = fieldCount;
    }

    public override string CommandText { get; set; } = "";
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }

    protected override DbConnection? DbConnection
    {
        get => _dbConnection;
        set => _dbConnection = value;
    }

    private readonly ExclusionMockParameterCollection _parameters = new();
    protected override DbParameterCollection DbParameterCollection => _parameters;
    protected override DbTransaction? DbTransaction { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.None;

    public override void Cancel() { }
    public override int ExecuteNonQuery() => 0;
    public override object? ExecuteScalar() => null;

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        if (_flags.ThrowException)
        {
            throw new InvalidOperationException("Connection failed simulated.");
        }
        if (CommandText.Contains("OBJECT_ID", StringComparison.Ordinal))
        {
            return new ExclusionSingleValueDataReader(_simulatedTableName);
        }
        if (CommandText.Contains("COL_LENGTH", StringComparison.Ordinal))
        {
            return new ExclusionBoolValueDataReader(_flags.HasSchemaColumn);
        }
        // A 3-column row set is only ever returned when the caller actually asked for the
        // SchemaName column (either the custom SQL text names it, or the table path detected
        // it via COL_LENGTH) — mirrors the real provider's positional column-count contract.
        bool includeSchemaColumn = _fieldCount >= 3 || CommandText.Contains("SchemaName", StringComparison.Ordinal);
        return new ExclusionMockDataReader(_rows, includeSchemaColumn ? 3 : 2);
    }

    protected override DbParameter CreateDbParameter()
    {
        return new ExclusionMockParameter();
    }

    public override void Prepare() { }
}

internal sealed class ExclusionMockDataReader : DbDataReader
{
    private readonly List<ExclusionRow> _rows;
    private readonly int _fieldCount;
    private int _readIndex = -1;

    public ExclusionMockDataReader(List<ExclusionRow> rows, int fieldCount)
    {
        _rows = rows;
        _fieldCount = fieldCount;
    }

    public override int FieldCount => _fieldCount;
    public override int Depth => 0;
    public override bool IsClosed => false;
    public override int RecordsAffected => -1;
    public override bool HasRows => _rows.Count > 0;

    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read()
    {
        if (_readIndex < _rows.Count - 1)
        {
            _readIndex++;
            return true;
        }
        return false;
    }

    public override Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Read());
    }

    public override bool NextResult() => false;

    public override string GetName(int ordinal) => ordinal switch
    {
        0 => "TableName",
        1 => "ColumnName",
        _ => "SchemaName"
    };

    public override int GetOrdinal(string name)
    {
        if (string.Equals(name, "ColumnName", StringComparison.OrdinalIgnoreCase)) return 1;
        if (string.Equals(name, "SchemaName", StringComparison.OrdinalIgnoreCase)) return 2;
        return 0;
    }

    public override object GetValue(int ordinal)
    {
        if (_readIndex < 0 || _readIndex >= _rows.Count)
        {
            throw new InvalidOperationException("No data available.");
        }
        var row = _rows[_readIndex];
        return ordinal switch
        {
            0 => row.Table,
            1 => row.Column,
            _ => (object?)row.Schema ?? DBNull.Value
        };
    }

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
    public override string GetString(int ordinal) => GetValue(ordinal)?.ToString() ?? "";

    public override int GetValues(object[] values)
    {
        int count = Math.Min(_fieldCount, values.Length);
        for (int i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }
        return count;
    }

    public override bool IsDBNull(int ordinal) => ordinal == 2 && _rows[_readIndex].Schema is null;

    public override System.Collections.IEnumerator GetEnumerator()
    {
        throw new NotImplementedException();
    }
}

internal sealed class ExclusionSingleValueDataReader : DbDataReader
{
    private readonly string? _value;
    private int _readIndex = -1;

    public ExclusionSingleValueDataReader(string? value)
    {
        _value = value;
    }

    public override int FieldCount => 1;
    public override int Depth => 0;
    public override bool IsClosed => false;
    public override int RecordsAffected => -1;
    public override bool HasRows => _value != null;

    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(0);

    public override bool Read()
    {
        if (_value != null && _readIndex < 0)
        {
            _readIndex++;
            return true;
        }
        return false;
    }

    public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());
    public override bool NextResult() => false;
    public override string GetName(int ordinal) => "Value";
    public override int GetOrdinal(string name) => 0;
    public override object GetValue(int ordinal) => (object?)_value ?? DBNull.Value;

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
    public override string GetString(int ordinal) => _value ?? "";
    public override bool IsDBNull(int ordinal) => _value == null;

    public override int GetValues(object[] values)
    {
        values[0] = GetValue(0);
        return 1;
    }

    public override System.Collections.IEnumerator GetEnumerator()
    {
        throw new NotImplementedException();
    }
}

/// <summary>Single-row, single-column reader for the <c>COL_LENGTH</c>-based schema-column existence check.</summary>
internal sealed class ExclusionBoolValueDataReader : DbDataReader
{
    private readonly bool _value;
    private int _readIndex = -1;

    public ExclusionBoolValueDataReader(bool value)
    {
        _value = value;
    }

    public override int FieldCount => 1;
    public override int Depth => 0;
    public override bool IsClosed => false;
    public override int RecordsAffected => -1;
    public override bool HasRows => true;

    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(0);

    public override bool Read()
    {
        if (_readIndex < 0)
        {
            _readIndex++;
            return true;
        }
        return false;
    }

    public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());
    public override bool NextResult() => false;
    public override string GetName(int ordinal) => "Value";
    public override int GetOrdinal(string name) => 0;
    public override object GetValue(int ordinal) => _value;

    public override bool GetBoolean(int ordinal) => _value;
    public override byte GetByte(int ordinal) => 0;
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
    public override char GetChar(int ordinal) => '\0';
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
    public override string GetDataTypeName(int ordinal) => "bit";
    public override DateTime GetDateTime(int ordinal) => DateTime.MinValue;
    public override decimal GetDecimal(int ordinal) => 0;
    public override double GetDouble(int ordinal) => 0;
    public override Type GetFieldType(int ordinal) => typeof(bool);
    public override float GetFloat(int ordinal) => 0;
    public override Guid GetGuid(int ordinal) => Guid.Empty;
    public override short GetInt16(int ordinal) => 0;
    public override int GetInt32(int ordinal) => _value ? 1 : 0;
    public override long GetInt64(int ordinal) => 0;
    public override string GetString(int ordinal) => _value.ToString();
    public override bool IsDBNull(int ordinal) => false;

    public override int GetValues(object[] values)
    {
        values[0] = GetValue(0);
        return 1;
    }

    public override System.Collections.IEnumerator GetEnumerator()
    {
        throw new NotImplementedException();
    }
}

internal sealed class ExclusionMockParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _parameters = new();

    public override int Count => _parameters.Count;
    public override object SyncRoot => throw new NotImplementedException();
    public override int Add(object value)
    {
        _parameters.Add((DbParameter)value);
        return _parameters.Count - 1;
    }
    public override void AddRange(Array values)
    {
        foreach (var val in values) Add(val!);
    }
    public override void Clear() => _parameters.Clear();
    public override bool Contains(object value) => _parameters.Contains((DbParameter)value);
    public override int IndexOf(object value) => _parameters.IndexOf((DbParameter)value);
    public override void Insert(int index, object value) => _parameters.Insert(index, (DbParameter)value);
    public override void Remove(object value) => _parameters.Remove((DbParameter)value);
    public override void RemoveAt(int index) => _parameters.RemoveAt(index);
    public override void RemoveAt(string parameterName) => throw new NotImplementedException();
    protected override DbParameter GetParameter(int index) => _parameters[index];
    protected override DbParameter GetParameter(string parameterName) => throw new NotImplementedException();
    protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;
    protected override void SetParameter(string parameterName, DbParameter value) => throw new NotImplementedException();
    public override void CopyTo(Array array, int index) => throw new NotImplementedException();
    public override System.Collections.IEnumerator GetEnumerator() => _parameters.GetEnumerator();
    public override bool Contains(string value) => throw new NotImplementedException();
    public override int IndexOf(string parameterName) => throw new NotImplementedException();
}

internal sealed class ExclusionMockParameter : DbParameter
{
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }
    public override string ParameterName { get; set; } = string.Empty;
    public override int Size { get; set; }
    public override string SourceColumn { get; set; } = string.Empty;
    public override bool SourceColumnNullMapping { get; set; }
    public override object? Value { get; set; }
    public override void ResetDbType() { }
}
