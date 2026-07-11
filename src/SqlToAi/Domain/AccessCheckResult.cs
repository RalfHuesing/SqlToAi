#nullable enable

namespace SqlToAi.Domain;

/// <summary>
/// Cache entry representing the determined access level and its expiration timestamp.
/// </summary>
public sealed record AccessCheckResult(AccessLevel Level, DateTime ExpireTime)
{
    /// <summary>
    /// Gets a value indicating whether the cached access level has expired.
    /// </summary>
    /// <param name="currentTime">The current time to check against.</param>
    /// <returns>True if expired; otherwise, false.</returns>
    public bool IsExpired(DateTime currentTime) => currentTime >= ExpireTime;
}
