#nullable enable

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Unit tests for <see cref="QueryExecutionService"/> row-limit, timeout, error handling, and server option enforcement.
/// </summary>
public sealed class QueryExecutionServiceOptionsTests
{
    private static QueryExecutionService BuildService(
        MockQueryConnectionFactory factory,
        SqlToAiOptions? options = null)
    {
        options ??= new SqlToAiOptions();
        return new QueryExecutionService(
            factory,
            new FakeQuerySafetyValidator(new QuerySafetyCheckResult(AccessLevel.ReadOnly, IsWriteAllowed: false)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldRespectDefaultRowLimit()
    {
        var options = new SqlToAiOptions { QueryExecution = new QueryExecutionOptions { DefaultRowLimit = 2, MaxRowLimit = 100 } };
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(RowCount: 5));
        var service = BuildService(factory, options);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        int lineCount = result.Value.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.Equal(2, lineCount);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldCapAtMaxRowLimit()
    {
        var options = new SqlToAiOptions { QueryExecution = new QueryExecutionOptions { DefaultRowLimit = 100, MaxRowLimit = 3 } };
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(RowCount: 10));
        var service = BuildService(factory, options);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", 999, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        int lineCount = result.Value.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.Equal(3, lineCount);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldReturnTimeout_WhenTimeoutExceptionOccurs()
    {
        var options = new SqlToAiOptions();
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(ThrowOnExecute: new TimeoutException("Execution timed out")));
        var service = BuildService(factory, options);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.TimeoutCode, result.Error.Code);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldReturnInfrastructureError_WhenSocketExceptionOccurs()
    {
        var options = new SqlToAiOptions();
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(ThrowOnExecute: new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused)));
        var service = BuildService(factory, options);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InfrastructureErrorCode, result.Error.Code);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldIssueSetStatisticsCommands_BeforeMainQuery()
    {
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig("Col1\tVal1"));
        var service = BuildService(factory);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains(factory.ExecutedNonQueryCommands, c => string.Equals(c, "SET STATISTICS IO ON", StringComparison.Ordinal));
        Assert.Contains(factory.ExecutedNonQueryCommands, c => string.Equals(c, "SET STATISTICS TIME ON", StringComparison.Ordinal));
        Assert.Equal(0, result.Value.CpuTimeMs);
        Assert.Equal(0, result.Value.LogicalReads);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldApplyConfiguredCommandTimeout_ToCommand()
    {
        var options = new SqlToAiOptions { QueryExecution = new QueryExecutionOptions { CommandTimeoutSeconds = 45 } };
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig("Col1\tVal1"));
        var service = BuildService(factory, options);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(45, factory.LastConnection?.LastCommand?.CommandTimeout);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldIssueSetRowCount_WithRequestedRowLimit_BeforeMainQuery()
    {
        var options = new SqlToAiOptions { QueryExecution = new QueryExecutionOptions { DefaultRowLimit = 100, MaxRowLimit = 100 } };
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig("Col1\tVal1"));
        var service = BuildService(factory, options);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", 7, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains(factory.ExecutedNonQueryCommands, c => string.Equals(c, "SET ROWCOUNT 7", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldIssueSetRowCount_WithDefaultRowLimit_WhenNoneRequested()
    {
        var options = new SqlToAiOptions { QueryExecution = new QueryExecutionOptions { DefaultRowLimit = 42, MaxRowLimit = 100 } };
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig("Col1\tVal1"));
        var service = BuildService(factory, options);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains(factory.ExecutedNonQueryCommands, c => string.Equals(c, "SET ROWCOUNT 42", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldResetRowCountToZero_AfterMainQuery_InCorrectOrder()
    {
        var options = new SqlToAiOptions { QueryExecution = new QueryExecutionOptions { DefaultRowLimit = 5, MaxRowLimit = 100 } };
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig("Col1\tVal1"));
        var service = BuildService(factory, options);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        int setRowCountIndex = factory.ExecutedNonQueryCommands.IndexOf("SET ROWCOUNT 5");
        int resetIndex = factory.ExecutedNonQueryCommands.IndexOf("SET ROWCOUNT 0");
        Assert.True(setRowCountIndex >= 0, "SET ROWCOUNT {limit} was not issued.");
        Assert.True(resetIndex > setRowCountIndex, "SET ROWCOUNT 0 reset must come after SET ROWCOUNT {limit}.");
    }
}

