#nullable enable

using System.Collections.Concurrent;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Security;

#pragma warning disable CA1848 // Use the LoggerMessage delegates

/// <summary>
/// Implements dynamic checking of database access levels using a SQL probe query and thread-safe caching.
/// </summary>
public sealed class AccessLevelProvider : IAccessLevelProvider
{
    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly SqlToAiOptions _options;
    private readonly ILogger<AccessLevelProvider> _logger;
    private readonly ConcurrentDictionary<string, AccessCheckResult> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="AccessLevelProvider"/> class.
    /// </summary>
    /// <param name="connectionFactory">Connection factory to open SQL Server connections.</param>
    /// <param name="options">Options containing the safety probe SQL and caching parameters.</param>
    /// <param name="logger">System logger.</param>
    public AccessLevelProvider(
        IDatabaseConnectionFactory connectionFactory,
        IOptions<SqlToAiOptions> options,
        ILogger<AccessLevelProvider> logger)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves the access level allowed for the specified database. It queries the database if the cache is missing or expired.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The calculated <see cref="AccessLevel"/>.</returns>
    public async Task<AccessLevel> GetAccessLevelAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return AccessLevel.None;
        }

        var currentTime = DateTime.UtcNow;

        if (_cache.TryGetValue(databaseName, out var cachedResult) && !cachedResult.IsExpired(currentTime))
        {
            return cachedResult.Level;
        }

        // Calculate new access level
        AccessLevel level = await QueryAccessLevelAsync(databaseName, cancellationToken);

        // Cache the result
        var ttl = _options.Databases.CacheTtlSeconds > 0 ? _options.Databases.CacheTtlSeconds : 300;
        var expireTime = currentTime.AddSeconds(ttl);
        _cache[databaseName] = new AccessCheckResult(level, expireTime);

        return level;
    }

    private async Task<AccessLevel> QueryAccessLevelAsync(string databaseName, CancellationToken cancellationToken)
    {
        string sql = _options.Databases.AccessCheckSql;

        // If no dynamic access check query is configured, fail safe: read-only and anonymized.
        // There is no per-database signal to trust otherwise, so ReadWrite is never assumed.
        if (string.IsNullOrWhiteSpace(sql))
        {
            return AccessLevel.ReadOnlyAnonymized;
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            var queryResult = await connection.QueryFirstOrDefaultAsync<dynamic>(
                new CommandDefinition(sql, cancellationToken: cancellationToken, commandTimeout: _options.SqlDatabase.CommandTimeoutSeconds));

            if (queryResult is null)
            {
                _logger.LogWarning("AccessCheckSql executed but returned no rows/results for database {DatabaseName}.", databaseName);
                return AccessLevel.None;
            }

            return ParseResult(queryResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dynamic AccessCheckSql failed for database {DatabaseName}. Defaulting to AccessLevel.None.", databaseName);
            return AccessLevel.None;
        }
    }

    private static AccessLevel ParseResult(dynamic result)
    {
        object? rawValue = null;

        if (result is IDictionary<string, object> row)
        {
            // Attempt to find a column named AccessLevel (case-insensitive)
            var key = row.Keys.FirstOrDefault(k => string.Equals(k, "AccessLevel", StringComparison.OrdinalIgnoreCase));
            if (key != null)
            {
                rawValue = row[key];
            }
            else
            {
                // Fallback: If query returned a single-column row, use the first column's value
                rawValue = row.Values.FirstOrDefault();
            }
        }
        else
        {
            // If the query returned a scalar directly
            rawValue = result;
        }

        return ParseAccessLevel(rawValue);
    }

    private static AccessLevel ParseAccessLevel(object? val)
    {
        if (val is null)
        {
            return AccessLevel.None;
        }

        string strVal = val.ToString()?.Trim() ?? string.Empty;

        // 1. Try parsing as integer
        if (int.TryParse(strVal, out int intVal))
        {
            return intVal switch
            {
                0 => AccessLevel.None,
                1 => AccessLevel.SchemaOnly,
                2 => AccessLevel.ReadOnlyAnonymized,
                3 => AccessLevel.ReadOnly,
                4 => AccessLevel.ReadWrite,
                _ => AccessLevel.None
            };
        }

        // 2. Try parsing as AccessLevel Enum string name (handles aliases automatically)
        if (Enum.TryParse<AccessLevel>(strVal, true, out var parsedEnum))
        {
            return parsedEnum;
        }

        // 3. String value fallback aliases
        if (string.Equals(strVal, "ReadData", StringComparison.OrdinalIgnoreCase))
        {
            return AccessLevel.ReadOnly;
        }

        if (string.Equals(strVal, "ReadDataAnonymized", StringComparison.OrdinalIgnoreCase))
        {
            return AccessLevel.ReadOnlyAnonymized;
        }

        return AccessLevel.None;
    }
}
