#nullable enable

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Unit tests for <see cref="QueryComparisonService"/>, validating security guards, empty arguments,
/// and access-level checks for the query comparison engine. The service runs the
/// <see cref="IQuerySafetyValidator"/> pipeline twice (once for QueryA, once for QueryB) and
/// short-circuits on the first failure, so most tests here can stand in for a single
/// <see cref="FakeQuerySafetyValidator"/> whose error the service surfaces unchanged.
/// </summary>
public sealed class QueryComparisonServiceTests
{
    private static QueryComparisonService BuildService(
        bool isAllowed = true,
        AccessLevel accessLevel = AccessLevel.ReadOnly,
        SqlToAiError? error = null)
    {
        var options = new SqlToAiOptions();

        IQuerySafetyValidator safetyValidator = error != null
            ? new FakeQuerySafetyValidator(error)
            : new FakeQuerySafetyValidator(
                new FakeSecurityGuard(isAllowed),
                new FakeAccessLevelProvider(accessLevel),
                new ReadOnlyGuard());

        return new QueryComparisonService(
            new ValidationMockConnectionFactory(),
            safetyValidator,
            Options.Create(options),
            NullLogger<QueryComparisonService>.Instance);
    }

    [Fact]
    public async Task CompareQueriesAsync_EmptyDatabase_ReturnsInvalidParameters()
    {
        var service = BuildService();
        var result = await service.CompareQueriesAsync("", "SELECT 1", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_EmptyQueries_ReturnsInvalidParameters()
    {
        var service = BuildService();
        var result = await service.CompareQueriesAsync("TestDb", "", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_DatabaseNotAllowed_ReturnsSafetyCheckFailed()
    {
        var service = BuildService(isAllowed: false);
        var result = await service.CompareQueriesAsync("ForbiddenDb", "SELECT 1", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_AccessLevelNone_ReturnsWriteOperationBlocked()
    {
        var service = BuildService(accessLevel: AccessLevel.None);
        var result = await service.CompareQueriesAsync("TestDb", "SELECT 1", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_MutatingQuery_ReturnsWriteOperationBlocked()
    {
        var service = BuildService(accessLevel: AccessLevel.ReadOnly);
        var result = await service.CompareQueriesAsync("TestDb", "DROP TABLE Users", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_MultiStatement_ReturnsMultipleStatementsForbidden()
    {
        var service = BuildService(accessLevel: AccessLevel.ReadOnly);
        var result = await service.CompareQueriesAsync("TestDb", "SELECT 1; SELECT 2", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }
}

