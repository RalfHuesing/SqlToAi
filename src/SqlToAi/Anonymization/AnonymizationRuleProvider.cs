using System.Data.Common;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Anonymization;

#pragma warning disable CA1848 // Use the LoggerMessage delegates

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
    private readonly TtlCache<string, IReadOnlyList<AnonymizationRule>> _cache = new();

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
        return await _cache.GetOrLoadAsync(
            CacheKey,
            LoadActiveRulesAsync,
            TimeSpan.FromSeconds(_options.AnonymizationRules.CacheTtlSeconds),
            cancellationToken);
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
            await using var connection = CreateConnection();
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            string failureType = SqlToAiErrorMapper.GetSafeConnectionFailureCategory(ex);
            _logger.LogError("AnonymizationRules {FailureType} while loading central rules; default masking remains active.", failureType);
            return [];
        }
    }

    private DbConnection CreateConnection()
    {
        var settings = new SecondaryConnectionSettings(
            _options.AnonymizationRules.Server,
            _options.AnonymizationRules.Database,
            _options.AnonymizationRules.UserId,
            _options.AnonymizationRules.Password,
            _options.AnonymizationRules.IntegratedSecurity,
            _options.AnonymizationRules.TrustServerCertificate,
            _options.AnonymizationRules.CommandTimeoutSeconds);

        return SecondaryConnectionBuilder.Create(
            settings,
            SecondaryConnectionBuilder.AnonymizationRulesApplicationName,
            string.Empty,
            _connectionFactory);
    }

    /// <summary>
    /// Detects the optional SchemaPattern column. Lookup failures use schema-agnostic rules;
    /// requested cancellation propagates to the caller.
    /// </summary>
    private async Task<bool> HasSchemaPatternColumnAsync(DbConnection connection, string tableName, CancellationToken cancellationToken)
    {
        try
        {
            const string checkSql = "SELECT CASE WHEN COL_LENGTH(@TableName, 'SchemaPattern') IS NOT NULL THEN 1 ELSE 0 END";
            return await connection.QueryFirstOrDefaultAsync<bool>(
                new CommandDefinition(checkSql, new { TableName = tableName }, cancellationToken: cancellationToken, commandTimeout: _options.AnonymizationRules.CommandTimeoutSeconds));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            string failureType = SqlToAiErrorMapper.GetSafeConnectionFailureCategory(ex);
            _logger.LogWarning("AnonymizationRules {FailureType} while checking the optional SchemaPattern column; using schema-agnostic rules.", failureType);
            return false;
        }
    }

    /// <summary>
    /// Picks the most specific active rule matching all four dimensions.
    /// </summary>
    /// <remarks>
    /// A rule dominates another when its specificity is at least equal in all four dimensions
    /// and greater in one. Among non-dominated rules, protective rules win; weighted specificity
    /// provides a deterministic final tie-break.
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
    /// Weights apply only to the final tie-break between equally protective, non-dominated rules.
    /// </summary>
    private const int DatabaseDimensionWeight = 1000;
    private const int SchemaDimensionWeight = 100;
    private const int TableDimensionWeight = 10;

    private static int WeightedScore(int[] scores) =>
        (scores[0] * DatabaseDimensionWeight)
        + (scores[1] * SchemaDimensionWeight)
        + (scores[2] * TableDimensionWeight)
        + scores[3];

    private sealed class RuleRow
    {
        public string DatabasePattern { get; init; } = "%";
        public string SchemaPattern { get; init; } = "%";
        public string TablePattern { get; init; } = "%";
        public string ColumnPattern { get; init; } = string.Empty;
        public bool Anonymize { get; init; }
    }
}
