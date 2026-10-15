using System;
using System.Net.Sockets;
using System.ComponentModel;
using System.Security.Authentication;
using Microsoft.Data.SqlClient;
using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Maps exceptions occurring during SQL operations to standardized <see cref="SqlToAiError"/> objects.
/// </summary>
public static class SqlToAiErrorMapper
{
    internal const string PrimaryTlsFailureMessage = "SqlServer TLS/certificate validation or handshake failed. Check the server certificate trust, validity and name, and the encrypted transport configuration.";
    /// <summary>
    /// Maps the given exception to a corresponding <see cref="SqlToAiError"/>.
    /// </summary>
    /// <param name="ex">The caught exception.</param>
    /// <param name="customQueryErrorMessage">Optional custom message for query/infrastructure errors.</param>
    /// <returns>A structured <see cref="SqlToAiError"/> instance.</returns>
    public static SqlToAiError MapException(Exception ex, string? customQueryErrorMessage = null)
    {
        if (ex is PrimaryConnectionException connectionException)
        {
            return connectionException.Error;
        }

        if (IsTimeoutException(ex))
        {
            return SqlToAiError.Timeout();
        }

        if (IsTlsException(ex))
        {
            return SqlToAiError.InfrastructureError(PrimaryTlsFailureMessage);
        }

        if (IsInfrastructureException(ex))
        {
            string message = customQueryErrorMessage ?? ex.Message;
            return SqlToAiError.InfrastructureError(message);
        }

        string queryErrorMessage = customQueryErrorMessage ?? ex.Message;
        return SqlToAiError.QueryError(queryErrorMessage);
    }

    /// <summary>
    /// Determines whether the exception represents a SQL execution timeout.
    /// </summary>
    public static bool IsTimeoutException(Exception ex)
    {
        if (ex is PrimaryConnectionException connectionException)
        {
            return connectionException.Error.Code == SqlToAiError.TimeoutCode;
        }

        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (current is TimeoutException
                || current is SqlException sql && sql.Errors.Cast<SqlError>().Any(error => error.Number is
                    SqlServerErrorCode.ClientQueryTimeout or SqlServerErrorCode.SemaphoreTimeout or SqlServerErrorCode.WaitTimeout))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether the exception represents an infrastructure or connection failure.
    /// </summary>
    public static bool IsInfrastructureException(Exception ex)
    {
        if (ex is PrimaryConnectionException connectionException)
        {
            return connectionException.Error.Code == SqlToAiError.InfrastructureErrorCode;
        }

        if (IsTlsException(ex))
        {
            return true;
        }
        if (ex is SocketException || ex.InnerException is SocketException)
        {
            return true;
        }

        if (ex is SqlException sqlEx)
        {
            if (sqlEx.Class >= 20)
            {
                return true;
            }

            return sqlEx.Number switch
            {
                SqlServerErrorCode.InstanceNotFound
                    or SqlServerErrorCode.StatementTooComplex
                    or SqlServerErrorCode.ServerNotFound
                    or SqlServerErrorCode.ConnectionInitializationError
                    or SqlServerErrorCode.ConnectionReset
                    or SqlServerErrorCode.ConnectionTimedOut
                    or SqlServerErrorCode.ConnectionRefused
                    or SqlServerErrorCode.LoginFailed => true,
                _ => false
            };
        }

        return false;
    }

    internal static bool IsTlsException(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is AuthenticationException
                || current is Win32Exception native && IsCertificateError(native.NativeErrorCode))
            {
                return true;
            }

            if (current is SqlException sql && sql.Errors.Cast<SqlError>().Any(error =>
                IsCertificateError(error.Number)
                || (error.Number == 0 || error.Class >= 20)
                    && (error.Message.Contains("SSL Provider", StringComparison.OrdinalIgnoreCase)
                        || error.Message.Contains("TLS handshake", StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }
        }

        return false;
    }

    internal static string GetSafeConnectionFailureCategory(Exception exception) =>
        IsTlsException(exception)
            ? "TLS/certificate connection failure"
            : "SQL connection or provider failure";

    private static bool IsCertificateError(int number) => unchecked((uint)number) is
        0x80090322 or 0x80090325 or 0x80090326 or 0x80090327 or 0x80090328 or 0x80090331
        or >= 0x800B0101 and <= 0x800B0114;
}
