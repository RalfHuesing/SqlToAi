#nullable enable

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Unit tests for <see cref="QueryExecutionService"/>.
/// Uses a test double for the connection factory and real implementations of the guards.
/// </summary>
public sealed class QueryExecutionServiceTests
{
    // -------------------------------------------------------------------------
    // Helpers: build service with configurable fakes
    // -------------------------------------------------------------------------

    private static QueryExecutionService BuildService(
        AccessLevel accessLevel = AccessLevel.ReadOnly,
        bool isAllowed = true,
        bool readOnlySafe = true,
        string? mockData = null,
        SqlToAiOptions? options = null)
    {
        options ??= new SqlToAiOptions();
        var factory = new MockQueryConnectionFactory(mockData ?? "Col1\tVal1");
        var securityGuard = new FakeSecurityGuard(isAllowed);
        var accessLevelProvider = new FakeAccessLevelProvider(accessLevel);
        var readOnlyGuard = new FakeReadOnlyGuard(readOnlySafe);
        var anonymizer = new Anonymizer(Options.Create(options));
        return new QueryExecutionService(
            factory, securityGuard, accessLevelProvider, readOnlyGuard,
            anonymizer, Options.Create(options), NullLogger<QueryExecutionService>.Instance);
    }

    // -------------------------------------------------------------------------
    // Tests: input validation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_ShouldFail_WhenDatabaseNameIsEmpty()
    {
        var service = BuildService();
        var result = await service.ExecuteQueryAsync("", "SELECT 1", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldFail_WhenQueryIsEmpty()
    {
        var service = BuildService();
        var result = await service.ExecuteQueryAsync("DemoDb", "   ", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Tests: security checks
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_ShouldFail_WhenDatabaseNotAllowed()
    {
        var service = BuildService(isAllowed: false);
        var result = await service.ExecuteQueryAsync("BlockedDb", "SELECT 1", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    [Theory]
    [InlineData(AccessLevel.None)]
    [InlineData(AccessLevel.SchemaOnly)]
    public async Task ExecuteQueryAsync_ShouldFail_WhenAccessLevelTooLow(AccessLevel level)
    {
        var service = BuildService(accessLevel: level);
        var result = await service.ExecuteQueryAsync("DemoDb", "SELECT 1", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldFail_WhenQueryIsMutating()
    {
        var service = BuildService(readOnlySafe: false);
        var result = await service.ExecuteQueryAsync("DemoDb", "DELETE FROM Customers", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Tests: multi-statement detection
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1 ; DROP TABLE Foo")]
    [InlineData("SELECT 'hello'; SELECT 'world'")]
    public async Task ExecuteQueryAsync_ShouldFail_WhenMultipleStatements(string query)
    {
        var service = BuildService();
        var result = await service.ExecuteQueryAsync("DemoDb", query, null, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("SELECT 1;")]           // trailing semicolon only — allowed
    [InlineData("SELECT 'hello;world'")] // semicolon inside string literal
    [InlineData("SELECT 1 -- note; comment")]
    public async Task ExecuteQueryAsync_ShouldSucceed_WhenSingleStatement(string query)
    {
        var service = BuildService();
        var result = await service.ExecuteQueryAsync("DemoDb", query, null, TestContext.Current.CancellationToken);
        // We only verify the multi-statement check passes; actual query execution may return stub data
        Assert.True(result.IsSuccess || result.Error.Code == SqlToAiError.QueryErrorCode);
    }

    // -------------------------------------------------------------------------
    // Tests: ReadWrite access level unlocks mutating statements (and commits them)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_ShouldAllowMutatingQuery_AndCommit_WhenReadWrite()
    {
        var options = new SqlToAiOptions();
        var factory = new MockQueryConnectionFactory();
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadWrite),
            new FakeReadOnlyGuard(safe: false), // guard would reject it — must be bypassed
            new Anonymizer(Options.Create(options)),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync("DemoDb", "UPDATE Customers SET Name = 'X'", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.CommitCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.RollbackCount);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldStillRollBack_WhenAccessLevelIsNotReadWrite()
    {
        var options = new SqlToAiOptions();
        var factory = new MockQueryConnectionFactory();
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly), // not ReadWrite
            new FakeReadOnlyGuard(safe: true), new Anonymizer(Options.Create(options)),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync("DemoDb", "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldStillForbidMultipleStatements_WhenWriteAllowed()
    {
        var options = new SqlToAiOptions();
        var service = new QueryExecutionService(
            new MockQueryConnectionFactory(), new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadWrite),
            new FakeReadOnlyGuard(safe: true), new Anonymizer(Options.Create(options)),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync("DemoDb", "UPDATE Foo SET X=1; UPDATE Bar SET Y=2", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Tests: row limit enforcement
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_ShouldRespectDefaultRowLimit()
    {
        var options = new SqlToAiOptions { QueryExecution = new QueryExecutionOptions { DefaultRowLimit = 2, MaxRowLimit = 100 } };
        // MockQueryConnectionFactory returns 5 rows; default limit is 2
        var factory = new MockQueryConnectionFactory(null, rowCount: 5);
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly),
            new FakeReadOnlyGuard(true), new Anonymizer(Options.Create(options)),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync("DemoDb", "SELECT 1", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        int lineCount = result.Value.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.Equal(2, lineCount);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldCapAtMaxRowLimit()
    {
        var options = new SqlToAiOptions { QueryExecution = new QueryExecutionOptions { DefaultRowLimit = 100, MaxRowLimit = 3 } };
        var factory = new MockQueryConnectionFactory(null, rowCount: 10);
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly),
            new FakeReadOnlyGuard(true), new Anonymizer(Options.Create(options)),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync("DemoDb", "SELECT 1", 999, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        int lineCount = result.Value.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.Equal(3, lineCount);
    }

    // -------------------------------------------------------------------------
    // Tests: anonymization
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_ShouldAnonymizeStrings_WhenReadOnlyAnonymized()
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        var factory = new MockQueryConnectionFactory(stringValue: "Ralf Huesing");
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
            new FakeReadOnlyGuard(true), new Anonymizer(Options.Create(options)),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync("DemoDb", "SELECT Name FROM Customers", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("Ralf Huesing", result.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldNotAnonymize_WhenReadOnly()
    {
        const string original = "Ralf Huesing";
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        var factory = new MockQueryConnectionFactory(stringValue: original);
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly),
            new FakeReadOnlyGuard(true), new Anonymizer(Options.Create(options)),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync("DemoDb", "SELECT Name FROM Customers", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Contains(original, result.Value, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // Test doubles
    // =========================================================================

    private sealed class FakeSecurityGuard(bool allowed) : ISecurityGuard
    {
        public bool IsDatabaseAllowed(string databaseName) => allowed;
    }

    private sealed class FakeAccessLevelProvider(AccessLevel level) : IAccessLevelProvider
    {
        public Task<AccessLevel> GetAccessLevelAsync(string databaseName, CancellationToken cancellationToken = default)
            => Task.FromResult(level);
    }

    private sealed class FakeReadOnlyGuard(bool safe) : IReadOnlyGuard
    {
        public bool IsQuerySafe(string query) => safe;
    }

    // -------------------------------------------------------------------------
    // Connection factory / reader mock
    // -------------------------------------------------------------------------

    private sealed class MockQueryConnectionFactory : IDatabaseConnectionFactory
    {
        private readonly string? _stringValue;
        private readonly int _rowCount;

        public MockQueryConnectionFactory(string? stringValue = null, int rowCount = 1)
        {
            _stringValue = stringValue;
            _rowCount = rowCount;
        }

        /// <summary>The most recently created connection — lets tests inspect its transaction.</summary>
        public MockQueryConnection? LastConnection { get; private set; }

        public DbConnection CreateConnection(string? databaseName)
        {
            LastConnection = new MockQueryConnection(_stringValue, _rowCount);
            return LastConnection;
        }

        public DbConnection CreateConnection() => CreateConnection((string?)null);
    }

#pragma warning disable CS8765
    private sealed class MockQueryConnection(string? stringValue, int rowCount) : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;

        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "DemoDb";
        public override string DataSource => "mock";
        public override string ServerVersion => "16.0";
        public override ConnectionState State => _state;

        /// <summary>The most recently started transaction — lets tests inspect commit/rollback calls.</summary>
        public MockQueryTransaction? LastTransaction { get; private set; }

        public override void Open() => _state = ConnectionState.Open;
        public override Task OpenAsync(CancellationToken cancellationToken) { _state = ConnectionState.Open; return Task.CompletedTask; }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            LastTransaction = new MockQueryTransaction(this);
            return LastTransaction;
        }

        protected override DbCommand CreateDbCommand()
            => new MockQueryCommand(this, stringValue, rowCount);

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() => _state = ConnectionState.Closed;
    }

    private sealed class MockQueryTransaction(DbConnection connection) : DbTransaction
    {
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }

        protected override DbConnection DbConnection => connection;
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public override void Commit() => CommitCount++;
        public override void Rollback() => RollbackCount++;
    }

    private sealed class MockQueryCommand(DbConnection connection, string? stringValue, int rowCount) : DbCommand
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
            => new MockQueryReader(stringValue, rowCount);

        protected override DbParameter CreateDbParameter() => new MockQueryParameter();
        public override void Prepare() { }
    }

    private sealed class MockQueryReader(string? stringValue, int totalRows) : DbDataReader
    {
        private int _rowIndex = -1;

        public override int FieldCount => 1;
        public override bool HasRows => totalRows > 0;
        public override bool IsClosed => false;
        public override int RecordsAffected => 0;
        public override int Depth => 0;

        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));

        public override bool Read() { _rowIndex++; return _rowIndex < totalRows; }
        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());
        public override bool NextResult() => false;

        public override string GetName(int ordinal) => "Name";
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

    private sealed class MockQueryParameterCollectionAdapter : DbParameterCollection
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

    private sealed class MockQueryParameter : DbParameter
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
#pragma warning restore CS8765
}
