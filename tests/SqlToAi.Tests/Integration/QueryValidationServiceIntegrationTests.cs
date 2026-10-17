using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(SqlServerCollectionFixture.Name)]
public sealed class QueryValidationServiceIntegrationTests
{
    private readonly SqlServerFixture _fx;
    private readonly string _db;

    public QueryValidationServiceIntegrationTests(SqlServerFixture fx)
    {
        _fx = fx;
        _db = TestConstants.DatabaseName;
    }

    [Fact]
    public async Task ValidateQueryAsync_RejectsNoexecBypassForReadWrite()
    {
        var result = await _fx.QueryValidationService.ValidateQueryAsync(
            _db, "SET NOEXEC OFF; THROW 51000, 'fictional-validation-executed', 1;",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
        Assert.DoesNotContain("fictional-validation-executed", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateQueryAsync_ShouldSucceed_ForValidQuery()
    {
        var result = await _fx.QueryValidationService.ValidateQueryAsync(_db, "SELECT 1 AS One", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
    }

    [Fact]
    public async Task ValidateQueryAsync_ShouldSucceed_ForValidQueryAgainstKnownTable()
    {
        var result = await _fx.QueryValidationService.ValidateQueryAsync(
            _db,
            "SELECT TOP 1 * FROM dbo.FakeProjects",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
    }

    [Fact]
    public async Task ValidateQueryAsync_ShouldFail_ForInvalidSyntax()
    {
        var result = await _fx.QueryValidationService.ValidateQueryAsync(
            _db,
            "SELECT FROM",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
    }

    [Fact]
    public async Task ValidateQueryAsync_ShouldSucceed_ForSyntacticallyValidMutation()
    {
        // ReadWrite permits DDL validation; NOEXEC compiles it without executing the DROP.
        var result = await _fx.QueryValidationService.ValidateQueryAsync(
            _db,
            "DROP TABLE dbo.SomeTable",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
    }

    [Fact]
    public async Task ValidateQueryAsync_ShouldBlock_SpExecuteSqlEmbeddedCommitExploit_BeforeTouchingNoexec()
    {
        // A forced read-only access level must reject opaque EXEC before validation starts.
        var customAccessProvider = new FakeAccessLevelProvider(AccessLevel.ReadOnly);
        var customValidator = new QuerySafetyValidator(_fx.SecurityGuard, customAccessProvider, _fx.ReadOnlyGuard);
        var service = new QueryValidationService(
            _fx.ConnectionFactory,
            customValidator,
            Microsoft.Extensions.Options.Options.Create(_fx.Options),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryValidationService>.Instance);

        var result = await service.ValidateQueryAsync(
            _db,
            "sp_executesql N'DELETE FROM dbo.FakeProjects; COMMIT'",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure, IntegrationAssertions.FormatFailure(result));
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    private sealed class FakeAccessLevelProvider(AccessLevel level) : IAccessLevelProvider
    {
        public Task<AccessLevel> GetAccessLevelAsync(string databaseName, CancellationToken cancellationToken = default)
            => Task.FromResult(level);
    }
}

