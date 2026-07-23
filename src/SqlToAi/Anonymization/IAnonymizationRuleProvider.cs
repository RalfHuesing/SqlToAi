#nullable enable

namespace SqlToAi.Anonymization;

/// <summary>
/// Resolves the optional, central, cross-database <c>AnonymizationRules</c> table: for a given
/// (database, table, column), whether the most specific matching active rule says the column
/// should be shown in clear text (excluded from anonymization).
/// </summary>
public interface IAnonymizationRuleProvider
{
    /// <summary>
    /// Returns <c>true</c> when the most specific matching active rule for this
    /// (database, table, column) says <c>Anonymize = false</c> (i.e. show it in clear text).
    /// Returns <c>false</c> when no rule matches, or the feature is disabled.
    /// </summary>
    Task<bool> IsExcludedAsync(string databaseName, string tableName, string columnName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <c>true</c> when the most specific matching active rule for this
    /// (database, table, column) says <c>SearchableToken = true</c> (i.e. use reversible
    /// tokenization instead of regular masking). Returns <c>false</c> when no rule matches,
    /// or the feature is disabled.
    /// </summary>
    Task<bool> IsSearchableTokenAsync(string databaseName, string tableName, string columnName, CancellationToken cancellationToken = default);
}
