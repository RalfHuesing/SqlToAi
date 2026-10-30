using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Database;

/// <summary>
/// Executes read-only queries and streams results directly into CSV or JSONL files.
/// </summary>
public sealed class DataExportService : IDataExportService
{
    private static readonly Action<ILogger, string, string, Exception?> LogExportFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(10, "DataExportFailed"),
            "Data export failed for database {Database}. Query: {Query}");

    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly IQuerySafetyValidator _querySafetyValidator;
    private readonly IReadOnlyGuard _readOnlyGuard;
    private readonly IAnonymizer _anonymizer;
    private readonly IAnonymizationRuleProvider? _anonymizationRuleProvider;
    private readonly IQueryTokenResolver? _queryTokenResolver;
    private readonly QueryExecutionOptions _options;
    private readonly TokenizationOptions _tokenizationOptions;
    private readonly DatabaseTargetResolver _targets;
    private readonly ILogger<DataExportService> _logger;

    /// <summary>Initializes a new instance of <see cref="DataExportService"/>.</summary>
    public DataExportService(
        IDatabaseConnectionFactory connectionFactory,
        IQuerySafetyValidator querySafetyValidator,
        IReadOnlyGuard readOnlyGuard,
        AnonymizationDependencies anonymization,
        IOptions<SqlToAiOptions> options,
        ILogger<DataExportService> logger)
    {
        _connectionFactory = connectionFactory;
        _querySafetyValidator = querySafetyValidator;
        _readOnlyGuard = readOnlyGuard;
        _anonymizer = anonymization.Anonymizer;
        _anonymizationRuleProvider = anonymization.RuleProvider;
        _queryTokenResolver = anonymization.TokenResolver;
        _options = options.Value.QueryExecution;
        _tokenizationOptions = options.Value.Anonymizer.Tokenization;
        _targets = new DatabaseTargetResolver(options.Value);
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<DataExportResult>> ExportDataAsync(DataExportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Resolve target database
        var targetResult = _targets.Resolve(request.Database);
        if (targetResult.IsFailure)
        {
            return targetResult.Error;
        }
        DatabaseTargetContext target = targetResult.Value;

        // 2. Validate query is not null/whitespace
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return SqlToAiError.InvalidParameters("Query must not be empty.");
        }

        // 3. Validate output_path
        if (string.IsNullOrWhiteSpace(request.OutputPath) || !IsQualifiedWindowsPath(request.OutputPath))
        {
            return SqlToAiError.InvalidParameters($"Output path '{request.OutputPath}' must be a qualified absolute Windows drive-rooted or UNC path.");
        }

        if (request.OutputPath.EndsWith('\\') || request.OutputPath.EndsWith('/'))
        {
            return SqlToAiError.InvalidParameters($"Output path '{request.OutputPath}' must be a file path, not a directory.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(request.OutputPath);
        }
        catch (Exception ex)
        {
            return SqlToAiError.InvalidParameters($"Invalid output path '{request.OutputPath}': {ex.Message}");
        }

        string? fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return SqlToAiError.InvalidParameters($"Output path '{request.OutputPath}' does not contain a valid file name.");
        }

        if (Directory.Exists(fullPath))
        {
            return SqlToAiError.InvalidParameters($"Output path '{fullPath}' is an existing directory.");
        }

        string? targetDir = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir))
        {
            return SqlToAiError.InvalidParameters($"The target directory '{targetDir}' does not exist.");
        }

        // 4. Validate format
        var formatResult = DataWriterFactory.ResolveFormat(request.Format, fullPath, _options.Export.DefaultFormat);
        if (formatResult.IsFailure)
        {
            return formatResult.Error;
        }
        string format = formatResult.Value;

        // 5. Validate row limit
        if (request.RequestedRowLimit.HasValue && request.RequestedRowLimit.Value <= 0)
        {
            return SqlToAiError.InvalidParameters("Requested row limit must be greater than zero.");
        }
        int effectiveLimit = request.RequestedRowLimit.HasValue
            ? Math.Min(request.RequestedRowLimit.Value, _options.Export.MaxRowLimit)
            : _options.Export.DefaultRowLimit;

        // 6. Validate overwrite protection
        if (!request.Overwrite && File.Exists(fullPath))
        {
            return SqlToAiError.FileAlreadyExists(fullPath);
        }

        // 7. Validate query safety
        var safetyResult = await _querySafetyValidator
            .ValidateTargetQuerySafetyAsync(target, request.Query, allowSchemaOnly: false, cancellationToken)
            .ConfigureAwait(false);
        if (safetyResult.IsFailure)
        {
            return safetyResult.Error;
        }

        if (safetyResult.Value.IsWriteAllowed && !_readOnlyGuard.IsQuerySafe(request.Query))
        {
            return SqlToAiError.WriteOperationBlocked(
                "The query contains mutating SQL statements (e.g. INSERT, UPDATE, DELETE, MERGE, DDL, EXEC) and was rejected because sql_export_data is strictly read-only.");
        }

        // 8. Resolve tokens for anonymized databases
        var accessLevel = safetyResult.Value.AccessLevel;
        bool anonymize = accessLevel.RequiresAnonymization();
        string effectiveQuery = anonymize && _queryTokenResolver != null
            ? _queryTokenResolver.ResolveTokens(request.Query, target)
            : request.Query;

        // 9-10. Open connection, stream and atomically finalize file
        return await ExecuteExportAsync(
            target, effectiveQuery, fullPath, targetDir, fileName, format, effectiveLimit, anonymize, request.Parameters, request.Overwrite, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<DataExportResult>> ExecuteExportAsync(
        DatabaseTargetContext target,
        string query,
        string fullPath,
        string targetDir,
        string fileName,
        string format,
        int effectiveLimit,
        bool anonymize,
        object? parameters,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        string tempPath = Path.Combine(targetDir, $"{fileName}.{Guid.NewGuid():N}.tmp");
        FileStream? fileStream = null;
        IDataRowWriter? writer = null;
        DbConnection? connection = null;
        DbTransaction? transaction = null;
        bool executionFailed = false;

        try
        {
            connection = _connectionFactory.CreateTargetConnection(target);
            await PrimaryConnection.OpenTargetAsync(connection, target, cancellationToken).ConfigureAwait(false);

            transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

            int baselineTranCount = await TransactionIntegrityGuard.GetTranCountAsync(
                connection, transaction, _options.Export.CommandTimeoutSeconds, cancellationToken).ConfigureAwait(false);

            var messages = new List<string>();
            var sqlConn = connection as SqlConnection;
            SqlInfoMessageEventHandler infoMessageHandler = (_, e) => messages.Add(e.Message);
            if (sqlConn is not null)
            {
                sqlConn.InfoMessage += infoMessageHandler;
            }

            try
            {
                await DatabaseCommandExecutor.ExecuteSetOptionAsync(
                    connection, transaction, "SET STATISTICS IO ON", _options.Export.CommandTimeoutSeconds, cancellationToken).ConfigureAwait(false);
                await DatabaseCommandExecutor.ExecuteSetOptionAsync(
                    connection, transaction, "SET STATISTICS TIME ON", _options.Export.CommandTimeoutSeconds, cancellationToken).ConfigureAwait(false);
                await DatabaseCommandExecutor.ExecuteSetOptionAsync(
                    connection, transaction, $"SET ROWCOUNT {effectiveLimit}", _options.Export.CommandTimeoutSeconds, cancellationToken).ConfigureAwait(false);

                var stopwatch = Stopwatch.StartNew();

                fileStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 65536, useAsync: true);
                writer = DataWriterFactory.CreateWriter(format, fileStream);

                await using var command = connection.CreateCommand();
                command.CommandText = query;
                command.Transaction = transaction;
                command.CommandTimeout = _options.Export.CommandTimeoutSeconds;
                SqlParameterBinder.BindParameters(command, parameters);

                CommandBehavior behavior = anonymize
                    ? CommandBehavior.SequentialAccess | CommandBehavior.KeyInfo
                    : CommandBehavior.SequentialAccess;

                int rowCount = 0;
                bool hasResultSet = false;
                var tracker = new QueryExecutionService.RowAnonymizationTracker();
                List<ExportColumnInfo> exportColumns = [];

                var reader = await command.ExecuteReaderAsync(behavior, cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    if (reader.FieldCount > 0)
                    {
                        hasResultSet = true;
                        var visibleColumns = QueryExecutionService.GetColumnNames(reader);
                        exportColumns = new List<ExportColumnInfo>(visibleColumns.Length);
                        for (int i = 0; i < visibleColumns.Length; i++)
                        {
                            Type fieldType = reader.GetFieldType(visibleColumns[i].Ordinal);
                            exportColumns.Add(new ExportColumnInfo(visibleColumns[i].Name, MapToColumnType(fieldType)));
                        }

                        await writer.WriteHeaderAsync(exportColumns, cancellationToken).ConfigureAwait(false);

                        var anonCtx = await QueryExecutionService.ResolveAnonymizationContextAsync(
                            reader, visibleColumns, anonymize, target, _anonymizationRuleProvider, _tokenizationOptions.IsUsable, cancellationToken).ConfigureAwait(false);

                        var values = new object?[visibleColumns.Length];
                        while (rowCount < effectiveLimit && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            for (int i = 0; i < visibleColumns.Length; i++)
                            {
                                int ordinal = visibleColumns[i].Ordinal;
                                object? raw = await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false)
                                    ? null
                                    : reader.GetValue(ordinal);
                                values[i] = QueryExecutionService.AnonymizeCell(_anonymizer, visibleColumns[i].Name, raw, anonCtx, ordinal, tracker);
                            }
                            await writer.WriteRowAsync(exportColumns, values, cancellationToken).ConfigureAwait(false);
                            rowCount++;
                        }
                    }
                }

                if (!hasResultSet)
                {
                    await writer.DisposeAsync().ConfigureAwait(false);
                    writer = null;
                    await fileStream.DisposeAsync().ConfigureAwait(false);
                    fileStream = null;
                    CleanupTempFile(tempPath);
                    await DatabaseCleanup.RollbackAsync(transaction, _options.Export.CommandTimeoutSeconds, _logger, preservePrimaryFailure: true).ConfigureAwait(false);
                    return SqlToAiError.WriteOperationBlocked("The query did not return a result set.");
                }

                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                await writer.DisposeAsync().ConfigureAwait(false);
                writer = null;

                await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                await fileStream.DisposeAsync().ConfigureAwait(false);
                fileStream = null;

                await DatabaseCleanup.ResetRowCountAsync(connection, transaction, _options.Export.CommandTimeoutSeconds, _logger, executionFailed).ConfigureAwait(false);

                int tranCountAfterExecution = await TransactionIntegrityGuard.GetTranCountAsync(
                    connection, transaction, _options.Export.CommandTimeoutSeconds, cancellationToken).ConfigureAwait(false);
                if (tranCountAfterExecution != baselineTranCount)
                {
                    CleanupTempFile(tempPath);
                    return await TransactionIntegrityGuard.RejectViolationAsync<DataExportResult>(
                        _logger, target.QualifiedName, transaction, _options.Export.CommandTimeoutSeconds).ConfigureAwait(false);
                }

                await DatabaseCleanup.RollbackAsync(transaction, _options.Export.CommandTimeoutSeconds, _logger).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                stopwatch.Stop();

                File.Move(tempPath, fullPath, overwrite: overwrite);

                long fileSizeBytes = new FileInfo(fullPath).Length;
                var (cpu, _, logical, _, _, _) = PerformanceMetricsCalculator.ParseRunMessages(messages);

                return new DataExportResult(
                    Status: "completed",
                    FilePath: fullPath,
                    Format: format,
                    RowCount: rowCount,
                    FileSizeBytes: fileSizeBytes,
                    Columns: exportColumns,
                    ElapsedMs: stopwatch.ElapsedMilliseconds,
                    CpuMs: cpu,
                    LogicalReads: logical,
                    WasAnonymized: tracker.WasAnonymized,
                    AnonymizedColumns: tracker.AnonymizedColumns);
            }
            finally
            {
                if (sqlConn is not null)
                {
                    sqlConn.InfoMessage -= infoMessageHandler;
                }
            }
        }
        catch (OperationCanceledException)
        {
            executionFailed = true;
            if (writer != null) { try { await writer.DisposeAsync().ConfigureAwait(false); } catch { } writer = null; }
            if (fileStream != null) { try { await fileStream.DisposeAsync().ConfigureAwait(false); } catch { } fileStream = null; }
            CleanupTempFile(tempPath);
            if (transaction != null)
            {
                await DatabaseCleanup.RollbackAsync(transaction, _options.Export.CommandTimeoutSeconds, _logger, preservePrimaryFailure: true).ConfigureAwait(false);
            }
            throw;
        }
        catch (Exception ex)
        {
            executionFailed = true;
            if (writer != null) { try { await writer.DisposeAsync().ConfigureAwait(false); } catch { } writer = null; }
            if (fileStream != null) { try { await fileStream.DisposeAsync().ConfigureAwait(false); } catch { } fileStream = null; }
            CleanupTempFile(tempPath);
            if (transaction != null)
            {
                await DatabaseCleanup.RollbackAsync(transaction, _options.Export.CommandTimeoutSeconds, _logger, preservePrimaryFailure: true).ConfigureAwait(false);
            }

            LogExportFailed(_logger, target.QualifiedName, query, ex);
            string? anonymizedMessage = anonymize ? BuildAnonymizedQueryErrorMessage(ex) : null;
            return SqlToAiErrorMapper.MapException(ex, anonymizedMessage);
        }
        finally
        {
            if (writer != null)
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
            if (fileStream != null)
            {
                await fileStream.DisposeAsync().ConfigureAwait(false);
            }
            if (transaction != null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
            if (connection != null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    internal static bool IsQualifiedWindowsPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        string normalized = path.Replace('/', '\\');
        if (normalized.StartsWith(@"\\?\", StringComparison.Ordinal) || normalized.StartsWith(@"\\.\", StringComparison.Ordinal))
            return false;
        if (normalized.Length >= 3 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':' && normalized[2] == '\\')
        {
            return !normalized[2..].Contains(':', StringComparison.Ordinal);
        }
        if (!normalized.StartsWith(@"\\", StringComparison.Ordinal))
            return false;
        string[] parts = normalized[2..].Split('\\');
        return parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1])
            && parts[0] is not ("?" or ".") && !normalized.Contains(':', StringComparison.Ordinal);
    }

    internal static string MapToColumnType(Type? type)
    {
        if (type == null) return "string";

        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(int)) return "int";
        if (type == typeof(long)) return "long";
        if (type == typeof(short)) return "short";
        if (type == typeof(byte)) return "byte";
        if (type == typeof(sbyte)) return "short";
        if (type == typeof(uint)) return "long";
        if (type == typeof(ulong)) return "decimal";
        if (type == typeof(ushort)) return "int";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(double)) return "double";
        if (type == typeof(float)) return "float";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(string)) return "string";
        if (type == typeof(DateTime)) return "datetime";
        if (type == typeof(DateTimeOffset)) return "datetimeoffset";
        if (type == typeof(TimeSpan)) return "timespan";
        if (type == typeof(Guid)) return "guid";
        if (type == typeof(byte[])) return "binary";

        return "string";
    }

    private static string BuildAnonymizedQueryErrorMessage(Exception ex) => ex switch
    {
        SqlException sqlEx => $"(SQL error {sqlEx.Number}) the query failed during execution. Check syntax and column types; see server-side logs for details.",
        _ => "the query failed during execution.",
    };

    private static void CleanupTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // Ignore errors during temp file cleanup
        }
    }
}
