using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Database;

#pragma warning disable CA1848 // Use the LoggerMessage delegates

/// <summary>
/// Retrieves server and database environment metadata through a fixed, read-only diagnostic query.
/// </summary>
public sealed class EnvironmentService : IEnvironmentService
{
    private const string MetadataQuery = """
        SELECT
            CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS ProductVersion,
            CAST(SERVERPROPERTY('ProductMajorVersion') AS int) AS ProductMajorVersion,
            CAST(SERVERPROPERTY('Edition') AS nvarchar(128)) AS Edition,
            CAST(SERVERPROPERTY('EngineEdition') AS int) AS EngineEdition,
            d.compatibility_level AS CompatibilityLevel,
            d.collation_name AS Collation,
            d.state_desc AS StateDesc,
            d.is_read_only AS IsReadOnly
        FROM sys.databases AS d
        WHERE d.database_id = DB_ID();
        """;

    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly ISecurityGuard _securityGuard;
    private readonly IAccessLevelProvider _accessLevelProvider;
    private readonly SqlToAiOptions _options;
    private readonly DatabaseTargetResolver _targets;
    private readonly ILogger<EnvironmentService> _logger;

    public EnvironmentService(
        IDatabaseConnectionFactory connectionFactory,
        ISecurityGuard securityGuard,
        IAccessLevelProvider accessLevelProvider,
        IOptions<SqlToAiOptions> options,
        ILogger<EnvironmentService> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _securityGuard = securityGuard ?? throw new ArgumentNullException(nameof(securityGuard));
        _accessLevelProvider = accessLevelProvider ?? throw new ArgumentNullException(nameof(accessLevelProvider));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _targets = new DatabaseTargetResolver(_options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task<Result<DatabaseEnvironmentResult>> GetEnvironmentAsync(
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return Task.FromResult(Result<DatabaseEnvironmentResult>.Failure(
                SqlToAiError.InvalidParameters("database name must not be empty.")));
        }

        var targetResult = _targets.Resolve(databaseName);
        if (targetResult.IsFailure)
        {
            return Task.FromResult(Result<DatabaseEnvironmentResult>.Failure(targetResult.Error));
        }

        return GetEnvironmentAsync(targetResult.Value, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<DatabaseEnvironmentResult>> GetEnvironmentAsync(
        DatabaseTargetContext target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!_securityGuard.IsTargetAllowed(target))
        {
            return SqlToAiError.SafetyCheckFailed($"Database '{target.QualifiedName}' is blocked by security policies (static whitelist).");
        }

        var accessLevel = await _accessLevelProvider.GetTargetAccessLevelAsync(target, cancellationToken);
        if (accessLevel == AccessLevel.None)
        {
            return SqlToAiError.SafetyCheckFailed($"Database '{target.QualifiedName}' access was denied (AccessLevel: None).");
        }

        try
        {
            await using var connection = _connectionFactory.CreateTargetConnection(target);
            await PrimaryConnection.OpenTargetAsync(connection, target, cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = MetadataQuery;
            command.CommandType = CommandType.Text;
            command.CommandTimeout = _options.QueryExecution.CommandTimeoutSeconds;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return SqlToAiError.QueryError($"No environment metadata returned for database '{target.QualifiedName}'.");
            }

            var unavailableFields = new List<string>();

            string? productVersion = GetStringOrNull(reader, "ProductVersion");
            if (productVersion is null)
            {
                unavailableFields.Add("product_version");
            }

            int? productMajorVersion = GetInt32OrNull(reader, "ProductMajorVersion");
            if (productMajorVersion is null)
            {
                unavailableFields.Add("product_major_version");
            }

            string? edition = GetStringOrNull(reader, "Edition");
            if (edition is null)
            {
                unavailableFields.Add("edition");
            }

            int? engineEdition = GetInt32OrNull(reader, "EngineEdition");
            if (engineEdition is null)
            {
                unavailableFields.Add("engine_edition");
            }

            byte? compatibilityLevel = GetByteOrNull(reader, "CompatibilityLevel");
            if (compatibilityLevel is null)
            {
                unavailableFields.Add("compatibility_level");
            }

            string? collation = GetStringOrNull(reader, "Collation");
            if (collation is null)
            {
                unavailableFields.Add("collation");
            }

            string? stateDesc = GetStringOrNull(reader, "StateDesc");
            if (stateDesc is null)
            {
                unavailableFields.Add("state_desc");
            }

            bool? isReadOnly = GetBooleanOrNull(reader, "IsReadOnly");
            if (isReadOnly is null)
            {
                unavailableFields.Add("is_read_only");
            }

            return new DatabaseEnvironmentResult(
                Target: target.QualifiedName,
                ProductVersion: productVersion,
                ProductMajorVersion: productMajorVersion,
                Edition: edition,
                EngineEdition: engineEdition,
                CompatibilityLevel: compatibilityLevel,
                Collation: collation,
                StateDesc: stateDesc,
                IsReadOnly: isReadOnly,
                UnavailableFields: unavailableFields);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve environment metadata for database {Database}.", target.QualifiedName);
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

            if (IsPermissionException(ex))
            {
                return SqlToAiError.SafetyCheckFailed($"Permission denied while retrieving environment metadata: {ex.Message}");
            }

            if (SqlToAiErrorMapper.IsInfrastructureException(ex))
            {
                return SqlToAiError.InfrastructureError(ex.Message);
            }

            return SqlToAiError.QueryError(ex.Message);
        }
    }

    private static string? GetStringOrNull(DbDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? GetInt32OrNull(DbDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        object val = reader.GetValue(ordinal);
        return Convert.ToInt32(val, CultureInfo.InvariantCulture);
    }

    private static byte? GetByteOrNull(DbDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        object val = reader.GetValue(ordinal);
        return Convert.ToByte(val, CultureInfo.InvariantCulture);
    }

    private static bool? GetBooleanOrNull(DbDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        object val = reader.GetValue(ordinal);
        return Convert.ToBoolean(val, CultureInfo.InvariantCulture);
    }

    private static bool IsPermissionException(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (current is SqlException sql)
            {
                if (sql.Errors.Cast<SqlError>().Any(e => e.Number is SqlServerErrorCode.ActionPermissionDenied
                        or SqlServerErrorCode.InsufficientPermission
                        or SqlServerErrorCode.ShowplanPermissionMissing
                        or 229 or 230)
                    || sql.Number is SqlServerErrorCode.ActionPermissionDenied
                        or SqlServerErrorCode.InsufficientPermission
                        or SqlServerErrorCode.ShowplanPermissionMissing
                        or 229 or 230
                    || sql.Message.Contains("permission was denied", StringComparison.OrdinalIgnoreCase)
                    || sql.Message.Contains("permission denied", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
