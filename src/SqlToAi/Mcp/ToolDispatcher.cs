#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Mcp;

/// <summary>
/// Dispatches incoming MCP <c>tools/call</c> requests to the appropriate service and
/// converts the <see cref="Result{T}"/> into a <see cref="ToolCallResult"/>.
/// </summary>
public interface IToolDispatcher
{
    /// <summary>Handles a parsed <see cref="ToolCallParams"/> and returns a <see cref="ToolCallResult"/>.</summary>
    Task<ToolCallResult> DispatchAsync(ToolCallParams callParams, CancellationToken cancellationToken = default);
}

/// <inheritdoc/>
public sealed class ToolDispatcher : IToolDispatcher
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Action<ILogger, string, Exception?> LogUnknownTool =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, "UnknownTool"),
            "Unknown tool requested: {ToolName}.");

    private readonly ISchemaService _schemaService;
    private readonly IQueryExecutionService _queryExecutionService;
    private readonly IQueryValidationService _queryValidationService;
    private readonly DatabasesOptions _dbOptions;
    private readonly ILogger<ToolDispatcher> _logger;

    /// <summary>Initializes a new instance of <see cref="ToolDispatcher"/>.</summary>
    public ToolDispatcher(
        ISchemaService schemaService,
        IQueryExecutionService queryExecutionService,
        IQueryValidationService queryValidationService,
        IOptions<SqlToAiOptions> options,
        ILogger<ToolDispatcher> logger)
    {
        _schemaService = schemaService;
        _queryExecutionService = queryExecutionService;
        _queryValidationService = queryValidationService;
        _dbOptions = options.Value.Databases;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<ToolCallResult> DispatchAsync(ToolCallParams callParams, CancellationToken cancellationToken = default)
    {
        string db = GetString(callParams, McpConstants.ArgDatabase) ?? _dbOptions.Default;

        return callParams.Name switch
        {
            McpConstants.ToolListDatabases =>
                CallAsync(() => _schemaService.ListDatabasesAsync(cancellationToken),
                    list => JsonSerializer.Serialize(list, SerializerOptions)),

            McpConstants.ToolSearchDatabases =>
                CallAsync(() => _schemaService.SearchDatabasesAsync(
                    Require(callParams, McpConstants.ArgSearchTerm), cancellationToken),
                    list => JsonSerializer.Serialize(list, SerializerOptions)),

            McpConstants.ToolValidateQuery =>
                CallAsync(() => _queryValidationService.ValidateQueryAsync(
                    db, Require(callParams, McpConstants.ArgQuery), cancellationToken)),

            McpConstants.ToolSearchObjects =>
                CallAsync(() => _schemaService.SearchObjectsAsync(
                    db,
                    Require(callParams, McpConstants.ArgSearchTerm),
                    GetInt(callParams, McpConstants.ArgMaxResults),
                    cancellationToken)),

            McpConstants.ToolGetSchema =>
                CallAsync(() => _schemaService.GetSchemaAsync(
                    db, Require(callParams, McpConstants.ArgObjectName), cancellationToken)),

            McpConstants.ToolGetSchemaForeignKeys =>
                CallAsync(() => _schemaService.GetSchemaForeignKeysAsync(
                    db, Require(callParams, McpConstants.ArgObjectName), cancellationToken)),

            McpConstants.ToolGetSchemaIndexes =>
                CallAsync(() => _schemaService.GetSchemaIndexesAsync(
                    db, Require(callParams, McpConstants.ArgObjectName), cancellationToken)),

            McpConstants.ToolGetSchemaConstraints =>
                CallAsync(() => _schemaService.GetSchemaConstraintsAsync(
                    db, Require(callParams, McpConstants.ArgObjectName), cancellationToken)),

            McpConstants.ToolGetTriggerDefinition =>
                CallAsync(() => _schemaService.GetTriggerDefinitionAsync(
                    db,
                    Require(callParams, McpConstants.ArgObjectName),
                    Require(callParams, McpConstants.ArgTriggerName),
                    cancellationToken)),

            McpConstants.ToolGetObjectReferences =>
                CallAsync(() => _schemaService.GetObjectReferencesAsync(
                    db, Require(callParams, McpConstants.ArgObjectName), cancellationToken)),

            McpConstants.ToolGetRoutineParameters =>
                CallAsync(() => _schemaService.GetRoutineParametersAsync(
                    db, Require(callParams, McpConstants.ArgObjectName), cancellationToken)),

            McpConstants.ToolExecuteQuery =>
                CallAsync(() => _queryExecutionService.ExecuteQueryAsync(
                    db,
                    Require(callParams, McpConstants.ArgQuery),
                    GetInt(callParams, McpConstants.ArgRequestedRowLimit),
                    cancellationToken)),

            _ => UnknownTool(callParams.Name)
        };
    }

    // -------------------------------------------------------------------------
    // Private dispatch helpers
    // -------------------------------------------------------------------------

    private static async Task<ToolCallResult> CallAsync<T>(
        Func<Task<Result<T>>> action,
        Func<T, string>? serializer = null)
    {
        Result<T> result;
        try
        {
            result = await action();
        }
        catch (ArgumentException ex)
        {
            return ToolCallResult.Failure(SqlToAiError.InvalidParametersCode, ex.Message);
        }

        if (result.IsFailure)
        {
            return ToolCallResult.Failure(result.Error.Code, result.Error.Message);
        }

        string text = serializer != null
            ? serializer(result.Value)
            : result.Value?.ToString() ?? string.Empty;

        return ToolCallResult.Success(text);
    }

    private Task<ToolCallResult> UnknownTool(string toolName)
    {
        LogUnknownTool(_logger, toolName, null);
        return Task.FromResult(
            ToolCallResult.Failure(SqlToAiError.InvalidParametersCode, $"Unknown tool: {toolName}"));
    }

    // -------------------------------------------------------------------------
    // Argument extraction helpers
    // -------------------------------------------------------------------------

    /// <summary>Gets a required string argument, throwing <see cref="ArgumentException"/> if absent or empty.</summary>
    private static string Require(ToolCallParams p, string key)
    {
        if (p.Arguments.TryGetValue(key, out object? raw)
            && raw is string s && !string.IsNullOrWhiteSpace(s))
        {
            return s;
        }

        // Try JsonElement (deserialization may yield boxed JsonElement)
        if (p.Arguments.TryGetValue(key, out raw)
            && raw is JsonElement el && el.ValueKind == JsonValueKind.String)
        {
            string? str = el.GetString();
            if (!string.IsNullOrWhiteSpace(str)) return str;
        }

        throw new ArgumentException($"Required argument '{key}' is missing or empty.");
    }

    private static string? GetString(ToolCallParams p, string key)
    {
        if (!p.Arguments.TryGetValue(key, out object? raw)) return null;
        if (raw is string s) return string.IsNullOrWhiteSpace(s) ? null : s;
        if (raw is JsonElement el && el.ValueKind == JsonValueKind.String) return el.GetString();
        return null;
    }

    private static int? GetInt(ToolCallParams p, string key)
    {
        if (!p.Arguments.TryGetValue(key, out object? raw)) return null;
        if (raw is int i) return i;
        if (raw is long l) return (int)l;
        if (raw is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out int v)) return v;
        }
        return null;
    }
}
