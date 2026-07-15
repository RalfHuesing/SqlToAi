#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using Xunit;

namespace SqlToAi.Tests.Anonymization;

#pragma warning disable CS8765 // Nullability of parameter doesn't match overridden member

// @covers SqlToAi.Anonymization.AnonymizerExclusionProvider
// @covers SqlToAi.Anonymization.ExclusionCheckResult
public sealed class AnonymizerExclusionProviderTests
{
    private static readonly Type TargetType = typeof(AnonymizerExclusionProvider);

    [Fact]
    public async Task GetExclusionsAsync_ShouldReturnEmpty_WhenExclusionSqlIsEmpty()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.AnonymizerExclusionSql = "";

        var mockFactory = new DummyConnectionFactory();
        var provider = new AnonymizerExclusionProvider(mockFactory, Options.Create(options), NullLogger<AnonymizerExclusionProvider>.Instance);

        // Act
        var exclusions = await provider.GetExclusionsAsync("TestDb", TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(exclusions);
        Assert.Equal(0, mockFactory.ConnectionCreatedCount);
    }

    [Fact]
    public async Task GetExclusionsAsync_ShouldLoadExclusions_AndCacheThemWithTtl()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.AnonymizerExclusionSql = "SELECT TableName, ColumnName FROM Exclusions";
        options.Databases.CacheTtlSeconds = 1; // 1 second TTL

        var initialRows = new List<(string, string)>
        {
            ("Kunden", "Name"),
            ("FakeProjects", "ProjectName")
        };

        var mockConn = new MockConnection(initialRows);
        var mockFactory = new DummyConnectionFactory(mockConn);
        var provider = new AnonymizerExclusionProvider(mockFactory, Options.Create(options), NullLogger<AnonymizerExclusionProvider>.Instance);

        // Act & Assert
        // First Call: Queries DB
        var exclusions1 = await provider.GetExclusionsAsync("TestDb", TestContext.Current.CancellationToken);
        Assert.Equal(2, exclusions1.Count);
        Assert.Contains("kunden.name", exclusions1);
        Assert.Contains("fakeprojects.projectname", exclusions1);
        Assert.Equal(1, mockFactory.ConnectionCreatedCount);

        // Second Call (Immediate): Cached
        var exclusions2 = await provider.GetExclusionsAsync("TestDb", TestContext.Current.CancellationToken);
        Assert.Equal(2, exclusions2.Count);
        Assert.Equal(1, mockFactory.ConnectionCreatedCount); // No new connection

        // Wait for TTL expiration
        await Task.Delay(1100, TestContext.Current.CancellationToken);

        // Third Call (After TTL): DB queried again
        var exclusions3 = await provider.GetExclusionsAsync("TestDb", TestContext.Current.CancellationToken);
        Assert.Equal(2, exclusions3.Count);
        Assert.Equal(2, mockFactory.ConnectionCreatedCount);
    }

    [Fact]
    public async Task GetExclusionsAsync_ShouldReturnEmpty_WhenSqlThrowsException()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.AnonymizerExclusionSql = "SELECT TableName, ColumnName FROM Exclusions";

        var mockConn = new MockConnection(new List<(string, string)>(), throwException: true);
        var mockFactory = new DummyConnectionFactory(mockConn);
        var provider = new AnonymizerExclusionProvider(mockFactory, Options.Create(options), NullLogger<AnonymizerExclusionProvider>.Instance);

        // Act
        var exclusions = await provider.GetExclusionsAsync("TestDb", TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(exclusions);
    }

    [Fact]
    public async Task GetExclusionsAsync_ShouldLoadFromExclusionTable_WhenTableExists()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Anonymizer.ExclusionTableName = "dbo.MyExclusions";

        var tableRows = new List<(string, string)>
        {
            ("Kunden", "Vorname"),
            ("Bestellungen", "BestellNr")
        };

        var mockConn = new MockConnection(tableRows, simulatedTableName: "dbo.MyExclusions");
        var mockFactory = new DummyConnectionFactory(mockConn);
        var provider = new AnonymizerExclusionProvider(mockFactory, Options.Create(options), NullLogger<AnonymizerExclusionProvider>.Instance);

        // Act
        var exclusions = await provider.GetExclusionsAsync("TestDb", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, exclusions.Count);
        Assert.Contains("kunden.vorname", exclusions);
        Assert.Contains("bestellungen.bestellnr", exclusions);
    }

    [Fact]
    public async Task GetExclusionsAsync_ShouldNotLoadFromExclusionTable_WhenTableDoesNotExist()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Anonymizer.ExclusionTableName = "dbo.MyExclusions";

        // Simulated table name = null means table does not exist
        var mockConn = new MockConnection([], simulatedTableName: null);
        var mockFactory = new DummyConnectionFactory(mockConn);
        var provider = new AnonymizerExclusionProvider(mockFactory, Options.Create(options), NullLogger<AnonymizerExclusionProvider>.Instance);

        // Act
        var exclusions = await provider.GetExclusionsAsync("TestDb", TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(exclusions);
    }

    [Fact]
    public async Task GetExclusionsAsync_ShouldFallBackSafely_WhenExclusionTableQueryFails()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Anonymizer.ExclusionTableName = "dbo.MyExclusions";

        // Throw exception simulates a database error during query
        var mockConn = new MockConnection([], throwException: true);
        var mockFactory = new DummyConnectionFactory(mockConn);
        var provider = new AnonymizerExclusionProvider(mockFactory, Options.Create(options), NullLogger<AnonymizerExclusionProvider>.Instance);

        // Act
        var exclusions = await provider.GetExclusionsAsync("TestDb", TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(exclusions);
    }

    // Helper classes for mocking ADO.NET connections
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
        private readonly List<(string Table, string Column)> _rows;
        private readonly bool _throwException;
        private readonly string? _simulatedTableName;
        private string _connectionString = "";

        public MockConnection(List<(string Table, string Column)> rows, bool throwException = false, string? simulatedTableName = "dbo.MyExclusions")
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

        protected override DbCommand CreateDbCommand()
        {
            return new MockCommand(_rows, _throwException, _simulatedTableName);
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class MockCommand : DbCommand
    {
        private readonly List<(string Table, string Column)> _rows;
        private readonly bool _throwException;
        private readonly string? _simulatedTableName;
        private DbConnection? _dbConnection;

        public MockCommand(List<(string Table, string Column)> rows, bool throwException, string? simulatedTableName)
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
            if (CommandText != null && CommandText.Contains("OBJECT_ID"))
            {
                return new SingleValueDataReader(_simulatedTableName);
            }
            return new MockDataReader(_rows);
        }

        protected override DbParameter CreateDbParameter()
        {
            return new MockParameter();
        }

        public override void Prepare() { }
    }

    private sealed class MockDataReader : DbDataReader
    {
        private readonly List<(string Table, string Column)> _rows;
        private int _readIndex = -1;

        public MockDataReader(List<(string Table, string Column)> rows)
        {
            _rows = rows;
        }

        public override int FieldCount => 2;
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

        public override string GetName(int ordinal) => ordinal == 0 ? "TableName" : "ColumnName";
        public override int GetOrdinal(string name) => string.Equals(name, "ColumnName", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        public override object GetValue(int ordinal)
        {
            if (_readIndex < 0 || _readIndex >= _rows.Count)
            {
                throw new InvalidOperationException("No data available.");
            }
            return ordinal == 0 ? _rows[_readIndex].Table : _rows[_readIndex].Column;
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
        public override string GetString(int ordinal) => GetValue(ordinal).ToString() ?? "";
        
        public override int GetValues(object[] values)
        {
            values[0] = GetValue(0);
            values[1] = GetValue(1);
            return 2;
        }

        public override bool IsDBNull(int ordinal) => false;

        public override System.Collections.IEnumerator GetEnumerator()
        {
            throw new NotImplementedException();
        }
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

        public override System.Collections.IEnumerator GetEnumerator()
        {
            throw new NotImplementedException();
        }
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
