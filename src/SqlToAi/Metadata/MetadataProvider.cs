#nullable enable

using System.Data.Common;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
#pragma warning disable CA1848 // Use the LoggerMessage delegates

namespace SqlToAi.Metadata;

/// <summary>
/// Provides table and column descriptions by querying native extended properties (MS_Description) or custom metadata tables.
/// </summary>
public sealed class MetadataProvider : IMetadataProvider
{
    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly SqlToAiOptions _options;
    private readonly ILogger<MetadataProvider> _logger;

    // Standard native queries for SQL Server Extended Properties
    private const string NativeTableQuery = """
        SELECT CAST(value AS NVARCHAR(MAX)) AS Description
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(@TableName)
          AND minor_id = 0
          AND name = 'MS_Description'
        """;

    private const string NativeColumnQuery = """
        SELECT 
            c.name AS ColumnName,
            CAST(ep.value AS NVARCHAR(MAX)) AS Description
        FROM sys.extended_properties ep
        INNER JOIN sys.columns c ON ep.major_id = c.object_id AND ep.minor_id = c.column_id
        WHERE ep.class = 1
          AND ep.major_id = OBJECT_ID(@TableName)
          AND ep.name = 'MS_Description'
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataProvider"/> class.
    /// </summary>
    /// <param name="connectionFactory">The database connection factory.</param>
    /// <param name="options">Options containing metadata provider settings.</param>
    /// <param name="logger">System logger.</param>
    public MetadataProvider(
        IDatabaseConnectionFactory connectionFactory,
        IOptions<SqlToAiOptions> options,
        ILogger<MetadataProvider> logger)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves the description for a table.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="tableName">The name of the table.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The table description, or null if not found.</returns>
    public async Task<string?> GetTableDescriptionAsync(string databaseName, string tableName, CancellationToken cancellationToken = default)
    {
        if (!_options.MetadataProvider.Enabled || string.IsNullOrWhiteSpace(tableName))
        {
            return null;
        }

        string sql;
        string queryTableName = tableName;

        if (!string.IsNullOrWhiteSpace(_options.MetadataProvider.TableMetadataQuery))
        {
            sql = _options.MetadataProvider.TableMetadataQuery;
            // Clean schema prefix from tableName for custom metadata queries (e.g. "dbo.KHKVKBelege" -> "KHKVKBelege")
            int dotIndex = tableName.LastIndexOf('.');
            if (dotIndex >= 0)
            {
                queryTableName = tableName[(dotIndex + 1)..];
            }
        }
        else
        {
            sql = NativeTableQuery;
        }

        try
        {
            using var connection = CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            string? description = await connection.QueryFirstOrDefaultAsync<string>(
                new CommandDefinition(sql, new { TableName = queryTableName }, cancellationToken: cancellationToken));

            return description;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve table description for {TableName} in database {DatabaseName}.", tableName, databaseName);
            return null;
        }
    }

    /// <summary>
    /// Retrieves a dictionary mapping column names to their descriptions for a specific table.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="tableName">The name of the table.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary of column names to descriptions.</returns>
    public async Task<IReadOnlyDictionary<string, string>> GetColumnDescriptionsAsync(string databaseName, string tableName, CancellationToken cancellationToken = default)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!_options.MetadataProvider.Enabled || string.IsNullOrWhiteSpace(tableName))
        {
            return dict;
        }

        string sql;
        string queryTableName = tableName;

        if (!string.IsNullOrWhiteSpace(_options.MetadataProvider.ColumnMetadataQuery))
        {
            sql = _options.MetadataProvider.ColumnMetadataQuery;
            // Clean schema prefix from tableName for custom metadata queries (e.g. "dbo.KHKVKBelege" -> "KHKVKBelege")
            int dotIndex = tableName.LastIndexOf('.');
            if (dotIndex >= 0)
            {
                queryTableName = tableName[(dotIndex + 1)..];
            }
        }
        else
        {
            sql = NativeColumnQuery;
        }

        try
        {
            using var connection = CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            var rows = await connection.QueryAsync<object>(
                new CommandDefinition(sql, new { TableName = queryTableName }, cancellationToken: cancellationToken));

            foreach (var row in rows)
            {
                if (row is IDictionary<string, object> dictRow)
                {
                    ExtractColumnDescription(dictRow, dict);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve column descriptions for table {TableName} in database {DatabaseName}.", tableName, databaseName);
        }

        return dict;
    }

    private static void ExtractColumnDescription(IDictionary<string, object> dictRow, Dictionary<string, string> dict)
    {
        var colKey = dictRow.Keys.FirstOrDefault(k => string.Equals(k, "ColumnName", StringComparison.OrdinalIgnoreCase));
        var descKey = dictRow.Keys.FirstOrDefault(k => string.Equals(k, "Description", StringComparison.OrdinalIgnoreCase));

        if (colKey != null && descKey != null)
        {
            string? colName = dictRow[colKey]?.ToString();
            string? desc = dictRow[descKey]?.ToString();

            if (!string.IsNullOrWhiteSpace(colName) && desc != null)
            {
                dict[colName] = desc;
            }
        }
    }

    private DbConnection CreateConnection(string databaseName)
    {
        var settings = new SecondaryConnectionSettings(
            _options.MetadataProvider.Server,
            _options.MetadataProvider.Database,
            _options.MetadataProvider.UserId,
            _options.MetadataProvider.Password,
            _options.MetadataProvider.IntegratedSecurity,
            _options.MetadataProvider.CommandTimeoutSeconds);

        return SecondaryConnectionBuilder.Create(settings, "SqlToAi-Metadata", databaseName, _connectionFactory);
    }
}
