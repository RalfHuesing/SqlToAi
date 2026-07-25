#nullable enable

using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;

namespace SqlToAi.Security;

/// <summary>
/// Enforces database security policy guardrails using static allowed and blocked patterns.
/// </summary>
public sealed class SecurityGuard : ISecurityGuard
{
    private readonly SqlToAiOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityGuard"/> class.
    /// </summary>
    /// <param name="options">The bound options containing databases configurations.</param>
    public SecurityGuard(IOptions<SqlToAiOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Checks if a database name is allowed by comparing it against configured allowed and blocked patterns.
    /// </summary>
    /// <param name="databaseName">The database name to check.</param>
    /// <returns>True if the database matches an allowed pattern and does not match any blocked pattern; otherwise, false.</returns>
    public bool IsDatabaseAllowed(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return false;
        }

        // 1. Check against Blocked list and ExcludedDatabases list
        if (IsMatchedByAnyPattern(databaseName, _options.Databases.Blocked) ||
            IsMatchedByAnyPattern(databaseName, _options.SqlServer.ExcludedDatabases))
        {
            return false;
        }

        // 2. Check against Allowed list
        return IsMatchedByAnyPattern(databaseName, _options.Databases.Allowed);
    }

    private static bool IsMatchedByAnyPattern(string databaseName, IEnumerable<string> patterns)
    {
        foreach (string pattern in patterns)
        {
            if (MatchesPattern(databaseName, pattern))
            {
                return true;
            }
        }
        return false;
    }

    internal static bool MatchesPattern(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        // Convert wildcard glob pattern (* and ?) to Regex equivalent
        string regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        try
        {
            return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
