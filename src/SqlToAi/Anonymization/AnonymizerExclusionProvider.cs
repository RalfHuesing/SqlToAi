#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
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
public sealed record ExclusionCheckResult(AnonymizerExclusionSet Exclusions, DateTime ExpireTime)
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
    public async Task<AnonymizerExclusionSet> GetExclusionsAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return AnonymizerExclusionSet.Empty;
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

    private async Task<AnonymizerExclusionSet> LoadExclusionsAsync(string databaseName, CancellationToken cancellationToken)
    {
        string sql = _options.Databases.AnonymizerExclusionSql;
        string? tableName = _options.Anonymizer.ExclusionTableName;

        if (string.IsNullOrWhiteSpace(sql) && string.IsNullOrWhiteSpace(tableName))
        {
            return AnonymizerExclusionSet.Empty;
        }

        var exclusions = new List<AnonymizerExclusionEntry>();

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(sql))
            {
                await LoadExclusionsFromSqlAsync(connection, sql, exclusions, databaseName, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(tableName))
            {
                await LoadExclusionsFromTableAsync(connection, tableName, exclusions, databaseName, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open connection or load anonymizer exclusions for database {DatabaseName}.", databaseName);
        }

        return new AnonymizerExclusionSet(exclusions);
    }

    private async Task LoadExclusionsFromSqlAsync(
        DbConnection connection,
        string sql,
        List<AnonymizerExclusionEntry> exclusions,
        string databaseName,
        CancellationToken cancellationToken)
    {
        try
        {
            var queryResult = await connection.QueryAsync<object>(
                new CommandDefinition(sql, cancellationToken: cancellationToken, commandTimeout: _options.SqlServer.CommandTimeoutSeconds));

            ParseExclusionRows(queryResult, exclusions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute AnonymizerExclusionSql for database {DatabaseName}.", databaseName);
        }
    }

    private async Task LoadExclusionsFromTableAsync(
        DbConnection connection,
        string tableName,
        List<AnonymizerExclusionEntry> exclusions,
        string databaseName,
        CancellationToken cancellationToken)
    {
        try
        {
            string checkSql = "SELECT QUOTENAME(OBJECT_SCHEMA_NAME(OBJECT_ID(@TableName, 'U'))) + '.' + QUOTENAME(OBJECT_NAME(OBJECT_ID(@TableName, 'U')))";
            string? safeTableName = await connection.QueryFirstOrDefaultAsync<string>(
                new CommandDefinition(checkSql, new { TableName = tableName }, cancellationToken: cancellationToken, commandTimeout: _options.SqlServer.CommandTimeoutSeconds));

            if (!string.IsNullOrEmpty(safeTableName))
            {
                bool hasSchemaColumn = await HasSchemaNameColumnAsync(connection, tableName, databaseName, cancellationToken);
                string loadSql = hasSchemaColumn
                    ? $"SELECT [TableName], [ColumnName], [SchemaName] FROM {safeTableName}"
                    : $"SELECT [TableName], [ColumnName] FROM {safeTableName}";
                var queryResult = await connection.QueryAsync<object>(
                    new CommandDefinition(loadSql, cancellationToken: cancellationToken, commandTimeout: _options.SqlServer.CommandTimeoutSeconds));

                ParseExclusionRows(queryResult, exclusions);
            }
            else
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Exclusion table {TableName} does not exist in database {DatabaseName}.", tableName, databaseName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load exclusions from table {TableName} in database {DatabaseName}.", tableName, databaseName);
        }
    }

    /// <summary>
    /// Detects, without ever throwing, whether the physical exclusion table already has the
    /// optional <c>SchemaName</c> column — so a customer database that hasn't run the migration
    /// adding it keeps working with zero-config schema-agnostic matching, exactly like before this
    /// column existed (mirrors the table-existence check above).
    /// </summary>
    private async Task<bool> HasSchemaNameColumnAsync(DbConnection connection, string tableName, string databaseName, CancellationToken cancellationToken)
    {
        try
        {
            const string checkSql = "SELECT CASE WHEN COL_LENGTH(@TableName, 'SchemaName') IS NOT NULL THEN 1 ELSE 0 END";
            return await connection.QueryFirstOrDefaultAsync<bool>(
                new CommandDefinition(checkSql, new { TableName = tableName }, cancellationToken: cancellationToken, commandTimeout: _options.SqlServer.CommandTimeoutSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for optional SchemaName column on exclusion table {TableName} in database {DatabaseName}.", tableName, databaseName);
            return false;
        }
    }

    /// <summary>
    /// Parses exclusion rows in positional column order: <c>TableName</c>, <c>ColumnName</c>, and
    /// an optional 3rd <c>SchemaName</c> column. Both the <c>AnonymizerExclusionSql</c> custom query
    /// and the physical exclusion table can supply the 3rd column; when it's absent, entries are
    /// schema-agnostic, exactly matching pre-existing behavior.
    /// </summary>
    private static void ParseExclusionRows(IEnumerable<object> queryResult, List<AnonymizerExclusionEntry> exclusions)
    {
        foreach (var rowObj in queryResult)
        {
            if (rowObj is IDictionary<string, object> row && TryParseExclusionRow(row, out var entry))
            {
                exclusions.Add(entry);
            }
        }
    }

    /// <summary>Parses a single row; fails when it doesn't carry a usable table/column pair.</summary>
    private static bool TryParseExclusionRow(IDictionary<string, object> row, out AnonymizerExclusionEntry entry)
    {
        var values = row.Values.ToList();
        string? table = values.Count >= 1 ? values[0]?.ToString()?.Trim() : null;
        string? column = values.Count >= 2 ? values[1]?.ToString()?.Trim() : null;
        string? schema = values.Count >= 3 ? values[2]?.ToString()?.Trim() : null;

        if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(column))
        {
            entry = null!;
            return false;
        }

        entry = new AnonymizerExclusionEntry(string.IsNullOrEmpty(schema) ? null : schema, table, column);
        return true;
    }
}
