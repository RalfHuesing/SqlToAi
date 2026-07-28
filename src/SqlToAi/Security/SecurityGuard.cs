#nullable enable

using System.Collections.Generic;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;

namespace SqlToAi.Security;

/// <summary>
/// Enforces database security policy guardrails using configured database access levels and excluded database patterns.
/// </summary>
public sealed class SecurityGuard : ISecurityGuard
{
    private readonly SqlToAiOptions _options;
    private readonly IAccessLevelProvider _accessLevelProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityGuard"/> class with an explicit access level provider.
    /// </summary>
    /// <param name="options">The bound options containing databases configurations.</param>
    /// <param name="accessLevelProvider">The access level provider.</param>
    public SecurityGuard(IOptions<SqlToAiOptions> options, IAccessLevelProvider accessLevelProvider)
    {
        _options = options.Value;
        _accessLevelProvider = accessLevelProvider;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityGuard"/> class.
    /// </summary>
    /// <param name="options">The bound options containing databases configurations.</param>
    public SecurityGuard(IOptions<SqlToAiOptions> options)
        : this(options, new AccessLevelProvider(options))
    {
    }

    /// <summary>
    /// Checks if a database name is allowed by checking global exclusions and ensuring it has a configured AccessLevel != None.
    /// </summary>
    /// <param name="databaseName">The database name to check.</param>
    /// <returns>True if the database is allowed and not globally excluded; otherwise, false.</returns>
    public bool IsDatabaseAllowed(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return false;
        }

        // 1. Check against global ExcludedDatabases list
        if (IsMatchedByAnyPattern(databaseName, _options.SqlServer.ExcludedDatabases))
        {
            return false;
        }

        // 2. Check if AccessLevel is anything other than None
        AccessLevel level = _accessLevelProvider.GetAccessLevelAsync(databaseName).GetAwaiter().GetResult();
        return level != AccessLevel.None;
    }

    private static bool IsMatchedByAnyPattern(string databaseName, IEnumerable<string> patterns)
    {
        foreach (string pattern in patterns)
        {
            if (GlobMatcher.IsMatch(databaseName, pattern))
            {
                return true;
            }
        }
        return false;
    }
}
