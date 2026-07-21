#nullable enable

using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace SqlToAi.Database;

/// <summary>
/// Connection parameters for a cross-cutting secondary database (e.g. schema metadata
/// descriptions, central anonymization rules) that may live on its own SQL Server instance,
/// independent of any customer database connection.
/// </summary>
internal sealed record SecondaryConnectionSettings(
    string? Server,
    string? Database,
    string? UserId,
    string? Password,
    bool IntegratedSecurity,
    int CommandTimeoutSeconds);

/// <summary>
/// Builds ADO.NET connections for cross-cutting secondary databases, falling back to the
/// standard <see cref="IDatabaseConnectionFactory"/> (the customer connection) when no
/// dedicated server is configured for the secondary database.
/// </summary>
internal static class SecondaryConnectionBuilder
{
    /// <summary>
    /// Creates a connection for a secondary database. If <see cref="SecondaryConnectionSettings.Server"/>
    /// is not configured, falls back to <paramref name="fallbackConnectionFactory"/> against
    /// <paramref name="fallbackDatabaseName"/> (the customer connection).
    /// </summary>
    public static DbConnection Create(
        SecondaryConnectionSettings settings,
        string applicationName,
        string fallbackDatabaseName,
        IDatabaseConnectionFactory fallbackConnectionFactory)
    {
        string targetDb = !string.IsNullOrWhiteSpace(settings.Database) ? settings.Database! : fallbackDatabaseName;

        if (string.IsNullOrWhiteSpace(settings.Server))
        {
            return fallbackConnectionFactory.CreateConnection(targetDb);
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = settings.Server,
            InitialCatalog = !string.IsNullOrWhiteSpace(targetDb)
                ? targetDb
                : throw new InvalidOperationException($"{applicationName} SQL connection error: Database name must be explicitly specified."),
            ApplicationName = applicationName,
            TrustServerCertificate = true, // Facilitate developer local connections
            ConnectTimeout = settings.CommandTimeoutSeconds
        };

        ApplyCredentials(builder, settings, applicationName);

        return new SqlConnection(builder.ConnectionString);
    }

    private static void ApplyCredentials(SqlConnectionStringBuilder builder, SecondaryConnectionSettings settings, string applicationName)
    {
        if (settings.IntegratedSecurity)
        {
            builder.IntegratedSecurity = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.UserId) || string.IsNullOrWhiteSpace(settings.Password))
        {
            throw new InvalidOperationException($"{applicationName} SQL authentication error: 'IntegratedSecurity' is false, but 'UserId' or 'Password' is not configured.");
        }
        builder.UserID = settings.UserId;
        builder.Password = settings.Password;
    }
}
