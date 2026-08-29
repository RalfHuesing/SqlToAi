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
    public async Task ExecuteAsync_EmptyScript_ReturnsInvalidParametersBeforeOpeningConnection()
    {
        var factory = new MockQueryConnectionFactory();
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(AccessLevel.ReadWrite, true));
        var executor = new RecordingBatchExecutor();
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAsync(
            BuildRequest("  \r\n  "), TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Failed, result.Status);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error!.Code);
        Assert.Equal(ScriptTransactionMode.NotStarted, result.Mode);
        Assert.Null(factory.LastConnection);
        Assert.Empty(safety.BatchQueries);
        Assert.Empty(executor.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_PreflightsAllBatchesBeforeOpeningConnection()
    {
        var factory = new MockQueryConnectionFactory();
        var safety = new RecordingSafetyValidator(query => query.Contains("SELECT 2", StringComparison.Ordinal)
            ? SqlToAiError.WriteOperationBlocked("second batch rejected")
            : new QuerySafetyCheckResult(AccessLevel.ReadWrite, true));
        var executor = new RecordingBatchExecutor();
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAsync(
            BuildRequest("SELECT 1\nGO\nSELECT 2"), TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Failed, result.Status);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error!.Code);
        Assert.Equal(ScriptBatchStatus.Failed, result.Batches[1].Status);
        Assert.Equal(ScriptTransactionMode.NotStarted, result.Mode);
        Assert.Equal(2, safety.BatchQueries.Count);
        Assert.Null(factory.LastConnection);
        Assert.Empty(executor.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_ReadWriteRunsBatchesSequentiallyAndCommitsOnce()
    {
        var factory = new MockQueryConnectionFactory();
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(AccessLevel.ReadWrite, true));
        var executor = new RecordingBatchExecutor();
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAsync(
            BuildRequest("UPDATE A SET X = 1\nGO\nUPDATE B SET X = 2"), TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Success, result.Status);
        Assert.Equal(2, executor.Calls.Count);
        Assert.All(result.Batches, batch => Assert.Equal(ScriptBatchStatus.Success, batch.Status));
        Assert.Equal(ScriptTransactionMode.ReadWriteAtomic, result.Mode);
        Assert.Same(executor.Calls[0].Transaction, executor.Calls[1].Transaction);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.CommitCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.RollbackCount);
    }

    [Fact]
    public void ScriptExecutionRequest_DefaultsToTransactionalExecution()
    {
        Assert.True(BuildRequest("SELECT 1").UseTransaction);
    }

    [Fact]
    public async Task ExecuteAsync_ReadWriteWithoutTransaction_UsesProviderAutocommit()
    {
        var factory = new MockQueryConnectionFactory();
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(AccessLevel.ReadWrite, true));
        var executor = new RecordingBatchExecutor();
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAsync(
            BuildRequest("UPDATE A SET X = 1\nGO\nALTER DATABASE [TestDb] SET READ_COMMITTED_SNAPSHOT ON") with
            {
                UseTransaction = false
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Success, result.Status);
        Assert.Equal(2, executor.Calls.Count);
        Assert.All(executor.Calls, call => Assert.Null(call.Transaction));
        Assert.Null(factory.LastConnection?.LastTransaction);
        Assert.Equal(ScriptTransactionMode.ReadWriteProviderAutocommit, result.Mode);
    }

    [Fact]
    public async Task ExecuteAsync_ReadWriteWithoutTransaction_StopsAfterFailureWithoutRollback()
    {
        var factory = new MockQueryConnectionFactory();
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(AccessLevel.ReadWrite, true));
        var executor = new RecordingBatchExecutor();
        executor.EnqueueSuccess();
        executor.EnqueueFailure(SqlToAiError.QueryError("second batch failed"));
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAsync(
            BuildRequest("SELECT 1\nGO\nSELECT 2\nGO\nSELECT 3") with
            {
                UseTransaction = false
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Failed, result.Status);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error!.Code);
        Assert.Equal(2, executor.Calls.Count);
        Assert.All(executor.Calls, call => Assert.Null(call.Transaction));
        Assert.Null(factory.LastConnection?.LastTransaction);
        Assert.Equal(ScriptTransactionMode.ReadWriteProviderAutocommit, result.Mode);
        Assert.Equal(ScriptBatchStatus.Success, result.Batches[0].Status);
        Assert.Equal(ScriptBatchStatus.Failed, result.Batches[1].Status);
        Assert.Equal(ScriptBatchStatus.NotExecuted, result.Batches[2].Status);
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnly, false)]
    [InlineData(AccessLevel.ReadOnlyAnonymized, true)]
    public async Task ExecuteAsync_ReadOnlyModesRollbackAndSelectAnonymization(
        AccessLevel accessLevel,
        bool expectedAnonymize)
    {
        var factory = new MockQueryConnectionFactory();
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(accessLevel, false));
        var executor = new RecordingBatchExecutor();
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAsync(
            BuildRequest("SELECT 1\nGO\nSELECT 2"), TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Success, result.Status);
        Assert.All(executor.Calls, call => Assert.Equal(expectedAnonymize, call.Anonymize));
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount);
        Assert.Equal(
            expectedAnonymize ? ScriptTransactionMode.ReadOnlyAnonymizedRollback : ScriptTransactionMode.ReadOnlyRollback,
            result.Mode);
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnly, false)]
    [InlineData(AccessLevel.ReadOnlyAnonymized, true)]
    public async Task ExecuteAsync_ReadOnlyModesForceRollbackWhenTransactionDisabled(
        AccessLevel accessLevel,
        bool expectedAnonymize)
    {
        var factory = new MockQueryConnectionFactory();
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(accessLevel, false));
        var executor = new RecordingBatchExecutor();
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAsync(
            BuildRequest("SELECT 1\nGO\nSELECT 2") with
            {
                UseTransaction = false
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Success, result.Status);
        Assert.All(executor.Calls, call =>
        {
            Assert.NotNull(call.Transaction);
            Assert.Equal(expectedAnonymize, call.Anonymize);
        });
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount);
        Assert.Equal(
            expectedAnonymize ? ScriptTransactionMode.ReadOnlyAnonymizedRollback : ScriptTransactionMode.ReadOnlyRollback,
            result.Mode);
    }

    [Fact]
    public async Task ExecuteAsync_HonorsRepeatCountAndPreservesBatchMetadata()
    {
        var factory = new MockQueryConnectionFactory();
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(AccessLevel.ReadWrite, true));
        var executor = new RecordingBatchExecutor();
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAsync(
            BuildRequest("SELECT 1\nGO 2\nSELECT 2"), TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Success, result.Status);
        Assert.Equal(3, executor.Calls.Count);
        Assert.Equal(2, safety.BatchQueries.Count);
        Assert.Equal(2, result.Batches[0].Batch.RepeatCount);
        Assert.Equal(2, result.Batches[0].Executions.Count);
        Assert.Equal(1, result.Batches[1].Batch.RepeatCount);
        Assert.Equal(3, result.Batches[1].Batch.StartLine);
        Assert.Contains("SELECT 1", result.Batches[0].Batch.Text, StringComparison.Ordinal);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.CommitCount);
    }

    [Fact]
    public async Task ExecuteAsync_SumsMetricsAcrossRepetitionsInReport()
    {
        var factory = new MockQueryConnectionFactory();
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(AccessLevel.ReadWrite, true));
        var executor = new RecordingBatchExecutor();
        executor.EnqueueSuccess(new QueryExecutionResult("{\"id\":1}", false, [], "ScramblePattern", ElapsedMs: 11, RowCount: 1, CpuTimeMs: 7, LogicalReads: 13));
        executor.EnqueueSuccess(new QueryExecutionResult("{\"id\":2}", false, [], "ScramblePattern", ElapsedMs: 17, RowCount: 1, CpuTimeMs: 5, LogicalReads: 19));
        executor.EnqueueSuccess(new QueryExecutionResult("{\"id\":3}", false, [], "ScramblePattern", ElapsedMs: 23, RowCount: 1, CpuTimeMs: 3, LogicalReads: 29));
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAsync(
            BuildRequest("SELECT 1\nGO 2\nSELECT 2"), TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Success, result.Status);
        Assert.Equal(51, result.ElapsedMs);
        Assert.Equal(15, result.CpuTimeMs);
        Assert.Equal(61, result.LogicalReads);
        Assert.Equal(2, result.Batches[0].Executions.Count);
        Assert.Equal("{\"id\":1}", result.Batches[0].Executions[0].Data);
    }

    [Fact]
    public async Task ExecuteAsync_StopsAfterBatchFailureAndRollsBack()
    {
        var factory = new MockQueryConnectionFactory();
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(AccessLevel.ReadWrite, true));
        var executor = new RecordingBatchExecutor();
        executor.EnqueueSuccess();
        executor.EnqueueFailure(SqlToAiError.QueryError("second batch failed"));
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAsync(
            BuildRequest("SELECT 1\nGO\nSELECT 2\nGO\nSELECT 3"), TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Failed, result.Status);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error!.Code);
        Assert.Equal(2, executor.Calls.Count);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount);
        Assert.Equal(ScriptBatchStatus.Success, result.Batches[0].Status);
        Assert.Equal(ScriptBatchStatus.Failed, result.Batches[1].Status);
        Assert.Equal(ScriptBatchStatus.NotExecuted, result.Batches[2].Status);
        Assert.Equal(2, result.Batches[1].BatchNumber);
    }

    [Fact]
    public async Task ExecuteAsync_BatchExceptionMapsToQueryErrorAndRollsBack()
    {
        var factory = new MockQueryConnectionFactory();
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(AccessLevel.ReadWrite, true));
        var executor = new RecordingBatchExecutor { ExceptionToThrow = new InvalidOperationException("execution failed") };
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAsync(
            BuildRequest("SELECT 1"), TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Failed, result.Status);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error!.Code);
        Assert.Single(executor.Calls);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount);
        Assert.Equal(ScriptBatchStatus.Failed, result.Batches[0].Status);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationRollsBackAndRethrows()
    {
        var factory = new MockQueryConnectionFactory();
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(AccessLevel.ReadWrite, true));
        var executor = new RecordingBatchExecutor { ThrowCancellation = true };
        var service = BuildService(factory, safety, executor);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ExecuteAsync(
            BuildRequest("SELECT 1"), CancellationToken.None));

        Assert.Single(executor.Calls);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsTransactionIntegrityChangeWithDefensiveRollback()
    {
        var factory = new MockQueryConnectionFactory(
            new MockQueryRowConfig(TranCountSequence: new MockTranCountSequence(1, 0)));
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(AccessLevel.ReadOnly, false));
        var executor = new RecordingBatchExecutor();
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAsync(
            BuildRequest("SELECT 1"), TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Failed, result.Status);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error!.Code);
        Assert.Single(executor.Calls);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount);
        Assert.Equal(ScriptBatchStatus.Failed, result.Batches[0].Status);
    }

    [Fact]
    public async Task ExecuteAsync_ReadWriteTransactionIntegrityChangeIsRejectedWithoutCommit()
    {
        var factory = new MockQueryConnectionFactory(
            new MockQueryRowConfig(TranCountSequence: new MockTranCountSequence(1, 0)));
        var safety = new RecordingSafetyValidator(_ => new QuerySafetyCheckResult(AccessLevel.ReadWrite, true));
        var executor = new RecordingBatchExecutor();
        var service = BuildService(factory, safety, executor);

        var result = await service.ExecuteAsync(
            BuildRequest("SELECT 1"), TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Failed, result.Status);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error!.Code);
        Assert.Single(executor.Calls);
        Assert.NotNull(executor.Calls[0].Transaction);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount);
        Assert.Equal(ScriptBatchStatus.Failed, result.Batches[0].Status);
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

        public void EnqueueSuccess(QueryExecutionResult result) => _results.Enqueue(result);

        public void EnqueueFailure(SqlToAiError error) => _results.Enqueue(error);

        private static Result<QueryExecutionResult> SuccessResult() =>
            new QueryExecutionResult("[]", false, [], "ScramblePattern");
    }
}
