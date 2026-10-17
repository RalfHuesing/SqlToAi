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
/// Rule caches are partitioned by policy owner (canonical server alias) and effective source
/// identity (endpoint, actual source database including per-target fallback, rules table), so
/// owners never borrow foreign cached exemptions.
/// </summary>
public sealed class AnonymizationRuleProvider : IAnonymizationRuleProvider
{
    /// <summary>
    /// Cache identity for one rule load. <see cref="PolicyOwner"/> is the canonical server alias
    /// (empty for the legacy single-server path), so aliases remain distinct owners even when they
    /// point at the same endpoint. <see cref="Endpoint"/> and <see cref="SourceDatabase"/> capture
    /// the effective source including per-target fallback databases, and <see cref="RulesTable"/>
    /// the configured rule table name.
    /// </summary>
    private sealed record RuleCacheKey(string PolicyOwner, string Endpoint, string SourceDatabase, string RulesTable);

    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly SqlToAiOptions _options;
    private readonly ILogger<AnonymizationRuleProvider> _logger;
    private readonly TtlCache<RuleCacheKey, IReadOnlyList<AnonymizationRule>> _cache = new();
    private DatabaseTargetResolver? _targets;

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

    /// <inheritdoc/>
    public async Task<bool> IsExcludedAsync(DatabaseTargetContext target, string schemaName, string tableName, string columnName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return false;
        }

        if (!TryGetOwnerOptions(target.ServerAlias, out NamedServerOptions? ownerServer, out AnonymizationRulesOptions? rules))
        {
            // Unknown owner or absent provider settings: no central-rule exemptions, default masking.
            return false;
        }

        if (!rules.Enabled)
        {
            return false;
        }

        RuleCacheKey cacheKey;
        try
        {
            cacheKey = BuildCacheKey(target, ownerServer, rules);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            // Misconfigured source (e.g. dedicated server without an explicit database):
            // fail-safe to default masking instead of surfacing configuration details.
            string failureType = SqlToAiErrorMapper.GetSafeConnectionFailureCategory(ex);
            _logger.LogError("AnonymizationRules {FailureType} while resolving the rule source; default masking remains active.", failureType);
            return false;
        }

        Func<DbConnection> createConnection = () => SecondaryConnectionBuilder.CreateForOwner(
            target, ownerServer, rules, SecondaryConnectionBuilder.AnonymizationRulesApplicationName, _connectionFactory);

        IReadOnlyList<AnonymizationRule> activeRules = await _cache.GetOrLoadAsync(
            cacheKey,
            token => LoadRulesFromAsync(createConnection, rules, token),
            TimeSpan.FromSeconds(rules.CacheTtlSeconds),
            cancellationToken);

        AnonymizationRule? match = FindMostSpecificMatch(
            activeRules, target.DatabaseName, schemaName ?? string.Empty, tableName ?? string.Empty, columnName);
        return match is not null && !match.Anonymize;
    }

    private async Task<IReadOnlyList<AnonymizationRule>> GetActiveRulesAsync(CancellationToken cancellationToken)
    {
        var rules = _options.AnonymizationRules;
        RuleCacheKey cacheKey = new(
            PolicyOwner: string.Empty,
            Endpoint: !string.IsNullOrWhiteSpace(rules.Server) ? rules.Server : _options.SqlServer.Server,
            SourceDatabase: rules.Database ?? string.Empty,
            RulesTable: rules.TableName);

        return await _cache.GetOrLoadAsync(
            cacheKey,
            LoadActiveRulesAsync,
            TimeSpan.FromSeconds(rules.CacheTtlSeconds),
            cancellationToken);
    }

    private Task<IReadOnlyList<AnonymizationRule>> LoadActiveRulesAsync(CancellationToken cancellationToken)
        => LoadRulesFromAsync(CreateConnection, _options.AnonymizationRules, cancellationToken);

    /// <summary>
    /// Loads the active rules through the supplied connection factory for one provider settings
    /// slice. Load failures return an empty rule set (default masking); requested cancellation
    /// propagates and caches nothing.
    /// </summary>
    private async Task<IReadOnlyList<AnonymizationRule>> LoadRulesFromAsync(
        Func<DbConnection> createConnection,
        AnonymizationRulesOptions rules,
        CancellationToken cancellationToken)
    {
        string tableName = rules.TableName;
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return [];
        }

        try
        {
            await using var connection = createConnection();
            await connection.OpenAsync(cancellationToken);

            string? safeTableName = await connection.QueryFirstOrDefaultAsync<string>(
                new CommandDefinition(
                    "SELECT QUOTENAME(OBJECT_SCHEMA_NAME(OBJECT_ID(@TableName, 'U'))) + '.' + QUOTENAME(OBJECT_NAME(OBJECT_ID(@TableName, 'U')))",
                    new { TableName = tableName },
                    cancellationToken: cancellationToken,
                    commandTimeout: rules.CommandTimeoutSeconds));

            if (string.IsNullOrEmpty(safeTableName))
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Anonymization rule table {TableName} does not exist.", tableName);
                }
                return [];
            }

            bool hasSchemaPattern = await HasSchemaPatternColumnAsync(connection, rules, cancellationToken);
            string columnList = hasSchemaPattern
                ? "[DatabasePattern], [SchemaPattern], [TablePattern], [ColumnPattern], [Anonymize]"
                : "[DatabasePattern], [TablePattern], [ColumnPattern], [Anonymize]";
            string sql = $"SELECT {columnList} FROM {safeTableName} WHERE [IsActive] = 1";
            var rows = await connection.QueryAsync<RuleRow>(
                new CommandDefinition(sql, cancellationToken: cancellationToken, commandTimeout: rules.CommandTimeoutSeconds));

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

    private RuleCacheKey BuildCacheKey(DatabaseTargetContext target, NamedServerOptions ownerServer, AnonymizationRulesOptions rules)
    {
        ProviderSourceResolution source = SecondaryConnectionBuilder.ResolveSource(
            ownerServer, rules, target.DatabaseName, SecondaryConnectionBuilder.AnonymizationRulesApplicationName);
        return new RuleCacheKey(target.ServerAlias, source.Endpoint, source.SourceDatabase, rules.TableName);
    }

    private bool TryGetOwnerOptions(string serverAlias, out NamedServerOptions ownerServer, out AnonymizationRulesOptions rules)
    {
        ownerServer = new NamedServerOptions();
        rules = _options.AnonymizationRules;
        if (DatabaseTargetParser.IsValidAlias(serverAlias)
            && Targets().TryGetServerOptions(serverAlias, out NamedServerOptions? resolved)
            && resolved is not null)
        {
            ownerServer = resolved;
            rules = resolved.AnonymizationRules;
            return true;
        }

        return false;
    }

    private DatabaseTargetResolver Targets() => _targets ??= new DatabaseTargetResolver(_options);

    private DbConnection CreateConnection()
    {
        var rules = _options.AnonymizationRules;
        var settings = new SecondaryConnectionSettings(
            rules.Server,
            rules.Database,
            rules.UserId,
            rules.Password,
            rules.IntegratedSecurity,
            rules.TrustServerCertificate,
            rules.CommandTimeoutSeconds);

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
    private async Task<bool> HasSchemaPatternColumnAsync(DbConnection connection, AnonymizationRulesOptions rules, CancellationToken cancellationToken)
    {
        try
        {
            const string checkSql = "SELECT CASE WHEN COL_LENGTH(@TableName, 'SchemaPattern') IS NOT NULL THEN 1 ELSE 0 END";
            return await connection.QueryFirstOrDefaultAsync<bool>(
                new CommandDefinition(checkSql, new { TableName = rules.TableName }, cancellationToken: cancellationToken, commandTimeout: rules.CommandTimeoutSeconds));
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
