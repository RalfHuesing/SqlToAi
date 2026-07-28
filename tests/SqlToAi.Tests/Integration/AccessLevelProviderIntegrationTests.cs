#nullable enable

using SqlToAi.Domain;
using Xunit;

namespace SqlToAi.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(SqlServerCollectionFixture.Name)]
public sealed class AccessLevelProviderIntegrationTests
{
    private readonly SqlServerFixture _fx;
    private readonly string _db;

    public AccessLevelProviderIntegrationTests(SqlServerFixture fx)
    {
        _fx = fx;
        _db = TestConstants.DatabaseName;
    }

    [Fact]
    public async Task GetAccessLevelAsync_ShouldReturnConfiguredLevel_ForAllowedDatabase()
    {
        var level = await _fx.AccessLevelProvider.GetAccessLevelAsync(_db, TestContext.Current.CancellationToken);
        Assert.NotEqual(AccessLevel.None, level);
    }

    [Fact]
    public async Task GetAccessLevelAsync_ShouldReturnNone_ForUnconfiguredDatabase()
    {
        var level = await _fx.AccessLevelProvider.GetAccessLevelAsync("UnknownDatabase999", TestContext.Current.CancellationToken);
        Assert.Equal(AccessLevel.None, level);
    }

    [Fact]
    public async Task GetAccessLevelAsync_ShouldReturnNone_ForEmptyDatabaseName()
    {
        var level = await _fx.AccessLevelProvider.GetAccessLevelAsync("", TestContext.Current.CancellationToken);
        Assert.Equal(AccessLevel.None, level);
    }
}
