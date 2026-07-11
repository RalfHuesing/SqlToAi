#nullable enable

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
    /// <param name="options">Options containing the anonymization rules and exclusion patterns.</param>
    public Anonymizer(IOptions<SqlToAiOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Anonymizes a string value if the column matches configured rules and is not excluded.
    /// </summary>
    /// <param name="columnName">The column name containing the value.</param>
    /// <param name="originalValue">The original string value.</param>
    /// <returns>The anonymized value, or the original value if anonymization is disabled or not applicable.</returns>
    public string Anonymize(string columnName, string originalValue)
    {
        if (!_options.Anonymizer.Enabled || string.IsNullOrEmpty(originalValue))
        {
            return originalValue;
        }

        // 1. Check if column matches any exclusion pattern
        foreach (string excludedPattern in _options.Anonymizer.ExcludedColumns)
        {
            if (MatchesPattern(columnName, excludedPattern))
            {
                return originalValue;
            }
        }

        // 2. Determine selected mode by matching rules
        string? selectedMode = null;
        foreach (var rule in _options.Anonymizer.Rules)
        {
            if (MatchesPattern(columnName, rule.Pattern))
            {
                selectedMode = rule.Mode;
                break;
            }
        }

        // If specific rules are configured and none match, skip anonymization
        if (selectedMode is null)
        {
            if (_options.Anonymizer.Rules.Count > 0)
            {
                return originalValue;
            }

            // Secure by default fallback: if no rules are defined, anonymize all strings
            selectedMode = !string.IsNullOrWhiteSpace(_options.Anonymizer.DefaultMode)
                ? _options.Anonymizer.DefaultMode
                : _options.Anonymizer.Mode;
        }

        return RunAnonymization(originalValue, selectedMode);
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
        var sb = new StringBuilder(val.Length);
        foreach (char c in val)
        {
            if (char.IsAsciiLetterUpper(c))
            {
                sb.Append('X');
            }
            else if (char.IsAsciiLetterLower(c))
            {
                sb.Append('x');
            }
            else if (char.IsDigit(c))
            {
                sb.Append('9');
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
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

    private static bool MatchesPattern(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        string regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        try
        {
            return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
