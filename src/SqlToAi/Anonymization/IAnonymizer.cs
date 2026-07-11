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
}
