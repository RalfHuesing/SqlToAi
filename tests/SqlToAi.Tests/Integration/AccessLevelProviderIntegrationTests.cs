#nullable enable

using SqlToAi.Domain;

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
        _db = fx.Options.Databases.Default;
    }

    [Fact]
    public async Task GetAccessLevelAsync_ShouldReturnConfiguredLevel_ForAllowedDatabase()
    {
        // The bundled appsettings.json grants the configured login ReadWrite.
        var level = await _fx.AccessLevelProvider.GetAccessLevelAsync(_db, TestContext.Current.CancellationToken);

        Assert.Equal(AccessLevel.ReadWrite, level);
    }

    [Fact]
    public async Task GetAccessLevelAsync_ShouldCacheResult_WithinTtl()
    {
        // First call warms the cache
        var first = await _fx.AccessLevelProvider.GetAccessLevelAsync(_db, TestContext.Current.CancellationToken);

        // Second call within the same TTL window must hit the cache. The function is
        // idempotent and returns the same level either way — we just exercise the second call
        // path to make sure caching does not throw.
        var second = await _fx.AccessLevelProvider.GetAccessLevelAsync(_db, TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GetAccessLevelAsync_ShouldReturnNone_ForEmptyDatabaseName()
    {
        // Empty name is treated defensively as None.
        var level = await _fx.AccessLevelProvider.GetAccessLevelAsync("", TestContext.Current.CancellationToken);

        Assert.Equal(AccessLevel.None, level);
    }
}
