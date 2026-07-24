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
        return Anonymize(originalValue, new AnonymizationColumnContext(null, columnName, null, null));
    }

    /// <inheritdoc/>
    public string Anonymize(string originalValue, AnonymizationColumnContext context)
    {
        if (string.IsNullOrEmpty(originalValue) || IsColumnExcluded(context))
        {
            return originalValue;
        }

        // Pauschale Anonymisierung: every non-excluded string column is anonymized with the
        // configured default mode. Per-database opt-out is handled at the AccessLevel layer
        // (ReadOnlyAnonymized vs ReadOnly) — the Anonymizer is only ever invoked when the
        // access level already decided "yes, anonymize".
        return RunAnonymization(originalValue, _options.Anonymizer.DefaultMode);
    }

    /// <inheritdoc/>
    public string Tokenize(string columnName, string originalValue) =>
        Tokenize(originalValue, new AnonymizationColumnContext(null, columnName, null, null));

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
            // Fail-safe fallback: tokenization isn't properly configured, so never expose the
            // value in clear text — mask it exactly like a non-searchable column instead.
            return RunAnonymization(originalValue, _options.Anonymizer.DefaultMode);
        }

        string token = ComputeToken(originalValue, tokenization);
        _tokenVault.Store(token, originalValue);
        return token;
    }

    /// <summary>
    /// The single exclusion decision shared by <see cref="Anonymize(string, AnonymizationColumnContext)"/>
    /// and <see cref="Tokenize(string, AnonymizationColumnContext)"/>, so tokenization can never
    /// bypass an exclusion that regular masking would honor: the master switch, the database-specific
    /// exclusion table, and the <see cref="AnonymizerOptions.ExcludedColumns"/> glob patterns.
    /// Both the exclusion-table lookup and the glob-pattern match are keyed off
    /// <see cref="AnonymizationColumnContext.OriginColumnName"/> — the query result's real source
    /// column — never off a query's output alias, so <c>SELECT SSN AS RecordId</c> cannot dodge an
    /// <c>*Id</c> exclusion pattern meant for actual ID columns. The exclusion-table lookup is also
    /// schema-aware (<see cref="AnonymizationColumnContext.SchemaName"/>), so a same-named table in
    /// a different schema never inherits an exclusion scoped to another schema.
    /// </summary>
    private bool IsColumnExcluded(AnonymizationColumnContext context)
    {
        if (!_options.Anonymizer.Enabled)
        {
            return true;
        }

        if (context.DbExclusions != null && !string.IsNullOrEmpty(context.TableName) && !string.IsNullOrEmpty(context.OriginColumnName))
        {
            if (context.DbExclusions.Contains(context.SchemaName, context.TableName, context.OriginColumnName))
            {
                return true;
            }
        }

        if (string.IsNullOrEmpty(context.OriginColumnName))
        {
            // Fail-safe: the column's real origin could not be resolved (e.g. a computed,
            // literal, or aggregate expression with no traceable source column). Never trust an
            // alias against the plain pattern list in that case — treat it as not excluded so it
            // still gets anonymized/tokenized.
            return false;
        }

        foreach (string excludedPattern in _options.Anonymizer.ExcludedColumns)
        {
            if (GlobPatternMatcher.IsMatch(context.OriginColumnName, excludedPattern))
            {
                return true;
            }
        }

        return false;
    }

    private static string ComputeToken(string value, TokenizationOptions tokenization)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes(tokenization.Secret);
        byte[] valueBytes = Encoding.UTF8.GetBytes(value);
        byte[] hash = HMACSHA256.HashData(keyBytes, valueBytes);

        string body = Convert.ToBase64String(hash)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        return tokenization.Prefix + body + tokenization.Suffix;
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
