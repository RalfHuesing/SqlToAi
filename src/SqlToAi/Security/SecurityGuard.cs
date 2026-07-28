#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityGuard"/> class.
    /// </summary>
    /// <param name="options">The bound options containing databases configurations.</param>
    public SecurityGuard(IOptions<SqlToAiOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Checks if a database name is allowed by checking global exclusions and ensuring it is listed in an access level list.
    /// </summary>
    /// <param name="databaseName">The database name to check.</param>
    /// <returns>True if the database is allowed and not globally excluded; otherwise, false.</returns>
    public bool IsDatabaseAllowed(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return false;
        }

        string trimmedName = databaseName.Trim();

        // 1. Check against global ExcludedDatabases list
        if (IsMatchedByAnyPattern(trimmedName, _options.SqlServer.ExcludedDatabases))
        {
            return false;
        }

        // 2. Check if database is contained in any of the allowed level lists (case-insensitive exact match)
        return _options.Databases.SchemaOnly.Any(db => string.Equals(db, trimmedName, StringComparison.OrdinalIgnoreCase))
            || _options.Databases.ReadOnlyAnonymized.Any(db => string.Equals(db, trimmedName, StringComparison.OrdinalIgnoreCase))
            || _options.Databases.ReadOnly.Any(db => string.Equals(db, trimmedName, StringComparison.OrdinalIgnoreCase))
            || _options.Databases.ReadWrite.Any(db => string.Equals(db, trimmedName, StringComparison.OrdinalIgnoreCase));
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
