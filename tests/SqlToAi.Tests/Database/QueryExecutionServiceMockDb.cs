#nullable enable

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using SqlToAi.Anonymization;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Tests.Database;

#pragma warning disable CS8765 // Nullability of parameter doesn't match overridden member

internal sealed class AlwaysExcludeRuleProvider : IAnonymizationRuleProvider
{
    public Task<bool> IsExcludedAsync(string databaseName, string schemaName, string tableName, string columnName, CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}

/// <summary>
/// Test double for <see cref="IAnonymizationRuleProvider"/> that excludes (shows in clear text)
/// only when the resolved schema equals <see cref="ExcludedSchema"/> (case-insensitive) — used to
/// verify that <c>QueryExecutionService</c> actually threads the resolved <c>BaseSchemaName</c>
/// through to the rule provider, instead of collapsing same-named tables in different schemas into
/// one decision (see tasks/audit-2026-07-24/02-anonymisierung-tokenisierung.md, Finding
/// "Ausschluss-/Regel-Abgleich ist schema-blind").
/// </summary>
internal sealed class SchemaScopedRuleProvider(string excludedSchema) : IAnonymizationRuleProvider
{
    public Task<bool> IsExcludedAsync(string databaseName, string schemaName, string tableName, string columnName, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Equals(schemaName, excludedSchema, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Test double for <see cref="IAnonymizerExclusionProvider"/> returning a fixed, pre-built
/// <see cref="AnonymizerExclusionSet"/> — lets tests exercise schema-scoped exclusion matching
/// end-to-end through <c>QueryExecutionService</c> without a real database connection.
/// </summary>
internal sealed class FakeExclusionProvider(AnonymizerExclusionSet exclusions) : IAnonymizerExclusionProvider
{
    public Task<AnonymizerExclusionSet> GetExclusionsAsync(string databaseName, CancellationToken cancellationToken = default)
        => Task.FromResult(exclusions);
}

/// <summary>
/// Minimal <see cref="ILogger{T}"/> test double that captures the last logged message and
/// exception, so tests can assert what actually reaches the log — independent of what (if
/// anything) is filtered before being returned to the AI as the tool's error response.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public string? LastMessage { get; private set; }
    public Exception? LastException { get; private set; }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        LastMessage = formatter(state, exception);
        LastException = exception;
    }
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

/// <summary>
/// Schema-table origin metadata (<c>BaseSchemaName</c>/<c>BaseTableName</c>/<c>BaseColumnName</c>)
/// a mock reader reports for column 0, or "unavailable" (<see cref="Available"/> false) to simulate
/// a provider without schema-table support. Bundled into its own record (see AiNetLinter
/// <c>MaxConstructorDependencies</c>) so the mock DB constructors stay within the project's
/// parameter-count limit.
/// </summary>
internal sealed record MockSchemaOrigin(string? BaseTableName = null, string? BaseColumnName = null, bool Available = true, string? BaseSchemaName = null);

/// <summary>Bundles all per-test mock DB configuration into one parameter object.</summary>
internal sealed record MockQueryRowConfig(
    string? StringValue = null,
    int RowCount = 1,
    string ColumnName = "Name",
    MockSchemaOrigin? Origin = null,
    MockTranCountSequence? TranCountSequence = null,
    bool ThrowOnRollback = false,
    Exception? ThrowOnExecute = null);

/// <summary>
/// Simulates a sequence of <c>SELECT @@TRANCOUNT</c> probe results, for testing
/// <c>QueryExecutionService</c>'s layer-2 transaction-tampering detection (see
/// <see cref="TransactionIntegrityGuard"/>) without needing an actual mutating keyword. Each call
/// to <see cref="Next"/> returns the next configured value; once exhausted, the last value
/// repeats (only two calls — baseline and post-execution — are expected per query).
/// </summary>
internal sealed class MockTranCountSequence(params int[] values)
{
    private int _index;

    public int Next()
    {
        int value = values[Math.Min(_index, values.Length - 1)];
        _index++;
        return value;
    }
}

internal sealed class MockQueryConnectionFactory(MockQueryRowConfig? config = null) : IDatabaseConnectionFactory
{
    private readonly MockQueryRowConfig _config = config ?? new MockQueryRowConfig();

    /// <summary>The most recently created connection — lets tests inspect its transaction.</summary>
    public MockQueryConnection? LastConnection { get; private set; }

    public DbConnection CreateConnection(string? databaseName)
    {
        LastConnection = new MockQueryConnection(_config);
        return LastConnection;
    }

    public DbConnection CreateConnection() => CreateConnection((string?)null);
}

internal sealed class MockQueryConnection(MockQueryRowConfig config) : DbConnection
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
        LastTransaction = new MockQueryTransaction(this, config);
        return LastTransaction;
    }

    protected override DbCommand CreateDbCommand() => new MockQueryCommand(this, config);

    /// <summary>
    /// Records the command actually run through a data reader (the real query) — deliberately
    /// NOT every command created on this connection, so an incidental <c>SELECT @@TRANCOUNT</c>
    /// probe (see <see cref="TransactionIntegrityGuard"/>), which only ever calls
    /// <c>ExecuteScalar</c>, never overwrites <see cref="LastCommand"/> and existing assertions
    /// on the resolved query text keep working unchanged.
    /// </summary>
    internal void RecordExecutedCommand(MockQueryCommand command) => LastCommand = command;

    public override void ChangeDatabase(string databaseName) { }
    public override void Close() => _state = ConnectionState.Closed;
}

internal sealed class MockQueryTransaction(DbConnection connection, MockQueryRowConfig config) : DbTransaction
{
    public int CommitCount { get; private set; }
    public int RollbackCount { get; private set; }

    protected override DbConnection DbConnection => connection;
    public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
    public override void Commit() => CommitCount++;

    public override void Rollback()
    {
        RollbackCount++;
        if (config.ThrowOnRollback)
        {
            // Simulates the real-world case where the underlying transaction is already gone
            // (e.g. committed server-side by the statement itself) by the time our code tries
            // to roll it back defensively.
            throw new InvalidOperationException("This transaction has already completed; it is no longer usable.");
        }
    }
}

internal sealed class MockQueryCommand(DbConnection connection, MockQueryRowConfig config) : DbCommand
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

    /// <summary>
    /// Returns 1 by default (preserving prior behavior for every unconfigured test). When a
    /// <see cref="MockQueryRowConfig.TranCountSequence"/> is configured and the command text is
    /// the <c>SELECT @@TRANCOUNT</c> probe issued by <see cref="TransactionIntegrityGuard"/>,
    /// returns the next simulated value instead — letting tests drive the layer-2
    /// transaction-tampering detection without any real mutating keyword.
    /// </summary>
    public override object? ExecuteScalar() =>
        config.TranCountSequence != null && CommandText.Contains("@@TRANCOUNT", StringComparison.OrdinalIgnoreCase)
            ? config.TranCountSequence.Next()
            : 1;

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        ((MockQueryConnection)DbConnection!).RecordExecutedCommand(this);
        if (config.ThrowOnExecute != null)
        {
            throw config.ThrowOnExecute;
        }
        return new MockQueryReader(config);
    }

    protected override DbParameter CreateDbParameter() => new MockQueryParameter();
    public override void Prepare() { }
}

internal sealed class MockQueryReader(MockQueryRowConfig config) : DbDataReader
{
    private int _rowIndex = -1;
    private readonly string? _stringValue = config.StringValue;
    private readonly int _totalRows = config.RowCount;
    private readonly string _columnName = config.ColumnName;
    private readonly MockSchemaOrigin _origin = config.Origin ?? new MockSchemaOrigin();

    public override int FieldCount => 1;
    public override bool HasRows => _totalRows > 0;
    public override bool IsClosed => false;
    public override int RecordsAffected => 0;
    public override int Depth => 0;

    /// <summary>
    /// Reports a BaseSchemaName/BaseTableName/BaseColumnName for column 0 when configured, so tests
    /// can exercise both the "TableName.ColumnName" qualification path and the alias-vs-origin
    /// exclusion decision in <c>QueryExecutionService.AnonymizeCell</c> / <c>GetColumnOrigins</c>.
    /// An unavailable origin simulates a provider without schema-table support by returning null.
    /// </summary>
    public override DataTable? GetSchemaTable()
    {
        if (!_origin.Available || (_origin.BaseTableName is null && _origin.BaseColumnName is null && _origin.BaseSchemaName is null))
        {
            return null;
        }

        var table = new DataTable();
        table.Columns.Add("ColumnOrdinal", typeof(int));
        table.Columns.Add("BaseTableName", typeof(string));
        table.Columns.Add("BaseColumnName", typeof(string));
        table.Columns.Add("BaseSchemaName", typeof(string));
        DataRow row = table.NewRow();
        row["ColumnOrdinal"] = 0;
        row["BaseTableName"] = (object?)_origin.BaseTableName ?? DBNull.Value;
        row["BaseColumnName"] = (object?)_origin.BaseColumnName ?? DBNull.Value;
        row["BaseSchemaName"] = (object?)_origin.BaseSchemaName ?? DBNull.Value;
        table.Rows.Add(row);
        return table;
    }

    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read() { _rowIndex++; return _rowIndex < _totalRows; }
    public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());
    public override bool NextResult() => false;

    public override string GetName(int ordinal) => _columnName;
    public override int GetOrdinal(string name) => 0;
    public override object GetValue(int ordinal) => (object?)_stringValue ?? "Val";
    public override bool IsDBNull(int ordinal) => _stringValue is null && ordinal == 0;
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
