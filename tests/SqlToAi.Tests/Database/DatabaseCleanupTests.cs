using Microsoft.Extensions.Logging.Abstractions;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

public sealed class DatabaseCleanupTests
{
    [Theory]
    [InlineData(17, false)]
    [InlineData(0, false)]
    [InlineData(17, true)]
    [InlineData(0, true)]
    [InlineData(int.MaxValue, false)]
    [InlineData(int.MaxValue, true)]
    public async Task Reset_UsesEffectiveTimeoutAndIndependentCancelableToken(int configuredTimeout, bool noexec)
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        CancellationToken observed = default;
        int observedTimeout = 0;
        string? observedSql = null;
        await using var connection = new FakeDbConnection(conn => new FakeDbCommand(conn,
            new FakeDbCommandHandlers(ExecuteNonQueryAsync: (command, token) =>
            {
                observed = token;
                observedTimeout = command.CommandTimeout;
                observedSql = command.CommandText;
                return Task.FromResult(0);
            })));
        await using var transaction = new FakeDbTransaction(connection);

        if (noexec)
            await DatabaseCleanup.ResetNoexecAsync(connection, transaction, configuredTimeout, NullLogger.Instance);
        else
            await DatabaseCleanup.ResetRowCountAsync(connection, transaction, configuredTimeout, NullLogger.Instance);

        int expectedTimeout = configuredTimeout == int.MaxValue
            ? (int)((uint.MaxValue - 1) / 1000)
            : configuredTimeout > 0 ? configuredTimeout : new QueryExecutionOptions().CommandTimeoutSeconds;
        Assert.Equal(expectedTimeout, observedTimeout);
        Assert.Equal(noexec ? "SET NOEXEC OFF" : "SET ROWCOUNT 0", observedSql);
        Assert.True(observed.CanBeCanceled);
        Assert.False(observed.IsCancellationRequested);
        Assert.NotEqual(cancelled.Token, observed);
    }

    [Fact]
    public async Task Rollback_AwaitsCompletionWithIndependentCancelableToken()
    {
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = new FakeDbConnection(_ => throw new NotSupportedException());
        await using var transaction = new FakeDbTransaction(connection, onRollbackAsync: async token =>
        {
            entered.SetResult(token);
            await completed.Task.WaitAsync(TestContext.Current.CancellationToken);
        });

        Task pending = DatabaseCleanup.RollbackAsync(transaction, 17, NullLogger.Instance);
        CancellationToken observed = await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.True(observed.CanBeCanceled);
            Assert.False(observed.IsCancellationRequested);
            Assert.NotEqual(caller.Token, observed);
            Assert.False(pending.IsCompleted);
        }
        finally
        {
            completed.TrySetResult();
        }
        await pending;
        Assert.Equal(1, transaction.RollbackCount);
    }

    [Fact]
    public async Task Rollback_CleanupDeadlineBecomesTimeout()
    {
        await using var connection = new FakeDbConnection(_ => throw new NotSupportedException());
        await using var transaction = new FakeDbTransaction(connection, onRollbackAsync: async token =>
        {
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var registration = token.Register(() => cancelled.TrySetCanceled(token));
            await cancelled.Task;
        });

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            DatabaseCleanup.RollbackAsync(transaction, 1, NullLogger.Instance));

        var cancellation = Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
        Assert.True(cancellation.CancellationToken.IsCancellationRequested);
        Assert.Equal(SqlToAiError.TimeoutCode, SqlToAiErrorMapper.MapException(exception).Code);
        Assert.Equal(1, transaction.RollbackCount);
    }

    [Fact]
    public async Task Reset_CleanupDeadlineBecomesTimeout()
    {
        await using var connection = new FakeDbConnection(conn => new FakeDbCommand(conn,
            new FakeDbCommandHandlers(ExecuteNonQueryAsync: async (command, token) =>
            {
                Assert.Equal(1, command.CommandTimeout);
                var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await using var registration = token.Register(() => cancelled.TrySetCanceled(token));
                await cancelled.Task;
                return 0;
            })));

        await Assert.ThrowsAsync<TimeoutException>(() =>
            DatabaseCleanup.ResetRowCountAsync(connection, null, 1, NullLogger.Instance));
    }

    [Fact]
    public async Task Rollback_UnrequestedProviderCancellationIsFailureWithoutTimeout()
    {
        var failure = new OperationCanceledException();
        await using var connection = new FakeDbConnection(_ => throw new NotSupportedException());
        await using var transaction = new FakeDbTransaction(connection, onRollback: () => throw failure);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseCleanup.RollbackAsync(transaction, 17, NullLogger.Instance));

        Assert.Same(failure, exception.InnerException);
        Assert.False(SqlToAiErrorMapper.IsTimeoutException(exception));
    }

    [Fact]
    public async Task Rollback_SecondaryFailureIsLoggedAndSuppressed()
    {
        var failure = new InvalidOperationException("secondary rollback failure");
        var logger = new CapturingLogger<DatabaseCleanupTests>();
        await using var connection = new FakeDbConnection(_ => throw new NotSupportedException());
        await using var transaction = new FakeDbTransaction(connection, onRollback: () => throw failure);

        await DatabaseCleanup.RollbackAsync(transaction, 17, logger, preservePrimaryFailure: true);

        Assert.Same(failure, Assert.Single(logger.Entries).Exception);
    }
}
