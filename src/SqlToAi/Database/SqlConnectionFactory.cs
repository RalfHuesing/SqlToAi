using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Factory for creating Microsoft SQL Server connections using ADO.NET and SqlClient.
/// </summary>
public sealed class SqlConnectionFactory : IDatabaseConnectionFactory
{
    private readonly SqlToAiOptions _options;
    private readonly DatabaseTargetResolver _targets;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlConnectionFactory"/> class.
    /// </summary>
    /// <param name="options">The application options containing database connection properties.</param>
    public SqlConnectionFactory(IOptions<SqlToAiOptions> options)
    {
        _options = options.Value;
        _targets = new DatabaseTargetResolver(_options);
    }

    public DbConnection CreateTargetConnection(DatabaseTargetContext target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return CreateNamedConnection(target.ServerAlias, target.DatabaseName);
    }

    public Result<DbConnection> CreateCatalogConnection(string? serverAlias)
    {
        var resolved = _targets.ResolveServerAlias(serverAlias);
        return resolved.IsFailure ? resolved.Error : CreateNamedConnection(resolved.Value, databaseName: null);
    }

    private DbConnection CreateNamedConnection(string serverAlias, string? databaseName)
    {
        if (!_targets.TryGetServerOptions(serverAlias, out var server) || server is null)
        {
            throw new InvalidOperationException($"Server alias '{serverAlias}' is not configured in SqlToAi:Servers.");
        }

        string settingsPath = $"SqlToAi:Servers:{serverAlias}";
        if (string.IsNullOrWhiteSpace(server.Server))
        {
            throw new InvalidOperationException($"Configure '{settingsPath}:Server'.");
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server.Server,
            ApplicationName = "SqlToAi",
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = server.TrustServerCertificate,
            ConnectTimeout = server.ConnectTimeoutSeconds,
            IntegratedSecurity = server.IntegratedSecurity
        };
        if (!server.IntegratedSecurity)
        {
            if (string.IsNullOrWhiteSpace(server.UserId) || string.IsNullOrWhiteSpace(server.Password))
            {
                throw new InvalidOperationException($"Configure '{settingsPath}:UserId' and '{settingsPath}:Password' when IntegratedSecurity is false.");
            }
            builder.UserID = server.UserId;
            builder.Password = server.Password;
        }

        if (databaseName is not null)
        {
            builder.InitialCatalog = databaseName;
        }

        return new SqlConnection(builder.ConnectionString);
    }
}
