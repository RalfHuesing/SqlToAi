#nullable enable

namespace SqlToAi.Database;

/// <summary>
/// Resolves previously issued anonymization tokens back into their real values inside a SQL query,
/// so a query built entirely from tokens the AI already received (e.g. from a prior anonymized
/// result) can still match real rows — without the AI ever learning the value itself.
/// </summary>
public interface IQueryTokenResolver
{
    /// <summary>
    /// Returns <paramref name="query"/> with every recognized, resolvable token inside a string
    /// literal replaced by its real value. Tokens the vault does not recognize are left untouched.
    /// </summary>
    string ResolveTokens(string query);
}
