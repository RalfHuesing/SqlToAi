#nullable enable

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Validates a SQL query using <c>SET PARSEONLY ON</c> within a rollback transaction,
/// guarded by the same whitelist and access-level checks as query execution.
/// </summary>
public sealed class QueryValidationService : IQueryValidationService
{
    private static readonly Action<ILogger, string, string, Exception?> LogValidationFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(1, "ValidationFailed"),
            "Query validation failed for database {Database}. Query: {Query}");

    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly IQuerySafetyValidator _querySafetyValidator;
    private readonly QueryExecutionOptions _queryExecutionOptions;
    private readonly ILogger<QueryValidationService> _logger;

    /// <summary>Initializes a new instance of <see cref="QueryValidationService"/>.</summary>
    public QueryValidationService(
        IDatabaseConnectionFactory connectionFactory,
        IQuerySafetyValidator querySafetyValidator,
        IOptions<SqlToAiOptions> options,
        ILogger<QueryValidationService> logger)
    {
        _connectionFactory = connectionFactory;
        _querySafetyValidator = querySafetyValidator;
        _queryExecutionOptions = options.Value.QueryExecution;
        _logger = logger;
    }

    /// <inheritdoc/>
    /// <inheritdoc/>
    public Task<Result<string>> ValidateQueryAsync(
        string databaseName,
        string query,
        CancellationToken cancellationToken = default)
    {
        return ValidateQueryAsync(databaseName, query, parameters: null, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<string>> ValidateQueryAsync(
        string databaseName,
        string query,
        object? parameters,
        CancellationToken cancellationToken = default)
    {
        // 1-5. Run the shared 6-stage guardrail pipeline. QueryValidationService is the only
        // service that allows SchemaOnly access — it is meant to validate schema queries
        // without ever touching data.
        var safetyResult = await _querySafetyValidator
            .ValidateQuerySafetyAsync(databaseName, query, allowSchemaOnly: true, cancellationToken)
            .ConfigureAwait(false);
        if (safetyResult.IsFailure)
        {
            return safetyResult.Error;
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            connection.Open();

            using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                await ExecuteParseonlyValidationAsync(connection, transaction, query, parameters, cancellationToken);
                return "Query syntax is valid.";
            }
            finally
            {
                await transaction.RollbackAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogValidationFailed(_logger, databaseName, query, ex);
            return SqlToAiErrorMapper.MapException(ex);
        }
    }

    private async Task ExecuteParseonlyValidationAsync(
        DbConnection connection,
        DbTransaction transaction,
        string query,
        object? parameters,
        CancellationToken cancellationToken)
    {
        using var setNoexecCmd = connection.CreateCommand();
        setNoexecCmd.CommandText = "SET NOEXEC ON";
        setNoexecCmd.Transaction = transaction;
        setNoexecCmd.CommandTimeout = _queryExecutionOptions.CommandTimeoutSeconds;
        await setNoexecCmd.ExecuteNonQueryAsync(cancellationToken);

        try
        {
            using var queryCmd = connection.CreateCommand();
            queryCmd.CommandText = query;
            queryCmd.Transaction = transaction;
            queryCmd.CommandTimeout = _queryExecutionOptions.CommandTimeoutSeconds;
            SqlParameterBinder.BindParameters(queryCmd, parameters);
            await queryCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            using var resetCmd = connection.CreateCommand();
            resetCmd.CommandText = "SET NOEXEC OFF";
            resetCmd.Transaction = transaction;
            resetCmd.CommandTimeout = _queryExecutionOptions.CommandTimeoutSeconds;
            await resetCmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
