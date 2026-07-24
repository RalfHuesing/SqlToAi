#nullable enable

using System.Collections.Concurrent;
using System.Data.Common;
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
/// per (database, schema, table, column) using SQL <c>LIKE</c>-style wildcard patterns.
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
    public async Task<bool> IsExcludedAsync(string databaseName, string schemaName, string tableName, string columnName, CancellationToken cancellationToken = default)
    {
        if (!_options.AnonymizationRules.Enabled || string.IsNullOrWhiteSpace(columnName))
        {
            return false;
        }

        var rules = await GetActiveRulesAsync(cancellationToken);
        AnonymizationRule? bestMatch = FindMostSpecificMatch(
            rules, databaseName ?? string.Empty, schemaName ?? string.Empty, tableName ?? string.Empty, columnName);
        return bestMatch is not null && !bestMatch.Anonymize;
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

            bool hasSchemaPattern = await HasSchemaPatternColumnAsync(connection, tableName, cancellationToken);
            string columnList = hasSchemaPattern
                ? "[DatabasePattern], [SchemaPattern], [TablePattern], [ColumnPattern], [Anonymize]"
                : "[DatabasePattern], [TablePattern], [ColumnPattern], [Anonymize]";
            string sql = $"SELECT {columnList} FROM {safeTableName} WHERE [IsActive] = 1";
            var rows = await connection.QueryAsync<RuleRow>(
                new CommandDefinition(sql, cancellationToken: cancellationToken, commandTimeout: _options.AnonymizationRules.CommandTimeoutSeconds));

            return rows.Select(r => new AnonymizationRule(r.DatabasePattern, r.SchemaPattern, r.TablePattern, r.ColumnPattern, r.Anonymize)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load anonymization rules from {TableName}.", tableName);
            return [];
        }
    }

    /// <summary>
    /// Detects, without ever throwing, whether the physical rule table already has the optional
    /// <c>SchemaPattern</c> column — so a rule set that hasn't run the migration adding it keeps
    /// working with zero-config schema-agnostic matching (<see cref="RuleRow.SchemaPattern"/>'s
    /// default <c>%</c>), exactly like before this column existed.
    /// </summary>
    private async Task<bool> HasSchemaPatternColumnAsync(DbConnection connection, string tableName, CancellationToken cancellationToken)
    {
        try
        {
            const string checkSql = "SELECT CASE WHEN COL_LENGTH(@TableName, 'SchemaPattern') IS NOT NULL THEN 1 ELSE 0 END";
            return await connection.QueryFirstOrDefaultAsync<bool>(
                new CommandDefinition(checkSql, new { TableName = tableName }, cancellationToken: cancellationToken, commandTimeout: _options.AnonymizationRules.CommandTimeoutSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for optional SchemaPattern column on rule table {TableName}.", tableName);
            return false;
        }
    }

    /// <summary>
    /// Picks the most specific active rule matching all four dimensions. The schema dimension is
    /// weighted between database and table (see the score below) — item 7 of the audit remediation
    /// restructures this weighted-sum scoring shape; this change only makes sure a schema dimension
    /// exists and is matched, without redesigning the formula itself.
    /// </summary>
    private static AnonymizationRule? FindMostSpecificMatch(
        IReadOnlyList<AnonymizationRule> rules, string databaseName, string schemaName, string tableName, string columnName)
    {
        AnonymizationRule? best = null;
        int bestScore = -1;

        foreach (var rule in rules)
        {
            if (!LikePatternMatcher.IsMatch(databaseName, rule.DatabasePattern) ||
                !LikePatternMatcher.IsMatch(schemaName, rule.SchemaPattern) ||
                !LikePatternMatcher.IsMatch(tableName, rule.TablePattern) ||
                !LikePatternMatcher.IsMatch(columnName, rule.ColumnPattern))
            {
                continue;
            }

            int score = (LikePatternMatcher.SpecificityScore(rule.DatabasePattern) * 1000)
                      + (LikePatternMatcher.SpecificityScore(rule.SchemaPattern) * 100)
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
        public string SchemaPattern { get; init; } = "%";
        public string TablePattern { get; init; } = "%";
        public string ColumnPattern { get; init; } = string.Empty;
        public bool Anonymize { get; init; }
    }
}
