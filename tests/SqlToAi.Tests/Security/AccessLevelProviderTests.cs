#nullable enable

using System.Data;
using System.Data.Common;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace SqlToAi.Tests.Security;

#pragma warning disable CS8765 // Nullability of parameter doesn't match overridden member

// @covers SqlToAi.Security.AccessLevelProvider
// @covers SqlToAi.Domain.AccessCheckResult
public sealed class AccessLevelProviderTests
{
    private static readonly Type TargetType = typeof(AccessLevelProvider);

    [Fact]
    public async Task GetAccessLevelAsync_ShouldReturnReadOnlyAnonymized_WhenAccessCheckSqlIsEmpty()
    {
        // Arrange — with no AccessCheckSql configured there is no per-database signal to
        // trust, so the fail-safe default (read-only, anonymized) always applies.
        var options = new SqlToAiOptions();
        options.Databases.AccessCheckSql = "";

        var mockFactory = new DummyConnectionFactory();
        var provider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);

        // Act
        var level = await provider.GetAccessLevelAsync(TestConstants.DatabaseName, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AccessLevel.ReadOnlyAnonymized, level);
        Assert.Equal(0, mockFactory.ConnectionCreatedCount);
    }

    [Fact]
    public async Task GetAccessLevelAsync_ShouldCacheResults_AndRespectTtl()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.AccessCheckSql = "SELECT 'ReadOnly' AS AccessLevel";
        options.Databases.CacheTtlSeconds = 1; // 1 second TTL

        var mockConn = new MockConnection("ReadOnly");
        var mockFactory = new DummyConnectionFactory(mockConn);
        var provider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);

        // Act & Assert
        // First call: Queries connection
        var level1 = await provider.GetAccessLevelAsync(TestConstants.DatabaseName, TestContext.Current.CancellationToken);
        Assert.Equal(AccessLevel.ReadOnly, level1);
        Assert.Equal(1, mockFactory.ConnectionCreatedCount);

        // Second call (immediate): Cached, does not query again
        var level2 = await provider.GetAccessLevelAsync(TestConstants.DatabaseName, TestContext.Current.CancellationToken);
        Assert.Equal(AccessLevel.ReadOnly, level2);
        Assert.Equal(1, mockFactory.ConnectionCreatedCount);

        // Wait for TTL to expire
        await Task.Delay(1100, TestContext.Current.CancellationToken);

        // Third call: Expired, queries again
        var level3 = await provider.GetAccessLevelAsync(TestConstants.DatabaseName, TestContext.Current.CancellationToken);
        Assert.Equal(AccessLevel.ReadOnly, level3);
        Assert.Equal(2, mockFactory.ConnectionCreatedCount);
    }

    [Fact]
    public async Task GetAccessLevelAsync_ShouldReturnNone_WhenSqlThrowsException()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.AccessCheckSql = "SELECT 'ReadOnly'";

        var mockConn = new MockConnection("ReadOnly", throwException: true);
        var mockFactory = new DummyConnectionFactory(mockConn);
        var provider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);

        // Act
        var level = await provider.GetAccessLevelAsync(TestConstants.DatabaseName, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AccessLevel.None, level);
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
            return _connectionToReturn ?? new MockConnection("None");
        }
    }

    private sealed class MockConnection : DbConnection
    {
        private readonly string _returnedValue;
        private readonly bool _throwException;
        private string _connectionString = "";

        public MockConnection(string returnedValue, bool throwException = false)
        {
            _returnedValue = returnedValue;
            _throwException = throwException;
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
            return new MockCommand(_returnedValue, _throwException);
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class MockCommand : DbCommand
    {
        private readonly string _returnedValue;
        private readonly bool _throwException;
        private DbConnection? _dbConnection;

        public MockCommand(string returnedValue, bool throwException)
        {
            _returnedValue = returnedValue;
            _throwException = throwException;
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

        protected override DbParameterCollection DbParameterCollection => throw new NotImplementedException();
        protected override DbTransaction? DbTransaction { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.None;

        public override void Cancel() { }
        public override int ExecuteNonQuery() => 0;
        public override object? ExecuteScalar() => _returnedValue;

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            if (_throwException)
            {
                throw new InvalidOperationException("Connection failed simulated.");
            }
            return new MockDataReader(_returnedValue);
        }

        protected override DbParameter CreateDbParameter()
        {
            throw new NotImplementedException();
        }

        public override void Prepare() { }
    }

    private sealed class MockDataReader : DbDataReader
    {
        private readonly string _value;
        private int _readCount;

        public MockDataReader(string value)
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

        public override string GetName(int ordinal) => "AccessLevel";
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
}
