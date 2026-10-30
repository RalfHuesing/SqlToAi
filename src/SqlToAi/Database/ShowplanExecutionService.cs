using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;

namespace SqlToAi.Database;

#pragma warning disable CA1848 // Use the LoggerMessage delegates

public sealed class ShowplanExecutionService : IShowplanExecutionService
{
    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly SqlToAiOptions _options;
    private readonly DatabaseTargetResolver _targets;
    private readonly ILogger<ShowplanExecutionService> _logger;

    public ShowplanExecutionService(
        IDatabaseConnectionFactory connectionFactory,
        IOptions<SqlToAiOptions> options,
        ILogger<ShowplanExecutionService> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _targets = new DatabaseTargetResolver(_options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<Result<string>> ExecuteShowplanAsync(
        string databaseName,
        string query,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return Task.FromResult(Result<string>.Failure(
                SqlToAiError.InvalidParameters("database name must not be empty.")));
        }

        var targetResult = _targets.Resolve(databaseName);
        if (targetResult.IsFailure)
        {
            return Task.FromResult(Result<string>.Failure(targetResult.Error));
        }

        return ExecuteShowplanAsync(targetResult.Value, query, parameters, cancellationToken);
    }

    public async Task<Result<string>> ExecuteShowplanAsync(
        DatabaseTargetContext target,
        string query,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(query))
        {
            return SqlToAiError.InvalidParameters("Query must not be empty.");
        }

        int commandTimeout = _options.QueryExecution.CommandTimeoutSeconds;
        long maxBytes = _options.ExplainQuery.MaxShowplanXmlBytes;

        DbConnection? connection = null;
        bool showplanOnSucceeded = false;
        bool discardConnection = false;

        try
        {
            connection = _connectionFactory.CreateTargetConnection(target, pooling: false);
            await PrimaryConnection.OpenTargetAsync(connection, target, cancellationToken).ConfigureAwait(false);

            // Batch 1: SET SHOWPLAN_XML ON
            await using (var onCmd = connection.CreateCommand())
            {
                onCmd.CommandText = "SET SHOWPLAN_XML ON";
                onCmd.CommandType = CommandType.Text;
                onCmd.CommandTimeout = commandTimeout;
                await onCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            showplanOnSucceeded = true;

            // Batch 2: The query with bound parameters
            Result<string> planResult;
            await using (var queryCmd = connection.CreateCommand())
            {
                queryCmd.CommandText = query;
                queryCmd.CommandType = CommandType.Text;
                queryCmd.CommandTimeout = commandTimeout;
                SqlParameterBinder.BindParameters(queryCmd, parameters);

                await using var reader = await queryCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                // Positive result check: exactly one single-column, single-row showplan XML result
                if (reader.FieldCount != 1)
                {
                    discardConnection = true;
                    return SqlToAiError.QueryError("Query did not produce an estimated showplan result.");
                }

                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    discardConnection = true;
                    return SqlToAiError.QueryError("Query did not produce an estimated showplan result.");
                }

                if (await reader.IsDBNullAsync(0, cancellationToken).ConfigureAwait(false))
                {
                    discardConnection = true;
                    return SqlToAiError.QueryError("Query did not produce an estimated showplan result.");
                }

                planResult = await ReadBoundedShowplanXmlAsync(reader, maxBytes, cancellationToken).ConfigureAwait(false);
                if (planResult.IsFailure)
                {
                    if (planResult.Error.Message.Contains("showplan", StringComparison.OrdinalIgnoreCase)
                        && !planResult.Error.Message.Contains("exceeds", StringComparison.OrdinalIgnoreCase))
                    {
                        discardConnection = true;
                    }
                    return planResult.Error;
                }

                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    discardConnection = true;
                    return SqlToAiError.QueryError("Query did not produce an estimated showplan result.");
                }

                if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                {
                    discardConnection = true;
                    return SqlToAiError.QueryError("Query did not produce an estimated showplan result.");
                }
            }

            return planResult;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute showplan query for database {Database}.", target.QualifiedName);

            if (ex is PrimaryConnectionException pex)
            {
                return pex.Error;
            }

            if (SqlToAiErrorMapper.IsTimeoutException(ex))
            {
                return SqlToAiError.Timeout();
            }

            if (SqlToAiErrorMapper.IsTlsException(ex))
            {
                return SqlToAiError.InfrastructureError(SqlToAiErrorMapper.PrimaryTlsFailureMessage);
            }

            if (TryMapPermissionError(ex, out var permissionError))
            {
                return permissionError!;
            }

            if (SqlToAiErrorMapper.IsInfrastructureException(ex))
            {
                return SqlToAiError.InfrastructureError(ex.Message);
            }

            return SqlToAiError.QueryError(ex.Message);
        }
        finally
        {
            if (connection is not null)
            {
                try
                {
                    if (showplanOnSucceeded && !discardConnection && connection.State == ConnectionState.Open)
                    {
                        await TryResetShowplanAsync(connection, commandTimeout, _logger).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task<Result<string>> ReadBoundedShowplanXmlAsync(
        DbDataReader reader,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        TextReader textReader;
        try
        {
            textReader = reader.GetTextReader(0);
        }
        catch (NotSupportedException)
        {
            textReader = new StringReader(reader.GetString(0));
        }

        using (textReader)
        {
            var sb = new StringBuilder();
            var encoder = Encoding.UTF8.GetEncoder();
            char[] buffer = new char[4096];
            long totalUtf8Bytes = 0;
            int charsRead;

            while ((charsRead = await textReader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int byteCount = encoder.GetByteCount(buffer, 0, charsRead, flush: false);
                totalUtf8Bytes += byteCount;
                if (totalUtf8Bytes > maxBytes)
                {
                    return SqlToAiError.QueryError($"Showplan XML size exceeds the configured limit of {maxBytes} bytes.");
                }
                sb.Append(buffer, 0, charsRead);
            }

            int flushBytes = encoder.GetByteCount(buffer, 0, 0, flush: true);
            totalUtf8Bytes += flushBytes;
            if (totalUtf8Bytes > maxBytes)
            {
                return SqlToAiError.QueryError($"Showplan XML size exceeds the configured limit of {maxBytes} bytes.");
            }

            string xml = sb.ToString();
            if (string.IsNullOrWhiteSpace(xml) || !IsShowplanXml(xml))
            {
                return Result<string>.Failure(SqlToAiError.QueryError("Query did not produce an estimated showplan result."));
            }

            return Result<string>.Success(xml);
        }
    }

    private static bool IsShowplanXml(string text)
    {
        ReadOnlySpan<char> span = text.AsSpan().Trim();
        if (span.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            int endDecl = span.IndexOf("?>", StringComparison.Ordinal);
            if (endDecl < 0)
            {
                return false;
            }
            span = span[(endDecl + 2)..].TrimStart();
        }

        return span.StartsWith("<ShowPlanXML", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task TryResetShowplanAsync(DbConnection connection, int commandTimeout, ILogger logger)
    {
        int cleanupTimeout = DatabaseCleanup.GetTimeoutSeconds(commandTimeout);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(cleanupTimeout));
        try
        {
            await DatabaseCommandExecutor.ExecuteSetOptionAsync(connection, transaction: null, "SET SHOWPLAN_XML OFF", cleanupTimeout, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to reset SHOWPLAN_XML OFF during cleanup; discarding non-pooled connection.");
        }
    }

    private static bool TryMapPermissionError(Exception ex, out SqlToAiError? error)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (current is SqlException sql)
            {
                if (sql.Number == SqlServerErrorCode.ShowplanPermissionMissing
                    || sql.Errors.Cast<SqlError>().Any(e => e.Number == SqlServerErrorCode.ShowplanPermissionMissing)
                    || sql.Message.Contains("SHOWPLAN", StringComparison.OrdinalIgnoreCase))
                {
                    error = SqlToAiError.SafetyCheckFailed("Permission denied: SHOWPLAN permission is missing.");
                    return true;
                }

                if (sql.Number is 229 or 230
                    || sql.Errors.Cast<SqlError>().Any(e => e.Number is 229 or 230)
                    || (sql.Message.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                        && (sql.Message.Contains("permission", StringComparison.OrdinalIgnoreCase)
                            || sql.Number is SqlServerErrorCode.ActionPermissionDenied or SqlServerErrorCode.InsufficientPermission)))
                {
                    error = SqlToAiError.SafetyCheckFailed("Permission denied: SELECT permission is missing.");
                    return true;
                }

                if (sql.Number is SqlServerErrorCode.ActionPermissionDenied or SqlServerErrorCode.InsufficientPermission
                    || sql.Errors.Cast<SqlError>().Any(e => e.Number is SqlServerErrorCode.ActionPermissionDenied or SqlServerErrorCode.InsufficientPermission)
                    || sql.Message.Contains("permission was denied", StringComparison.OrdinalIgnoreCase)
                    || sql.Message.Contains("permission denied", StringComparison.OrdinalIgnoreCase))
                {
                    error = SqlToAiError.SafetyCheckFailed("Permission denied.");
                    return true;
                }
            }
        }

        error = null;
        return false;
    }
}
