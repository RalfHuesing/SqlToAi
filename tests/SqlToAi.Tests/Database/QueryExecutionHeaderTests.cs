using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Mcp;
using SqlToAi.Tests.Mcp;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

public sealed class QueryExecutionHeaderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(-1)]
    public async Task Update_ReportsFinalAffectedCountAfterDisposal(int affected)
    {
        await using var reader = new FinalCountReader([new FakeDbResultSet([], [])], affected);
        var factory = new ReaderFactory(reader);
        var result = await ExecuteAsync(factory, AccessLevel.ReadWrite, "UPDATE dbo.T SET Id = 1 WHERE 1 = 0");

        Assert.False(result.IsError);
        Assert.Matches($@"^Execution Info: accessLevel=ReadWrite \| transaction=committed \| {affected} rows affected in \d+ ms \| cpu: 0 ms \| logical reads: 0\.$", result.Content[0].Text!);
        Assert.DoesNotContain("rows returned", result.Content[0].Text, StringComparison.Ordinal);
        Assert.Equal("[]", result.Content[1].Text);
        Assert.True(reader.Disposed);
        Assert.Equal(1, factory.Connection?.LastTransaction?.CommitCount);
        Assert.Equal(0, factory.Connection?.LastTransaction?.RollbackCount);
    }

    [Theory]
    [InlineData(AccessLevel.ReadWrite, "committed", 1)]
    [InlineData(AccessLevel.ReadWriteAnonymized, "committed", 1)]
    [InlineData(AccessLevel.ReadOnly, "rolled_back", 0)]
    [InlineData(AccessLevel.ReadOnlyAnonymized, "rolled_back", 0)]
    public async Task Select_ReportsAccessLevelAndTransaction(AccessLevel level, string outcome, int commits)
    {
        await using var reader = new FinalCountReader([new FakeDbResultSet(["Id"], [[1]])], -1);
        var factory = new ReaderFactory(reader);
        var result = await ExecuteAsync(factory, level, "SELECT 1 AS Id");

        Assert.False(result.IsError);
        Assert.StartsWith($"Execution Info: accessLevel={level} | transaction={outcome} | 1 rows returned in ", result.Content[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("rows affected", result.Content[0].Text, StringComparison.Ordinal);
        Assert.Equal("{\"Id\":1}", result.Content[1].Text);
        Assert.Equal(commits, factory.Connection?.LastTransaction?.CommitCount);
        Assert.Equal(1 - commits, factory.Connection?.LastTransaction?.RollbackCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ColumnBearingResultSet_ReportsReturnedRowsEvenWhenEmpty(bool output, bool laterResultSet)
    {
        var sets = new List<FakeDbResultSet>();
        if (laterResultSet) sets.Add(new FakeDbResultSet([], []));
        sets.Add(new FakeDbResultSet(["Id"], []));
        sets.Add(new FakeDbResultSet([], []));
        await using var reader = new FinalCountReader(sets, 4);
        var factory = new ReaderFactory(reader);
        var query = output ? "INSERT INTO dbo.T (Id) OUTPUT inserted.Id VALUES (1)" : "SELECT Id FROM dbo.T WHERE 1 = 0";
        var result = await ExecuteAsync(factory, AccessLevel.ReadWrite, query);

        Assert.False(result.IsError);
        Assert.Contains("0 rows returned", result.Content[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("rows affected", result.Content[0].Text, StringComparison.Ordinal);
        Assert.Equal("[]", result.Content[1].Text);
    }

    [Fact]
    public async Task InsertOutput_SerializesRowsAndKeepsReturnedCount()
    {
        await using var reader = new FinalCountReader([new FakeDbResultSet(["Id"], [[9]])], 1);
        var factory = new ReaderFactory(reader);
        var result = await ExecuteAsync(factory, AccessLevel.ReadWrite,
            "INSERT INTO dbo.T (Id) OUTPUT inserted.Id VALUES (9)");

        Assert.False(result.IsError);
        Assert.Contains("1 rows returned", result.Content[0].Text, StringComparison.Ordinal);
        Assert.Equal("{\"Id\":9}", result.Content[1].Text);
    }

    [Fact]
    public async Task MultipleColumnlessResultSets_UseFinalAffectedCount()
    {
        await using var reader = new FinalCountReader([
            new FakeDbResultSet([], []), new FakeDbResultSet([], [])], 12);
        var factory = new ReaderFactory(reader);
        var result = await ExecuteAsync(factory, AccessLevel.ReadWrite, "UPDATE dbo.T SET Id = 1");

        Assert.False(result.IsError);
        Assert.Contains("12 rows affected", result.Content[0].Text, StringComparison.Ordinal);
        Assert.Equal("[]", result.Content[1].Text);
    }

    [Fact]
    public async Task ZeroRowLimit_DetectsLaterColumnBearingResultSet()
    {
        await using var reader = new FinalCountReader([
            new FakeDbResultSet([], []), new FakeDbResultSet(["Id"], [[1]])], 1);
        var factory = new ReaderFactory(reader);
        var result = await ExecuteAsync(factory, AccessLevel.ReadWrite, "SELECT 1 AS Id", rowLimit: 0);

        Assert.False(result.IsError);
        Assert.Contains("0 rows returned", result.Content[0].Text, StringComparison.Ordinal);
        Assert.Equal("[]", result.Content[1].Text);
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.ReadOnlyAnonymized)]
    public async Task ReadOnlyUpdate_IsRejectedBeforeConnection(AccessLevel level)
    {
        await using var reader = new FinalCountReader([], 0);
        var factory = new ReaderFactory(reader);
        var result = await ExecuteAsync(factory, level, "UPDATE dbo.T SET Id = 1 WHERE 1 = 0");

        Assert.True(result.IsError);
        Assert.Contains("SQL-AI-0107", result.Content[0].Text, StringComparison.Ordinal);
        Assert.Null(factory.Connection);
    }

    private static Task<ToolCallResult> ExecuteAsync(ReaderFactory factory, AccessLevel level, string query, int rowLimit = 100)
    {
        var options = new SqlToAiOptions().WithNamedServers(TestTargets.ServerAlias);
        var service = new QueryExecutionService(factory, FakeQuerySafetyValidator.Create(accessLevel: level),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);
        return ToolDispatcherTestHelper.BuildDispatcher(exec: service).DispatchAsync(
            ToolDispatcherTestHelper.Call(McpConstants.ToolExecuteQuery,
                (McpConstants.ArgDatabase, TestConstants.QualifiedDatabaseName), (McpConstants.ArgQuery, query),
                (McpConstants.ArgRequestedRowLimit, rowLimit)),
            TestContext.Current.CancellationToken);
    }

    private sealed class FinalCountReader(IReadOnlyList<FakeDbResultSet> sets, int finalCount) : FakeDbDataReader(sets)
    {
        public bool Disposed { get; private set; }
        public override int RecordsAffected => Disposed ? finalCount : -99;

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return base.DisposeAsync();
        }
    }

    private sealed class ReaderFactory(DbDataReader reader) : IDatabaseConnectionFactory
    {
        public FakeDbConnection? Connection { get; private set; }

        public DbConnection CreateTargetConnection(DatabaseTargetContext target)
        {
            Connection = new FakeDbConnection(connection => new FakeDbCommand(connection,
                new FakeDbCommandHandlers(ExecuteScalar: _ => 1, ExecuteReader: _ => reader)),
                new FakeDbConnectionOptions(BeginTransaction: (connection, _) => new FakeDbTransaction(connection)));
            return Connection;
        }

        public Result<DbConnection> CreateCatalogConnection(string? serverAlias) => throw new NotSupportedException();
    }
}
