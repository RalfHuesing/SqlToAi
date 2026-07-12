#nullable enable

using System.Data.Common;
using System.Text;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Metadata;
using SqlToAi.Security;

namespace SqlToAi.Database;

#pragma warning disable CA1848 // Use the LoggerMessage delegates

/// <summary>
/// Implements database schema and object exploration queries, returning cleanly formatted Markdown.
/// </summary>
public sealed class SchemaService : ISchemaService
{
    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly ISecurityGuard _securityGuard;
    private readonly IAccessLevelProvider _accessLevelProvider;
    private readonly SqlToAiOptions _options;
    private readonly ILogger<SchemaService> _logger;
    private readonly TableSchemaRenderer _tableSchemaRenderer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaService"/> class.
    /// </summary>
    public SchemaService(
        IDatabaseConnectionFactory connectionFactory,
        ISecurityGuard securityGuard,
        IAccessLevelProvider accessLevelProvider,
        IMetadataProvider metadataProvider,
        IOptions<SqlToAiOptions> options,
        ILogger<SchemaService> logger)
    {
        _connectionFactory = connectionFactory;
        _securityGuard = securityGuard;
        _accessLevelProvider = accessLevelProvider;
        _options = options.Value;
        _logger = logger;
        _tableSchemaRenderer = new TableSchemaRenderer(metadataProvider);
    }

    /// <summary>
    /// Checks static whitelisting and dynamic access level checks for the database.
    /// </summary>
    private async Task<Result> VerifyDatabaseAccessAsync(string databaseName, CancellationToken cancellationToken)
    {
        if (!_securityGuard.IsDatabaseAllowed(databaseName))
        {
            return SqlToAiError.SafetyCheckFailed($"Database '{databaseName}' is blocked by security policies (static whitelist).");
        }

        var accessLevel = await _accessLevelProvider.GetAccessLevelAsync(databaseName, cancellationToken);
        if (accessLevel == AccessLevel.None)
        {
            return SqlToAiError.SafetyCheckFailed($"Database '{databaseName}' access was denied (AccessLevel: None).");
        }

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<string>>> ListDatabasesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            var databases = await connection.QueryAsync<string>(
                new CommandDefinition("SELECT name FROM sys.databases WHERE state = 0", cancellationToken: cancellationToken));

            var allowedDbs = databases
                .Where(db => _securityGuard.IsDatabaseAllowed(db))
                .OrderBy(db => db)
                .ToList();

            return allowedDbs;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query sys.databases catalog. Falling back to configured static Allowed list.");
            var staticAllowed = _options.Databases.Allowed
                .Where(pattern => !pattern.Contains('*') && !pattern.Contains('?'))
                .OrderBy(db => db)
                .ToList();
            return staticAllowed;
        }
    }

    public async Task<Result<IReadOnlyList<string>>> SearchDatabasesAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        var dbsResult = await ListDatabasesAsync(cancellationToken);
        if (dbsResult.IsFailure)
        {
            return dbsResult.Error;
        }

        var filtered = dbsResult.Value
            .Where(db => db.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return filtered;
    }

    public async Task<Result<string>> SearchObjectsAsync(string databaseName, string searchTerm, int? maxResults = null, CancellationToken cancellationToken = default)
    {
        var accessCheck = await VerifyDatabaseAccessAsync(databaseName, cancellationToken);
        if (accessCheck.IsFailure)
        {
            return accessCheck.Error;
        }

        int limit = maxResults ?? 100;
        string sql = $"""
            SELECT TOP (@Limit)
                schema_name(schema_id) AS SchemaName,
                name AS ObjectName,
                type_desc AS TypeDescription
            FROM sys.objects
            WHERE is_ms_shipped = 0
              AND name LIKE @SearchPattern
            ORDER BY type_desc, schema_name(schema_id), name
            """;

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            var rows = await connection.QueryAsync<ObjectRow>(
                new CommandDefinition(sql, new { Limit = limit, SearchPattern = $"%{searchTerm}%" }, cancellationToken: cancellationToken));

            var renderedRows = new List<string[]>();
            foreach (var r in rows)
            {
                renderedRows.Add([ r.SchemaName, r.ObjectName, r.TypeDescription ]);
            }

            if (renderedRows.Count == 0)
            {
                return $"No objects found matching '{searchTerm}' in database '{databaseName}'.";
            }

            return RenderMarkdownTable(["Schema", "Name", "Type"], renderedRows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search objects in database {DatabaseName}.", databaseName);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    public async Task<Result<string>> GetSchemaAsync(string databaseName, string objectName, CancellationToken cancellationToken = default)
    {
        var accessCheck = await VerifyDatabaseAccessAsync(databaseName, cancellationToken);
        if (accessCheck.IsFailure)
        {
            return accessCheck.Error;
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            // 1. Identify object type
            var typeCode = await connection.QueryFirstOrDefaultAsync<string>(
                new CommandDefinition("SELECT RTRIM(type) FROM sys.objects WHERE object_id = OBJECT_ID(@ObjectName)", new { ObjectName = objectName }, cancellationToken: cancellationToken));

            if (string.IsNullOrEmpty(typeCode))
            {
                return SqlToAiError.ObjectNotFound(objectName);
            }

            // 2. Query schema depending on type code
            if (typeCode == "U")
            {
                return await _tableSchemaRenderer.GetTableSchemaMarkdownAsync(connection, databaseName, objectName, cancellationToken);
            }
            if (typeCode == "V")
            {
                string tableMarkdown = await _tableSchemaRenderer.GetTableSchemaMarkdownAsync(connection, databaseName, objectName, cancellationToken);
                string definitionMarkdown = await TableSchemaRenderer.GetViewDefinitionMarkdownAsync(connection, objectName, cancellationToken);
                return tableMarkdown + definitionMarkdown;
            }
            return await TableSchemaRenderer.GetRoutineSchemaMarkdownAsync(connection, objectName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve schema for {ObjectName} in database {DatabaseName}.", objectName, databaseName);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    public async Task<Result<string>> GetSchemaForeignKeysAsync(string databaseName, string tableName, CancellationToken cancellationToken = default)
    {
        var accessCheck = await VerifyDatabaseAccessAsync(databaseName, cancellationToken);
        if (accessCheck.IsFailure)
        {
            return accessCheck.Error;
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            return await DetailSchemaRenderer.GetSchemaForeignKeysAsync(connection, tableName, databaseName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve foreign keys for table {TableName} in database {DatabaseName}.", tableName, databaseName);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    public async Task<Result<string>> GetSchemaIndexesAsync(string databaseName, string tableName, CancellationToken cancellationToken = default)
    {
        var accessCheck = await VerifyDatabaseAccessAsync(databaseName, cancellationToken);
        if (accessCheck.IsFailure)
        {
            return accessCheck.Error;
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            return await DetailSchemaRenderer.GetSchemaIndexesAsync(connection, tableName, databaseName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve indexes for table {TableName} in database {DatabaseName}.", tableName, databaseName);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    public async Task<Result<string>> GetSchemaConstraintsAsync(string databaseName, string tableName, CancellationToken cancellationToken = default)
    {
        var accessCheck = await VerifyDatabaseAccessAsync(databaseName, cancellationToken);
        if (accessCheck.IsFailure)
        {
            return accessCheck.Error;
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            return await DetailSchemaRenderer.GetSchemaConstraintsAsync(connection, tableName, databaseName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve constraints for table {TableName} in database {DatabaseName}.", tableName, databaseName);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    public async Task<Result<string>> GetTriggerDefinitionAsync(string databaseName, string tableName, string triggerName, CancellationToken cancellationToken = default)
    {
        var accessCheck = await VerifyDatabaseAccessAsync(databaseName, cancellationToken);
        if (accessCheck.IsFailure)
        {
            return accessCheck.Error;
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            return await DetailSchemaRenderer.GetTriggerDefinitionAsync(connection, tableName, triggerName, databaseName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve trigger DDL for {TriggerName} in database {DatabaseName}.", triggerName, databaseName);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    public async Task<Result<string>> GetObjectReferencesAsync(string databaseName, string objectName, CancellationToken cancellationToken = default)
    {
        var accessCheck = await VerifyDatabaseAccessAsync(databaseName, cancellationToken);
        if (accessCheck.IsFailure)
        {
            return accessCheck.Error;
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            return await DetailSchemaRenderer.GetObjectReferencesAsync(connection, objectName, databaseName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve referencing entities for {ObjectName} in database {DatabaseName}.", objectName, databaseName);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    public async Task<Result<string>> GetRoutineParametersAsync(string databaseName, string routineName, CancellationToken cancellationToken = default)
    {
        var accessCheck = await VerifyDatabaseAccessAsync(databaseName, cancellationToken);
        if (accessCheck.IsFailure)
        {
            return accessCheck.Error;
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            return await DetailSchemaRenderer.GetRoutineParametersAsync(connection, routineName, databaseName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve routine parameters for {RoutineName} in database {DatabaseName}.", routineName, databaseName);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    private static string RenderMarkdownTable(string[] headers, List<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append("| ").Append(string.Join(" | ", headers)).AppendLine(" |");
        sb.Append("| ").Append(string.Join(" | ", headers.Select(_ => "---"))).AppendLine(" |");
        foreach (var row in rows)
        {
            sb.Append("| ").Append(string.Join(" | ", row.Select(r => r?.Replace("|", "\\|") ?? ""))).AppendLine(" |");
        }
        return sb.ToString();
    }

    private sealed class ObjectRow
    {
        public string SchemaName { get; init; } = string.Empty;
        public string ObjectName { get; init; } = string.Empty;
        public string TypeDescription { get; init; } = string.Empty;
    }
}
