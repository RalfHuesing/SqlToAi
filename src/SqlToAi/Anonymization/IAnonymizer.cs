#nullable enable

namespace SqlToAi.Anonymization;

/// <summary>
/// Handles on-the-fly string anonymization to protect PII (Personally Identifiable Information).
/// </summary>
public interface IAnonymizer
{
    /// <summary>
    /// Anonymizes a string value based on the column name and the configured rules.
    /// </summary>
    /// <param name="columnName">The name of the column containing the value.</param>
    /// <param name="originalValue">The original raw value.</param>
    /// <returns>The anonymized string value, or the original value if it should be excluded or not matched.</returns>
    string Anonymize(string columnName, string originalValue);

    /// <summary>
    /// Anonymizes a string value based on the column name, table name, and database-specific exclusions.
    /// </summary>
    /// <param name="columnName">The name of the column containing the value.</param>
    /// <param name="originalValue">The original raw value.</param>
    /// <param name="tableName">The optional table name containing the column.</param>
    /// <param name="dbExclusions">The optional set of database-specific exclusions ("TableName.ColumnName").</param>
    /// <returns>The anonymized string value, or the original value if it should be excluded.</returns>
    string Anonymize(string columnName, string originalValue, string? tableName, HashSet<string>? dbExclusions);
}
