#nullable enable

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;

namespace SqlToAi.Anonymization;

/// <summary>
/// Implements on-the-fly string anonymization to protect PII data before sharing with AI agents.
/// </summary>
public sealed class Anonymizer : IAnonymizer
{
    private readonly SqlToAiOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="Anonymizer"/> class.
    /// </summary>
    /// <param name="options">Options containing the default anonymization mode and excluded columns.</param>
    public Anonymizer(IOptions<SqlToAiOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Anonymizes a string value if anonymization is enabled and the column is not excluded.
    /// Default behavior: every string column is anonymized, except those that match a pattern
    /// in <see cref="AnonymizerOptions.ExcludedColumns"/>.
    /// </summary>
    /// <param name="columnName">The column name containing the value.</param>
    /// <param name="originalValue">The original string value.</param>
    /// <returns>The anonymized value, or the original value if anonymization is disabled or excluded.</returns>
    public string Anonymize(string columnName, string originalValue)
    {
        return Anonymize(columnName, originalValue, null, null);
    }

    /// <inheritdoc/>
    public string Anonymize(string columnName, string originalValue, string? tableName, HashSet<string>? dbExclusions)
    {
        if (!_options.Anonymizer.Enabled || string.IsNullOrEmpty(originalValue))
        {
            return originalValue;
        }

        // 1. Check database-specific exclusions first (TableName.ColumnName)
        if (dbExclusions != null && !string.IsNullOrEmpty(tableName))
        {
            string key = $"{tableName}.{columnName}";
            if (dbExclusions.Contains(key))
            {
                return originalValue;
            }
        }

        // 2. Check global exclusion patterns
        foreach (string excludedPattern in _options.Anonymizer.ExcludedColumns)
        {
            if (GlobPatternMatcher.IsMatch(columnName, excludedPattern))
            {
                return originalValue;
            }
        }

        // 3. Pauschale Anonymisierung: every non-excluded string column is anonymized with the
        //    configured default mode. Per-database opt-out is handled at the AccessLevel layer
        //    (ReadOnlyAnonymized vs ReadOnly) — the Anonymizer is only ever invoked when the
        //    access level already decided "yes, anonymize".
        return RunAnonymization(originalValue, _options.Anonymizer.DefaultMode);
    }

    private static string RunAnonymization(string value, string mode)
    {
        if (string.Equals(mode, "Hash", StringComparison.OrdinalIgnoreCase))
        {
            return HashValue(value);
        }

        return Scramble(value);
    }

    private static string Scramble(string val)
    {
        int seed = GetStableHashCode(val);
        var rand = new Random(seed);

        var sb = new StringBuilder(val.Length);
        foreach (char c in val)
        {
            if (char.IsAsciiLetterUpper(c))
            {
                sb.Append((char)rand.Next('A', 'Z' + 1));
            }
            else if (char.IsAsciiLetterLower(c))
            {
                sb.Append((char)rand.Next('a', 'z' + 1));
            }
            else if (char.IsDigit(c))
            {
                sb.Append((char)rand.Next('0', '9' + 1));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static int GetStableHashCode(string val)
    {
        uint hash = 2166136261;
        foreach (char c in val)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return (int)hash;
    }

    private static string HashValue(string val)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(val));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}
