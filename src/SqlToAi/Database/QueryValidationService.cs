#nullable enable

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Security;

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
    private readonly ISecurityGuard _securityGuard;
    private readonly IAccessLevelProvider _accessLevelProvider;
    private readonly SqlServerOptions _dbOptions;
    private readonly ILogger<QueryValidationService> _logger;

    /// <summary>Initializes a new instance of <see cref="QueryValidationService"/>.</summary>
    public QueryValidationService(
        IDatabaseConnectionFactory connectionFactory,
        ISecurityGuard securityGuard,
        IAccessLevelProvider accessLevelProvider,
        IOptions<SqlToAiOptions> options,
        ILogger<QueryValidationService> logger)
    {
        _connectionFactory = connectionFactory;
        _securityGuard = securityGuard;
        _accessLevelProvider = accessLevelProvider;
        _dbOptions = options.Value.SqlServer;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<string>> ValidateQueryAsync(
        string databaseName,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return SqlToAiError.InvalidParameters("Database name must not be empty.");
        }
        if (string.IsNullOrWhiteSpace(query))
        {
            return SqlToAiError.InvalidParameters("Query must not be empty.");
        }

        if (!_securityGuard.IsDatabaseAllowed(databaseName))
        {
            return SqlToAiError.SafetyCheckFailed(databaseName);
        }

        var accessLevel = await _accessLevelProvider.GetAccessLevelAsync(databaseName, cancellationToken);
        if (accessLevel == AccessLevel.None)
        {
            return SqlToAiError.WriteOperationBlocked($"Database '{databaseName}' has AccessLevel None.");
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            connection.Open();

            using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                await ExecuteParseonlyValidationAsync(connection, transaction, query, cancellationToken);
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
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    private async Task ExecuteParseonlyValidationAsync(
        DbConnection connection,
        DbTransaction transaction,
        string query,
        CancellationToken cancellationToken)
    {
        using var setParseonlyCmd = connection.CreateCommand();
        setParseonlyCmd.CommandText = "SET PARSEONLY ON";
        setParseonlyCmd.Transaction = transaction;
        setParseonlyCmd.CommandTimeout = _dbOptions.CommandTimeoutSeconds;
        await setParseonlyCmd.ExecuteNonQueryAsync(cancellationToken);

        using var queryCmd = connection.CreateCommand();
        queryCmd.CommandText = query;
        queryCmd.Transaction = transaction;
        queryCmd.CommandTimeout = _dbOptions.CommandTimeoutSeconds;
        await queryCmd.ExecuteNonQueryAsync(cancellationToken);

        using var resetCmd = connection.CreateCommand();
        resetCmd.CommandText = "SET PARSEONLY OFF";
        resetCmd.Transaction = transaction;
        resetCmd.CommandTimeout = _dbOptions.CommandTimeoutSeconds;
        await resetCmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
