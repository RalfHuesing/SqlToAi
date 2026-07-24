#nullable enable

namespace SqlToAi.Anonymization;

/// <summary>
/// A single row from the central <c>AnonymizationRules</c> table: whether a
/// (database, schema, table, column) pattern match should be anonymized or shown in clear text.
/// </summary>
/// <param name="SchemaPattern">
/// <c>LIKE</c>-style pattern for the resolved base schema name. Defaults to <c>%</c> (any schema)
/// for backward compatibility with rule sets created before this column existed, so a same-named
/// table in a different schema is no longer silently matched by a rule meant for one schema only
/// (see tasks/audit-2026-07-24/02-anonymisierung-tokenisierung.md, Finding
/// "Ausschluss-/Regel-Abgleich ist schema-blind").
/// </param>
public sealed record AnonymizationRule(string DatabasePattern, string SchemaPattern, string TablePattern, string ColumnPattern, bool Anonymize);
