using System.Data.Common;
using System.Text.Json;
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
    private readonly DatabaseTargetResolver _targets;
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
        _targets = new DatabaseTargetResolver(_options);
        _logger = logger;
        _tableSchemaRenderer = new TableSchemaRenderer(metadataProvider, policyResolver);
    }

    /// <summary>
    /// Resolves the caller's qualified target and verifies static whitelisting and dynamic access level.
    /// </summary>
    private async Task<Result<DatabaseTargetContext>> ResolveAndVerifyTargetAsync(string databaseName, CancellationToken cancellationToken)
    {
        var targetResult = _targets.Resolve(databaseName);
        if (targetResult.IsFailure)
        {
            return targetResult.Error;
        }

        var target = targetResult.Value;
        if (!_securityGuard.IsTargetAllowed(target))
        {
            return SqlToAiError.SafetyCheckFailed($"Database '{target.QualifiedName}' is blocked by security policies (static whitelist).");
        }

        var accessLevel = await _accessLevelProvider.GetTargetAccessLevelAsync(target, cancellationToken);
        if (accessLevel == AccessLevel.None)
        {
            return SqlToAiError.SafetyCheckFailed($"Database '{target.QualifiedName}' access was denied (AccessLevel: None).");
        }

        return target;
    }

    public Result<IReadOnlyList<string>> ListServers()
    {
        return Result<IReadOnlyList<string>>.Success(_targets.ConfiguredServerAliases);
    }

    public async Task<Result<IReadOnlyList<string>>> ListDatabasesAsync(string server, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            return Result<IReadOnlyList<string>>.Failure(SqlToAiError.InvalidParameters("server must not be empty."));
        }

        var serverResult = _targets.ResolveServerAlias(server);
        if (serverResult.IsFailure)
        {
            return Result<IReadOnlyList<string>>.Failure(serverResult.Error);
        }
        string canonicalServer = serverResult.Value;

        try
        {
            var connResult = _connectionFactory.CreateCatalogConnection(canonicalServer);
            if (connResult.IsFailure)
            {
                return Result<IReadOnlyList<string>>.Failure(connResult.Error);
            }
            await using var connection = connResult.Value;
            await PrimaryConnection.OpenCatalogAsync(connection, canonicalServer, cancellationToken);

            var databases = await connection.QueryAsync<string>(
                new CommandDefinition("SELECT name FROM sys.databases WHERE state = 0", cancellationToken: cancellationToken));

            var allowedDbs = databases
                .Where(db => _securityGuard.IsTargetAllowed(new DatabaseTargetContext(canonicalServer, db)))
                .Select(db => $"{canonicalServer}/{db}")
                .OrderBy(db => db, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return allowedDbs;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                throw;
            }
            if (ex is PrimaryConnectionException pex && (pex.IsTlsFailure || pex.Error.Code == SqlToAiError.TimeoutCode))
            {
                string failureType = pex.IsTlsFailure
                    ? "TLS/certificate connection failure"
                    : "SQL connection timeout";
                _logger.LogWarning(pex, "SqlServer catalog connection failed for settings {SettingsPath} ({FailureType}).",
                    $"SqlToAi:Servers:{canonicalServer}", failureType);
                return pex.Error;
            }

            if (SqlToAiErrorMapper.IsTimeoutException(ex) || SqlToAiErrorMapper.IsTlsException(ex))
            {
                SqlToAiError mapped = SqlToAiErrorMapper.MapException(ex);
                string settingsPath = $"SqlToAi:Servers:{canonicalServer}";
                string failureType = mapped.Code == SqlToAiError.TimeoutCode
                    ? "SQL catalog query timeout"
                    : "TLS/certificate failure";
                _logger.LogWarning("SqlServer catalog query failed for settings {SettingsPath} ({FailureType}).",
                    settingsPath, failureType);
                return mapped with { Message = $"{mapped.Message} Settings: {settingsPath}." };
            }

            _logger.LogWarning(ex is PrimaryConnectionException ? ex : null,
                "Failed to query sys.databases catalog for settings {SettingsPath} ({FailureType}). Falling back to configured static database lists.",
                $"SqlToAi:Servers:{canonicalServer}", SqlToAiErrorMapper.GetSafeConnectionFailureCategory(ex));
            return Result<IReadOnlyList<string>>.Success(GetStaticAllowedDatabases(canonicalServer));
        }
    }

    private IReadOnlyList<string> GetStaticAllowedDatabases(string canonicalServer)
    {
        var allGrants = _options.Databases.ReadWrite
            .Concat(_options.Databases.ReadOnly)
            .Concat(_options.Databases.ReadOnlyAnonymized)
            .Concat(_options.Databases.SchemaOnly);

        var dbNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string grant in allGrants)
        {
            var parsed = DatabaseTargetParser.Parse(grant);
            if (parsed.IsSuccess && string.Equals(parsed.Value.ServerAlias, canonicalServer, StringComparison.Ordinal))
            {
                var target = new DatabaseTargetContext(canonicalServer, parsed.Value.DatabaseName);
                if (_securityGuard.IsTargetAllowed(target))
                {
                    dbNames.Add(target.QualifiedName);
                }
            }
        }

        return dbNames.OrderBy(db => db, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<Result<IReadOnlyList<string>>> SearchDatabasesAsync(string server, string searchTerm, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return Result<IReadOnlyList<string>>.Failure(SqlToAiError.InvalidParameters("search_term must not be empty."));
        }

        var dbsResult = await ListDatabasesAsync(server, cancellationToken);
        if (dbsResult.IsFailure)
        {
            return dbsResult.Error;
        }

        var filtered = dbsResult.Value
            .Where(qualified =>
            {
                var parsed = DatabaseTargetParser.Parse(qualified);
                string rawDb = parsed.IsSuccess ? parsed.Value.DatabaseName : qualified;
                return rawDb.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        return filtered;
    }

    public async Task<Result<string>> SearchObjectsAsync(string databaseName, string searchTerm, int? maxResults = null, string? objectType = null, CancellationToken cancellationToken = default)
    {
        var targetCheck = await ResolveAndVerifyTargetAsync(databaseName, cancellationToken);
        if (targetCheck.IsFailure)
        {
            return targetCheck.Error;
        }
        var target = targetCheck.Value;

        int limit = maxResults ?? 100;
        try
        {
            await using var connection = _connectionFactory.CreateTargetConnection(target);
            await PrimaryConnection.OpenTargetAsync(connection, target, cancellationToken);

            var rows = await SchemaObjectDiscovery.QueryAsync(connection, $"%{searchTerm}%", objectType, limit, false, cancellationToken);

            var renderedRows = new List<string[]>();
            foreach (var r in rows)
            {
                renderedRows.Add([ r.SchemaName, r.ObjectName, r.TypeDescription ]);
            }

            if (renderedRows.Count == 0)
            {
                return $"No objects found matching '{searchTerm}' in database '{target.QualifiedName}'.";
            }

            return MarkdownTableRenderer.Render(["Schema", "Name", "Type"], renderedRows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search objects in database {DatabaseName}.", target.QualifiedName);
            return ex is PrimaryConnectionException ? SqlToAiErrorMapper.MapException(ex) : SqlToAiError.QueryError(ex.Message);
        }
    }

    public async Task<Result<IReadOnlyList<SchemaObject>>> GetExportObjectsAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        var targetCheck = await ResolveAndVerifyTargetAsync(databaseName, cancellationToken);
        if (targetCheck.IsFailure)
        {
            return targetCheck.Error;
        }
        var target = targetCheck.Value;

        try
        {
            await using var connection = _connectionFactory.CreateTargetConnection(target);
            await PrimaryConnection.OpenTargetAsync(connection, target, cancellationToken);
            var rows = await SchemaObjectDiscovery.QueryAsync(connection, "%", null, null, true, cancellationToken);
            return rows.Select(row => row.ToSchemaObject()).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover export objects in database {DatabaseName}.", target.QualifiedName);
            return ex is PrimaryConnectionException ? SqlToAiErrorMapper.MapException(ex) : SqlToAiError.QueryError(ex.Message);
        }
    }

    public Task<Result<string>> GetExportTriggerDefinitionAsync(string databaseName, SchemaObject trigger, CancellationToken cancellationToken = default)
        => ExecuteDetailQueryAsync(databaseName, trigger.Identity.DisplayName, "export trigger definition",
            (connection, _, ct) => DetailSchemaRenderer.GetExportTriggerDefinitionAsync(connection, trigger, ct), cancellationToken);

    public async Task<Result<IReadOnlyList<SchemaObject>>> GetSelectedExportObjectsAsync(
        string databaseName, IReadOnlyList<string> objectNames, CancellationToken cancellationToken = default)
    {
        if (objectNames.Count == 0)
            return SqlToAiError.InvalidParameters("At least one non-empty object name is required.");
        var selection = await ResolveObjectsAsync(databaseName, objectNames, forExport: true, cancellationToken);
        if (selection.IsFailure) return selection.Error;
        var objects = new List<SchemaObject>();
        var ids = new HashSet<int>();
        foreach (var entry in selection.Value)
        {
            if (entry.Object.IsFailure) return entry.Object.Error;
            if (ids.Add(entry.Object.Value.Identity.ObjectId)) objects.Add(entry.Object.Value);
        }
        return Result<IReadOnlyList<SchemaObject>>.Success(objects);
    }

    internal async Task<Result<IReadOnlyList<SchemaObjectSelectionEntry>>> ResolveObjectsAsync(
        string databaseName, IReadOnlyList<string> names, bool forExport, CancellationToken cancellationToken = default)
    {
        var targetCheck = await ResolveAndVerifyTargetAsync(databaseName, cancellationToken);
        if (targetCheck.IsFailure)
        {
            return targetCheck.Error;
        }
        var target = targetCheck.Value;

        try
        {
            await using var connection = _connectionFactory.CreateTargetConnection(target);
            await PrimaryConnection.OpenTargetAsync(connection, target, cancellationToken);
            return Result<IReadOnlyList<SchemaObjectSelectionEntry>>.Success(
                await SchemaObjectSelection.ResolveAsync(connection, names, forExport, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve selected objects in database {DatabaseName}.", target.QualifiedName);
            return ex is PrimaryConnectionException ? SqlToAiErrorMapper.MapException(ex) : SqlToAiError.QueryError(ex.Message);
        }
    }

    public async Task<Result<string>> GetSchemaAsync(string databaseName, string objectName, CancellationToken cancellationToken = default)
        => await GetSchemaCoreAsync(databaseName, objectName, null, cancellationToken);

    public async Task<Result<SchemaBatchResponse>> GetSchemaBatchAsync(
        string databaseName, JsonElement objectName = default, JsonElement objectNames = default, CancellationToken cancellationToken = default)
    {
        var normalized = SchemaObjectSelection.Normalize(objectName, objectNames);
        if (normalized.IsFailure) return normalized.Error;
        var preflight = SchemaBatchComposer.ValidateRequest(normalized.Value, _options.SchemaBatch);
        if (preflight.IsFailure) return preflight.Error;
        var targetCheck = await ResolveAndVerifyTargetAsync(databaseName, cancellationToken);
        if (targetCheck.IsFailure) return targetCheck.Error;
        var target = targetCheck.Value;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var connection = _connectionFactory.CreateTargetConnection(target);
            await PrimaryConnection.OpenTargetAsync(connection, target, cancellationToken);
            var selected = await SchemaObjectSelection.ResolveAsync(connection, normalized.Value, false, cancellationToken);
            return await SchemaBatchComposer.ComposeAsync(selected, _options.SchemaBatch.MaxResponseBytes,
                async (item, ct) =>
                {
                    try
                    {
                        string type = item.Identity.Kind switch { SchemaObjectKind.Table => "U", SchemaObjectKind.View => "V", _ => "P" };
                        return Result<string>.Success(await RenderPrimarySchemaAsync(connection, target, item.Identity.QualifiedName, type, null, ct, item.Identity));
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) when (ex is not PrimaryConnectionException)
                    {
                        _logger.LogError(ex, "Failed to retrieve batch schema for {ObjectName} in database {DatabaseName}.", item.Identity.DisplayName, target.QualifiedName);
                        return SqlToAiError.QueryError(ex.Message);
                    }
                }, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve schema batch in database {DatabaseName}.", target.QualifiedName);
            return ex is PrimaryConnectionException ? SqlToAiErrorMapper.MapException(ex) : SqlToAiError.QueryError(ex.Message);
        }
    }

    public Task<Result<string>> GetExportSchemaAsync(string databaseName, SchemaRenderingContext context, CancellationToken cancellationToken = default)
        => GetSchemaCoreAsync(databaseName, context.Source.QualifiedName, context, cancellationToken);

    private async Task<Result<string>> GetSchemaCoreAsync(string databaseName, string objectName, SchemaRenderingContext? context, CancellationToken cancellationToken)
    {
        var targetCheck = await ResolveAndVerifyTargetAsync(databaseName, cancellationToken);
        if (targetCheck.IsFailure)
        {
            return targetCheck.Error;
        }
        var target = targetCheck.Value;

        try
        {
            await using var connection = _connectionFactory.CreateTargetConnection(target);
            await PrimaryConnection.OpenTargetAsync(connection, target, cancellationToken);

            // 1. Identify object type
            var typeCode = await connection.QueryFirstOrDefaultAsync<string>(
                new CommandDefinition("SELECT RTRIM(type) FROM sys.objects WHERE object_id = OBJECT_ID(@ObjectName)", new { ObjectName = objectName }, cancellationToken: cancellationToken));

            if (string.IsNullOrEmpty(typeCode))
            {
                return SqlToAiError.ObjectNotFound(objectName);
            }

            return await RenderPrimarySchemaAsync(connection, target, objectName, typeCode, context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve schema for {ObjectName} in database {DatabaseName}.", objectName, target.QualifiedName);
            return ex is PrimaryConnectionException ? SqlToAiErrorMapper.MapException(ex) : SqlToAiError.QueryError(ex.Message);
        }
    }

    private async Task<string> RenderPrimarySchemaAsync(DbConnection connection, DatabaseTargetContext target, string objectName,
        string typeCode, SchemaRenderingContext? context, CancellationToken cancellationToken, SchemaObjectIdentity? interactiveIdentity = null)
    {
        if (typeCode == "U")
            return await _tableSchemaRenderer.GetTableSchemaMarkdownAsync(connection, target, objectName, cancellationToken, context, interactiveIdentity);
        if (typeCode == "V")
        {
            string table = await _tableSchemaRenderer.GetTableSchemaMarkdownAsync(connection, target, objectName, cancellationToken, context, interactiveIdentity);
            return table + await TableSchemaRenderer.GetViewDefinitionMarkdownAsync(connection, objectName, cancellationToken);
        }
        return await TableSchemaRenderer.GetRoutineSchemaMarkdownAsync(connection, objectName, cancellationToken, context);
    }

    public Task<Result<string>> GetSchemaForeignKeysAsync(string databaseName, string tableName, CancellationToken cancellationToken = default) =>
        ExecuteDetailQueryAsync(databaseName, tableName, "foreign keys",
            (connection, target, ct) => DetailSchemaRenderer.GetSchemaForeignKeysAsync(connection, tableName, target.QualifiedName, ct),
            cancellationToken);

    public Task<Result<string>> GetSchemaIndexesAsync(string databaseName, string tableName, CancellationToken cancellationToken = default) =>
        ExecuteDetailQueryAsync(databaseName, tableName, "indexes",
            (connection, target, ct) => DetailSchemaRenderer.GetSchemaIndexesAsync(connection, tableName, target.QualifiedName, ct),
            cancellationToken);

    public Task<Result<string>> GetExportSchemaForeignKeysAsync(string databaseName, SchemaRenderingContext context, CancellationToken cancellationToken = default)
        => ExecuteDetailQueryAsync(databaseName, context.Source.DisplayName, "foreign keys",
            (connection, target, ct) => DetailSchemaRenderer.GetSchemaForeignKeysAsync(connection, context.Source.QualifiedName, target.QualifiedName, ct, context), cancellationToken);

    public Task<Result<string>> GetExportObjectReferencesAsync(string databaseName, SchemaRenderingContext context, CancellationToken cancellationToken = default)
        => ExecuteDetailQueryAsync(databaseName, context.Source.DisplayName, "referencing entities",
            (connection, target, ct) => DetailSchemaRenderer.GetObjectReferencesAsync(connection, context.Source.QualifiedName, target.QualifiedName, ct, context), cancellationToken);

    public Task<Result<string>> GetExportSchemaIndexesAsync(string databaseName, SchemaRenderingContext context, CancellationToken cancellationToken = default)
        => ExecuteDetailQueryAsync(databaseName, context.Source.DisplayName, "indexes",
            (connection, target, ct) => DetailSchemaRenderer.GetSchemaIndexesAsync(connection, context.Source.QualifiedName, target.QualifiedName, ct, context), cancellationToken);

    public Task<Result<string>> GetExportSchemaConstraintsAsync(string databaseName, SchemaRenderingContext context, CancellationToken cancellationToken = default)
        => ExecuteDetailQueryAsync(databaseName, context.Source.DisplayName, "constraints",
            (connection, target, ct) => DetailSchemaRenderer.GetSchemaConstraintsAsync(connection, context.Source.QualifiedName, target.QualifiedName, ct, context), cancellationToken);

    public Task<Result<string>> GetExportRoutineParametersAsync(string databaseName, SchemaRenderingContext context, CancellationToken cancellationToken = default)
        => ExecuteDetailQueryAsync(databaseName, context.Source.DisplayName, "routine parameters",
            (connection, target, ct) => DetailSchemaRenderer.GetRoutineParametersAsync(connection, context.Source.QualifiedName, target.QualifiedName, ct, context), cancellationToken);

    public Task<Result<string>> GetSchemaConstraintsAsync(string databaseName, string tableName, CancellationToken cancellationToken = default) =>
        ExecuteDetailQueryAsync(databaseName, tableName, "constraints",
            (connection, target, ct) => DetailSchemaRenderer.GetSchemaConstraintsAsync(connection, tableName, target.QualifiedName, ct),
            cancellationToken);

    public Task<Result<string>> GetTriggerDefinitionAsync(string databaseName, string tableName, string triggerName, CancellationToken cancellationToken = default) =>
        ExecuteDetailQueryAsync(databaseName, triggerName, "trigger DDL",
            (connection, target, ct) => DetailSchemaRenderer.GetTriggerDefinitionAsync(connection, tableName, triggerName, target.QualifiedName, ct),
            cancellationToken);

    public Task<Result<string>> GetObjectReferencesAsync(string databaseName, string objectName, CancellationToken cancellationToken = default) =>
        ExecuteDetailQueryAsync(databaseName, objectName, "referencing entities",
            (connection, target, ct) => DetailSchemaRenderer.GetObjectReferencesAsync(connection, objectName, target.QualifiedName, ct),
            cancellationToken);

    public Task<Result<string>> GetRoutineParametersAsync(string databaseName, string routineName, CancellationToken cancellationToken = default) =>
        ExecuteDetailQueryAsync(databaseName, routineName, "routine parameters",
            (connection, target, ct) => DetailSchemaRenderer.GetRoutineParametersAsync(connection, routineName, target.QualifiedName, ct),
            cancellationToken);

    /// <summary>
    /// Common skeleton for the six detail-query delegations: verify access, open a connection,
    /// run a single DetailSchemaRenderer call inside a try/catch, log and translate any
    /// exception while preserving safe connection errors and existing query-error handling.
    /// </summary>
    private async Task<Result<string>> ExecuteDetailQueryAsync(
        string databaseName,
        string objectName,
        string operationName,
        Func<DbConnection, DatabaseTargetContext, CancellationToken, Task<Result<string>>> query,
        CancellationToken cancellationToken)
    {
        var targetCheck = await ResolveAndVerifyTargetAsync(databaseName, cancellationToken);
        if (targetCheck.IsFailure)
        {
            return targetCheck.Error;
        }
        var target = targetCheck.Value;

        try
        {
            await using var connection = _connectionFactory.CreateTargetConnection(target);
            await PrimaryConnection.OpenTargetAsync(connection, target, cancellationToken);
            return await query(connection, target, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve {Operation} for {ObjectName} in database {DatabaseName}.", operationName, objectName, target.QualifiedName);
            return ex is PrimaryConnectionException ? SqlToAiErrorMapper.MapException(ex) : SqlToAiError.QueryError(ex.Message);
        }
    }
}
