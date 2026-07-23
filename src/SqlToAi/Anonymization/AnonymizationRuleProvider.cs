#nullable enable

using System.Collections.Concurrent;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;

namespace SqlToAi.Anonymization;

#pragma warning disable CA1848 // Use the LoggerMessage delegates

/// <summary>
/// Cache-backed container for the full central rule set, reloaded as a whole once per TTL —
/// unlike <see cref="ExclusionCheckResult"/>, this is not keyed per customer database, since
/// the rule table lives in its own dedicated database independent of any customer connection.
/// </summary>
public sealed record RuleCacheEntry(IReadOnlyList<AnonymizationRule> Rules, DateTime ExpireTime)
{
    /// <summary>Checks whether this cache entry has passed its TTL.</summary>
    public bool IsExpired(DateTime currentTime) => currentTime >= ExpireTime;
}

/// <summary>
/// Implements <see cref="IAnonymizationRuleProvider"/> by loading the full central rule set from
/// a dedicated (possibly separate-server) database and resolving the most specific matching rule
/// per (database, table, column) using SQL <c>LIKE</c>-style wildcard patterns.
/// </summary>
public sealed class AnonymizationRuleProvider : IAnonymizationRuleProvider
{
    private const string CacheKey = "all-rules";

    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly SqlToAiOptions _options;
    private readonly ILogger<AnonymizationRuleProvider> _logger;
    private readonly ConcurrentDictionary<string, RuleCacheEntry> _cache = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="AnonymizationRuleProvider"/> class.</summary>
    public AnonymizationRuleProvider(
        IDatabaseConnectionFactory connectionFactory,
        IOptions<SqlToAiOptions> options,
        ILogger<AnonymizationRuleProvider> logger)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> IsExcludedAsync(string databaseName, string tableName, string columnName, CancellationToken cancellationToken = default)
    {
        AnonymizationRule? bestMatch = await ResolveBestMatchAsync(databaseName, tableName, columnName, cancellationToken);
        return bestMatch is not null && !bestMatch.Anonymize;
    }

    /// <inheritdoc/>
    public async Task<bool> IsSearchableTokenAsync(string databaseName, string tableName, string columnName, CancellationToken cancellationToken = default)
    {
        AnonymizationRule? bestMatch = await ResolveBestMatchAsync(databaseName, tableName, columnName, cancellationToken);
        return bestMatch is not null && bestMatch.SearchableToken;
    }

    private async Task<AnonymizationRule?> ResolveBestMatchAsync(string databaseName, string tableName, string columnName, CancellationToken cancellationToken)
    {
        if (!_options.AnonymizationRules.Enabled || string.IsNullOrWhiteSpace(columnName))
        {
            return null;
        }

        var rules = await GetActiveRulesAsync(cancellationToken);
        return FindMostSpecificMatch(rules, databaseName ?? string.Empty, tableName ?? string.Empty, columnName);
    }

    private async Task<IReadOnlyList<AnonymizationRule>> GetActiveRulesAsync(CancellationToken cancellationToken)
    {
        var currentTime = DateTime.UtcNow;

        if (_cache.TryGetValue(CacheKey, out var cached) && !cached.IsExpired(currentTime))
        {
            return cached.Rules;
        }

        var rules = await LoadActiveRulesAsync(cancellationToken);

        int ttl = _options.AnonymizationRules.CacheTtlSeconds > 0 ? _options.AnonymizationRules.CacheTtlSeconds : 300;
        _cache[CacheKey] = new RuleCacheEntry(rules, currentTime.AddSeconds(ttl));

        return rules;
    }

    private async Task<IReadOnlyList<AnonymizationRule>> LoadActiveRulesAsync(CancellationToken cancellationToken)
    {
        string tableName = _options.AnonymizationRules.TableName;
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return [];
        }

        try
        {
            var settings = new SecondaryConnectionSettings(
                _options.AnonymizationRules.Server,
                _options.AnonymizationRules.Database,
                _options.AnonymizationRules.UserId,
                _options.AnonymizationRules.Password,
                _options.AnonymizationRules.IntegratedSecurity,
                _options.AnonymizationRules.CommandTimeoutSeconds);

            using var connection = SecondaryConnectionBuilder.Create(settings, "SqlToAi-AnonymizationRules", string.Empty, _connectionFactory);
            await connection.OpenAsync(cancellationToken);

            string? safeTableName = await connection.QueryFirstOrDefaultAsync<string>(
                new CommandDefinition(
                    "SELECT QUOTENAME(OBJECT_SCHEMA_NAME(OBJECT_ID(@TableName, 'U'))) + '.' + QUOTENAME(OBJECT_NAME(OBJECT_ID(@TableName, 'U')))",
                    new { TableName = tableName },
                    cancellationToken: cancellationToken,
                    commandTimeout: _options.AnonymizationRules.CommandTimeoutSeconds));

            if (string.IsNullOrEmpty(safeTableName))
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Anonymization rule table {TableName} does not exist.", tableName);
                }
                return [];
            }

            string sql = $"SELECT [DatabasePattern], [TablePattern], [ColumnPattern], [Anonymize], [SearchableToken] FROM {safeTableName} WHERE [IsActive] = 1";
            var rows = await connection.QueryAsync<RuleRow>(
                new CommandDefinition(sql, cancellationToken: cancellationToken, commandTimeout: _options.AnonymizationRules.CommandTimeoutSeconds));

            return rows.Select(r => new AnonymizationRule(r.DatabasePattern, r.TablePattern, r.ColumnPattern, r.Anonymize, r.SearchableToken)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load anonymization rules from {TableName}.", tableName);
            return [];
        }
    }

    private static AnonymizationRule? FindMostSpecificMatch(
        IReadOnlyList<AnonymizationRule> rules, string databaseName, string tableName, string columnName)
    {
        AnonymizationRule? best = null;
        int bestScore = -1;

        foreach (var rule in rules)
        {
            if (!LikePatternMatcher.IsMatch(databaseName, rule.DatabasePattern) ||
                !LikePatternMatcher.IsMatch(tableName, rule.TablePattern) ||
                !LikePatternMatcher.IsMatch(columnName, rule.ColumnPattern))
            {
                continue;
            }

            int score = (LikePatternMatcher.SpecificityScore(rule.DatabasePattern) * 100)
                      + (LikePatternMatcher.SpecificityScore(rule.TablePattern) * 10)
                      + LikePatternMatcher.SpecificityScore(rule.ColumnPattern);

            if (score > bestScore)
            {
                bestScore = score;
                best = rule;
            }
        }

        return best;
    }

    private sealed class RuleRow
    {
        public string DatabasePattern { get; init; } = "%";
        public string TablePattern { get; init; } = "%";
        public string ColumnPattern { get; init; } = string.Empty;
        public bool Anonymize { get; init; }
        public bool SearchableToken { get; init; }
    }
}
