using System.Data;
using System.Data.Common;
using SqlToAi.Database;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

public sealed class DatabaseCommandExecutorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Queries_BufferFirstResultAndDrainAllRemainingRows(bool firstOnly)
    {
        await using var reader = new TrackingReader([
            new FakeDbResultSet(["Value"], [[1], [2]]),
            new FakeDbResultSet(["Ignored"], [[3]])]);
        await using var command = new TrackingCommand(reader);
        using var connection = CreateConnection(command);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        if (firstOnly)
        {
            Assert.Equal(1, await DatabaseCommandExecutor.QueryFirstOrDefaultAsync(connection,
                "SELECT Value", static row => row.GetInt32(0), TestContext.Current.CancellationToken));
        }
        else
        {
            Assert.Equal([1, 2], await DatabaseCommandExecutor.QueryAsync(connection,
                "SELECT Value", static row => row.GetInt32("value"), TestContext.Current.CancellationToken));
        }

        Assert.Equal(5, reader.ReadCount);
        Assert.Equal(2, reader.NextCount);
        Assert.All(reader.Tokens, token => Assert.Equal(TestContext.Current.CancellationToken, token));
        Assert.Equal(TestContext.Current.CancellationToken, command.ExecuteToken);
        Assert.True(reader.DisposedAsync);
        Assert.True(command.DisposedAsync);
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Equal(1, connection.OpenCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstOrDefault_PreservesEmptyAndNullResults(bool hasNullRow)
    {
        await using var reader = new TrackingReader([new FakeDbResultSet(["Value"], hasNullRow ? [[DBNull.Value]] : [])]);
        await using var command = new TrackingCommand(reader);
        using var connection = CreateConnection(command);
        Assert.Null(await DatabaseCommandExecutor.QueryFirstOrDefaultAsync(connection,
            "SELECT Value", static row => row.GetStringOrNull(0), TestContext.Current.CancellationToken));
        Assert.True(command.DisposedAsync);
        Assert.True(reader.DisposedAsync);
    }

    [Fact]
    public async Task Query_MapsAliasesNullDefaultsAndSqlNumericRepresentations()
    {
        await using var reader = new TrackingReader([new FakeDbResultSet(
            ["NAME", "Tiny", "Small", "Bit", "NullValue"], [["column", (byte)12, (short)-1, true, DBNull.Value]])]);
        await using var command = new TrackingCommand(reader);
        using var connection = CreateConnection(command);
        var row = Assert.Single(await DatabaseCommandExecutor.QueryAsync(connection, "SELECT fields",
            static row => (Name: row.GetString("name"), Tiny: row.GetInt32("tiny"), Small: row.GetInt32("Small"),
                Bit: row.GetInt32("Bit"), Boolean: row.GetBoolean("Tiny"), Null: row.GetStringOrNull("NullValue"),
                Default: row.GetString("Absent", "%"), Number: row.GetInt64("Absent"),
                Nullable: row.GetNullableInt32("Absent"), Date: row.GetNullableDateTime("NullValue")),
            TestContext.Current.CancellationToken));
        Assert.Equal(("column", 12, -1, 1, true, null, "%", 0L, null, null), row);
    }

    [Theory]
    [InlineData(null, 43)]
    [InlineData(0, 0)]
    [InlineData(7, 7)]
    public async Task Query_PreservesDefaultOrExplicitTimeoutAndTypedNullParameters(int? timeout, int expected)
    {
        await using var reader = new TrackingReader([new FakeDbResultSet(["Value"], [])]);
        await using var command = new TrackingCommand(reader) { CommandTimeout = 43 };
        using var connection = CreateConnection(command);
        await DatabaseCommandExecutor.QueryAsync(connection, "SELECT Value", static row => row.GetInt32(0),
            TestContext.Current.CancellationToken, new Dictionary<string, object?>
            {
                ["Limit"] = new SqlParameterValue(null, DbType.Int32),
                ["Filter"] = new SqlParameterValue(null, DbType.String),
                ["Score"] = new SqlParameterValue(null, DbType.Double),
                ["Name"] = "2026-01-01"
            }, timeout);
        Assert.Equal(expected, command.CommandTimeout);
        Assert.Equal(CommandType.Text, command.CommandType);
        Assert.Equal(DbType.Int32, command.Parameters["@Limit"].DbType);
        Assert.Equal(DbType.String, command.Parameters["@Filter"].DbType);
        Assert.Equal(DbType.Double, command.Parameters["@Score"].DbType);
        Assert.Equal(DBNull.Value, command.Parameters["@Limit"].Value);
        Assert.Equal("2026-01-01", command.Parameters["@Name"].Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Queries_PropagateLateResultErrorsAndAwaitDisposal(bool firstOnly)
    {
        var failure = new TimeoutException("late result");
        await using var reader = new TrackingReader([new FakeDbResultSet(["Value"], [[1]])]) { BeforeNext = () => throw failure };
        await using var command = new TrackingCommand(reader);
        using var connection = CreateConnection(command);
        async Task Act()
        {
            if (firstOnly)
                await DatabaseCommandExecutor.QueryFirstOrDefaultAsync(connection, "SELECT Value", static row => row.GetInt32(0), TestContext.Current.CancellationToken);
            else
                await DatabaseCommandExecutor.QueryAsync(connection, "SELECT Value", static row => row.GetInt32(0), TestContext.Current.CancellationToken);
        }
        Assert.Same(failure, await Assert.ThrowsAsync<TimeoutException>(Act));
        Assert.True(reader.DisposedAsync);
        Assert.True(command.DisposedAsync);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Queries_PropagateCancellationWhileDraining(bool cancelDuringNextResult)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var reader = new TrackingReader([new FakeDbResultSet(["Value"], [[1], [2]])]);
        if (cancelDuringNextResult)
            reader.BeforeNext = cancellation.Cancel;
        else
            reader.BeforeRead = () => { if (reader.ReadCount == 2) cancellation.Cancel(); };
        await using var command = new TrackingCommand(reader);
        using var connection = CreateConnection(command);
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DatabaseCommandExecutor.QueryFirstOrDefaultAsync(
            connection, "SELECT Value", static row => row.GetInt32(0), cancellation.Token));
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.True(reader.DisposedAsync);
        Assert.True(command.DisposedAsync);
    }

    [Fact]
    public async Task Query_PreCanceledDoesNotCreateCommand()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var connection = new FakeDbConnection(_ => throw new InvalidOperationException("Must not create a command."));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DatabaseCommandExecutor.QueryAsync(
            connection, "SELECT Value", static row => row.GetInt32(0), cancellation.Token));
    }

    [Fact]
    public async Task Query_MappingFailureAwaitsReaderAndCommandDisposal()
    {
        await using var reader = new TrackingReader([new FakeDbResultSet(["Value"], [["invalid"]])]);
        await using var command = new TrackingCommand(reader);
        using var connection = CreateConnection(command);
        await Assert.ThrowsAsync<FormatException>(() => DatabaseCommandExecutor.QueryAsync(
            connection, "SELECT Value", static row => row.GetInt32(0), TestContext.Current.CancellationToken));
        Assert.True(reader.DisposedAsync);
        Assert.True(command.DisposedAsync);
    }

    private static FakeDbConnection CreateConnection(TrackingCommand command) => new(_ => command);

    private sealed class TrackingCommand(TrackingReader reader) : FakeDbCommand(null)
    {
        internal CancellationToken ExecuteToken { get; private set; }
        internal bool DisposedAsync { get; private set; }

        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteToken = cancellationToken;
            return reader;
        }

        public override async ValueTask DisposeAsync()
        {
            await Task.Yield();
            DisposedAsync = true;
        }
    }

    private sealed class TrackingReader(IReadOnlyList<FakeDbResultSet> sets) : FakeDbDataReader(sets)
    {
        internal int ReadCount { get; private set; }
        internal int NextCount { get; private set; }
        internal List<CancellationToken> Tokens { get; } = [];
        internal Action? BeforeRead { get; set; }
        internal Action? BeforeNext { get; set; }
        internal bool DisposedAsync { get; private set; }

        public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            ReadCount++;
            Tokens.Add(cancellationToken);
            BeforeRead?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return await base.ReadAsync(cancellationToken);
        }

        public override async Task<bool> NextResultAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            NextCount++;
            Tokens.Add(cancellationToken);
            BeforeNext?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return await base.NextResultAsync(cancellationToken);
        }

        public override async ValueTask DisposeAsync()
        {
            await Task.Yield();
            DisposedAsync = true;
        }
    }
}
