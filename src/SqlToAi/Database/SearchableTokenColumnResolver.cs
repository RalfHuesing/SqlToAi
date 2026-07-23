#nullable enable

using SqlToAi.Anonymization;
using SqlToAi.Configuration;

namespace SqlToAi.Database;

/// <summary>
/// Resolves, once per column ordinal per query (not per row), whether a column should use
/// reversible tokenization (see <c>Anonymizer.Tokenize</c>) instead of regular masking —
/// combining the appsettings glob list (<see cref="TokenizationOptions.SearchableColumns"/>)
/// with the central rule provider's <c>SearchableToken</c> flag; either source saying "yes" wins.
/// </summary>
internal sealed class SearchableTokenColumnResolver
{
    private readonly TokenizationOptions _options;
    private readonly IAnonymizationRuleProvider? _ruleProvider;

    public SearchableTokenColumnResolver(TokenizationOptions options, IAnonymizationRuleProvider? ruleProvider)
    {
        _options = options;
        _ruleProvider = ruleProvider;
    }

    public async Task<bool[]> ResolveAsync(
        string databaseName, string[] columnNames, string?[] baseTableNames, CancellationToken cancellationToken)
    {
        var result = new bool[columnNames.Length];
        for (int i = 0; i < columnNames.Length; i++)
        {
            result[i] = await IsSearchableAsync(databaseName, columnNames[i], i, baseTableNames, cancellationToken);
        }
        return result;
    }

    private async Task<bool> IsSearchableAsync(
        string databaseName, string columnName, int columnIndex, string?[] baseTableNames, CancellationToken cancellationToken)
    {
        if (MatchesGlob(columnName))
        {
            return true;
        }

        if (_ruleProvider is null)
        {
            return false;
        }

        string tableName = columnIndex < baseTableNames.Length ? baseTableNames[columnIndex] ?? string.Empty : string.Empty;
        return await _ruleProvider.IsSearchableTokenAsync(databaseName, tableName, columnName, cancellationToken);
    }

    private bool MatchesGlob(string columnName)
    {
        foreach (string pattern in _options.SearchableColumns)
        {
            if (GlobPatternMatcher.IsMatch(columnName, pattern))
            {
                return true;
            }
        }
        return false;
    }
}
