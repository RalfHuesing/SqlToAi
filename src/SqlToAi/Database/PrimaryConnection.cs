using System.Data.Common;
using SqlToAi.Domain;

namespace SqlToAi.Database;

internal static class PrimaryConnection
{
    internal static async Task OpenAsync(DbConnection connection, CancellationToken cancellationToken)
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
                ? "SqlServer connection attempt exceeded the configured time limit."
                : isTlsFailure
                ? SqlToAiErrorMapper.PrimaryTlsFailureMessage
                : "SqlServer connection failed. Check endpoint, authentication and encrypted transport settings.";
            throw new PrimaryConnectionException(error with { Message = message }, isTlsFailure);
        }
    }
}

internal sealed class PrimaryConnectionException(SqlToAiError error, bool isTlsFailure) : Exception(error.Message)
{
    internal SqlToAiError Error { get; } = error;
    internal bool IsTlsFailure { get; } = isTlsFailure;
}
