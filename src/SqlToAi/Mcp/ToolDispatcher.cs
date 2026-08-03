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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = McpJsonContext.Default
    };

    private static readonly Action<ILogger, string, Exception?> LogUnknownTool =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, "UnknownTool"),
            "Unknown tool requested: {ToolName}.");

    private readonly ISchemaService _schemaService;
    private readonly IQueryExecutionService _queryExecutionService;
    private readonly IQueryValidationService _queryValidationService;
    private readonly IQueryComparisonService _queryComparisonService;
    private readonly DatabasesOptions _dbOptions;
    private readonly ILogger<ToolDispatcher> _logger;
    private readonly Dictionary<string, Func<ToolCallParams, CancellationToken, Task<ToolCallResult>>> _handlers;

    /// <summary>Initializes a new instance of <see cref="ToolDispatcher"/>.</summary>
    public ToolDispatcher(
        ISchemaService schemaService,
        IQueryExecutionService queryExecutionService,
        IQueryValidationService queryValidationService,
        IQueryComparisonService queryComparisonService,
        IOptions<SqlToAiOptions> options,
        ILogger<ToolDispatcher> logger)
    {
        _schemaService = schemaService;
        _queryExecutionService = queryExecutionService;
        _queryValidationService = queryValidationService;
        _queryComparisonService = queryComparisonService;
        _dbOptions = options.Value.Databases;
        _logger = logger;

        _handlers = new(StringComparer.Ordinal)
        {
            [McpConstants.ToolListDatabases] = (paramsObj, ct) =>
                CallAsync(() => _schemaService.ListDatabasesAsync(ct),
                    list => JsonSerializer.Serialize(list, typeof(IReadOnlyList<string>), McpJsonContext.Default)),

            [McpConstants.ToolSearchDatabases] = (paramsObj, ct) =>
                CallAsync(() => _schemaService.SearchDatabasesAsync(
                    Require(paramsObj, McpConstants.ArgSearchTerm), ct),
                    list => JsonSerializer.Serialize(list, typeof(IReadOnlyList<string>), McpJsonContext.Default)),

            [McpConstants.ToolValidateQuery] = (paramsObj, ct) =>
                CallAsync(() => _queryValidationService.ValidateQueryAsync(
                    GetDb(paramsObj), Require(paramsObj, McpConstants.ArgQuery), GetObject(paramsObj, McpConstants.ArgParameters), ct)),

            [McpConstants.ToolSearchObjects] = (paramsObj, ct) =>
                CallAsync(() => _schemaService.SearchObjectsAsync(
                    GetDb(paramsObj),
                    Require(paramsObj, McpConstants.ArgSearchTerm),
                    GetInt(paramsObj, McpConstants.ArgMaxResults),
                    GetString(paramsObj, McpConstants.ArgObjectType),
                    ct)),

            [McpConstants.ToolGetSchema] = (paramsObj, ct) =>
                CallAsync(() => _schemaService.GetSchemaAsync(
                    GetDb(paramsObj), Require(paramsObj, McpConstants.ArgObjectName), ct)),

            [McpConstants.ToolGetSchemaForeignKeys] = (paramsObj, ct) =>
                CallAsync(() => _schemaService.GetSchemaForeignKeysAsync(
                    GetDb(paramsObj), Require(paramsObj, McpConstants.ArgObjectName), ct)),

            [McpConstants.ToolGetSchemaIndexes] = (paramsObj, ct) =>
                CallAsync(() => _schemaService.GetSchemaIndexesAsync(
                    GetDb(paramsObj), Require(paramsObj, McpConstants.ArgObjectName), ct)),

            [McpConstants.ToolGetSchemaConstraints] = (paramsObj, ct) =>
                CallAsync(() => _schemaService.GetSchemaConstraintsAsync(
                    GetDb(paramsObj), Require(paramsObj, McpConstants.ArgObjectName), ct)),

            [McpConstants.ToolGetTriggerDefinition] = (paramsObj, ct) =>
                CallAsync(() => _schemaService.GetTriggerDefinitionAsync(
                    GetDb(paramsObj),
                    Require(paramsObj, McpConstants.ArgObjectName),
                    Require(paramsObj, McpConstants.ArgTriggerName),
                    ct)),

            [McpConstants.ToolGetObjectReferences] = (paramsObj, ct) =>
                CallAsync(() => _schemaService.GetObjectReferencesAsync(
                    GetDb(paramsObj), Require(paramsObj, McpConstants.ArgObjectName), ct)),

            [McpConstants.ToolGetRoutineParameters] = (paramsObj, ct) =>
                CallAsync(() => _schemaService.GetRoutineParametersAsync(
                    GetDb(paramsObj), Require(paramsObj, McpConstants.ArgObjectName), ct)),

            [McpConstants.ToolExecuteQuery] = async (paramsObj, ct) =>
            {
                Result<QueryExecutionResult> result;
                try
                {
                    result = await _queryExecutionService.ExecuteQueryAsync(
                        GetDb(paramsObj),
                        Require(paramsObj, McpConstants.ArgQuery),
                        GetInt(paramsObj, McpConstants.ArgRequestedRowLimit),
                        GetObject(paramsObj, McpConstants.ArgParameters),
                        ct);
                }
                catch (ArgumentException ex)
                {
                    return ToolCallResult.Failure(SqlToAiError.InvalidParametersCode, ex.Message);
                }

                if (result.IsFailure)
                {
                    return ToolCallResult.Failure(result.Error.Code, result.Error.Message);
                }

                var queryResult = result.Value;
                if (queryResult.WasAnonymized)
                {
                    string noteText = BuildAnonymizationNote(queryResult);
                    return new ToolCallResult
                    {
                        Content = new[]
                        {
                            new ToolContent { Type = "text", Text = noteText },
                            new ToolContent { Type = "text", Text = queryResult.Data }
                        },
                        IsError = false
                    };
                }

                return ToolCallResult.Success(queryResult.Data);
            },

            [McpConstants.ToolCompareQueries] = (paramsObj, ct) =>
                CallAsync(() => _queryComparisonService.CompareQueriesAsync(
                    new QueryComparisonArgs(
                        GetDb(paramsObj),
                        Require(paramsObj, McpConstants.ArgQueryA),
                        Require(paramsObj, McpConstants.ArgQueryB),
                        GetObject(paramsObj, McpConstants.ArgParametersA),
                        GetObject(paramsObj, McpConstants.ArgParametersB),
                        GetObject(paramsObj, McpConstants.ArgParameters),
                        GetInt(paramsObj, McpConstants.ArgMaxDiffRows) ?? 5),
                    ct),
                    res => JsonSerializer.Serialize(res, typeof(QueryComparisonResult), McpJsonContext.Default))
        };
    }

    /// <inheritdoc/>
    public Task<ToolCallResult> DispatchAsync(ToolCallParams callParams, CancellationToken cancellationToken = default)
    {
        if (_handlers.TryGetValue(callParams.Name, out var handler))
        {
            return handler(callParams, cancellationToken);
        }
        return UnknownTool(callParams.Name);
    }

    /// <summary>
    /// Builds the PII-protection note shown alongside anonymized query results — plus, when any
    /// column used reversible tokenization instead of masking, a compact hint that the AI can
    /// reuse those exact values in a later query (see <c>Anonymizer.Tokenize</c>).
    /// </summary>
    private static string BuildAnonymizationNote(QueryExecutionResult queryResult)
    {
        string columnsList = string.Join(", ", queryResult.AnonymizedColumns);
        string note = $"Note: The following query results have been anonymized (Mode: {queryResult.AnonymizationMode}) to protect PII. The following columns were anonymized: {columnsList}. " +
            "If this task needs any of these columns in clear text, tell the user which of these Table.Column names are affected and propose an exclusion rule rather than treating the scrambled values as real data; " +
            "for a view or computed column, trace the real source with sql_get_object_references first.";

        if (queryResult.SearchableTokenColumns.Count > 0)
        {
            string searchableList = string.Join(", ", queryResult.SearchableTokenColumns);
            note += $" Of these, {searchableList} are searchable tokens, not masked text: still not the real value, but reuse it verbatim (unchanged) in a later query's WHERE/JOIN/LIKE/IN/range predicate on that column — the server resolves it back to the real value before executing.";
        }

        return note;
    }

    private static string GetDb(ToolCallParams p)
    {
        string? db = GetString(p, McpConstants.ArgDatabase);
        if (string.IsNullOrWhiteSpace(db))
        {
            throw new ArgumentException($"Database name must be explicitly specified (argument '{McpConstants.ArgDatabase}' is required).");
        }
        return db;
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

    private static object? GetObject(ToolCallParams p, string key)
    {
        if (p.Arguments.TryGetValue(key, out object? raw)) return raw;
        return null;
    }
}
