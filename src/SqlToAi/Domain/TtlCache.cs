#nullable enable

using System.Collections.Concurrent;

namespace SqlToAi.Domain;

/// <summary>
/// Thread-safe, lazy-reloading cache with a per-entry time-to-live. Each key maps to exactly one
/// (value, absolute-expiry) pair; expired entries are reloaded on next access via the supplied
/// <c>loader</c>. The cache is a pure in-memory helper — no eviction, no size limit, no background
/// refresh. It exists to deduplicate the <c>ConcurrentDictionary + IsExpired + Reload</c> pattern
/// that several long-lived providers (e.g. <c>AccessLevelProvider</c>,
/// <c>AnonymizationRuleProvider</c>) otherwise had to re-implement identically.
/// </summary>
/// <remarks>
/// Concurrency: lock-free, <see cref="ConcurrentDictionary{TKey, TValue}"/> with
/// <c>TryGetValue</c> + indexer-set. A race between two concurrent expirations may invoke
/// <paramref name="loader"/> more than once for the same key — accepted, identical to the
/// pre-extraction behavior in the call sites.
/// </remarks>
/// <typeparam name="TKey">Cache key type. Constrained to <c>notnull</c> because
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> rejects null keys.</typeparam>
/// <typeparam name="TValue">Cached value type. Unconstrained — supports both reference types
/// and value types.</typeparam>
internal sealed class TtlCache<TKey, TValue> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Entry> _entries = new();

    /// <summary>
    /// Returns the cached value for <paramref name="key"/> if present and unexpired;
    /// otherwise invokes <paramref name="loader"/>, stores the result with an absolute
    /// expiry of <c>UtcNow + ttl</c>, and returns it. Cancellation propagates into the
    /// loader.
    /// </summary>
    /// <param name="key">Cache key. Must be non-null.</param>
    /// <param name="loader">Async factory invoked on miss or expiry. Cancellation token
    /// passed through is the one supplied to this call (or <see cref="CancellationToken.None"/>
    /// when the caller omits it).</param>
    /// <param name="ttl">Time-to-live applied to the freshly loaded value. A value of
    /// <see cref="TimeSpan.Zero"/> is allowed and causes the next call to re-load
    /// (instant expiry) — useful for tests; configuration-level validation of <c>0</c> is
    /// out of scope for this helper.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TValue> GetOrLoadAsync(
        TKey key,
        Func<CancellationToken, Task<TValue>> loader,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        if (_entries.TryGetValue(key, out var cached) && !cached.IsExpired(now))
        {
            return cached.Value;
        }

        var value = await loader(cancellationToken);
        _entries[key] = new Entry(value, now.Add(ttl));
        return value;
    }

    private sealed record Entry(TValue Value, DateTime ExpireTime)
    {
        public bool IsExpired(DateTime now) => now >= ExpireTime;
    }
}
