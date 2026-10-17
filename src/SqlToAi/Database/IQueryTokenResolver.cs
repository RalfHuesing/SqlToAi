using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Resolves previously issued anonymization tokens back into their real values inside a SQL query,
/// so a query built entirely from tokens the AI already received (e.g. from a prior anonymized
/// result) can still match real rows — without the AI ever learning the value itself.
/// </summary>
public interface IQueryTokenResolver
{
    /// <summary>
    /// Returns <paramref name="query"/> with every token resolvable within
    /// <paramref name="target"/>'s scope replaced by its real value. Tokens issued or stored for
    /// another target — and unrecognized tokens — stay literal SQL text.
    /// </summary>
    string ResolveTokens(string query, DatabaseTargetContext target);
}
