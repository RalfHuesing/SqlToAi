#nullable enable

using SqlToAi.Domain;

namespace SqlToAi.Security;

/// <summary>
/// Provider for dynamically checking and caching the access level of a target database.
/// </summary>
public interface IAccessLevelProvider
{
    /// <summary>
    /// Gets the access level allowed for the specified database, utilizing cache when valid.
    /// </summary>
    /// <param name="databaseName">The target database name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The determined <see cref="AccessLevel"/>.</returns>
    Task<AccessLevel> GetAccessLevelAsync(string databaseName, CancellationToken cancellationToken = default);
}
