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

        // No schema context is available at this call site (schema tools report on a bare table
        // name ahead of any query) — passing null/unknown here is the fail-safe direction: it only
        // ever matches a schema-agnostic exclusion/rule, never a schema-scoped one, so this never
        // over-reports "not anonymized" for a table it can't actually place in a schema.
        var legacyExclusions = await _exclusionProvider.GetExclusionsAsync(databaseName, cancellationToken);
        if (legacyExclusions.Contains(null, tableName, columnName))
        {
            return false;
        }

        if (await _ruleProvider.IsExcludedAsync(databaseName, string.Empty, tableName, columnName, cancellationToken))
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public bool IsTokenizationActive => _options.Anonymizer.Tokenization.IsUsable;
}
