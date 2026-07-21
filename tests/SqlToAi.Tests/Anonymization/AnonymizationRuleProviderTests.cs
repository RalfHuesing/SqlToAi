#nullable enable

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;

namespace SqlToAi.Tests.Anonymization;

#pragma warning disable CS8765 // Nullability of parameter doesn't match overridden member

// @covers SqlToAi.Anonymization.AnonymizationRuleProvider
// @covers SqlToAi.Anonymization.RuleCacheEntry
// @covers SqlToAi.Anonymization.AnonymizationRule
public sealed class AnonymizationRuleProviderTests
{
    private static readonly Type TargetType = typeof(AnonymizationRuleProvider);

    private static SqlToAiOptions BuildOptions(bool enabled = true, int cacheTtlSeconds = 300)
    {
        var options = new SqlToAiOptions();
        options.AnonymizationRules.Enabled = enabled;
        options.AnonymizationRules.TableName = "dbo.AnonymizationRules";
        options.AnonymizationRules.CacheTtlSeconds = cacheTtlSeconds;
        return options;
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldReturnFalse_WhenDisabled()
    {
        var options = BuildOptions(enabled: false);
        var factory = new DummyConnectionFactory();
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excluded = await provider.IsExcludedAsync("AnyDb", "AnyTable", "AnyColumn", TestContext.Current.CancellationToken);

        Assert.False(excluded);
        Assert.Equal(0, factory.ConnectionCreatedCount);
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldReturnFalse_WhenNoRuleMatches()
    {
        var options = BuildOptions();
        var rows = new List<RuleRowData> { new("%", "FakeConsultants", "%", false) };
        var factory = new DummyConnectionFactory(new MockConnection(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excluded = await provider.IsExcludedAsync("AnyDb", "OtherTable", "OtherColumn", TestContext.Current.CancellationToken);

        Assert.False(excluded);
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldExclude_WhenWildcardRuleAllowsWholeTable()
    {
        var options = BuildOptions();
        var rows = new List<RuleRowData> { new("%", "FakeConsultants", "%", false) };
        var factory = new DummyConnectionFactory(new MockConnection(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excluded = await provider.IsExcludedAsync("AnyDb", "FakeConsultants", "Phone", TestContext.Current.CancellationToken);

        Assert.True(excluded);
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldPreferMoreSpecificRule_ThatReAnonymizesOneColumn()
    {
        // Regression scenario from the design discussion: a broad "allow whole table" rule
        // combined with a specific "except this one column" override.
        var options = BuildOptions();
        var rows = new List<RuleRowData>
        {
            new("%", "FakeConsultants", "%", false),
            new("%", "FakeConsultants", "FullName", true)
        };
        var factory = new DummyConnectionFactory(new MockConnection(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool nameExcluded = await provider.IsExcludedAsync("AnyDb", "FakeConsultants", "FullName", TestContext.Current.CancellationToken);
        bool phoneExcluded = await provider.IsExcludedAsync("AnyDb", "FakeConsultants", "Phone", TestContext.Current.CancellationToken);

        Assert.False(nameExcluded); // more specific rule wins -> stays anonymized
        Assert.True(phoneExcluded); // falls back to the wildcard rule -> allowed raw
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldSupportAllowListOnlyDatabase()
    {
        // A database with no broad wildcard rule stays fully anonymized except for explicit allows.
        var options = BuildOptions();
        var rows = new List<RuleRowData> { new("FakeHighSecurityDb", "%", "ContactEmail", false) };
        var factory = new DummyConnectionFactory(new MockConnection(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool emailExcluded = await provider.IsExcludedAsync("FakeHighSecurityDb", "Contacts", "ContactEmail", TestContext.Current.CancellationToken);
        bool otherExcluded = await provider.IsExcludedAsync("FakeHighSecurityDb", "Contacts", "Notes", TestContext.Current.CancellationToken);

        Assert.True(emailExcluded);
        Assert.False(otherExcluded);
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldCacheRules_AndReloadAfterTtlExpires()
    {
        var options = BuildOptions(cacheTtlSeconds: 1);
        var rows = new List<RuleRowData> { new("%", "FakeConsultants", "%", false) };
        var mockConn = new MockConnection(rows);
        var factory = new DummyConnectionFactory(mockConn);
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        await provider.IsExcludedAsync("AnyDb", "FakeConsultants", "Phone", TestContext.Current.CancellationToken);
        Assert.Equal(1, factory.ConnectionCreatedCount);

        await provider.IsExcludedAsync("AnyDb", "FakeConsultants", "Phone", TestContext.Current.CancellationToken);
        Assert.Equal(1, factory.ConnectionCreatedCount); // still cached

        await Task.Delay(1100, TestContext.Current.CancellationToken);

        await provider.IsExcludedAsync("AnyDb", "FakeConsultants", "Phone", TestContext.Current.CancellationToken);
        Assert.Equal(2, factory.ConnectionCreatedCount); // reloaded after TTL
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldReturnFalse_WhenTableDoesNotExist()
    {
        var options = BuildOptions();
        var factory = new DummyConnectionFactory(new MockConnection([], simulatedTableName: null));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excluded = await provider.IsExcludedAsync("AnyDb", "FakeConsultants", "Phone", TestContext.Current.CancellationToken);

        Assert.False(excluded);
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldReturnFalse_WhenQueryThrows()
    {
        var options = BuildOptions();
        var factory = new DummyConnectionFactory(new MockConnection([], throwException: true));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excluded = await provider.IsExcludedAsync("AnyDb", "FakeConsultants", "Phone", TestContext.Current.CancellationToken);

        Assert.False(excluded);
    }

    // -------------------------------------------------------------------------
    // Test doubles
    // -------------------------------------------------------------------------

    private sealed record RuleRowData(string DatabasePattern, string TablePattern, string ColumnPattern, bool Anonymize);

    private sealed class DummyConnectionFactory : IDatabaseConnectionFactory
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
            return _connectionToReturn ?? new MockConnection([]);
        }
    }

    private sealed class MockConnection : DbConnection
    {
        private readonly List<RuleRowData> _rows;
        private readonly bool _throwException;
        private readonly string? _simulatedTableName;
        private string _connectionString = "";

        public MockConnection(List<RuleRowData> rows, bool throwException = false, string? simulatedTableName = "dbo.AnonymizationRules")
        {
            _rows = rows;
            _throwException = throwException;
            _simulatedTableName = simulatedTableName;
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

        protected override DbCommand CreateDbCommand() => new MockCommand(_rows, _throwException, _simulatedTableName);

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotImplementedException();
    }

    private sealed class MockCommand : DbCommand
    {
        private readonly List<RuleRowData> _rows;
        private readonly bool _throwException;
        private readonly string? _simulatedTableName;
        private DbConnection? _dbConnection;

        public MockCommand(List<RuleRowData> rows, bool throwException, string? simulatedTableName)
        {
            _rows = rows;
            _throwException = throwException;
            _simulatedTableName = simulatedTableName;
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

        private readonly MockParameterCollection _parameters = new();
        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.None;

        public override void Cancel() { }
        public override int ExecuteNonQuery() => 0;
        public override object? ExecuteScalar() => null;

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            if (_throwException)
            {
                throw new InvalidOperationException("Connection failed simulated.");
            }
            if (CommandText.Contains("OBJECT_ID", StringComparison.Ordinal))
            {
                return new SingleValueDataReader(_simulatedTableName);
            }
            return new RuleDataReader(_rows);
        }

        protected override DbParameter CreateDbParameter() => new MockParameter();

        public override void Prepare() { }
    }

    private sealed class RuleDataReader : DbDataReader
    {
        private readonly List<RuleRowData> _rows;
        private int _readIndex = -1;

        public RuleDataReader(List<RuleRowData> rows)
        {
            _rows = rows;
        }

        public override int FieldCount => 4;
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

        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());
        public override bool NextResult() => false;

        public override string GetName(int ordinal) => ordinal switch
        {
            0 => "DatabasePattern",
            1 => "TablePattern",
            2 => "ColumnPattern",
            _ => "Anonymize"
        };

        public override int GetOrdinal(string name) => name switch
        {
            "DatabasePattern" => 0,
            "TablePattern" => 1,
            "ColumnPattern" => 2,
            _ => 3
        };

        public override object GetValue(int ordinal)
        {
            var row = _rows[_readIndex];
            return ordinal switch
            {
                0 => row.DatabasePattern,
                1 => row.TablePattern,
                2 => row.ColumnPattern,
                _ => row.Anonymize
            };
        }

        public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
        public override byte GetByte(int ordinal) => 0;
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
        public override char GetChar(int ordinal) => '\0';
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
        public override string GetDataTypeName(int ordinal) => ordinal == 3 ? "bit" : "nvarchar";
        public override DateTime GetDateTime(int ordinal) => DateTime.MinValue;
        public override decimal GetDecimal(int ordinal) => 0;
        public override double GetDouble(int ordinal) => 0;
        public override Type GetFieldType(int ordinal) => ordinal == 3 ? typeof(bool) : typeof(string);
        public override float GetFloat(int ordinal) => 0;
        public override Guid GetGuid(int ordinal) => Guid.Empty;
        public override short GetInt16(int ordinal) => 0;
        public override int GetInt32(int ordinal) => 0;
        public override long GetInt64(int ordinal) => 0;
        public override string GetString(int ordinal) => GetValue(ordinal).ToString() ?? "";

        public override int GetValues(object[] values)
        {
            for (int i = 0; i < FieldCount; i++)
            {
                values[i] = GetValue(i);
            }
            return FieldCount;
        }

        public override bool IsDBNull(int ordinal) => false;

        public override System.Collections.IEnumerator GetEnumerator() => throw new NotImplementedException();
    }

    private sealed class SingleValueDataReader : DbDataReader
    {
        private readonly string? _value;
        private int _readIndex = -1;

        public SingleValueDataReader(string? value)
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

        public override System.Collections.IEnumerator GetEnumerator() => throw new NotImplementedException();
    }

    private sealed class MockParameterCollection : DbParameterCollection
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

    private sealed class MockParameter : DbParameter
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
}
