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
    /// (database, schema, table, column) says <c>Anonymize = false</c> (i.e. show it in clear text).
    /// Returns <c>false</c> when no rule matches, or the feature is disabled.
    /// </summary>
    /// <param name="databaseName">The database being queried.</param>
    /// <param name="schemaName">
    /// The resolved base schema name, or empty/null if unknown. An unknown schema only ever
    /// matches a rule whose <see cref="AnonymizationRule.SchemaPattern"/> is the schema-agnostic
    /// <c>%</c> default — fail-safe in the "keep anonymizing" direction, so a same-named table in
    /// an unrelated schema never inherits a rule scoped to a specific schema.
    /// </param>
    /// <param name="tableName">The resolved base table name.</param>
    /// <param name="columnName">The resolved base column name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> IsExcludedAsync(string databaseName, string schemaName, string tableName, string columnName, CancellationToken cancellationToken = default);
}
