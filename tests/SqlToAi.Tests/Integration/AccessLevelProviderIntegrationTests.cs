using SqlToAi.Domain;
using Xunit;

namespace SqlToAi.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(SqlServerCollectionFixture.Name)]
public sealed class AccessLevelProviderIntegrationTests
{
    private readonly SqlServerFixture _fx;
    private readonly string _server;
    private readonly string _db;

    public AccessLevelProviderIntegrationTests(SqlServerFixture fx)
    {
        _fx = fx;
        _server = TestTargets.ServerAlias;
        _db = TestConstants.DatabaseName;
    }

    [Fact]
    public async Task GetTargetAccessLevelAsync_ShouldReturnConfiguredLevel_ForAllowedDatabase()
    {
        var level = await _fx.AccessLevelProvider.GetTargetAccessLevelAsync(new DatabaseTargetContext(_server, _db), TestContext.Current.CancellationToken);
        Assert.NotEqual(AccessLevel.None, level);
    }

    [Fact]
    public async Task GetTargetAccessLevelAsync_ShouldReturnNone_ForUnconfiguredDatabase()
    {
        var level = await _fx.AccessLevelProvider.GetTargetAccessLevelAsync(new DatabaseTargetContext(_server, "UnknownDatabase999"), TestContext.Current.CancellationToken);
        Assert.Equal(AccessLevel.None, level);
    }

    [Fact]
    public async Task GetTargetAccessLevelAsync_ShouldReturnNone_ForEmptyDatabaseName()
    {
        var level = await _fx.AccessLevelProvider.GetTargetAccessLevelAsync(new DatabaseTargetContext(_server, ""), TestContext.Current.CancellationToken);
        Assert.Equal(AccessLevel.None, level);
    }
}
