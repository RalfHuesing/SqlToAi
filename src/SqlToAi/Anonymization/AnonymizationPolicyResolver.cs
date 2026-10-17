using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Anonymization;

/// <summary>
/// Implements <see cref="IAnonymizationPolicyResolver"/> by composing the same exclusion
/// sources <see cref="Anonymizer"/> and <see cref="Database.QueryExecutionService"/> apply at
/// query time, so schema tools can annotate columns identically before a query is ever run.
/// </summary>
public sealed class AnonymizationPolicyResolver : IAnonymizationPolicyResolver
{
    private readonly SqlToAiOptions _options;
    private readonly IAnonymizationRuleProvider _ruleProvider;

    /// <summary>Initializes a new instance of the <see cref="AnonymizationPolicyResolver"/> class.</summary>
    public AnonymizationPolicyResolver(
        IOptions<SqlToAiOptions> options,
        IAnonymizationRuleProvider ruleProvider)
    {
        _options = options.Value;
        _ruleProvider = ruleProvider;
    }

    /// <inheritdoc/>
    public async Task<bool> WillAnonymizeAsync(string databaseName, string tableName, string columnName, CancellationToken cancellationToken = default)
    {
        if (!_options.Anonymizer.Enabled)
        {
            return false;
        }

        if (await _ruleProvider.IsExcludedAsync(databaseName, string.Empty, tableName, columnName, cancellationToken))
        {
            return false;
        }

        return true;
    }

    public async Task<bool> WillAnonymizeAsync(string databaseName, SchemaObjectIdentity identity, string columnName, CancellationToken cancellationToken = default)
        => _options.Anonymizer.Enabled && !await _ruleProvider.IsExcludedAsync(
            databaseName, identity.SchemaName, identity.ObjectName, columnName, cancellationToken);

    /// <inheritdoc/>
    public async Task<bool> WillAnonymizeAsync(DatabaseTargetContext target, string tableName, string columnName, CancellationToken cancellationToken = default)
    {
        if (!_options.Anonymizer.Enabled)
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
        => _options.Anonymizer.Enabled && !await _ruleProvider.IsExcludedAsync(
            target, identity.SchemaName, identity.ObjectName, columnName, cancellationToken);

    /// <inheritdoc/>
    public bool IsTokenizationActive => _options.Anonymizer.Tokenization.IsUsable;
}
