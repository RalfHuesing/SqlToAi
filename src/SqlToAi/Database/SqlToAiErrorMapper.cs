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

        if (ex is SqlException sqlEx && (sqlEx.Number == -2 || sqlEx.Number == 121 || sqlEx.Number == 258))
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
                20 or 40 or 53 or 233 or 10054 or 10060 or 10061 or 18456 => true,
                _ => false
            };
        }

        return false;
    }
}
