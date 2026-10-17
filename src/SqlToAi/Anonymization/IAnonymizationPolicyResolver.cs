using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Anonymization;

/// <summary>
/// Answers, ahead of any query execution, whether a given (database, table, column) would be
/// anonymized under the current configuration — combining the global master switch
/// (<c>AnonymizerOptions.Enabled</c>) and the central <see cref="IAnonymizationRuleProvider"/>
/// rules — the only two exclusion sources that currently exist (see
/// <c>AnonymizationPolicyResolver.WillAnonymizeAsync</c>). Used by schema tools to proactively
/// tell the caller which columns are masked, before a query is ever written.
/// </summary>
public interface IAnonymizationPolicyResolver
{
    /// <summary>Target-aware form: resolves against the owning server's provider settings and rule cache.</summary>
    Task<bool> WillAnonymizeAsync(DatabaseTargetContext target, SchemaObjectIdentity identity, string columnName, CancellationToken cancellationToken = default)
        => WillAnonymizeAsync(target, identity.DisplayName, columnName, cancellationToken);

    /// <summary>Target-aware form: resolves against the owning server's provider settings and rule cache.</summary>
    Task<bool> WillAnonymizeAsync(DatabaseTargetContext target, string tableName, string columnName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether anonymized columns currently use reversible, searchable tokenization (see
    /// <c>Anonymizer.Tokenize</c>) instead of regular scramble/hash masking — a global mode switch
    /// (see <c>Anonymizer.Tokenization.Enabled</c>), not a per-column decision, so this needs no
    /// database/table/column context. Only meaningful when <see cref="WillAnonymizeAsync"/> is also true.
    /// </summary>
    bool IsTokenizationActive { get; }
}
