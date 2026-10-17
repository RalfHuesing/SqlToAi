using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

public sealed class QueryCleanupTests
{
    [Theory]
    [InlineData(false, 17)]
    [InlineData(true, 17)]
    [InlineData(false, int.MaxValue)]
    [InlineData(true, int.MaxValue)]
    public async Task PrimaryFailureSurvivesResetAndRollbackFailures(bool validation, int commandTimeout)
    {
        var factory = new CleanupConnectionFactory
        {
            MainFailure = new InvalidOperationException("primary failure"),
            ResetFailure = new TimeoutException("secondary reset failure"),
            RollbackFailure = new TimeoutException("secondary rollback failure")
        };

        var error = await RunAsync(validation, factory, TestContext.Current.CancellationToken, commandTimeout);

        Assert.Equal(SqlToAiError.QueryError("primary failure"), error);
        Assert.Single(factory.ResetTokens);
        Assert.Single(factory.RollbackTokens);
    }

    [Fact]
    public async Task BaselineFailureSurvivesRollbackFailure()
    {
        var factory = new CleanupConnectionFactory
        {
            ScalarFailure = new InvalidOperationException("baseline failure"),
            RollbackFailure = new TimeoutException("secondary rollback failure")
        };

        var error = await RunAsync(false, factory, TestContext.Current.CancellationToken);

        Assert.Equal(SqlToAiError.QueryError("baseline failure"), error);
        Assert.Equal(1, factory.ScalarCalls);
        Assert.Empty(factory.ResetTokens);
        CancellationToken token = Assert.Single(factory.RollbackTokens);
        Assert.True(token.CanBeCanceled);
        Assert.NotEqual(TestContext.Current.CancellationToken, token);
    }

    [Fact]
    public async Task BaselineCancellationSurvivesRollbackFailure()
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var factory = new CleanupConnectionFactory
        {
            BeforeScalar = caller.Cancel,
            ScalarFailure = new OperationCanceledException(caller.Token),
            RollbackFailure = new InvalidOperationException("secondary rollback failure")
        };

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(false, factory, caller.Token));

        Assert.Equal(caller.Token, exception.CancellationToken);
        Assert.Equal(1, factory.ScalarCalls);
        Assert.Empty(factory.ResetTokens);
        CancellationToken token = Assert.Single(factory.RollbackTokens);
        Assert.True(token.CanBeCanceled);
        Assert.False(token.IsCancellationRequested);
        Assert.NotEqual(caller.Token, token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationSurvivesResetAndRollbackFailures(bool validation)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var factory = new CleanupConnectionFactory
        {
            BeforeMain = caller.Cancel,
            MainFailure = new OperationCanceledException(caller.Token),
            ResetFailure = new InvalidOperationException("secondary reset failure"),
            RollbackFailure = new InvalidOperationException("secondary rollback failure")
        };

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(validation, factory, caller.Token));

        Assert.Equal(caller.Token, exception.CancellationToken);
        Assert.All(factory.ResetTokens.Concat(factory.RollbackTokens), token =>
        {
            Assert.True(token.CanBeCanceled);
            Assert.False(token.IsCancellationRequested);
            Assert.NotEqual(caller.Token, token);
        });
        Assert.Single(factory.ResetTokens);
        Assert.Single(factory.RollbackTokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResetFailureAfterSuccessfulWorkSurvivesRollbackFailure(bool validation)
    {
        var factory = new CleanupConnectionFactory
        {
            ResetFailure = new InvalidOperationException("primary reset failure"),
            RollbackFailure = new TimeoutException("secondary rollback failure")
        };

        var error = await RunAsync(validation, factory, TestContext.Current.CancellationToken);

        Assert.Equal(SqlToAiError.QueryError("primary reset failure"), error);
        Assert.Single(factory.RollbackTokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RollbackFailureAfterSuccessfulWorkReturnsFailure(bool validation)
    {
        var factory = new CleanupConnectionFactory { RollbackFailure = new InvalidOperationException("rollback failed") };

        var error = await RunAsync(validation, factory, TestContext.Current.CancellationToken);

        Assert.Equal(SqlToAiError.QueryError("rollback failed"), error);
        Assert.Single(factory.RollbackTokens);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CallerCancellationDuringSuccessfulCleanupCannotReturnSuccess(bool validation, bool duringReset)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var factory = new CleanupConnectionFactory
        {
            DuringReset = duringReset ? caller.Cancel : null,
            DuringRollback = duringReset ? null : caller.Cancel
        };

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(validation, factory, caller.Token));

        Assert.Equal(caller.Token, exception.CancellationToken);
        Assert.Single(factory.ResetTokens);
        Assert.Single(factory.RollbackTokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationDuringRollbackDoesNotReplaceExistingFailure(bool validation)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var factory = new CleanupConnectionFactory
        {
            MainFailure = new InvalidOperationException("primary failure"),
            DuringRollback = caller.Cancel
        };

        var error = await RunAsync(validation, factory, caller.Token);

        Assert.True(caller.IsCancellationRequested);
        Assert.Equal(SqlToAiError.QueryError("primary failure"), error);
    }

    [Fact]
    public async Task Validation_ZeroTimeoutLeavesOrdinaryCommandsUnlimitedAndBoundsReset()
    {
        var factory = new CleanupConnectionFactory();

        Assert.Null(await RunAsync(true, factory, TestContext.Current.CancellationToken, 0));

        Assert.Equal([0, 0, new QueryExecutionOptions().CommandTimeoutSeconds], factory.CommandTimeouts);
    }

    private static async Task<SqlToAiError?> RunAsync(bool validation, CleanupConnectionFactory factory,
        CancellationToken cancellationToken, int commandTimeout = 17)
    {
        var options = Options.Create(new SqlToAiOptions
        {
            QueryExecution = new QueryExecutionOptions { CommandTimeoutSeconds = commandTimeout }
        });
        var safety = new FakeQuerySafetyValidator(new QuerySafetyCheckResult(AccessLevel.ReadOnly, false));
        if (validation)
        {
            var service = new QueryValidationService(factory, safety, options, NullLogger<QueryValidationService>.Instance);
            var result = await service.ValidateQueryAsync(TestConstants.DatabaseName, "SELECT 1", cancellationToken);
            return result.IsFailure ? result.Error : null;
        }
        var execution = new QueryExecutionService(factory, safety,
            new AnonymizationDependencies(new Anonymizer(options, new TokenVault())),
            options, NullLogger<QueryExecutionService>.Instance);
        var queryResult = await execution.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, cancellationToken);
        return queryResult.IsFailure ? queryResult.Error : null;
    }

    private sealed class CleanupConnectionFactory : IDatabaseConnectionFactory
    {
        internal Exception? MainFailure { get; init; }
        internal Exception? ScalarFailure { get; init; }
        internal Exception? ResetFailure { get; init; }
        internal Exception? RollbackFailure { get; init; }
        internal Action? BeforeMain { get; init; }
        internal Action? BeforeScalar { get; init; }
        internal Action? DuringReset { get; init; }
        internal Action? DuringRollback { get; init; }
        internal List<CancellationToken> ResetTokens { get; } = [];
        internal List<CancellationToken> RollbackTokens { get; } = [];
        internal List<int> CommandTimeouts { get; } = [];
        internal int ScalarCalls { get; private set; }

        public DbConnection CreateConnection(string? databaseName = null) => new FakeDbConnection(
            connection => new FakeDbCommand(connection, new FakeDbCommandHandlers(
                ExecuteScalar: _ =>
                {
                    ScalarCalls++;
                    BeforeScalar?.Invoke();
                    if (ScalarFailure is not null) throw ScalarFailure;
                    return 1;
                },
                ExecuteReader: _ =>
                {
                    ExecuteMain();
                    return new FakeDbDataReader(["Value"], [[1]]);
                },
                ExecuteNonQueryAsync: (command, token) =>
                {
                    CommandTimeouts.Add(command.CommandTimeout);
                    if (command.CommandText is "SET ROWCOUNT 0" or "SET NOEXEC OFF")
                    {
                        ResetTokens.Add(token);
                        DuringReset?.Invoke();
                        if (ResetFailure is not null) throw ResetFailure;
                    }
                    else if (command.CommandText == "SELECT 1") ExecuteMain();
                    return Task.FromResult(0);
                })),
            new FakeDbConnectionOptions(BeginTransaction: (connection, _) => new FakeDbTransaction(connection,
                onRollbackAsync: token =>
                {
                    RollbackTokens.Add(token);
                    DuringRollback?.Invoke();
                    if (RollbackFailure is not null) throw RollbackFailure;
                    return Task.CompletedTask;
                })));

        private void ExecuteMain()
        {
            BeforeMain?.Invoke();
            if (MainFailure is not null) throw MainFailure;
        }
    }
}
