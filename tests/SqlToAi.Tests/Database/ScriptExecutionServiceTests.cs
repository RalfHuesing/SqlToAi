#nullable enable

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

// @covers SqlToAi.Database.ScriptExecutionService
public sealed class ScriptExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAtomicallyAsync_EmptyScript_ReturnsInvalidParametersBeforeOpeningConnection()
    {
        var factory = new MockQueryConnectionFactory();
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(AccessLevel.ReadWrite, true));
        var executor = new RecordingBatchExecutor();
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAtomicallyAsync(
            BuildRequest("  \r\n  "), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Null(factory.LastConnection);
        Assert.Empty(safety.BatchQueries);
        Assert.Empty(executor.Calls);
    }

    [Fact]
    public async Task ExecuteAtomicallyAsync_PreflightsAllBatchesBeforeOpeningConnection()
    {
        var factory = new MockQueryConnectionFactory();
        var safety = new RecordingSafetyValidator(query => query.Contains("SELECT 2", StringComparison.Ordinal)
            ? SqlToAiError.WriteOperationBlocked("second batch rejected")
            : new QuerySafetyCheckResult(AccessLevel.ReadWrite, true));
        var executor = new RecordingBatchExecutor();
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAtomicallyAsync(
            BuildRequest("SELECT 1\nGO\nSELECT 2"), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
        Assert.Equal(2, safety.BatchQueries.Count);
        Assert.Null(factory.LastConnection);
        Assert.Empty(executor.Calls);
    }

    [Fact]
    public async Task ExecuteAtomicallyAsync_ReadWriteRunsBatchesSequentiallyAndCommitsOnce()
    {
        var factory = new MockQueryConnectionFactory();
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(AccessLevel.ReadWrite, true));
        var executor = new RecordingBatchExecutor();
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAtomicallyAsync(
            BuildRequest("UPDATE A SET X = 1\nGO\nUPDATE B SET X = 2"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, executor.Calls.Count);
        Assert.Same(executor.Calls[0].Transaction, executor.Calls[1].Transaction);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.CommitCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.RollbackCount);
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnly, false)]
    [InlineData(AccessLevel.ReadOnlyAnonymized, true)]
    public async Task ExecuteAtomicallyAsync_ReadOnlyModesRollbackAndSelectAnonymization(
        AccessLevel accessLevel,
        bool expectedAnonymize)
    {
        var factory = new MockQueryConnectionFactory();
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(accessLevel, false));
        var executor = new RecordingBatchExecutor();
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAtomicallyAsync(
            BuildRequest("SELECT 1\nGO\nSELECT 2"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.All(executor.Calls, call => Assert.Equal(expectedAnonymize, call.Anonymize));
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount);
    }

    [Fact]
    public async Task ExecuteAtomicallyAsync_HonorsRepeatCountAndPreservesBatchMetadata()
    {
        var factory = new MockQueryConnectionFactory();
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(AccessLevel.ReadWrite, true));
        var executor = new RecordingBatchExecutor();
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAtomicallyAsync(
            BuildRequest("SELECT 1\nGO 2\nSELECT 2"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, executor.Calls.Count);
        Assert.Equal(2, safety.BatchQueries.Count);
        Assert.Equal(2, result.Value[0].Batch.RepeatCount);
        Assert.Equal(2, result.Value[0].Executions.Count);
        Assert.Equal(1, result.Value[1].Batch.RepeatCount);
        Assert.Equal(3, result.Value[1].Batch.StartLine);
        Assert.Contains("SELECT 1", result.Value[0].Batch.Text, StringComparison.Ordinal);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.CommitCount);
    }

    [Fact]
    public async Task ExecuteAtomicallyAsync_StopsAfterBatchFailureAndRollsBack()
    {
        var factory = new MockQueryConnectionFactory();
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(AccessLevel.ReadWrite, true));
        var executor = new RecordingBatchExecutor();
        executor.EnqueueSuccess();
        executor.EnqueueFailure(SqlToAiError.QueryError("second batch failed"));
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAtomicallyAsync(
            BuildRequest("SELECT 1\nGO\nSELECT 2\nGO\nSELECT 3"), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Equal(2, executor.Calls.Count);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount);
    }

    [Fact]
    public async Task ExecuteAtomicallyAsync_BatchExceptionMapsToQueryErrorAndRollsBack()
    {
        var factory = new MockQueryConnectionFactory();
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(AccessLevel.ReadWrite, true));
        var executor = new RecordingBatchExecutor { ExceptionToThrow = new InvalidOperationException("execution failed") };
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAtomicallyAsync(
            BuildRequest("SELECT 1"), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Single(executor.Calls);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount);
    }

    [Fact]
    public async Task ExecuteAtomicallyAsync_CancellationRollsBackAndRethrows()
    {
        var factory = new MockQueryConnectionFactory();
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(AccessLevel.ReadWrite, true));
        var executor = new RecordingBatchExecutor { ThrowCancellation = true };
        var service = BuildService(factory, safety, executor);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ExecuteAtomicallyAsync(
            BuildRequest("SELECT 1"), CancellationToken.None));

        Assert.Single(executor.Calls);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount);
    }

    [Fact]
    public async Task ExecuteAtomicallyAsync_RejectsTransactionIntegrityChangeWithDefensiveRollback()
    {
        var factory = new MockQueryConnectionFactory(
            new MockQueryRowConfig(TranCountSequence: new MockTranCountSequence(1, 0)));
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(AccessLevel.ReadOnly, false));
        var executor = new RecordingBatchExecutor();
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAtomicallyAsync(
            BuildRequest("SELECT 1"), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Single(executor.Calls);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount);
    }

    private static ScriptExecutionService BuildService(
        MockQueryConnectionFactory factory,
        RecordingSafetyValidator safety,
        RecordingBatchExecutor executor)
    {
        var options = new SqlToAiOptions();
        return new ScriptExecutionService(
            factory,
            safety,
            executor,
            Options.Create(options),
            NullLogger<ScriptExecutionService>.Instance);
    }

    private static ScriptExecutionRequest BuildRequest(string text) =>
        new(new SqlScriptFile("script.sql", text, "UTF-8"), TestConstants.DatabaseName);

    private sealed class RecordingSafetyValidator(
        Func<string, Result<QuerySafetyCheckResult>> validator) : IQuerySafetyValidator
    {
        public List<string> BatchQueries { get; } = [];

        public Task<Result<QuerySafetyCheckResult>> ValidateQuerySafetyAsync(
            string databaseName,
            string query,
            bool allowSchemaOnly = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(validator(query));

        public Task<Result<QuerySafetyCheckResult>> ValidateBatchSafetyAsync(
            string databaseName,
            string query,
            CancellationToken cancellationToken = default)
        {
            BatchQueries.Add(query);
            return Task.FromResult(validator(query));
        }
    }

    private sealed class RecordingBatchExecutor : IQueryBatchExecutor
    {
        private readonly Queue<Result<QueryExecutionResult>> _results = [];

        public List<QueryBatchExecutionArgs> Calls { get; } = [];

        public Exception? ExceptionToThrow { get; init; }

        public bool ThrowCancellation { get; init; }

        public Task<Result<QueryExecutionResult>> ExecuteBatchAsync(
            QueryBatchExecutionArgs args,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(args);
            if (ThrowCancellation)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }
            return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : SuccessResult());
        }

        public void EnqueueSuccess() => _results.Enqueue(SuccessResult());

        public void EnqueueFailure(SqlToAiError error) => _results.Enqueue(error);

        private static Result<QueryExecutionResult> SuccessResult() =>
            new QueryExecutionResult("[]", false, [], "ScramblePattern");
    }
}
