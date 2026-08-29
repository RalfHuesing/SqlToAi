#nullable enable

using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

// @covers SqlToAi.Database.QueryExecutionService
public sealed class QueryExecutionServiceBatchTests
{
    [Fact]
    public async Task ExecuteBatchAsync_UsesCallerTransactionAndExistingExecutionPipeline()
    {
        var options = new SqlToAiOptions
        {
            QueryExecution = new QueryExecutionOptions { DefaultRowLimit = 2, MaxRowLimit = 10 }
        };
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(RowCount: 3));
        var service = new QueryExecutionService(
            factory,
            new FakeQuerySafetyValidator(new QuerySafetyCheckResult(AccessLevel.ReadOnly, IsWriteAllowed: false)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options),
            NullLogger<QueryExecutionService>.Instance);

        using var connection = (FakeDbConnection)factory.CreateConnection(TestConstants.DatabaseName);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        using var transaction = (FakeDbTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, TestContext.Current.CancellationToken);
        IQueryBatchExecutor executor = service;
        var args = new QueryBatchExecutionArgs(
            connection,
            transaction,
            TestConstants.DatabaseName,
            "SELECT @Id",
            2,
            false,
            new Dictionary<string, object?> { ["Id"] = 7 });

        var first = await executor.ExecuteBatchAsync(args, TestContext.Current.CancellationToken);
        var second = await executor.ExecuteBatchAsync(args with { Query = "SELECT @Id + 1" }, TestContext.Current.CancellationToken);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, factory.ExecutedReaderCommands.Count);
        Assert.All(factory.ExecutedReaderCommands, command => Assert.Same(transaction, command.Transaction));
        Assert.All(factory.ExecutedReaderCommands, command => Assert.Equal(7, command.Parameters["@Id"].Value));
        Assert.Contains(factory.ExecutedNonQueryCommands, command => command == "SET ROWCOUNT 2");
        Assert.Equal(2, factory.ExecutedNonQueryCommands.Count(command => command == "SET ROWCOUNT 0"));
        Assert.Equal(0, transaction.CommitCount);
        Assert.Equal(0, transaction.RollbackCount);
    }

    [Fact]
    public async Task ExecuteBatchAsync_AllowsNullTransactionAndUsesExistingExecutionPipeline()
    {
        var options = new SqlToAiOptions
        {
            QueryExecution = new QueryExecutionOptions { DefaultRowLimit = 2, MaxRowLimit = 10 }
        };
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(RowCount: 3));
        var service = new QueryExecutionService(
            factory,
            new FakeQuerySafetyValidator(new QuerySafetyCheckResult(AccessLevel.ReadOnly, IsWriteAllowed: false)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options),
            NullLogger<QueryExecutionService>.Instance);

        using var connection = (FakeDbConnection)factory.CreateConnection(TestConstants.DatabaseName);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        IQueryBatchExecutor executor = service;
        var args = new QueryBatchExecutionArgs(
            connection,
            null,
            TestConstants.DatabaseName,
            "SELECT @Id",
            2,
            false,
            new Dictionary<string, object?> { ["Id"] = 7 });

        var result = await executor.ExecuteBatchAsync(args, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.RowCount);
        Assert.Single(factory.ExecutedReaderCommands);
        Assert.Null(factory.ExecutedReaderCommands[0].Transaction);
        Assert.Equal(7, factory.ExecutedReaderCommands[0].Parameters["@Id"].Value);
        Assert.Contains(factory.ExecutedNonQueryCommands, command => command == "SET ROWCOUNT 2");
        Assert.Equal(1, factory.ExecutedNonQueryCommands.Count(command => command == "SET ROWCOUNT 0"));
    }
}
