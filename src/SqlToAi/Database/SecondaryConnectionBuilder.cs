using System.Data.Common;
using Microsoft.Data.SqlClient;
using SqlToAi.Configuration;
using SqlToAi.Domain;

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
    bool TrustServerCertificate,
    int CommandTimeoutSeconds);

/// <summary>
/// The effective source of one supporting-provider call: the physical endpoint and the source
/// database the provider will actually contact, including per-target fallback databases.
/// </summary>
internal sealed record ProviderSourceResolution(string Endpoint, string SourceDatabase, bool IsDedicated);

/// <summary>
/// Builds ADO.NET connections for cross-cutting secondary databases.
/// <see cref="CreateForOwner"/> resolves the effective source per policy owner
/// and target: without a dedicated secondary server, the owner's primary endpoint, authentication,
/// TLS and connect timeout are used; a dedicated secondary server always uses its own explicit raw
/// database, authentication and TLS settings and never falls back to the primary endpoint.
/// </summary>
internal static class SecondaryConnectionBuilder
{
    internal const string AnonymizationRulesApplicationName = "SqlToAi-AnonymizationRules";
    internal const string MetadataApplicationName = "SqlToAi-Metadata";



    /// <summary>
    /// Resolves the effective source for one owner-specific provider call without opening a
    /// connection. Without a dedicated secondary server the owner's primary endpoint is used with
    /// the explicit provider database or the target's raw database; a dedicated secondary server
    /// requires its own explicit raw database and never uses the target database implicitly.
    /// </summary>
    internal static ProviderSourceResolution ResolveSource(
        NamedServerOptions ownerServer,
        IProviderConnectionOptions provider,
        string targetDatabaseName,
        string applicationName)
    {
        // An explicit provider Database is a raw database name and is preserved exactly, including
        // significant whitespace (" " is a real raw name); only null or empty means it was omitted.
        // A supplied name is never silently rerouted to another source, and on a dedicated server
        // any non-empty explicit database satisfies the dedicated requirement (concept Must 8).
        bool hasExplicitDatabase = !string.IsNullOrEmpty(provider.Database);

        if (string.IsNullOrWhiteSpace(provider.Server))
        {
            string sourceDatabase = hasExplicitDatabase ? provider.Database! : targetDatabaseName;
            return new ProviderSourceResolution(ownerServer.Server, sourceDatabase, IsDedicated: false);
        }

        if (!hasExplicitDatabase)
        {
            throw new InvalidOperationException(
                $"{applicationName} SQL connection error: a dedicated secondary server requires an explicitly configured database.");
        }

        return new ProviderSourceResolution(provider.Server, provider.Database!, IsDedicated: true);
    }

    /// <summary>
    /// Creates the supporting-provider connection for one target context and its owning server.
    /// The fallback path routes through the connection factory so the connection uses the owner's
    /// primary endpoint, authentication, TLS and connect timeout with the effective source
    /// database. The dedicated path builds a standalone connection from the provider's own
    /// settings and never contacts the primary endpoint.
    /// </summary>
    public static DbConnection CreateForOwner(
        DatabaseTargetContext target,
        NamedServerOptions ownerServer,
        IProviderConnectionOptions provider,
        string applicationName,
        IDatabaseConnectionFactory fallbackConnectionFactory,
        Func<DbConnection>? dedicatedConnectionFactory = null)
    {
        ProviderSourceResolution source = ResolveSource(ownerServer, provider, target.DatabaseName, applicationName);

        if (!source.IsDedicated)
        {
            return string.Equals(source.SourceDatabase, target.DatabaseName, StringComparison.Ordinal)
                ? fallbackConnectionFactory.CreateTargetConnection(target)
                : fallbackConnectionFactory.CreateTargetConnection(new DatabaseTargetContext(target.ServerAlias, source.SourceDatabase));
        }

        // Offline-test seam: substitutes only the physical dedicated connection; the dedicated vs.
        // fallback decision still runs through ResolveSource. Production code passes null.
        if (dedicatedConnectionFactory is not null)
        {
            return dedicatedConnectionFactory();
        }

        var settings = new SecondaryConnectionSettings(
            provider.Server,
            source.SourceDatabase,
            provider.UserId,
            provider.Password,
            provider.IntegratedSecurity,
            provider.TrustServerCertificate,
            provider.CommandTimeoutSeconds);

        return BuildDedicatedConnection(settings, applicationName, source.SourceDatabase);
    }

    private static DbConnection BuildDedicatedConnection(
        SecondaryConnectionSettings settings,
        string applicationName,
        string targetDb)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = settings.Server,
            InitialCatalog = !string.IsNullOrEmpty(targetDb)
                ? targetDb
                : throw new InvalidOperationException($"{applicationName} SQL connection error: Database name must be explicitly specified."),
            ApplicationName = applicationName,
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = settings.TrustServerCertificate,
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
