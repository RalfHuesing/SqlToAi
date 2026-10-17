using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

public sealed class ScriptCleanupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypedBatchFailureSurvivesRollbackFailureWithExecutionsAndStatuses(bool writeAllowed)
    {
        var failure = SqlToAiError.QueryError("typed batch failure");
        var success = new QueryExecutionResult("[{\"Value\":1}]", false, [], "ScramblePattern", RowCount: 1);
        var factory = new RollbackTestConnectionFactory { RollbackFailure = new TimeoutException("secondary rollback failure") };
        var executor = new CallbackBatchExecutor((call, _) => call == 3 ? failure : success);

        var result = await BuildService(factory, executor, writeAllowed).ExecuteAsync(
            Request("SELECT 1\nGO\nSELECT 2\nGO 2\nSELECT 3"), TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Failed, result.Status);
        Assert.Same(failure, result.Error);
        Assert.Equal(3, executor.Calls);
        Assert.Equal([ScriptBatchStatus.Success, ScriptBatchStatus.Failed, ScriptBatchStatus.NotExecuted], result.Batches.Select(batch => batch.Status));
        Assert.Same(success, Assert.Single(result.Batches[0].Executions));
        Assert.Same(success, Assert.Single(result.Batches[1].Executions));
        Assert.Same(failure, result.Batches[1].Error);
        Assert.Single(factory.RollbackTokens);
        Assert.Equal(0, factory.Connection?.LastTransaction?.CommitCount);
    }

    [Fact]
    public async Task CallerCancellationSurvivesRollbackFailureWithIndependentToken()
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var factory = new RollbackTestConnectionFactory { RollbackFailure = new InvalidOperationException("secondary rollback failure") };
        var executor = new CallbackBatchExecutor((_, token) =>
        {
            caller.Cancel();
            throw new OperationCanceledException(token);
        });

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BuildService(factory, executor).ExecuteAsync(Request("SELECT 1"), caller.Token));

        Assert.Equal(caller.Token, exception.CancellationToken);
        CancellationToken token = Assert.Single(factory.RollbackTokens);
        Assert.True(token.CanBeCanceled);
        Assert.False(token.IsCancellationRequested);
        Assert.NotEqual(caller.Token, token);
    }

    [Fact]
    public async Task SuccessfulReadonlyWorkWithRollbackFailureRetainsBatchAfterOneAttempt()
    {
        var success = new QueryExecutionResult("[{\"Value\":1}]", false, [], "ScramblePattern", RowCount: 1);
        var factory = new RollbackTestConnectionFactory { RollbackFailure = new InvalidOperationException("rollback failure") };
        var executor = new CallbackBatchExecutor((_, _) => success);

        var result = await BuildService(factory, executor).ExecuteAsync(Request("SELECT 1"), TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Failed, result.Status);
        Assert.Equal(SqlToAiError.QueryError("rollback failure"), result.Error);
        var batch = Assert.Single(result.Batches);
        Assert.Equal(ScriptBatchStatus.Success, batch.Status);
        Assert.Same(success, Assert.Single(batch.Executions));
        Assert.Single(factory.RollbackTokens);
    }

    [Fact]
    public async Task CallerCancellationDuringSuccessfulReadonlyRollbackDoesNotRetryOrReturnSuccess()
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var factory = new RollbackTestConnectionFactory { DuringRollback = caller.Cancel };
        var executor = new CallbackBatchExecutor((_, _) => Success());

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BuildService(factory, executor).ExecuteAsync(Request("SELECT 1"), caller.Token));

        Assert.Equal(caller.Token, exception.CancellationToken);
        Assert.Single(factory.RollbackTokens);
    }

    [Fact]
    public async Task TypedBatchFailureSurvivesCallerCancellationDuringFailedRollback()
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var failure = SqlToAiError.QueryError("typed batch failure");
        var factory = new RollbackTestConnectionFactory
        {
            DuringRollback = caller.Cancel,
            RollbackFailure = new InvalidOperationException("secondary rollback failure")
        };
        var executor = new CallbackBatchExecutor((_, _) => failure);

        var result = await BuildService(factory, executor).ExecuteAsync(Request("SELECT 1"), caller.Token);

        Assert.True(caller.IsCancellationRequested);
        Assert.Same(failure, result.Error);
        Assert.Same(failure, Assert.Single(result.Batches).Error);
        Assert.Single(factory.RollbackTokens);
    }

    [Fact]
    public async Task BaselineFailureSurvivesRollbackFailure()
    {
        var factory = new RollbackTestConnectionFactory
        {
            ScalarFailure = new InvalidOperationException("baseline failure"),
            RollbackFailure = new TimeoutException("secondary rollback failure")
        };
        var executor = new CallbackBatchExecutor((_, _) => Success());

        var result = await BuildService(factory, executor).ExecuteAsync(Request("SELECT 1"), TestContext.Current.CancellationToken);

        Assert.Equal(SqlToAiError.QueryError("baseline failure"), result.Error);
        Assert.Equal(0, executor.Calls);
        Assert.Single(factory.RollbackTokens);
    }

    private static ScriptExecutionService BuildService(RollbackTestConnectionFactory factory, CallbackBatchExecutor executor,
        bool writeAllowed = false) => new(factory,
        new FakeQuerySafetyValidator(new QuerySafetyCheckResult(writeAllowed ? AccessLevel.ReadWrite : AccessLevel.ReadOnly, writeAllowed)),
        executor, Options.Create(new SqlToAiOptions { QueryExecution = new QueryExecutionOptions { CommandTimeoutSeconds = 17 } }.WithNamedServers(TestTargets.ServerAlias)),
        NullLogger<ScriptExecutionService>.Instance);

    private static ScriptExecutionRequest Request(string text) =>
        new(new SqlScriptFile("script.sql", text, "UTF-8"), TestConstants.QualifiedDatabaseName);

    private static QueryExecutionResult Success() => new("[]", false, [], "ScramblePattern");

    private sealed class CallbackBatchExecutor(Func<int, CancellationToken, Result<QueryExecutionResult>> execute) : IQueryBatchExecutor
    {
        internal int Calls { get; private set; }

        public Task<Result<QueryExecutionResult>> ExecuteBatchAsync(QueryBatchExecutionArgs args, CancellationToken cancellationToken = default) =>
            Task.FromResult(execute(++Calls, cancellationToken));
    }
}
