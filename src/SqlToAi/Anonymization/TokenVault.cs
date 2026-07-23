#nullable enable

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace SqlToAi.Anonymization;

/// <summary>
/// Process-lifetime, in-memory implementation of <see cref="ITokenVault"/>. Registered as a
/// singleton, so it survives for the duration of the stdio MCP server process — long enough to
/// resolve a token issued in one tool call from a later tool call within the same session.
/// </summary>
public sealed class TokenVault : ITokenVault
{
    private readonly ConcurrentDictionary<string, string> _tokenToValue = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public void Store(string token, string value) => _tokenToValue[token] = value;

    /// <inheritdoc/>
    public bool TryResolve(string token, [NotNullWhen(true)] out string? value) =>
        _tokenToValue.TryGetValue(token, out value);
}
