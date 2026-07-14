#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;

namespace SqlToAi.Anonymization;

#pragma warning disable CA1848 // Use the LoggerMessage delegates

/// <summary>
/// Cache-backed container for exclusion query evaluation lifetime.
/// </summary>
public sealed record ExclusionCheckResult(HashSet<string> Exclusions, DateTime ExpireTime)
{
    /// <summary>Checks whether this cache entry has passed its TTL.</summary>
    public bool IsExpired(DateTime currentTime) => currentTime >= ExpireTime;
}

/// <summary>
/// Implements database-specific caching and evaluation of anonymization exclusions using SQL queries.
/// </summary>
public sealed class AnonymizerExclusionProvider : IAnonymizerExclusionProvider
{
    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly SqlToAiOptions _options;
    private readonly ILogger<AnonymizerExclusionProvider> _logger;
    private readonly ConcurrentDictionary<string, ExclusionCheckResult> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="AnonymizerExclusionProvider"/> class.
    /// </summary>
    public AnonymizerExclusionProvider(
        IDatabaseConnectionFactory connectionFactory,
        IOptions<SqlToAiOptions> options,
        ILogger<AnonymizerExclusionProvider> logger)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<HashSet<string>> GetExclusionsAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return [];
        }

        var currentTime = DateTime.UtcNow;

        if (_cache.TryGetValue(databaseName, out var cachedResult) && !cachedResult.IsExpired(currentTime))
        {
            return cachedResult.Exclusions;
        }

        var exclusions = await LoadExclusionsAsync(databaseName, cancellationToken);

        var ttl = _options.Databases.CacheTtlSeconds > 0 ? _options.Databases.CacheTtlSeconds : 300;
        var expireTime = currentTime.AddSeconds(ttl);
        _cache[databaseName] = new ExclusionCheckResult(exclusions, expireTime);

        return exclusions;
    }

    private async Task<HashSet<string>> LoadExclusionsAsync(string databaseName, CancellationToken cancellationToken)
    {
        string sql = _options.Databases.AnonymizerExclusionSql;

        if (string.IsNullOrWhiteSpace(sql))
        {
            return [];
        }

        var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            var queryResult = await connection.QueryAsync<object>(
                new CommandDefinition(sql, cancellationToken: cancellationToken, commandTimeout: _options.SqlServer.CommandTimeoutSeconds));

            foreach (var rowObj in queryResult)
            {
                if (rowObj is IDictionary<string, object> row)
                {
                    var values = row.Values.ToList();
                    if (values.Count >= 2)
                    {
                        string? table = values[0]?.ToString()?.Trim();
                        string? column = values[1]?.ToString()?.Trim();

                        if (!string.IsNullOrEmpty(table) && !string.IsNullOrEmpty(column))
                        {
                            exclusions.Add($"{table}.{column}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load anonymizer exclusions for database {DatabaseName}.", databaseName);
        }

        return exclusions;
    }
}
