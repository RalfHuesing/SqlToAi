#nullable enable

namespace SqlToAi.Anonymization;

/// <summary>
/// Answers, ahead of any query execution, whether a given (database, table, column) would be
/// anonymized under the current configuration — combining the global master switch, the glob
/// <c>ExcludedColumns</c> patterns, the legacy per-database exclusion table, and the central
/// <see cref="IAnonymizationRuleProvider"/> rules. Used by schema tools to proactively tell the
/// caller which columns are masked, before a query is ever written.
/// </summary>
public interface IAnonymizationPolicyResolver
{
    /// <summary>Returns <c>true</c> if string values in this column would currently be anonymized.</summary>
    Task<bool> WillAnonymizeAsync(string databaseName, string tableName, string columnName, CancellationToken cancellationToken = default);
}
