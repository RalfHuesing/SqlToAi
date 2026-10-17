using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Service-level tests for <see cref="QueryComparisonService.CompareQueriesAsync"/>.
/// Pins the 2-query identity of the service: pre-pipeline argument validation
/// (<see cref="QueryComparisonService.ValidateArgs"/>), short-circuit-on-first-failure
/// across the two <see cref="IQuerySafetyValidator"/> invocations, and 2-query-specific
/// branching (mutating/multi-statement in either QueryA or QueryB). The pure 6-stage
/// guardrail pipeline itself is covered end-to-end in <c>QuerySafetyValidatorTests</c>
/// (step-003 / DRY-T3); the service tests below target behaviour that the pipeline
/// tests cannot see (calling the validator twice with the right arguments, propagating
/// only the first error).
/// </summary>
public sealed class QueryComparisonServiceTests
{
    private static QueryComparisonService BuildService(
        bool isAllowed = true,
        AccessLevel accessLevel = AccessLevel.ReadOnly,
        SqlToAiError? error = null, IDatabaseConnectionFactory? factory = null)
    {
        var options = new SqlToAiOptions { QueryExecution = new QueryExecutionOptions { MaxRowLimit = 3 } };
        var dependencies = new QueryExecutionDependencies(
            factory ?? new ValidationMockConnectionFactory(),
            FakeQuerySafetyValidator.Create(isAllowed, accessLevel, error),
            Options.Create(options));

        return new QueryComparisonService(
            dependencies,
            NullLogger<QueryComparisonService>.Instance);
    }

    // ---- Pre-pipeline: empty arguments are rejected before the validator runs ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CompareQueriesAsync_EmptyDatabase_ReturnsInvalidParameters(string db)
    {
        var service = BuildService();
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(db, "SELECT 1", "SELECT 1"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_EmptyQueryA_ReturnsInvalidParameters()
    {
        var service = BuildService();
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "", "SELECT 1"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_EmptyQueryB_ReturnsInvalidParameters()
    {
        var service = BuildService();
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1", ""),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    // ---- Pipeline stages 3-4: whitelist + access level (both queries get the same probe) ----

    [Fact]
    public async Task CompareQueriesAsync_DatabaseNotAllowed_ReturnsSafetyCheckFailed()
    {
        var service = BuildService(isAllowed: false);
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1", "SELECT 2"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_AccessLevelNone_ReturnsWriteOperationBlocked()
    {
        var service = BuildService(accessLevel: AccessLevel.None);
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1", "SELECT 2"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    // ---- 2-Query-specific: Mutating / Multi-Statement in QueryA vs. QueryB ----

    [Fact]
    public async Task CompareQueriesAsync_MutatingQueryInQueryA_ReturnsError()
    {
        // ReadOnly pipeline; QueryA is mutating, QueryB is clean. Service must fail on
        // QueryA and never call the validator for QueryB (short-circuit proof).
        var service = BuildService(accessLevel: AccessLevel.ReadOnly);
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "DROP TABLE Users", "SELECT 1"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_MutatingQueryInQueryB_ReturnsError()
    {
        // ReadOnly pipeline; QueryA is clean, QueryB is mutating. Service must validate
        // QueryA (passes), then validate QueryB (fails). This proves the validator is
        // called for both queries in the expected order.
        var service = BuildService(accessLevel: AccessLevel.ReadOnly);
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1", "DROP TABLE Users"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_MultipleStatementsInQueryA_ReturnsError()
    {
        // ReadWrite pipeline (multi-statement is enforced at every access level).
        var service = BuildService(accessLevel: AccessLevel.ReadWrite);
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1; SELECT 2", "SELECT 3"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_MultipleStatementsInQueryB_ReturnsError()
    {
        var service = BuildService(accessLevel: AccessLevel.ReadWrite);
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1", "SELECT 2; SELECT 3"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    [InlineData(99, 3)]
    public async Task CompareQueriesAsync_Differences_ClampsBothSamples(int requestedLimit, int expectedRows)
    {
        var factory = new ComparisonConnectionFactory();
        var service = BuildService(factory: factory);

        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1 AS Value", "SELECT 2 AS Value",
                MaxDiffRows: requestedLimit), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.SchemaMatch);
        Assert.True(result.Value.CountMatch);
        Assert.False(result.Value.IsEqual);
        Assert.Equal(4, result.Value.RowCountA);
        Assert.Equal(4, result.Value.RowCountB);
        Assert.Equal(expectedRows, result.Value.RowsInANotInB.Split('\n').Length);
        Assert.Equal(expectedRows, result.Value.RowsInBNotInA.Split('\n').Length);
        Assert.Contains("\"Value\":1", result.Value.RowsInANotInB, StringComparison.Ordinal);
        Assert.Contains("\"Value\":101", result.Value.RowsInBNotInA, StringComparison.Ordinal);
        Assert.Equal(2, factory.DifferenceCalls);
        Assert.Equal(1, factory.Connection?.LastTransaction?.RollbackCount);
        Assert.Equal(0, factory.Connection?.LastTransaction?.CommitCount);
    }

    [Fact]
    public async Task CompareQueriesAsync_EmptyDifferencesWithZeroLimit_RemainsEqual()
    {
        var factory = new ComparisonConnectionFactory(hasDifferences: false);
        var result = await BuildService(factory: factory).CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1 AS Value", "SELECT 1 AS Value",
                MaxDiffRows: 0), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsEqual);
        Assert.Equal("[]", result.Value.RowsInANotInB);
        Assert.Equal("[]", result.Value.RowsInBNotInA);
        Assert.Equal(2, factory.DifferenceCalls);
        Assert.Equal(1, factory.Connection?.LastTransaction?.RollbackCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CompareQueriesAsync_DifferenceFailure_ReturnsErrorAndRollsBack(int failingDirection)
    {
        var factory = new ComparisonConnectionFactory(failingDirection: failingDirection,
            failure: new InvalidOperationException("Difference query failed."));
        var result = await BuildService(factory: factory).CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1 AS Value", "SELECT 2 AS Value",
                MaxDiffRows: 0), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Equal(failingDirection, factory.DifferenceCalls);
        Assert.Equal(1, factory.Connection?.LastTransaction?.RollbackCount);
        Assert.Equal(0, factory.Connection?.LastTransaction?.CommitCount);
    }

    private sealed class ComparisonConnectionFactory(
        bool hasDifferences = true, int failingDirection = 0, Exception? failure = null) : IDatabaseConnectionFactory
    {
        public FakeDbConnection? Connection { get; private set; }
        public int DifferenceCalls { get; private set; }

        public DbConnection CreateConnection(string? databaseName = null)
        {
            Connection = new FakeDbConnection(
                connection => new FakeDbCommand(connection, new FakeDbCommandHandlers(
                    ExecuteReader: command =>
                    {
                        if (!command.CommandText.Contains(" EXCEPT ", StringComparison.Ordinal))
                            return new FakeDbDataReader(["Value"], [[0]]);

                        DifferenceCalls++;
                        if (DifferenceCalls == failingDirection)
                            throw failure ?? new InvalidOperationException("Difference query failed.");

                        int offset = DifferenceCalls == 1 ? 0 : 100;
                        return new FakeDbDataReader(["Value"], hasDifferences
                            ? Enumerable.Range(1, 4).Select(value => new object?[] { value + offset }).ToArray()
                            : []);
                    },
                    ExecuteScalar: _ => 4L)),
                new FakeDbConnectionOptions(BeginTransaction: (connection, _) => new FakeDbTransaction(connection)));
            return Connection;
        }
    }
}
