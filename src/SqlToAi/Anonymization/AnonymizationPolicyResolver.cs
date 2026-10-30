using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Anonymization;

/// <summary>
/// Implements <see cref="IAnonymizationPolicyResolver"/> by composing database access level requirements,
/// global configuration, and the central rule exclusions that <see cref="Anonymizer"/> and
/// <see cref="Database.QueryExecutionService"/> apply at query time, so schema tools can annotate
/// columns identically before a query is ever run.
/// </summary>
public sealed class AnonymizationPolicyResolver : IAnonymizationPolicyResolver
{
    private readonly SqlToAiOptions _options;
    private readonly IAnonymizationRuleProvider _ruleProvider;
    private readonly IAccessLevelProvider _accessLevelProvider;

    /// <summary>Initializes a new instance of the <see cref="AnonymizationPolicyResolver"/> class.</summary>
    public AnonymizationPolicyResolver(
        IOptions<SqlToAiOptions> options,
        IAnonymizationRuleProvider ruleProvider,
        IAccessLevelProvider accessLevelProvider)
    {
        _options = options.Value;
        _ruleProvider = ruleProvider;
        _accessLevelProvider = accessLevelProvider;
    }

    /// <summary>Initializes a new instance of the <see cref="AnonymizationPolicyResolver"/> class.</summary>
    public AnonymizationPolicyResolver(
        IOptions<SqlToAiOptions> options,
        IAnonymizationRuleProvider ruleProvider)
        : this(options, ruleProvider, new AccessLevelProvider(options))
    {
    }

    /// <inheritdoc/>
    public async Task<bool> WillAnonymizeAsync(DatabaseTargetContext target, string tableName, string columnName, CancellationToken cancellationToken = default)
    {
        if (!_options.Anonymizer.Enabled)
        {
            return false;
        }

        var accessLevel = await _accessLevelProvider.GetTargetAccessLevelAsync(target, cancellationToken);
        if (!accessLevel.RequiresAnonymization())
        {
            return false;
        }

        if (await _ruleProvider.IsExcludedAsync(target, string.Empty, tableName, columnName, cancellationToken))
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> WillAnonymizeAsync(DatabaseTargetContext target, SchemaObjectIdentity identity, string columnName, CancellationToken cancellationToken = default)
    {
        if (!_options.Anonymizer.Enabled)
        {
            return false;
        }

        var accessLevel = await _accessLevelProvider.GetTargetAccessLevelAsync(target, cancellationToken);
        if (!accessLevel.RequiresAnonymization())
        {
            return false;
        }

        return !await _ruleProvider.IsExcludedAsync(
            target, identity.SchemaName, identity.ObjectName, columnName, cancellationToken);
    }

    /// <inheritdoc/>
    public bool IsTokenizationActive => _options.Anonymizer.Tokenization.IsUsable;
}
