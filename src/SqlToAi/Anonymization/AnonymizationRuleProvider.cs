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
    /// Picks the most specific active rule matching all four dimensions.
    /// </summary>
    /// <remarks>
    /// Item 7 of the audit remediation (tasks/audit-2026-07-24/02-anonymisierung-tokenisierung.md,
    /// Finding "Regel-Präzedenz gewichtet Datenbank- vor Spalten-Spezifität — breite DB-Regel kann
    /// gezielten Spalten-Schutz aushebeln") replaced the previous weighted-sum scoring
    /// (<c>DB*1000 + Schema*100 + Table*10 + Column</c>) with a Pareto-dominance comparison across
    /// the four per-dimension <see cref="LikePatternMatcher.SpecificityScore"/> values. The old
    /// weighted sum let a rule that was merely specific about the database dominate a rule that was
    /// exactly specific about the column, even when the column rule was meant as a universal,
    /// cross-database protection (e.g. an "SSN everywhere" rule losing to a "this one staging DB,
    /// wide open" rule purely because DB outweighs column in the sum). A plain lexicographic tuple
    /// comparison <c>(DB, Schema, Table, Column)</c> has the same flaw — DB still trumps every other
    /// dimension regardless of value — so it was rejected too.
    /// <para>
    /// Instead: a rule X <b>dominates</b> rule Y if X's score is <c>&gt;=</c> Y's in all four
    /// dimensions and strictly <c>&gt;</c> in at least one. Rules that no other matching rule
    /// dominates are "Pareto-maximal". If exactly one such rule exists, it wins outright — this is
    /// the unambiguous case and behaves the same as before (a rule that is more specific everywhere
    /// still wins). If several rules are mutually non-dominated — genuinely incomparable, like an
    /// all-databases/exact-column rule versus an exact-database/all-columns rule — the protective
    /// ones (<see cref="AnonymizationRule.Anonymize"/> <c>== true</c>) are preferred over the
    /// permissive ones, per audit option (a): when two configured intents genuinely conflict and
    /// neither is objectively more specific, protecting data is the fail-safe default. Only if that
    /// still leaves more than one candidate (e.g. two incomparable rules that are both protective, or
    /// both permissive) does the old weighted-sum total break the remaining tie — purely as an
    /// arbitrary-but-stable last resort with no security meaning of its own, just to avoid depending
    /// on list/dictionary iteration order.
    /// </para>
    /// </remarks>
    private static AnonymizationRule? FindMostSpecificMatch(
        IReadOnlyList<AnonymizationRule> rules, string databaseName, string schemaName, string tableName, string columnName)
    {
        var matches = GetMatchingRulesWithScores(rules, databaseName, schemaName, tableName, columnName);
        if (matches.Count == 0)
        {
            return null;
        }

        var nonDominated = GetNonDominated(matches);
        if (nonDominated.Count == 1)
        {
            return nonDominated[0].Rule;
        }

        var protectiveCandidates = nonDominated.Where(m => m.Rule.Anonymize).ToList();
        var tieBreakCandidates = protectiveCandidates.Count > 0 ? protectiveCandidates : nonDominated;

        // Last-resort deterministic tie-break: several rules remain genuinely incomparable even
        // after preferring protective ones (e.g. two incomparable rules that are both protective, or
        // both permissive). The old weighted-sum total has no particular security meaning here — it
        // is only used to guarantee a stable, repeatable pick instead of relying on list order.
        return tieBreakCandidates.Count == 1
            ? tieBreakCandidates[0].Rule
            : tieBreakCandidates.OrderByDescending(m => WeightedScore(m.Scores)).First().Rule;
    }

    /// <summary>
    /// Filters to active rules whose four patterns all match, paired with their per-dimension
    /// <see cref="LikePatternMatcher.SpecificityScore"/> tuple.
    /// </summary>
    private static List<(AnonymizationRule Rule, int[] Scores)> GetMatchingRulesWithScores(
        IReadOnlyList<AnonymizationRule> rules, string databaseName, string schemaName, string tableName, string columnName)
    {
        var matches = new List<(AnonymizationRule Rule, int[] Scores)>();

        foreach (var rule in rules)
        {
            if (!IsFullMatch(rule, databaseName, schemaName, tableName, columnName))
            {
                continue;
            }

            int[] scores =
            [
                LikePatternMatcher.SpecificityScore(rule.DatabasePattern),
                LikePatternMatcher.SpecificityScore(rule.SchemaPattern),
                LikePatternMatcher.SpecificityScore(rule.TablePattern),
                LikePatternMatcher.SpecificityScore(rule.ColumnPattern),
            ];
            matches.Add((rule, scores));
        }

        return matches;
    }

    private static bool IsFullMatch(AnonymizationRule rule, string databaseName, string schemaName, string tableName, string columnName) =>
        LikePatternMatcher.IsMatch(databaseName, rule.DatabasePattern) &&
        LikePatternMatcher.IsMatch(schemaName, rule.SchemaPattern) &&
        LikePatternMatcher.IsMatch(tableName, rule.TablePattern) &&
        LikePatternMatcher.IsMatch(columnName, rule.ColumnPattern);

    /// <summary>
    /// Returns the Pareto-maximal ("non-dominated") subset of <paramref name="matches"/> — the
    /// rules that no other matching rule dominates in every dimension (see <see cref="Dominates"/>).
    /// </summary>
    private static List<(AnonymizationRule Rule, int[] Scores)> GetNonDominated(
        List<(AnonymizationRule Rule, int[] Scores)> matches)
    {
        var nonDominated = new List<(AnonymizationRule Rule, int[] Scores)>();
        for (int i = 0; i < matches.Count; i++)
        {
            if (!IsDominatedByAnother(matches, i))
            {
                nonDominated.Add(matches[i]);
            }
        }

        return nonDominated;
    }

    private static bool IsDominatedByAnother(List<(AnonymizationRule Rule, int[] Scores)> matches, int index)
    {
        for (int j = 0; j < matches.Count; j++)
        {
            if (j != index && Dominates(matches[j].Scores, matches[index].Scores))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns whether <paramref name="x"/> Pareto-dominates <paramref name="y"/>: at least as
    /// specific in every dimension and strictly more specific in at least one.
    /// </summary>
    private static bool Dominates(int[] x, int[] y)
    {
        bool strictlyGreaterInSomeDimension = false;
        for (int i = 0; i < x.Length; i++)
        {
            if (x[i] < y[i])
            {
                return false;
            }
            if (x[i] > y[i])
            {
                strictlyGreaterInSomeDimension = true;
            }
        }
        return strictlyGreaterInSomeDimension;
    }

    /// <summary>
    /// The old weighted-sum score, kept only as the deterministic last-resort tie-break between
    /// mutually non-dominated rules that are equally (non-)protective (see
    /// <see cref="FindMostSpecificMatch"/>). Not used to rank comparable rules anymore.
    /// </summary>
    private static int WeightedScore(int[] scores) =>
        (scores[0] * 1000) + (scores[1] * 100) + (scores[2] * 10) + scores[3];

    private sealed class RuleRow
    {
        public string DatabasePattern { get; init; } = "%";
        public string SchemaPattern { get; init; } = "%";
        public string TablePattern { get; init; } = "%";
        public string ColumnPattern { get; init; } = string.Empty;
        public bool Anonymize { get; init; }
    }
}
