using System.Diagnostics.CodeAnalysis;
using SqlToAi.Domain;

namespace SqlToAi.Anonymization;

/// <summary>
/// In-memory reverse lookup for reversible anonymization tokens produced by <see cref="Anonymizer.Tokenize"/>.
/// A token can only ever be resolved back to a value if this process previously handed that exact
/// token to the AI — an unrecognized (guessed or forged) token simply fails to resolve.
/// <para>
/// The context-aware members scope token state by canonical server alias plus exact raw database
/// name (see <see cref="DatabaseTargetContext"/>): identical plaintext in different targets yields
/// different tokens, and a token from one target cannot be resolved through another. The unscoped
/// members are legacy state kept until all consumers are migrated.
/// </para>
/// </summary>
public interface ITokenVault
{
    /// <summary>
    /// Returns an existing token for the specified <paramref name="value"/> if already generated,
    /// or creates and stores a new short token (e.g. <c>§§§T1§§§</c>) bi-directionally.
    /// </summary>
    string GetOrAddToken(string value, string prefix, string suffix);

    /// <summary>Remembers the real value behind a token, so it can be resolved later.</summary>
    void Store(string token, string value);

    /// <summary>Attempts to resolve a previously issued token back to its real value.</summary>
    bool TryResolve(string token, [NotNullWhen(true)] out string? value);

    /// <summary>
    /// Returns an existing token for <paramref name="value"/> within <paramref name="target"/>'s
    /// scope, or creates and stores a new short token bi-directionally. The same plaintext in a
    /// different target gets a different token that cannot be resolved through this one.
    /// </summary>
    string GetOrAddToken(string value, string prefix, string suffix, DatabaseTargetContext target);

    /// <summary>
    /// Remembers the real value behind a token within <paramref name="target"/>'s scope only; it
    /// never overwrites a token owned by another target.
    /// </summary>
    void Store(string token, string value, DatabaseTargetContext target);

    /// <summary>
    /// Attempts to resolve a token back to its real value within <paramref name="target"/>'s scope.
    /// A token issued or stored for a different target fails to resolve.
    /// </summary>
    bool TryResolve(string token, DatabaseTargetContext target, [NotNullWhen(true)] out string? value);
}
