#nullable enable

using Microsoft.Extensions.Options;
using SqlToAi.Configuration;

namespace SqlToAi.Anonymization;

/// <summary>
/// Implements <see cref="IAnonymizationPolicyResolver"/> by composing the same exclusion
/// sources <see cref="Anonymizer"/> and <see cref="Database.QueryExecutionService"/> apply at
/// query time, so schema tools can annotate columns identically before a query is ever run.
/// </summary>
public sealed class AnonymizationPolicyResolver : IAnonymizationPolicyResolver
{
    private readonly SqlToAiOptions _options;
    private readonly IAnonymizerExclusionProvider _exclusionProvider;
    private readonly IAnonymizationRuleProvider _ruleProvider;

    /// <summary>Initializes a new instance of the <see cref="AnonymizationPolicyResolver"/> class.</summary>
    public AnonymizationPolicyResolver(
        IOptions<SqlToAiOptions> options,
        IAnonymizerExclusionProvider exclusionProvider,
        IAnonymizationRuleProvider ruleProvider)
    {
        _options = options.Value;
        _exclusionProvider = exclusionProvider;
        _ruleProvider = ruleProvider;
    }

    /// <inheritdoc/>
    public async Task<bool> WillAnonymizeAsync(string databaseName, string tableName, string columnName, CancellationToken cancellationToken = default)
    {
        if (!_options.Anonymizer.Enabled)
        {
            return false;
        }

        foreach (string excludedPattern in _options.Anonymizer.ExcludedColumns)
        {
            if (GlobPatternMatcher.IsMatch(columnName, excludedPattern))
            {
                return false;
            }
        }

        var legacyExclusions = await _exclusionProvider.GetExclusionsAsync(databaseName, cancellationToken);
        if (legacyExclusions.Contains($"{tableName}.{columnName}"))
        {
            return false;
        }

        if (await _ruleProvider.IsExcludedAsync(databaseName, tableName, columnName, cancellationToken))
        {
            return false;
        }

        return true;
    }
}
