#nullable enable

using SqlToAi.Domain;

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
}
