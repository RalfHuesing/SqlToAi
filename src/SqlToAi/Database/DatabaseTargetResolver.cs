using SqlToAi.Configuration;
using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>Resolves qualified target aliases against the configured named servers.</summary>
public sealed class DatabaseTargetResolver
{
    private readonly IReadOnlyDictionary<string, NamedServerOptions> _serversByAlias;

    public DatabaseTargetResolver(SqlToAiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var serversByAlias = new Dictionary<string, NamedServerOptions>(StringComparer.Ordinal);
        foreach ((string configuredAlias, NamedServerOptions serverOptions) in options.Servers)
        {
            if (!DatabaseTargetParser.IsValidAlias(configuredAlias))
            {
                throw new ArgumentException(
                    $"Configured server alias '{configuredAlias}' must match [A-Za-z][A-Za-z0-9_-]*.",
                    nameof(options));
            }

            string canonicalAlias = DatabaseTargetParser.CanonicalizeAlias(configuredAlias);
            if (!serversByAlias.TryAdd(canonicalAlias, serverOptions))
            {
                throw new ArgumentException(
                    $"Configured server aliases must be unique without regard to case: '{configuredAlias}'.",
                    nameof(options));
            }
        }

        _serversByAlias = serversByAlias;
    }

    public Result<DatabaseTargetContext> Resolve(string? target)
    {
        Result<ParsedDatabaseTarget> parsed = DatabaseTargetParser.Parse(target);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        return Resolve(parsed.Value);
    }

    public Result<DatabaseTargetContext> Resolve(ParsedDatabaseTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!_serversByAlias.ContainsKey(target.ServerAlias))
        {
            return SqlToAiError.InvalidParameters($"unknown server alias '{target.ServerAlias}' in database target.");
        }

        return new DatabaseTargetContext(target.ServerAlias, target.DatabaseName);
    }

    public bool TryGetServerOptions(string serverAlias, out NamedServerOptions? serverOptions)
    {
        string canonicalAlias = DatabaseTargetParser.CanonicalizeAlias(serverAlias);
        return _serversByAlias.TryGetValue(canonicalAlias, out serverOptions);
    }

    public Result<string> ResolveServerAlias(string? serverAlias)
    {
        if (!DatabaseTargetParser.IsValidAlias(serverAlias))
        {
            return SqlToAiError.InvalidParameters("server must be a configured alias matching [A-Za-z][A-Za-z0-9_-]*.");
        }

        string canonicalAlias = DatabaseTargetParser.CanonicalizeAlias(serverAlias!);
        return _serversByAlias.ContainsKey(canonicalAlias)
            ? canonicalAlias
            : SqlToAiError.InvalidParameters($"unknown server alias '{canonicalAlias}'.");
    }

    public IReadOnlyList<string> ConfiguredServerAliases => _serversByAlias.Keys.OrderBy(a => a, StringComparer.Ordinal).ToList();
}
