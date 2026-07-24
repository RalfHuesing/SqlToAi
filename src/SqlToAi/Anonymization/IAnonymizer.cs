#nullable enable

namespace SqlToAi.Anonymization;

/// <summary>
/// Bundles the table/origin-column/exclusion context needed to make the exclusion decision in
/// <see cref="IAnonymizer.Anonymize(string, AnonymizationColumnContext)"/> and
/// <see cref="IAnonymizer.Tokenize(string, AnonymizationColumnContext)"/> into a single parameter
/// object (see <c>.agents/rules/AiNetLinter.mdc</c>, <c>MaxMethodParameterCount</c>).
/// </summary>
/// <param name="TableName">The resolved base table name, or null/empty if unknown.</param>
/// <param name="OriginColumnName">
/// The resolved base column name — the query result's real source column, not its output alias.
/// Null when the origin could not be resolved (e.g. a computed/literal/aggregate expression with
/// no traceable source column). A null value is fail-safe: the column can never be excluded via
/// the plain <c>AnonymizerOptions.ExcludedColumns</c> glob-pattern list in that case, no matter
/// what the alias itself looks like.
/// </param>
/// <param name="DbExclusions">The optional set of database-specific exclusions ("TableName.ColumnName"), keyed by <see cref="TableName"/> and <see cref="OriginColumnName"/>.</param>
public sealed record AnonymizationColumnContext(string? TableName, string? OriginColumnName, HashSet<string>? DbExclusions);

/// <summary>
/// Handles on-the-fly string anonymization to protect PII (Personally Identifiable Information).
/// </summary>
public interface IAnonymizer
{
    /// <summary>
    /// Anonymizes a string value based on the column name and the configured rules. No schema
    /// context is available here, so <paramref name="columnName"/> doubles as its own origin —
    /// this overload was always alias-only and stays that way for backward compatibility. Prefer
    /// <see cref="Anonymize(string, AnonymizationColumnContext)"/> whenever a query result's real
    /// base column can be resolved, so an output alias can never bypass an exclusion.
    /// </summary>
    /// <param name="columnName">The name of the column containing the value.</param>
    /// <param name="originalValue">The original raw value.</param>
    /// <returns>The anonymized string value, or the original value if it should be excluded or not matched.</returns>
    string Anonymize(string columnName, string originalValue);

    /// <summary>
    /// Anonymizes a string value using the resolved table/origin-column/exclusion context. The
    /// exclusion decision is based on <see cref="AnonymizationColumnContext.OriginColumnName"/> —
    /// the query result's real source column — never on a query's output alias.
    /// </summary>
    /// <param name="originalValue">The original raw value.</param>
    /// <param name="context">The resolved table/origin-column/exclusion context.</param>
    /// <returns>The anonymized string value, or the original value if it should be excluded.</returns>
    string Anonymize(string originalValue, AnonymizationColumnContext context);

    /// <summary>
    /// Produces a deterministic, reversible token instead of the regular scramble/hash mask: the
    /// same value always yields the same token, and the server remembers the token-to-value mapping
    /// so a later query can reuse the token to find matching rows without the AI ever learning the
    /// real value. Respects the exact same exclusion rules as <see cref="Anonymize(string, string)"/>
    /// (master switch, <c>ExcludedColumns</c>) — this only changes *how* an already-anonymized value
    /// is anonymized, never *whether* it is. Falls back to the regular masking algorithm when
    /// tokenization is not configured to be usable (see <c>TokenizationOptions.IsUsable</c>). No
    /// schema context is available here, so <paramref name="columnName"/> doubles as its own origin
    /// — this overload was always alias-only and stays that way for backward compatibility.
    /// </summary>
    /// <param name="columnName">The name of the column containing the value.</param>
    /// <param name="originalValue">The original raw value.</param>
    /// <returns>A token that can be resolved back to <paramref name="originalValue"/>, or the value unchanged if excluded or empty.</returns>
    string Tokenize(string columnName, string originalValue);

    /// <summary>
    /// Produces a deterministic, reversible token using the resolved table/origin-column/exclusion
    /// context — see <see cref="Tokenize(string, string)"/> and <see cref="Anonymize(string, AnonymizationColumnContext)"/>.
    /// </summary>
    /// <param name="originalValue">The original raw value.</param>
    /// <param name="context">The resolved table/origin-column/exclusion context.</param>
    /// <returns>A token that can be resolved back to <paramref name="originalValue"/>, or the value unchanged if excluded or empty.</returns>
    string Tokenize(string originalValue, AnonymizationColumnContext context);
}
