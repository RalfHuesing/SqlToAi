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
    private readonly ITokenVault _tokenVault;

    /// <summary>
    /// Initializes a new instance of the <see cref="Anonymizer"/> class.
    /// </summary>
    /// <param name="options">Options containing the default anonymization mode and excluded columns.</param>
    /// <param name="tokenVault">Reverse lookup store for tokens produced by <see cref="Tokenize"/>.</param>
    public Anonymizer(IOptions<SqlToAiOptions> options, ITokenVault tokenVault)
    {
        _options = options.Value;
        _tokenVault = tokenVault;
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
        return Anonymize(originalValue, new AnonymizationColumnContext(null, columnName, null));
    }

    /// <inheritdoc/>
    public string Anonymize(string originalValue, AnonymizationColumnContext context)
    {
        if (string.IsNullOrEmpty(originalValue) || IsColumnExcluded(context))
        {
            return originalValue;
        }

        return RunAnonymization(originalValue, _options.Anonymizer.DefaultMode);
    }

    /// <inheritdoc/>
    public string Tokenize(string columnName, string originalValue) =>
        Tokenize(originalValue, new AnonymizationColumnContext(null, columnName, null));

    /// <inheritdoc/>
    public string Tokenize(string originalValue, AnonymizationColumnContext context)
    {
        if (string.IsNullOrEmpty(originalValue) || IsColumnExcluded(context))
        {
            return originalValue;
        }

        var tokenization = _options.Anonymizer.Tokenization;
        if (!tokenization.IsUsable)
        {
            return RunAnonymization(originalValue, _options.Anonymizer.DefaultMode);
        }

        return _tokenVault.GetOrAddToken(originalValue, tokenization.Prefix, tokenization.Suffix);
    }

    private bool IsColumnExcluded(AnonymizationColumnContext context)
    {
        return !_options.Anonymizer.Enabled;
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
