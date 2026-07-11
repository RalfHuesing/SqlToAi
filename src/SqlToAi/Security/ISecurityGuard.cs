#nullable enable

namespace SqlToAi.Security;

/// <summary>
/// Enforces database security policy guardrails.
/// </summary>
public interface ISecurityGuard
{
    /// <summary>
    /// Checks if a database name is allowed by checking it against configured allowed and blocked patterns.
    /// </summary>
    /// <param name="databaseName">The database name to check.</param>
    /// <returns>True if allowed; otherwise, false.</returns>
    bool IsDatabaseAllowed(string databaseName);
}
