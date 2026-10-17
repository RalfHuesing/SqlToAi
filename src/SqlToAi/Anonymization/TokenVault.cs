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
/// for one canonical alias plus raw database can never be resolved against another target, and
/// an explicit <see cref="Store"/> writes only into the scope it names. Token identifiers come
/// from one process-global counter, so distinct scopes never mint the same token text.
/// </para>
/// </summary>
public sealed class TokenVault : ITokenVault
{
    // Legacy, unscoped state kept until all consumers are migrated (M2.3/M4.1 remove it).
    private readonly ScopeState _legacyScope = new();
    private readonly ConcurrentDictionary<DatabaseTargetContext, ScopeState> _scopes = new();
    private int _counter;

    /// <inheritdoc/>
    public string GetOrAddToken(string value, string prefix, string suffix) =>
        GetOrAddToken(value, prefix, suffix, _legacyScope);

    /// <inheritdoc/>
    public string GetOrAddToken(string value, string prefix, string suffix, DatabaseTargetContext target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return GetOrAddToken(value, prefix, suffix, GetScope(target));
    }

    /// <inheritdoc/>
    public void Store(string token, string value) => _legacyScope.TokenToValue[token] = value;

    /// <inheritdoc/>
    public void Store(string token, string value, DatabaseTargetContext target)
    {
        ArgumentNullException.ThrowIfNull(target);
        GetScope(target).TokenToValue[token] = value;
    }

    /// <inheritdoc/>
    public bool TryResolve(string token, [NotNullWhen(true)] out string? value) =>
        _legacyScope.TokenToValue.TryGetValue(token, out value);

    /// <inheritdoc/>
    public bool TryResolve(string token, DatabaseTargetContext target, [NotNullWhen(true)] out string? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        return GetScope(target).TokenToValue.TryGetValue(token, out value);
    }

    private ScopeState GetScope(DatabaseTargetContext target) =>
        _scopes.GetOrAdd(target, _ => new ScopeState());

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

            int id = Interlocked.Increment(ref _counter);
            string token = $"{prefix}T{id}{suffix}";
            scope.TokenToValue[token] = value;
            scope.ValueToToken[value] = token;
            return token;
        }
    }

    private sealed class ScopeState
    {
        public object IssuanceLock { get; } = new();
        public ConcurrentDictionary<string, string> TokenToValue { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, string> ValueToToken { get; } = new(StringComparer.Ordinal);
    }
}
