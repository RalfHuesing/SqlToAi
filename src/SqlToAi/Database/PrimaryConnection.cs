using System.Data.Common;
using SqlToAi.Domain;

namespace SqlToAi.Database;

internal static class PrimaryConnection
{
    internal static Task OpenTargetAsync(DbConnection connection, DatabaseTargetContext target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        return OpenCoreAsync(connection, $"SqlToAi:Servers:{target.ServerAlias}", cancellationToken);
    }

    internal static Task OpenCatalogAsync(DbConnection connection, string serverAlias, CancellationToken cancellationToken) =>
        OpenCoreAsync(connection, $"SqlToAi:Servers:{DatabaseTargetParser.CanonicalizeAlias(serverAlias)}", cancellationToken);

    private static async Task OpenCoreAsync(DbConnection connection, string settingsPath, CancellationToken cancellationToken)
    {
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Discard the original exception before service loggers can serialize credentials
            // from its message, inner exceptions or provider-specific properties.
            var error = SqlToAiErrorMapper.MapException(exception);
            bool isTlsFailure = error.Code != SqlToAiError.TimeoutCode && SqlToAiErrorMapper.IsTlsException(exception);
            string message = error.Code == SqlToAiError.TimeoutCode
                ? $"{settingsPath} connection attempt exceeded the configured time limit. Check {settingsPath}:ConnectTimeoutSeconds."
                : isTlsFailure
                ? $"{settingsPath} TLS/certificate validation or handshake failed. Check the server certificate trust, validity and name, and the encrypted transport configuration."
                : $"{settingsPath} connection failed. Check endpoint, authentication and encrypted transport settings.";
            string code = error.Code == SqlToAiError.TimeoutCode
                ? SqlToAiError.TimeoutCode
                : SqlToAiError.InfrastructureErrorCode;
            throw new PrimaryConnectionException(new SqlToAiError(code, message), isTlsFailure);
        }
    }
}

internal sealed class PrimaryConnectionException(SqlToAiError error, bool isTlsFailure) : Exception(error.Message)
{
    internal SqlToAiError Error { get; } = error;
    internal bool IsTlsFailure { get; } = isTlsFailure;
}
