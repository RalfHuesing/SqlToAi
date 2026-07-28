#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Security;

/// <summary>
/// Implements in-memory database access level determination based on configured level lists.
/// </summary>
public sealed class AccessLevelProvider : IAccessLevelProvider
{
    private readonly SqlToAiOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="AccessLevelProvider"/> class.
    /// </summary>
    /// <param name="options">Options containing database access level configurations.</param>
    public AccessLevelProvider(IOptions<SqlToAiOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Legacy constructor for backward compatibility with connection factory and logger DI setups.
    /// </summary>
    public AccessLevelProvider(
        IDatabaseConnectionFactory connectionFactory,
        IOptions<SqlToAiOptions> options,
        ILogger<AccessLevelProvider> logger)
        : this(options)
    {
    }

    /// <summary>
    /// Retrieves the access level allowed for the specified database by evaluating configured level lists.
    /// Fail-safe conflict resolution: SchemaOnly > ReadOnlyAnonymized > ReadOnly > ReadWrite.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved <see cref="AccessLevel"/>.</returns>
    public Task<AccessLevel> GetAccessLevelAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return Task.FromResult(AccessLevel.None);
        }

        string trimmedName = databaseName.Trim();

        // Fail-Safe Whitelisting: SchemaOnly > ReadOnlyAnonymized > ReadOnly > ReadWrite
        if (_options.Databases.SchemaOnly.Any(db => string.Equals(db, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(AccessLevel.SchemaOnly);
        }

        if (_options.Databases.ReadOnlyAnonymized.Any(db => string.Equals(db, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(AccessLevel.ReadOnlyAnonymized);
        }

        if (_options.Databases.ReadOnly.Any(db => string.Equals(db, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(AccessLevel.ReadOnly);
        }

        if (_options.Databases.ReadWrite.Any(db => string.Equals(db, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(AccessLevel.ReadWrite);
        }

        return Task.FromResult(AccessLevel.None);
    }
}
