using SqlToAi.Domain;

namespace SqlToAi.Anonymization;

/// <summary>
/// Resolves the optional, central, cross-database <c>AnonymizationRules</c> table: for a given
/// (database, table, column), whether the most specific matching active rule says the column
/// should be shown in clear text (excluded from anonymization).
/// </summary>
public interface IAnonymizationRuleProvider
{


    /// <summary>
    /// Target-aware form of <see cref="IsExcludedAsync(string,string,string,string,CancellationToken)"/>:
    /// rule lookup, provider settings and cache partitioning use the target's canonical alias
    /// (policy owner) and its raw database name, so owners never borrow each other's exemptions.
    /// </summary>
    Task<bool> IsExcludedAsync(DatabaseTargetContext target, string schemaName, string tableName, string columnName, CancellationToken cancellationToken = default);
}
