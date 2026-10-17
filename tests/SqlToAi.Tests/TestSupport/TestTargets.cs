using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Tests.TestSupport;

/// <summary>
/// Helpers for qualified database targets in tests: configures named servers on options
/// instances and resolves qualified target strings to immutable contexts.
/// </summary>
internal static class TestTargets
{
    public const string ServerAlias = "test";

    public const string AlternateServerAlias = "prod";

    /// <summary>Adds named server entries so target resolution succeeds with these aliases.</summary>
    public static SqlToAiOptions WithNamedServers(this SqlToAiOptions options, params string[] aliases)
    {
        foreach (string alias in aliases)
        {
            // The endpoint address is irrelevant to offline tests: mock factories never dial it.
            options.Servers[alias] = new NamedServerOptions
            {
                Server = ".\\SQLTEST",
                IntegratedSecurity = true,
                MetadataProvider = new MetadataProviderOptions { Enabled = true },
                AnonymizationRules = new AnonymizationRulesOptions { Enabled = true }
            };
        }
        return options;
    }

    /// <summary>Resolves a qualified target string through the production resolver.</summary>
    public static DatabaseTargetContext ResolveTarget(SqlToAiOptions options, string qualifiedTarget)
    {
        Result<DatabaseTargetContext> resolved = new DatabaseTargetResolver(options).Resolve(qualifiedTarget);
        return resolved.IsSuccess
            ? resolved.Value
            : throw new InvalidOperationException($"Test target '{qualifiedTarget}' did not resolve: {resolved.Error.Message}");
    }
}
