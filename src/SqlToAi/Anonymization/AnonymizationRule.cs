#nullable enable

namespace SqlToAi.Anonymization;

/// <summary>
/// A single row from the central <c>AnonymizationRules</c> table: whether a
/// (database, table, column) pattern match should be anonymized or shown in clear text, and
/// whether it should use reversible, searchable tokenization instead of regular masking.
/// </summary>
public sealed record AnonymizationRule(
    string DatabasePattern,
    string TablePattern,
    string ColumnPattern,
    bool Anonymize,
    bool SearchableToken = false);
