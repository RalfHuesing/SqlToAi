using SqlToAi.Domain;

namespace SqlToAi.Security;

/// <summary>
/// Provider for dynamically checking and caching the access level of a target database.
/// </summary>
public interface IAccessLevelProvider
{
    Task<AccessLevel> GetTargetAccessLevelAsync(DatabaseTargetContext target, CancellationToken cancellationToken = default);
}
