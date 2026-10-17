using SqlToAi.Domain;

namespace SqlToAi.Anonymization;

/// <summary>
/// Bundles the table/origin-column/exclusion context needed to make the exclusion decision in
/// <see cref="IAnonymizer.Anonymize(string, AnonymizationColumnContext)"/> and
/// <see cref="IAnonymizer.Tokenize(string, AnonymizationColumnContext)"/> into a single parameter object.
/// </summary>
/// <param name="TableName">The resolved base table name, or null/empty if unknown.</param>
/// <param name="OriginColumnName">
/// The resolved base column name — the query result's real source column, not its output alias.
/// Null when the origin could not be resolved (e.g. a computed/literal/aggregate expression with
/// no traceable source column).
/// </param>
/// <param name="SchemaName">
/// The resolved base schema name, or null/empty if unknown. Lets a database-specific exclusion be
/// scoped to a single schema, so a same-named table in a different schema never inherits an
/// exclusion meant for another schema (see
/// tasks/audit-2026-07-24/02-anonymisierung-tokenisierung.md, Finding "Ausschluss-/Regel-Abgleich
/// ist schema-blind"). A null/empty value only ever satisfies a schema-agnostic exclusion entry,
/// never a schema-scoped one — fail-safe in the "keep anonymizing" direction.
/// </param>
/// <remarks>
/// The exclusion decision itself is never made inside <see cref="Anonymizer"/> — it is resolved
/// upstream by the caller via the central <see cref="IAnonymizationRuleProvider"/> (see
/// <c>QueryExecutionService.Anonymization.cs</c>), before <c>Anonymize</c>/<c>Tokenize</c> is even
/// called. This context record only carries table/column info for traceability and tests.
/// </remarks>
public sealed record AnonymizationColumnContext(string? TableName, string? OriginColumnName, string? SchemaName);

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
    /// real value. Token state is strictly scoped to the given canonical target (see
    /// <see cref="DatabaseTargetContext"/>): tokens issued for one target never resolve in another,
    /// so identical plaintext in different targets yields non-interchangeable tokens.
    /// </summary>
    /// <param name="originalValue">The original raw value.</param>
    /// <param name="context">The resolved table/origin-column/exclusion context.</param>
    /// <param name="target">The resolved target owning the token scope.</param>
    /// <returns>A token scoped to <paramref name="target"/>, or the value unchanged if excluded or empty.</returns>
    string Tokenize(string originalValue, AnonymizationColumnContext context, DatabaseTargetContext target);
}
