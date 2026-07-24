#nullable enable

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
        // The validation service deliberately does NOT block mutating statements — it only
        // checks syntax via PARSEONLY. The read-only guard is enforced at execution time
        // (QueryExecutionService). A well-formed DROP is a valid SQL statement.
        var result = await _fx.QueryValidationService.ValidateQueryAsync(
            _db,
            "DROP TABLE dbo.SomeTable",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
    }

    [Fact]
    public async Task ValidateQueryAsync_ShouldBlock_SpExecuteSqlEmbeddedCommitExploit_BeforeTouchingParseonly()
    {
        // Same exploit string as QueryExecutionServiceIntegrationTests' equivalent test (audit
        // finding 2), sent through sql_validate_query instead: sp_executesql with an embedded
        // COMMIT, against a database forced to a non-ReadWrite AccessLevel via a fake provider
        // while still using the real 'Agent' SQL login from appsettings.json. Proves the
        // read-only guard added for audit finding 4 rejects this before the query ever reaches
        // SET PARSEONLY, closing the gap where this tool's safety previously rested solely on
        // unverified PARSEONLY semantics.
        var customAccessProvider = new FakeAccessLevelProvider(AccessLevel.ReadOnly);
        var service = new QueryValidationService(
            _fx.ConnectionFactory,
            _fx.SecurityGuard,
            customAccessProvider,
            _fx.ReadOnlyGuard,
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
