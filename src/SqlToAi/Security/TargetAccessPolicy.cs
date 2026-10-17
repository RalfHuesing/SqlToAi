using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Security;

internal sealed class TargetAccessPolicy(SqlToAiOptions options)
{
    private readonly DatabaseTargetResolver _targets = new(options);

    internal AccessLevel GetAccessLevel(DatabaseTargetContext target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!_targets.TryGetServerOptions(target.ServerAlias, out var server)
            || server is null
            || server.ExcludedDatabases.Any(pattern => GlobMatcher.IsMatch(target.DatabaseName, pattern)))
        {
            return AccessLevel.None;
        }

        // Evaluate the most restrictive list first; database characters are never trimmed.
        if (Matches(options.Databases.SchemaOnly, target)) return AccessLevel.SchemaOnly;
        if (Matches(options.Databases.ReadOnlyAnonymized, target)) return AccessLevel.ReadOnlyAnonymized;
        if (Matches(options.Databases.ReadOnly, target)) return AccessLevel.ReadOnly;
        if (Matches(options.Databases.ReadWrite, target)) return AccessLevel.ReadWrite;
        return AccessLevel.None;
    }

    private static bool Matches(IEnumerable<string> grants, DatabaseTargetContext target) =>
        grants.Any(grant =>
        {
            var parsed = DatabaseTargetParser.Parse(grant);
            return parsed.IsSuccess
                && string.Equals(parsed.Value.ServerAlias, target.ServerAlias, StringComparison.Ordinal)
                && string.Equals(parsed.Value.DatabaseName, target.DatabaseName, StringComparison.OrdinalIgnoreCase);
        });
}
