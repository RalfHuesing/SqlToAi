#nullable enable

using System.Diagnostics.CodeAnalysis;

namespace SqlToAi.Anonymization;

/// <summary>
/// In-memory reverse lookup for reversible anonymization tokens produced by <see cref="Anonymizer.Tokenize"/>.
/// A token can only ever be resolved back to a value if this process previously handed that exact
/// token to the AI — an unrecognized (guessed or forged) token simply fails to resolve.
/// </summary>
public interface ITokenVault
{
    /// <summary>Remembers the real value behind a token, so it can be resolved later.</summary>
    void Store(string token, string value);

    /// <summary>Attempts to resolve a previously issued token back to its real value.</summary>
    bool TryResolve(string token, [NotNullWhen(true)] out string? value);
}
