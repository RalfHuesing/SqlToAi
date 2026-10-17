using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using SqlToAi.Domain;

namespace SqlToAi.Anonymization;

/// <summary>
/// Process-lifetime, in-memory implementation of <see cref="ITokenVault"/>. Registered as a
/// singleton, so it survives for the duration of the stdio MCP server process — long enough to
/// resolve a token issued in one tool call from a later tool call within the same session.
/// <para>
/// Token state is partitioned by <see cref="DatabaseTargetContext"/>: a token issued (or stored)
/// for one canonical alias plus raw database can never be resolved against another target. Every
/// token text has exactly one immutable owner scope for the vault's lifetime — minted and
/// explicitly stored alike — enforced by a shared ownership registry: a <see cref="Store"/> that
/// tries to bind a foreign-owned token to another scope throws
/// <see cref="InvalidOperationException"/> instead of silently rebinding, and minting skips IDs
/// whose text was already reserved by a <see cref="Store"/>. Token identifiers come from one
/// process-global counter (<c>long</c>, so IDs are never reused), so distinct scopes never mint
/// the same token text.
/// </para>
/// </summary>
public sealed class TokenVault : ITokenVault
{
    private readonly ConcurrentDictionary<DatabaseTargetContext, ScopeState> _scopes = new();
    private readonly ConcurrentDictionary<string, ScopeState> _tokenOwners = new(StringComparer.Ordinal);
    private long _counter;

    /// <inheritdoc/>
    public string GetOrAddToken(string value, string prefix, string suffix, DatabaseTargetContext target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return GetOrAddToken(value, prefix, suffix, GetScope(target));
    }

    /// <inheritdoc/>
    public void Store(string token, string value, DatabaseTargetContext target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Store(token, value, GetScope(target));
    }

    /// <inheritdoc/>
    public bool TryResolve(string token, DatabaseTargetContext target, [NotNullWhen(true)] out string? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        return GetScope(target).TokenToValue.TryGetValue(token, out value);
    }

    private ScopeState GetScope(DatabaseTargetContext target) =>
        _scopes.GetOrAdd(target, _ => new ScopeState());

    private void Store(string token, string value, ScopeState scope)
    {
        // GetOrAdd is atomic: under competing stores for the same text in different scopes, the
        // first caller becomes the immutable owner and every later caller is rejected. Binding a
        // foreign-owned token to another scope would break token non-resolution across targets.
        ScopeState owner = _tokenOwners.GetOrAdd(token, scope);
        if (!ReferenceEquals(owner, scope))
        {
            throw new InvalidOperationException(
                "The token is already owned by another target scope and cannot be bound to a different one.");
        }

        scope.TokenToValue[token] = value;
    }

    private string GetOrAddToken(string value, string prefix, string suffix, ScopeState scope)
    {
        // The lock keeps issuance atomic per scope: under parallel calls for the same value,
        // exactly one token is minted and mapped, so same-scope reuse holds even in a race.
        lock (scope.IssuanceLock)
        {
            if (scope.ValueToToken.TryGetValue(value, out string? existing))
            {
                return existing;
            }

            string token = MintToken(prefix, suffix, scope);
            scope.TokenToValue[token] = value;
            scope.ValueToToken[value] = token;
            return token;
        }
    }

    private string MintToken(string prefix, string suffix, ScopeState scope)
    {
        // Reserve the next identifier text atomically; text already reserved by an explicit Store
        // (owned by any scope) is skipped, so minted tokens can never collide with reserved ones.
        // The counter is 64-bit, so identifiers are never reused within the process lifetime.
        while (true)
        {
            long id = Interlocked.Increment(ref _counter);
            string token = $"{prefix}T{id}{suffix}";
            if (_tokenOwners.TryAdd(token, scope))
            {
                return token;
            }
        }
    }

    private sealed class ScopeState
    {
        public object IssuanceLock { get; } = new();
        public ConcurrentDictionary<string, string> TokenToValue { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, string> ValueToToken { get; } = new(StringComparer.Ordinal);
    }
}
