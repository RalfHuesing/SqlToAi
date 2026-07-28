#nullable enable

using System.Data.Common;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
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
        IAnonymizationPolicyResolver policyResolver,
        IOptions<SqlToAiOptions> options,
        ILogger<SchemaService> logger)
    {
        _connectionFactory = connectionFactory;
        _securityGuard = securityGuard;
        _accessLevelProvider = accessLevelProvider;
        _options = options.Value;
        _logger = logger;
        _tableSchemaRenderer = new TableSchemaRenderer(metadataProvider, policyResolver);
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
            _logger.LogWarning(ex, "Failed to query sys.databases catalog. Falling back to configured static database lists.");
            var staticAllowed = _options.Databases.ReadWrite
                .Concat(_options.Databases.ReadOnly)
                .Concat(_options.Databases.ReadOnlyAnonymized)
                .Concat(_options.Databases.SchemaOnly)
                .Distinct(StringComparer.OrdinalIgnoreCase)
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

    public async Task<Result<string>> SearchObjectsAsync(string databaseName, string searchTerm, int? maxResults = null, string? objectType = null, CancellationToken cancellationToken = default)
    {
        var accessCheck = await VerifyDatabaseAccessAsync(databaseName, cancellationToken);
        if (accessCheck.IsFailure)
        {
            return accessCheck.Error;
        }

        int limit = maxResults ?? 100;
        // Rank primary object types (tables, views, routines, triggers) ahead of the far more
        // numerous constraint objects (FK/PK/DEFAULT/CHECK), which otherwise dominate alphabetically
        // (e.g. "FOREIGN_KEY_CONSTRAINT" < "USER_TABLE") and crowd real objects out of the TOP N
        // when no object_type filter is given.
        string sql = $"""
            SELECT TOP (@Limit)
                schema_name(schema_id) AS SchemaName,
                name AS ObjectName,
                type_desc AS TypeDescription
            FROM sys.objects
            WHERE is_ms_shipped = 0
              AND name LIKE @SearchPattern
              AND (@TypeFilter IS NULL OR type_desc LIKE @TypeFilter)
            ORDER BY
                CASE type_desc
                    WHEN 'USER_TABLE' THEN 0
                    WHEN 'VIEW' THEN 1
                    WHEN 'SQL_STORED_PROCEDURE' THEN 2
                    WHEN 'SQL_SCALAR_FUNCTION' THEN 2
                    WHEN 'SQL_TABLE_VALUED_FUNCTION' THEN 2
                    WHEN 'SQL_INLINE_TABLE_VALUED_FUNCTION' THEN 2
                    WHEN 'SQL_TRIGGER' THEN 3
                    ELSE 9
                END,
                schema_name(schema_id), name
            """;

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            var rows = await connection.QueryAsync<ObjectRow>(
                new CommandDefinition(sql, new { Limit = limit, SearchPattern = $"%{searchTerm}%", TypeFilter = objectType }, cancellationToken: cancellationToken));

            var renderedRows = new List<string[]>();
            foreach (var r in rows)
            {
                renderedRows.Add([ r.SchemaName, r.ObjectName, r.TypeDescription ]);
            }

            if (renderedRows.Count == 0)
            {
                return $"No objects found matching '{searchTerm}' in database '{databaseName}'.";
            }

            return MarkdownTableRenderer.Render(["Schema", "Name", "Type"], renderedRows);
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

    public Task<Result<string>> GetSchemaForeignKeysAsync(string databaseName, string tableName, CancellationToken cancellationToken = default) =>
        ExecuteDetailQueryAsync(databaseName, tableName, "foreign keys",
            (connection, ct) => DetailSchemaRenderer.GetSchemaForeignKeysAsync(connection, tableName, databaseName, ct),
            cancellationToken);

    public Task<Result<string>> GetSchemaIndexesAsync(string databaseName, string tableName, CancellationToken cancellationToken = default) =>
        ExecuteDetailQueryAsync(databaseName, tableName, "indexes",
            (connection, ct) => DetailSchemaRenderer.GetSchemaIndexesAsync(connection, tableName, databaseName, ct),
            cancellationToken);

    public Task<Result<string>> GetSchemaConstraintsAsync(string databaseName, string tableName, CancellationToken cancellationToken = default) =>
        ExecuteDetailQueryAsync(databaseName, tableName, "constraints",
            (connection, ct) => DetailSchemaRenderer.GetSchemaConstraintsAsync(connection, tableName, databaseName, ct),
            cancellationToken);

    public Task<Result<string>> GetTriggerDefinitionAsync(string databaseName, string tableName, string triggerName, CancellationToken cancellationToken = default) =>
        ExecuteDetailQueryAsync(databaseName, triggerName, "trigger DDL",
            (connection, ct) => DetailSchemaRenderer.GetTriggerDefinitionAsync(connection, tableName, triggerName, databaseName, ct),
            cancellationToken);

    public Task<Result<string>> GetObjectReferencesAsync(string databaseName, string objectName, CancellationToken cancellationToken = default) =>
        ExecuteDetailQueryAsync(databaseName, objectName, "referencing entities",
            (connection, ct) => DetailSchemaRenderer.GetObjectReferencesAsync(connection, objectName, databaseName, ct),
            cancellationToken);

    public Task<Result<string>> GetRoutineParametersAsync(string databaseName, string routineName, CancellationToken cancellationToken = default) =>
        ExecuteDetailQueryAsync(databaseName, routineName, "routine parameters",
            (connection, ct) => DetailSchemaRenderer.GetRoutineParametersAsync(connection, routineName, databaseName, ct),
            cancellationToken);

    /// <summary>
    /// Common skeleton for the six detail-query delegations: verify access, open a connection,
    /// run a single DetailSchemaRenderer call inside a try/catch, log and translate any
    /// exception to <see cref="SqlToAiError.QueryError"/>.
    /// </summary>
    private async Task<Result<string>> ExecuteDetailQueryAsync(
        string databaseName,
        string objectName,
        string operationName,
        Func<DbConnection, CancellationToken, Task<Result<string>>> query,
        CancellationToken cancellationToken)
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
            return await query(connection, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve {Operation} for {ObjectName} in database {DatabaseName}.", operationName, objectName, databaseName);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    private sealed class ObjectRow
    {
        public string SchemaName { get; init; } = string.Empty;
        public string ObjectName { get; init; } = string.Empty;
        public string TypeDescription { get; init; } = string.Empty;
    }
}
