using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Security;

namespace SqlToAi.Database;

/// <summary>
/// Substitutes anonymization tokens produced by <c>Anonymizer.Tokenize</c> back into their real
/// values before a query reaches SQL Server. Substitution only ever happens inside actual string
/// literal content (via <see cref="SqlLiteralScanner"/>) — never in comments or identifiers,
/// including single-quoted column aliases — so a token embedded in a value literal (an exact match, an
/// <c>IN (...)</c> list, or wrapped in <c>LIKE '%...%'</c> wildcards) resolves correctly, because
/// the underlying database itself was never touched: only the literal text the AI wrote is
/// rewritten, and SQL Server then evaluates the real predicate against real data.
/// <para>
/// A token the vault does not recognize (forged, guessed, or simply never issued) is left exactly
/// as-is as ordinary literal text; it can still match stored data containing that text.
/// </para>
/// </summary>
public sealed class QueryTokenResolver : IQueryTokenResolver
{
    private readonly ITokenVault _vault;
    private readonly TokenizationOptions _options;
    private readonly Regex? _tokenPattern;

    /// <summary>Initializes a new instance of the <see cref="QueryTokenResolver"/> class.</summary>
    public QueryTokenResolver(ITokenVault vault, IOptions<SqlToAiOptions> options)
    {
        _vault = vault;
        _options = options.Value.Anonymizer.Tokenization;
        _tokenPattern = _options.IsUsable ? BuildTokenPattern(_options) : null;
    }


    /// <inheritdoc/>
    public string ResolveTokens(string query, Domain.DatabaseTargetContext target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return ResolveTokensCore(query, token =>
            _vault.TryResolve(token, target, out string? realValue) ? realValue : null);
    }

    private string ResolveTokensCore(string query, Func<string, string?> resolveValue)
    {
        if (_tokenPattern is null || string.IsNullOrEmpty(query))
        {
            return query;
        }

        var ranges = SqlLiteralScanner.GetLiteralContentRanges(query);
        if (ranges.Count == 0)
        {
            return query;
        }

        var sb = new StringBuilder(query.Length);
        int cursor = 0;
        foreach ((int start, int length) in ranges)
        {
            sb.Append(query, cursor, start - cursor);
            sb.Append(ResolveTokensInLiteral(query.Substring(start, length), resolveValue));
            cursor = start + length;
        }
        sb.Append(query, cursor, query.Length - cursor);
        return sb.ToString();
    }

    private string ResolveTokensInLiteral(string literalContent, Func<string, string?> resolveValue) =>
        _tokenPattern!.Replace(literalContent, match =>
        {
            string? realValue = resolveValue(match.Value);
            return realValue is null ? match.Value : EscapeForSqlLiteral(realValue);
        });

    private static string EscapeForSqlLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private static Regex BuildTokenPattern(TokenizationOptions options)
    {
        string pattern = Regex.Escape(options.Prefix) + "[A-Za-z0-9_-]+" + Regex.Escape(options.Suffix);
        return new Regex(pattern, RegexOptions.None, SecurityConstants.DefaultRegexTimeout);
    }
}
