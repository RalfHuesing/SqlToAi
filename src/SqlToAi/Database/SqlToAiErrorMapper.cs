#nullable enable

using System;
using System.Net.Sockets;
using Microsoft.Data.SqlClient;
using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Maps exceptions occurring during SQL operations to standardized <see cref="SqlToAiError"/> objects.
/// </summary>
public static class SqlToAiErrorMapper
{
    /// <summary>
    /// Maps the given exception to a corresponding <see cref="SqlToAiError"/>.
    /// </summary>
    /// <param name="ex">The caught exception.</param>
    /// <param name="customQueryErrorMessage">Optional custom message for query/infrastructure errors.</param>
    /// <returns>A structured <see cref="SqlToAiError"/> instance.</returns>
    public static SqlToAiError MapException(Exception ex, string? customQueryErrorMessage = null)
    {
        if (IsTimeoutException(ex))
        {
            return SqlToAiError.Timeout();
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
        if (ex is TimeoutException)
        {
            return true;
        }

        if (ex is SqlException sqlEx && (sqlEx.Number == SqlServerErrorCode.ClientQueryTimeout
            || sqlEx.Number == SqlServerErrorCode.SemaphoreTimeout
            || sqlEx.Number == SqlServerErrorCode.WaitTimeout))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Determines whether the exception represents an infrastructure or connection failure.
    /// </summary>
    public static bool IsInfrastructureException(Exception ex)
    {
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
}
