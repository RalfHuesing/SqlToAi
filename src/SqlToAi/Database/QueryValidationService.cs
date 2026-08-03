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
    private readonly IReadOnlyGuard _readOnlyGuard;
    private readonly SqlServerOptions _dbOptions;
    private readonly ILogger<QueryValidationService> _logger;

    /// <summary>Initializes a new instance of <see cref="QueryValidationService"/>.</summary>
    public QueryValidationService(
        IDatabaseConnectionFactory connectionFactory,
        ISecurityGuard securityGuard,
        IAccessLevelProvider accessLevelProvider,
        IReadOnlyGuard readOnlyGuard,
        IOptions<SqlToAiOptions> options,
        ILogger<QueryValidationService> logger)
    {
        _connectionFactory = connectionFactory;
        _securityGuard = securityGuard;
        _accessLevelProvider = accessLevelProvider;
        _readOnlyGuard = readOnlyGuard;
        _dbOptions = options.Value.SqlServer;
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

        // Read-only guard: reject mutating statements, unless this database is fully unlocked
        // via AccessCheckSql returning ReadWrite. This mirrors QueryExecutionService's layer 4 —
        // defense-in-depth on top of SET PARSEONLY, which alone should already prevent any
        // statement from actually executing, but whose exact semantics are not something this
        // tool's safety should rest on unverified and unaided.
        bool writeAllowed = accessLevel == AccessLevel.ReadWrite;

        if (!writeAllowed && !_readOnlyGuard.IsQuerySafe(query))
        {
            return SqlToAiError.WriteOperationBlocked("The query contains mutating SQL keywords and was rejected.");
        }

        // Multi-statement validation — always enforced, write-allowed or not, same as
        // QueryExecutionService, to keep the blast radius of a single call limited to one
        // statement regardless of PARSEONLY's actual behavior.
        if (SqlMultiStatementDetector.ContainsMultipleStatements(query))
        {
            return SqlToAiError.MultipleStatementsForbidden();
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
        setNoexecCmd.CommandTimeout = _dbOptions.CommandTimeoutSeconds;
        await setNoexecCmd.ExecuteNonQueryAsync(cancellationToken);

        try
        {
            using var queryCmd = connection.CreateCommand();
            queryCmd.CommandText = query;
            queryCmd.Transaction = transaction;
            queryCmd.CommandTimeout = _dbOptions.CommandTimeoutSeconds;
            SqlParameterBinder.BindParameters(queryCmd, parameters);
            await queryCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            using var resetCmd = connection.CreateCommand();
            resetCmd.CommandText = "SET NOEXEC OFF";
            resetCmd.Transaction = transaction;
            resetCmd.CommandTimeout = _dbOptions.CommandTimeoutSeconds;
            await resetCmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
