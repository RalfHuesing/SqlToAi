#nullable enable

using SqlToAi.Domain;

namespace SqlToAi.Tests.Domain;

// @covers SqlToAi.Domain.TtlCache
public sealed class TtlCacheTests
{
    private static readonly string[] ExpectedKeysAThenB = ["A", "B"];

    [Fact]
    public async Task GetOrLoadAsync_ShouldReturnCachedValue_WhenNotExpired()
    {
        // Arrange
        var cache = new TtlCache<string, int>();
        int loaderCalls = 0;
        Func<CancellationToken, Task<int>> loader = _ =>
        {
            loaderCalls++;
            return Task.FromResult(42);
        };

        // Act
        int first = await cache.GetOrLoadAsync("key", loader, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        int second = await cache.GetOrLoadAsync("key", loader, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(42, first);
        Assert.Equal(42, second);
        Assert.Equal(1, loaderCalls);
    }

    [Fact]
    public async Task GetOrLoadAsync_ShouldReloadValue_WhenExpired()
    {
        // Arrange — short TTL plus a wait long enough for it to elapse triggers a reload.
        var cache = new TtlCache<string, int>();
        int loaderCalls = 0;
        Func<CancellationToken, Task<int>> loader = _ =>
        {
            loaderCalls++;
            return Task.FromResult(loaderCalls);
        };

        // Act
        int first = await cache.GetOrLoadAsync("key", loader, TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        int second = await cache.GetOrLoadAsync("key", loader, TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(2, loaderCalls);
    }

    [Fact]
    public async Task GetOrLoadAsync_ShouldInvokeLoaderExactlyOnce_ForUnchangedTtl()
    {
        // Arrange — three consecutive lookups for the same key within a long TTL.
        var cache = new TtlCache<string, string>();
        int loaderCalls = 0;
        Func<CancellationToken, Task<string>> loader = _ =>
        {
            loaderCalls++;
            return Task.FromResult($"v{loaderCalls}");
        };

        // Act
        string a = await cache.GetOrLoadAsync("k", loader, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        string b = await cache.GetOrLoadAsync("k", loader, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        string c = await cache.GetOrLoadAsync("k", loader, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert — the loader runs exactly once; all three reads return the same cached value.
        Assert.Equal(1, loaderCalls);
        Assert.Equal("v1", a);
        Assert.Equal("v1", b);
        Assert.Equal("v1", c);
    }

    [Fact]
    public async Task GetOrLoadAsync_ShouldInvokeLoaderPerKey_ForDistinctKeys()
    {
        // Arrange — two distinct keys must each trigger their own loader invocation; the
        // cache does not coalesce them under a shared entry.
        var cache = new TtlCache<string, int>();
        int loaderCalls = 0;
        Func<CancellationToken, Task<int>> loader = _ =>
        {
            loaderCalls++;
            return Task.FromResult(loaderCalls);
        };

        // Act
        int first = await cache.GetOrLoadAsync("a", loader, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        int second = await cache.GetOrLoadAsync("b", loader, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, loaderCalls);
        Assert.Equal(1, first);
        Assert.Equal(2, second);
    }

    [Fact]
    public async Task GetOrLoadAsync_ShouldNotShareEntriesAcrossKeys()
    {
        // Arrange — a stateful loader that records the keys it was invoked for and hands
        // out a distinct value per key. Cross-talk would surface as the wrong value for
        // one of the reads.
        var cache = new TtlCache<string, string>();
        var valuesByKey = new Dictionary<string, string>
        {
            ["A"] = "alpha",
            ["B"] = "bravo",
        };
        var requestedKeys = new List<string>();
        string currentKey = string.Empty;
        Func<CancellationToken, Task<string>> loader = _ =>
        {
            requestedKeys.Add(currentKey);
            return Task.FromResult(valuesByKey[currentKey]);
        };

        // Act — set the per-call key, then load A, then B, then A again. A's value must
        // be stable and must not be polluted by the intervening B read.
        currentKey = "A";
        string a = await cache.GetOrLoadAsync("A", loader, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        currentKey = "B";
        string b = await cache.GetOrLoadAsync("B", loader, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        currentKey = "A";
        string aAgain = await cache.GetOrLoadAsync("A", loader, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert — each key reads back exactly its own loader result, and the second read
        // of A does not invoke the loader again.
        Assert.Equal("alpha", a);
        Assert.Equal("bravo", b);
        Assert.Equal("alpha", aAgain);
        Assert.Equal(ExpectedKeysAThenB, requestedKeys);
    }
}
