using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Cli;
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
internal sealed class ToolDispatcher : IToolDispatcher
{
    private static readonly Action<ILogger, string, Exception?> LogUnknownTool =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, "UnknownTool"),
            "Unknown tool requested: {ToolName}.");

    private readonly ISchemaService _schemaService;
    private readonly SchemaExportService _schemaExport;
    private readonly IQueryExecutionService _queryExecutionService;
    private readonly IScriptExecutionService _scriptExecutionService;
    private readonly IQueryValidationService _queryValidationService;
    private readonly IQueryComparisonService _queryComparisonService;
    private readonly IPerformanceMeasurementService _performanceMeasurementService;
    private readonly IOptimizationBenchmarkService _benchmarkService;
    private readonly IIndexSuggestionService _indexSuggestionService;
    private readonly QueryExecutionOptions _queryExecutionOptions;
    private readonly ILogger<ToolDispatcher> _logger;
    private readonly Dictionary<string, Func<ToolCallParams, CancellationToken, Task<ToolCallResult>>> _handlers;

    /// <summary>Initializes a new instance of <see cref="ToolDispatcher"/>.</summary>
    public ToolDispatcher(
        ISchemaService schemaService,
        IQueryExecutionService queryExecutionService,
        IScriptExecutionService scriptExecutionService,
        IQueryValidationService queryValidationService,
        DatabaseAnalysisServices analysisServices,
        IOptions<SqlToAiOptions> options,
        ILogger<ToolDispatcher> logger,
        SchemaExportService schemaExport)
    {
        _schemaService = schemaService;
        _schemaExport = schemaExport;
        _queryExecutionService = queryExecutionService;
        _scriptExecutionService = scriptExecutionService;
        _queryValidationService = queryValidationService;
        _queryComparisonService = analysisServices.QueryComparison;
        _performanceMeasurementService = analysisServices.PerformanceMeasurement;
        _benchmarkService = analysisServices.Benchmark;
        _indexSuggestionService = analysisServices.IndexSuggestion;
        _queryExecutionOptions = options.Value.QueryExecution;
        _logger = logger;

        _handlers = new(StringComparer.Ordinal)
        {
            [McpConstants.ToolListServers] = (paramsObj, ct) =>
                CallAsync(() => Task.FromResult(_schemaService.ListServers()),
                    list => JsonSerializer.Serialize(list, typeof(IReadOnlyList<string>), McpJsonContext.Default)),

            [McpConstants.ToolListDatabases] = (paramsObj, ct) =>
                CallAsync(() => _schemaService.ListDatabasesAsync(
                    Require(paramsObj, McpConstants.ArgServer), ct),
                    list => JsonSerializer.Serialize(list, typeof(IReadOnlyList<string>), McpJsonContext.Default)),

            [McpConstants.ToolSearchDatabases] = (paramsObj, ct) =>
                CallAsync(() => _schemaService.SearchDatabasesAsync(
                    Require(paramsObj, McpConstants.ArgServer),
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
                GetSchemaAsync(paramsObj, ct),

            [McpConstants.ToolExportSchema] = ExportSchemaAsync,

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
                string query;
                try
                {
                    query = Require(paramsObj, McpConstants.ArgQuery);
                    result = await _queryExecutionService.ExecuteQueryAsync(
                        GetDb(paramsObj),
                        query,
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
                string execInfoText = $"Execution Info: {queryResult.RowCount} rows returned in {queryResult.ElapsedMs} ms | cpu: {queryResult.CpuTimeMs} ms | logical reads: {queryResult.LogicalReads}.";
                var contentList = new List<ToolContent>();

                if (queryResult.WasAnonymized)
                {
                    contentList.Add(new ToolContent { Type = "text", Text = BuildAnonymizationNote(queryResult) });
                }

                contentList.Add(new ToolContent { Type = "text", Text = execInfoText });
                contentList.Add(new ToolContent { Type = "text", Text = queryResult.Data });

                string? schemaHint = SqlMetadataQueryDetector.GetResponseHint(query);
                if (schemaHint is not null)
                {
                    contentList.Add(new ToolContent { Type = "text", Text = schemaHint });
                }

                return new ToolCallResult
                {
                    Content = contentList,
                    IsError = false
                };
            },

            [McpConstants.ToolExecuteFile] = ExecuteFileAsync,

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
                    res => JsonSerializer.Serialize(res, typeof(QueryComparisonResult), McpAnalysisJsonContext.Default)),

            [McpConstants.ToolMeasurePerformance] = (paramsObj, ct) =>
                CallAsync(() => _performanceMeasurementService.MeasurePerformanceAsync(
                    new QueryPerformanceArgs(
                        GetDb(paramsObj),
                        Require(paramsObj, McpConstants.ArgQuery),
                        GetObject(paramsObj, McpConstants.ArgParameters),
                        GetInt(paramsObj, McpConstants.ArgWarmupRuns) ?? 1,
                        GetInt(paramsObj, McpConstants.ArgExecutionRuns) ?? 1,
                        GetBool(paramsObj, McpConstants.ArgIncludePlanAnalysis) ?? true),
                    ct),
                    res => JsonSerializer.Serialize(res, typeof(PerformanceMeasurementResult), McpAnalysisJsonContext.Default)),

            [McpConstants.ToolBenchmarkOptimization] = (paramsObj, ct) =>
                CallAsync(() => _benchmarkService.BenchmarkOptimizationAsync(
                    new QueryBenchmarkArgs(
                        GetDb(paramsObj),
                        Require(paramsObj, McpConstants.ArgQueryA),
                        Require(paramsObj, McpConstants.ArgQueryB),
                        GetObject(paramsObj, McpConstants.ArgParametersA),
                        GetObject(paramsObj, McpConstants.ArgParametersB),
                        GetObject(paramsObj, McpConstants.ArgParameters),
                        GetInt(paramsObj, McpConstants.ArgWarmupRuns) ?? 1,
                        GetInt(paramsObj, McpConstants.ArgExecutionRuns) ?? 1),
                    ct),
                    res => JsonSerializer.Serialize(res, typeof(OptimizationBenchmarkResult), McpAnalysisJsonContext.Default)),

            [McpConstants.ToolSuggestIndexes] = (paramsObj, ct) =>
                CallAsync(() => _indexSuggestionService.SuggestIndexesAsync(
                    new IndexSuggestionArgs(
                        GetDb(paramsObj),
                        GetString(paramsObj, McpConstants.ArgTableName),
                        GetDouble(paramsObj, McpConstants.ArgMinScore),
                        GetInt(paramsObj, McpConstants.ArgTop) ?? 10),
                    ct))
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

    private async Task<ToolCallResult> GetSchemaAsync(ToolCallParams call, CancellationToken ct)
    {
        try
        {
            JsonElement names = GetJsonArgument(call, McpConstants.ArgObjectNames);
            if (names.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                return await CallAsync(() => _schemaService.GetSchemaAsync(GetDb(call), Require(call, McpConstants.ArgObjectName), ct));

            var batch = await _schemaService.GetSchemaBatchAsync(GetDb(call), GetJsonArgument(call, McpConstants.ArgObjectName), names, ct);
            if (batch.IsFailure) return ToolCallResult.Failure(batch.Error.Code, batch.Error.Message);
            return new ToolCallResult
            {
                Content = [new ToolContent { Text = batch.Value.Json }],
                IsError = !batch.Value.Complete
            };
        }
        catch (ArgumentException ex)
        {
            return ToolCallResult.Failure(SqlToAiError.InvalidParametersCode, ex.Message);
        }
    }

    private async Task<ToolCallResult> ExportSchemaAsync(ToolCallParams call, CancellationToken ct)
    {
        SchemaExportOutcome outcome;
        string? resolvedOutput = null;
        try
        {
            string output = Require(call, McpConstants.ArgOutputPath);
            if (!SchemaExportToolResponse.IsQualifiedWindowsDirectory(output))
                throw new ArgumentException("output_path must be a fully qualified Windows drive-rooted or UNC directory; relative, URL and device-namespace paths are not allowed.");
            resolvedOutput = Path.GetFullPath(output);
            string scope = Require(call, McpConstants.ArgScope);
            if (scope is not ("selected" or "all"))
                throw new ArgumentException("scope must be explicitly selected or all.");
            outcome = await _schemaExport.ExportAsync(GetDb(call), output,
                scope == "selected" ? SchemaExportScope.Selected : SchemaExportScope.All,
                GetJsonArgument(call, McpConstants.ArgObjectName), GetJsonArgument(call, McpConstants.ArgObjectNames), ct);
        }
        catch (ArgumentException exception)
        {
            outcome = new(SchemaExportStatus.Failed, resolvedOutput,
                SchemaExportService.HasPartialOutput(resolvedOutput), SqlToAiError.InvalidParameters(exception.Message));
        }
        return SchemaExportToolResponse.Render(outcome);
    }

    private static JsonElement GetJsonArgument(ToolCallParams call, string key)
    {
        object? value = GetObject(call, key);
        return value is JsonElement json ? json : JsonSerializer.SerializeToElement(value, McpJsonContext.Default.Options);
    }

    private Task<ToolCallResult> UnknownTool(string toolName)
    {
        LogUnknownTool(_logger, toolName, null);
        return Task.FromResult(
            ToolCallResult.Failure(SqlToAiError.InvalidParametersCode, $"Unknown tool: {toolName}"));
    }

    private async Task<ToolCallResult> ExecuteFileAsync(ToolCallParams paramsObj, CancellationToken cancellationToken)
    {
        string filePath;
        string database;
        try
        {
            filePath = Require(paramsObj, McpConstants.ArgFilePath);
            database = GetDb(paramsObj);
        }
        catch (ArgumentException ex)
        {
            return ToolCallResult.Failure(SqlToAiError.InvalidParametersCode, ex.Message);
        }

        Result<SqlScriptFile> file = SqlScriptFileReader.Read(filePath, _queryExecutionOptions);
        if (file.IsFailure)
        {
            return ToolCallResult.Failure(file.Error.Code, file.Error.Message);
        }

        var request = new ScriptExecutionRequest(
            file.Value,
            database,
            GetInt(paramsObj, McpConstants.ArgRequestedRowLimit),
            GetObject(paramsObj, McpConstants.ArgParameters),
            GetBool(paramsObj, McpConstants.ArgUseTransaction) ?? true);
        ScriptExecutionReport report = await _scriptExecutionService.ExecuteAsync(request, cancellationToken);

        return new ToolCallResult
        {
            Content = [new ToolContent { Type = "text", Text = ScriptExecutionReportRenderer.Render(report) }],
            IsError = report.Status == ScriptExecutionStatus.Failed
        };
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

    private static double? GetDouble(ToolCallParams p, string key)
    {
        if (!p.Arguments.TryGetValue(key, out object? raw)) return null;
        if (raw is double d) return d;
        if (raw is float f) return (double)f;
        if (raw is decimal m) return (double)m;
        if (raw is int i) return i;
        if (raw is long l) return l;
        if (raw is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out double v)) return v;
        }
        return null;
    }

    private static object? GetObject(ToolCallParams p, string key)
    {
        if (p.Arguments.TryGetValue(key, out object? raw)) return raw;
        return null;
    }

    private static bool? GetBool(ToolCallParams p, string key)
    {
        if (!p.Arguments.TryGetValue(key, out object? raw)) return null;
        if (raw is bool b) return b;
        if (raw is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.True) return true;
            if (el.ValueKind == JsonValueKind.False) return false;
        }
        return null;
    }
}
