using System.Data.Common;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
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
    private DatabaseTargetResolver? _targets;

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

    /// <inheritdoc/>
    public Task<string?> GetTableDescriptionAsync(DatabaseTargetContext target, SchemaObjectIdentity identity, CancellationToken cancellationToken = default)
        => GetTableDescriptionCoreAsync(target, identity.QualifiedName, identity.ObjectName, cancellationToken);

    /// <inheritdoc/>
    public Task<string?> GetTableDescriptionAsync(DatabaseTargetContext target, string tableName, CancellationToken cancellationToken = default)
        => GetTableDescriptionCoreAsync(target, tableName, null, cancellationToken);

    private async Task<string?> GetTableDescriptionCoreAsync(DatabaseTargetContext target, string tableName, string? resolvedName, CancellationToken cancellationToken)
    {
        if (!TryGetOwnerOptions(target.ServerAlias, out NamedServerOptions? ownerServer, out MetadataProviderOptions? metadata))
        {
            // Unknown owner or absent provider settings: keep native descriptions.
            return null;
        }

        if (!metadata.Enabled || string.IsNullOrWhiteSpace(tableName))
        {
            return null;
        }

        (string sql, string queryTableName) = BuildTableQuery(metadata, tableName, resolvedName);

        try
        {
            await using var connection = CreateOwnerConnection(target, ownerServer, metadata);
            await connection.OpenAsync(cancellationToken);

            string? description = await connection.QueryFirstOrDefaultAsync<string>(
                new CommandDefinition(sql, new { TableName = queryTableName }, cancellationToken: cancellationToken));

            return description;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            string failureType = SqlToAiErrorMapper.GetSafeConnectionFailureCategory(ex);
            _logger.LogError("MetadataProvider {FailureType} while retrieving a table description.", failureType);
            return null;
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<string, string>> GetColumnDescriptionsAsync(DatabaseTargetContext target, SchemaObjectIdentity identity, CancellationToken cancellationToken = default)
        => GetColumnDescriptionsCoreAsync(target, identity.QualifiedName, identity.ObjectName, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<string, string>> GetColumnDescriptionsAsync(DatabaseTargetContext target, string tableName, CancellationToken cancellationToken = default)
        => GetColumnDescriptionsCoreAsync(target, tableName, null, cancellationToken);

    private async Task<IReadOnlyDictionary<string, string>> GetColumnDescriptionsCoreAsync(DatabaseTargetContext target, string tableName, string? resolvedName, CancellationToken cancellationToken)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!TryGetOwnerOptions(target.ServerAlias, out NamedServerOptions? ownerServer, out MetadataProviderOptions? metadata))
        {
            // Unknown owner or absent provider settings: keep native descriptions.
            return dict;
        }

        if (!metadata.Enabled || string.IsNullOrWhiteSpace(tableName))
        {
            return dict;
        }

        (string sql, string queryTableName) = BuildColumnQuery(metadata, tableName, resolvedName);

        try
        {
            await using var connection = CreateOwnerConnection(target, ownerServer, metadata);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            string failureType = SqlToAiErrorMapper.GetSafeConnectionFailureCategory(ex);
            _logger.LogError("MetadataProvider {FailureType} while retrieving column descriptions.", failureType);
        }

        return dict;
    }

    /// <summary>
    /// Retrieves the description for a table.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="tableName">The name of the table.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The table description, or null if not found.</returns>
    public async Task<string?> GetTableDescriptionAsync(string databaseName, string tableName, CancellationToken cancellationToken = default)
        => await GetTableDescriptionCoreAsync(databaseName, tableName, null, cancellationToken);

    public Task<string?> GetTableDescriptionAsync(string databaseName, SchemaObjectIdentity identity, CancellationToken cancellationToken = default)
        => GetTableDescriptionCoreAsync(databaseName, identity.QualifiedName, identity.ObjectName, cancellationToken);

    private async Task<string?> GetTableDescriptionCoreAsync(string databaseName, string tableName, string? resolvedName, CancellationToken cancellationToken)
    {
        if (!_options.MetadataProvider.Enabled || string.IsNullOrWhiteSpace(tableName))
        {
            return null;
        }

        (string sql, string queryTableName) = BuildTableQuery(_options.MetadataProvider, tableName, resolvedName);

        try
        {
            await using var connection = CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            string? description = await connection.QueryFirstOrDefaultAsync<string>(
                new CommandDefinition(sql, new { TableName = queryTableName }, cancellationToken: cancellationToken));

            return description;
        }
        catch (Exception ex)
        {
            string failureType = SqlToAiErrorMapper.GetSafeConnectionFailureCategory(ex);
            _logger.LogError("MetadataProvider {FailureType} while retrieving a table description.", failureType);
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
        => await GetColumnDescriptionsCoreAsync(databaseName, tableName, null, cancellationToken);

    public Task<IReadOnlyDictionary<string, string>> GetColumnDescriptionsAsync(string databaseName, SchemaObjectIdentity identity, CancellationToken cancellationToken = default)
        => GetColumnDescriptionsCoreAsync(databaseName, identity.QualifiedName, identity.ObjectName, cancellationToken);

    private async Task<IReadOnlyDictionary<string, string>> GetColumnDescriptionsCoreAsync(string databaseName, string tableName, string? resolvedName, CancellationToken cancellationToken)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!_options.MetadataProvider.Enabled || string.IsNullOrWhiteSpace(tableName))
        {
            return dict;
        }

        (string sql, string queryTableName) = BuildColumnQuery(_options.MetadataProvider, tableName, resolvedName);

        try
        {
            await using var connection = CreateConnection(databaseName);
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
            string failureType = SqlToAiErrorMapper.GetSafeConnectionFailureCategory(ex);
            _logger.LogError("MetadataProvider {FailureType} while retrieving column descriptions.", failureType);
        }

        return dict;
    }

    private static (string Sql, string QueryTableName) BuildTableQuery(MetadataProviderOptions metadata, string tableName, string? resolvedName)
    {
        if (!string.IsNullOrWhiteSpace(metadata.TableMetadataQuery))
        {
            // Clean schema prefix from tableName for custom metadata queries (e.g. "dbo.KHKVKBelege" -> "KHKVKBelege")
            return (metadata.TableMetadataQuery, resolvedName ?? tableName[(tableName.LastIndexOf('.') + 1)..]);
        }

        return (NativeTableQuery, tableName);
    }

    private static (string Sql, string QueryTableName) BuildColumnQuery(MetadataProviderOptions metadata, string tableName, string? resolvedName)
    {
        if (!string.IsNullOrWhiteSpace(metadata.ColumnMetadataQuery))
        {
            // Clean schema prefix from tableName for custom metadata queries (e.g. "dbo.KHKVKBelege" -> "KHKVKBelege")
            return (metadata.ColumnMetadataQuery, resolvedName ?? tableName[(tableName.LastIndexOf('.') + 1)..]);
        }

        return (NativeColumnQuery, tableName);
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

    private bool TryGetOwnerOptions(string serverAlias, out NamedServerOptions ownerServer, out MetadataProviderOptions metadata)
    {
        ownerServer = new NamedServerOptions();
        metadata = _options.MetadataProvider;
        if (DatabaseTargetParser.IsValidAlias(serverAlias)
            && Targets().TryGetServerOptions(serverAlias, out NamedServerOptions? resolved)
            && resolved is not null)
        {
            ownerServer = resolved;
            metadata = resolved.MetadataProvider;
            return true;
        }

        return false;
    }

    private DatabaseTargetResolver Targets() => _targets ??= new DatabaseTargetResolver(_options);

    private DbConnection CreateOwnerConnection(DatabaseTargetContext target, NamedServerOptions ownerServer, MetadataProviderOptions metadata)
        => SecondaryConnectionBuilder.CreateForOwner(
            target, ownerServer, metadata, SecondaryConnectionBuilder.MetadataApplicationName, _connectionFactory);

    private DbConnection CreateConnection(string databaseName)
    {
        var settings = new SecondaryConnectionSettings(
            _options.MetadataProvider.Server,
            _options.MetadataProvider.Database,
            _options.MetadataProvider.UserId,
            _options.MetadataProvider.Password,
            _options.MetadataProvider.IntegratedSecurity,
            _options.MetadataProvider.TrustServerCertificate,
            _options.MetadataProvider.CommandTimeoutSeconds);

        return SecondaryConnectionBuilder.Create(settings, SecondaryConnectionBuilder.MetadataApplicationName, databaseName, _connectionFactory);
    }
}
