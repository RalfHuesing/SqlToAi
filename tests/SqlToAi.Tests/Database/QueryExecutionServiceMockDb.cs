#nullable enable

using System.Data;
using System.Data.Common;
using SqlToAi.Anonymization;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Tests.Database;

#pragma warning disable CS8765 // Nullability of parameter doesn't match overridden member

internal sealed class AlwaysExcludeRuleProvider : IAnonymizationRuleProvider
{
    public Task<bool> IsExcludedAsync(string databaseName, string tableName, string columnName, CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}

internal sealed class FakeSecurityGuard(bool allowed) : ISecurityGuard
{
    public bool IsDatabaseAllowed(string databaseName) => allowed;
}

internal sealed class FakeAccessLevelProvider(AccessLevel level) : IAccessLevelProvider
{
    public Task<AccessLevel> GetAccessLevelAsync(string databaseName, CancellationToken cancellationToken = default)
        => Task.FromResult(level);
}

internal sealed class FakeReadOnlyGuard(bool safe) : IReadOnlyGuard
{
    public bool IsQuerySafe(string query) => safe;
}

// -------------------------------------------------------------------------
// Connection factory / reader mock
// -------------------------------------------------------------------------

internal sealed class MockQueryConnectionFactory : IDatabaseConnectionFactory
{
    private readonly string? _stringValue;
    private readonly int _rowCount;
    private readonly string? _baseTableName;
    private readonly string _columnName;

    public MockQueryConnectionFactory(string? stringValue = null, int rowCount = 1, string? baseTableName = null, string columnName = "Name")
    {
        _stringValue = stringValue;
        _rowCount = rowCount;
        _baseTableName = baseTableName;
        _columnName = columnName;
    }

    /// <summary>The most recently created connection — lets tests inspect its transaction.</summary>
    public MockQueryConnection? LastConnection { get; private set; }

    public DbConnection CreateConnection(string? databaseName)
    {
        LastConnection = new MockQueryConnection(_stringValue, _rowCount, _baseTableName, _columnName);
        return LastConnection;
    }

    public DbConnection CreateConnection() => CreateConnection((string?)null);
}

internal sealed class MockQueryConnection(string? stringValue, int rowCount, string? baseTableName = null, string columnName = "Name") : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;

    public override string ConnectionString { get; set; } = string.Empty;
    public override string Database => TestConstants.DatabaseName;
    public override string DataSource => "mock";
    public override string ServerVersion => "16.0";
    public override ConnectionState State => _state;

    /// <summary>The most recently started transaction — lets tests inspect commit/rollback calls.</summary>
    public MockQueryTransaction? LastTransaction { get; private set; }

    /// <summary>The most recently created command — lets tests inspect the resolved <c>CommandText</c> actually sent to "SQL".</summary>
    public MockQueryCommand? LastCommand { get; private set; }

    public override void Open() => _state = ConnectionState.Open;
    public override Task OpenAsync(CancellationToken cancellationToken) { _state = ConnectionState.Open; return Task.CompletedTask; }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        LastTransaction = new MockQueryTransaction(this);
        return LastTransaction;
    }

    protected override DbCommand CreateDbCommand()
    {
        LastCommand = new MockQueryCommand(this, stringValue, rowCount, baseTableName, columnName);
        return LastCommand;
    }

    public override void ChangeDatabase(string databaseName) { }
    public override void Close() => _state = ConnectionState.Closed;
}

internal sealed class MockQueryTransaction(DbConnection connection) : DbTransaction
{
    public int CommitCount { get; private set; }
    public int RollbackCount { get; private set; }

    protected override DbConnection DbConnection => connection;
    public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
    public override void Commit() => CommitCount++;
    public override void Rollback() => RollbackCount++;
}

internal sealed class MockQueryCommand(DbConnection connection, string? stringValue, int rowCount, string? baseTableName = null, string columnName = "Name") : DbCommand
{
    private readonly MockQueryParameterCollectionAdapter _parameters = new();

    public override string CommandText { get; set; } = string.Empty;
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; } = connection;
    protected override DbParameterCollection DbParameterCollection => _parameters;
    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel() { }
    public override int ExecuteNonQuery() => 0;
    public override object? ExecuteScalar() => 1;

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        => new MockQueryReader(stringValue, rowCount, baseTableName, columnName);

    protected override DbParameter CreateDbParameter() => new MockQueryParameter();
    public override void Prepare() { }
}

internal sealed class MockQueryReader(string? stringValue, int totalRows, string? baseTableName = null, string columnName = "Name") : DbDataReader
{
    private int _rowIndex = -1;

    public override int FieldCount => 1;
    public override bool HasRows => totalRows > 0;
    public override bool IsClosed => false;
    public override int RecordsAffected => 0;
    public override int Depth => 0;

    /// <summary>
    /// Reports a BaseTableName for column 0 when configured, so tests can exercise the
    /// TableName.ColumnName qualification path in <c>QueryExecutionService.AnonymizeCell</c>.
    /// </summary>
    public override DataTable? GetSchemaTable()
    {
        if (baseTableName is null)
        {
            return null;
        }

        var table = new DataTable();
        table.Columns.Add("ColumnOrdinal", typeof(int));
        table.Columns.Add("BaseTableName", typeof(string));
        DataRow row = table.NewRow();
        row["ColumnOrdinal"] = 0;
        row["BaseTableName"] = baseTableName;
        table.Rows.Add(row);
        return table;
    }

    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read() { _rowIndex++; return _rowIndex < totalRows; }
    public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());
    public override bool NextResult() => false;

    public override string GetName(int ordinal) => columnName;
    public override int GetOrdinal(string name) => 0;
    public override object GetValue(int ordinal) => (object?)stringValue ?? "Val";
    public override bool IsDBNull(int ordinal) => stringValue is null && ordinal == 0;
    public override string GetDataTypeName(int ordinal) => "varchar";
    public override Type GetFieldType(int ordinal) => typeof(string);
    public override int GetValues(object[] values) { if (values.Length > 0) values[0] = GetValue(0); return 1; }

    public override bool GetBoolean(int ordinal) => false;
    public override byte GetByte(int ordinal) => 0;
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
    public override char GetChar(int ordinal) => '\0';
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
    public override DateTime GetDateTime(int ordinal) => DateTime.MinValue;
    public override decimal GetDecimal(int ordinal) => 0m;
    public override double GetDouble(int ordinal) => 0;
    public override float GetFloat(int ordinal) => 0f;
    public override Guid GetGuid(int ordinal) => Guid.Empty;
    public override short GetInt16(int ordinal) => 0;
    public override int GetInt32(int ordinal) => 0;
    public override long GetInt64(int ordinal) => 0;
    public override string GetString(int ordinal) => GetValue(ordinal)?.ToString() ?? string.Empty;

    public override System.Collections.IEnumerator GetEnumerator() => throw new NotSupportedException();
}

internal sealed class MockQueryParameterCollectionAdapter : DbParameterCollection
{
    private readonly List<DbParameter> _params = [];
    public override int Count => _params.Count;
    public override object SyncRoot => this;
    public override bool IsReadOnly => false;
    public override bool IsFixedSize => false;
    public override int Add(object value) { _params.Add((DbParameter)value); return _params.Count - 1; }
    public override void AddRange(Array values) { foreach (var v in values) Add(v!); }
    public override void Clear() => _params.Clear();
    public override bool Contains(object value) => _params.Contains((DbParameter)value);
    public override int IndexOf(object value) => _params.IndexOf((DbParameter)value);
    public override void Insert(int index, object value) => _params.Insert(index, (DbParameter)value);
    public override void Remove(object value) => _params.Remove((DbParameter)value);
    public override void RemoveAt(int index) => _params.RemoveAt(index);
    protected override DbParameter GetParameter(int index) => _params[index];
    protected override DbParameter GetParameter(string parameterName) => _params.First(p => p.ParameterName == parameterName);
    protected override void SetParameter(int index, DbParameter value) => _params[index] = value;
    protected override void SetParameter(string parameterName, DbParameter value) { }
    public override bool Contains(string value) => _params.Any(p => p.ParameterName == value);
    public override int IndexOf(string parameterName) => _params.FindIndex(p => p.ParameterName == parameterName);
    public override void RemoveAt(string parameterName) => _params.RemoveAll(p => p.ParameterName == parameterName);
    public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_params).CopyTo(array, index);
    public override System.Collections.IEnumerator GetEnumerator() => _params.GetEnumerator();
}

internal sealed class MockQueryParameter : DbParameter
{
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ParameterName { get; set; } = string.Empty;
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override object Value { get; set; } = DBNull.Value;
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }
    public override int Size { get; set; }
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string SourceColumn { get; set; } = string.Empty;
    public override bool SourceColumnNullMapping { get; set; }
    public override void ResetDbType() { }
}
