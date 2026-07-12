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
        string? envConnectionString = Environment.GetEnvironmentVariable("SQLTOAI_CONNECTION_STRING");
        SqlConnectionStringBuilder builder;

        if (!string.IsNullOrWhiteSpace(envConnectionString))
        {
            builder = new SqlConnectionStringBuilder(envConnectionString);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(_options.SqlDatabase.Server))
            {
                throw new InvalidOperationException("SQL Server address must be configured either via 'SqlDatabase:Server' or the 'SQLTOAI_CONNECTION_STRING' environment variable.");
            }

            builder = new SqlConnectionStringBuilder
            {
                DataSource = _options.SqlDatabase.Server,
                InitialCatalog = !string.IsNullOrWhiteSpace(_options.SqlDatabase.DefaultDatabase)
                    ? _options.SqlDatabase.DefaultDatabase
                    : _options.Databases.Default,
                ApplicationName = "SqlToAi",
                TrustServerCertificate = true, // Facilitate developer local connections
                ConnectTimeout = _options.SqlDatabase.CommandTimeoutSeconds
            };

            if (!string.IsNullOrEmpty(_options.SqlDatabase.UserId))
            {
                builder.UserID = _options.SqlDatabase.UserId;
            }

            if (!string.IsNullOrEmpty(_options.SqlDatabase.Password))
            {
                builder.Password = _options.SqlDatabase.Password;
            }

            if (string.IsNullOrWhiteSpace(builder.UserID))
            {
                builder.IntegratedSecurity = true;
            }
        }

        // Set or override target database
        if (!string.IsNullOrWhiteSpace(databaseName))
        {
            builder.InitialCatalog = databaseName;
        }

        return new SqlConnection(builder.ConnectionString);
    }
}
