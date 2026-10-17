using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

public sealed class AnalysisCleanupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MainFailureSurvivesRollbackFailure(bool performance)
    {
        var factory = new RollbackTestConnectionFactory
        {
            MainFailure = new InvalidOperationException("primary analysis failure"),
            RollbackFailure = new TimeoutException("secondary rollback failure")
        };

        var error = await RunAsync(performance, factory, TestContext.Current.CancellationToken);

        Assert.Equal(SqlToAiError.QueryError("primary analysis failure"), error);
        Assert.Single(factory.RollbackTokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulWorkWithRollbackFailureReturnsFailureAfterOneAttempt(bool performance)
    {
        var factory = new RollbackTestConnectionFactory { RollbackFailure = new InvalidOperationException("rollback failure") };

        var error = await RunAsync(performance, factory, TestContext.Current.CancellationToken);

        Assert.Equal(SqlToAiError.QueryError("rollback failure"), error);
        Assert.Single(factory.RollbackTokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationSurvivesRollbackFailureWithIndependentToken(bool performance)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var factory = new RollbackTestConnectionFactory
        {
            BeforeMain = caller.Cancel,
            MainFailure = new OperationCanceledException(caller.Token),
            RollbackFailure = new InvalidOperationException("secondary rollback failure")
        };

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(performance, factory, caller.Token));

        Assert.Equal(caller.Token, exception.CancellationToken);
        CancellationToken token = Assert.Single(factory.RollbackTokens);
        Assert.True(token.CanBeCanceled);
        Assert.False(token.IsCancellationRequested);
        Assert.NotEqual(caller.Token, token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationDuringSuccessfulRollbackCannotReturnSuccess(bool performance)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var factory = new RollbackTestConnectionFactory { DuringRollback = caller.Cancel };

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(performance, factory, caller.Token));

        Assert.Equal(caller.Token, exception.CancellationToken);
        Assert.Single(factory.RollbackTokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationDuringRollbackDoesNotReplaceMainFailure(bool performance)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var factory = new RollbackTestConnectionFactory
        {
            MainFailure = new InvalidOperationException("primary analysis failure"),
            DuringRollback = caller.Cancel
        };

        var error = await RunAsync(performance, factory, caller.Token);

        Assert.True(caller.IsCancellationRequested);
        Assert.Equal(SqlToAiError.QueryError("primary analysis failure"), error);
    }

    private static async Task<SqlToAiError?> RunAsync(bool performance, RollbackTestConnectionFactory factory, CancellationToken token)
    {
        var dependencies = new QueryExecutionDependencies(factory,
            new FakeQuerySafetyValidator(new QuerySafetyCheckResult(AccessLevel.ReadOnly, false)),
            Options.Create(new SqlToAiOptions { QueryExecution = new QueryExecutionOptions { CommandTimeoutSeconds = 17 } }
                .WithNamedServers(TestTargets.ServerAlias)));
        if (performance)
        {
            var service = new PerformanceMeasurementService(dependencies, NullLogger<PerformanceMeasurementService>.Instance);
            var result = await service.MeasurePerformanceAsync(new QueryPerformanceArgs(TestConstants.QualifiedDatabaseName,
                "SELECT 1 AS Value", WarmupRuns: 0, ExecutionRuns: 1, IncludePlanAnalysis: false), token);
            return result.IsFailure ? result.Error : null;
        }
        var comparison = new QueryComparisonService(dependencies, NullLogger<QueryComparisonService>.Instance);
        var compared = await comparison.CompareQueriesAsync(TestConstants.QualifiedDatabaseName, "SELECT 1 AS Value", "SELECT 1 AS Value", token);
        return compared.IsFailure ? compared.Error : null;
    }
}

internal sealed class RollbackTestConnectionFactory : IDatabaseConnectionFactory
{
    public System.Data.Common.DbConnection CreateTargetConnection(SqlToAi.Domain.DatabaseTargetContext target) => CreateConnection(target.DatabaseName);
    public SqlToAi.Domain.Result<System.Data.Common.DbConnection> CreateCatalogConnection(string? serverAlias) => throw new NotSupportedException();

    internal Exception? MainFailure { get; init; }
    internal Exception? ScalarFailure { get; init; }
    internal Exception? RollbackFailure { get; init; }
    internal Action? BeforeMain { get; init; }
    internal Action? DuringRollback { get; init; }
    internal List<CancellationToken> RollbackTokens { get; } = [];
    internal FakeDbConnection? Connection { get; private set; }

    public DbConnection CreateConnection(string? databaseName = null)
    {
        Connection = new FakeDbConnection(connection => new FakeDbCommand(connection, new FakeDbCommandHandlers(
            ExecuteReader: command =>
            {
                BeforeMain?.Invoke();
                if (MainFailure is not null) throw MainFailure;
                return new FakeDbDataReader(["Value"], command.CommandText.Contains(" EXCEPT ", StringComparison.Ordinal) ? [] : [[1]]);
            },
            ExecuteScalar: _ =>
            {
                if (ScalarFailure is not null) throw ScalarFailure;
                return 1;
            })),
            new FakeDbConnectionOptions(BeginTransaction: (connection, _) => new FakeDbTransaction(connection,
                onRollbackAsync: token =>
                {
                    RollbackTokens.Add(token);
                    DuringRollback?.Invoke();
                    if (RollbackFailure is not null) throw RollbackFailure;
                    return Task.CompletedTask;
                })));
        return Connection;
    }
}
