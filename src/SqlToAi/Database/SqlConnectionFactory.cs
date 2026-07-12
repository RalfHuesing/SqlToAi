#nullable enable

using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;

namespace SqlToAi.Database;

/// <summary>
/// Factory for creating Microsoft SQL Server connections using ADO.NET and SqlClient.
/// </summary>
public sealed class SqlConnectionFactory : IDatabaseConnectionFactory
{
    private readonly SqlToAiOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlConnectionFactory"/> class.
    /// </summary>
    /// <param name="options">The application options containing database connection properties.</param>
    public SqlConnectionFactory(IOptions<SqlToAiOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Creates and returns a new <see cref="SqlConnection"/> using the configured connection string parameters.
    /// </summary>
    /// <param name="databaseName">The database to connect to. Overrides default database if specified.</param>
    /// <returns>A configured <see cref="SqlConnection"/> instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if server connection settings are missing.</exception>
    public DbConnection CreateConnection(string? databaseName = null)
    {
        if (string.IsNullOrWhiteSpace(_options.SqlServer.Server))
        {
            throw new InvalidOperationException("SQL Server address must be configured via 'SqlServer:Server'.");
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = _options.SqlServer.Server,
            ApplicationName = "SqlToAi",
            TrustServerCertificate = true, // Facilitate developer local connections
            ConnectTimeout = _options.SqlServer.CommandTimeoutSeconds
        };

        if (_options.SqlServer.IntegratedSecurity)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(_options.SqlServer.UserId) || string.IsNullOrWhiteSpace(_options.SqlServer.Password))
            {
                throw new InvalidOperationException("SQL Server authentication error: 'IntegratedSecurity' is false, but 'UserId' or 'Password' is not configured.");
            }
            builder.UserID = _options.SqlServer.UserId;
            builder.Password = _options.SqlServer.Password;
        }

        // Set or override target database
        if (!string.IsNullOrWhiteSpace(databaseName))
        {
            builder.InitialCatalog = databaseName;
        }

        return new SqlConnection(builder.ConnectionString);
    }
}
