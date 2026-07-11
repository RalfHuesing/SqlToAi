#nullable enable

namespace SqlToAi.Security;

/// <summary>
/// Verifies if a SQL query is read-only safe and free of mutating operations.
/// </summary>
public interface IReadOnlyGuard
{
    /// <summary>
    /// Checks if the query contains only safe statements (e.g. SELECT) and no mutating keywords.
    /// </summary>
    /// <param name="query">The SQL query string to evaluate.</param>
    /// <returns>True if the query is determined to be read-only safe; otherwise, false.</returns>
    bool IsQuerySafe(string query);
}
