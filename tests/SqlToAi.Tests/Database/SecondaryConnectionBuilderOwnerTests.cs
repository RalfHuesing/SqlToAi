using System.Data.Common;
using Microsoft.Data.SqlClient;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Verifies the context-aware provider source resolution: without a dedicated secondary server the
/// owner's primary endpoint is used (with the explicit provider database or the target's raw
/// database); a dedicated secondary server requires its own explicit raw database, uses its own
/// authentication/TLS settings and never touches the primary connection factory.
/// </summary>
public sealed class SecondaryConnectionBuilderOwnerTests
{
    private static NamedServerOptions BuildOwner(string endpoint = "primary-host") => new()
    {
        Server = endpoint,
        IntegratedSecurity = true,
        TrustServerCertificate = false,
        ConnectTimeoutSeconds = 30
    };

    private static DatabaseTargetContext Target(string databaseName, string alias = "test")
    {
        var options = new SqlToAiOptions();
        options.Servers[alias] = BuildOwner();
        return new DatabaseTargetResolver(options).Resolve($"{alias}/{databaseName}").Value;
    }

    private sealed class CountingFallbackFactory : IDatabaseConnectionFactory
    {
        public List<string> TargetDatabases { get; } = [];
        public List<string> TargetAliases { get; } = [];

        public DbConnection CreateTargetConnection(DatabaseTargetContext target)
        {
            TargetAliases.Add(target.ServerAlias);
            TargetDatabases.Add(target.DatabaseName);
            return MockConnection.Create();
        }

        public Result<DbConnection> CreateCatalogConnection(string? serverAlias) => throw new NotSupportedException();

        public DbConnection CreateConnection(string? databaseName = null) => throw new NotSupportedException();
    }

    private static class MockConnection
    {
        public static DbConnection Create() => new SqlConnection("Server=mock;Integrated Security=true;");
    }

    // -------------------------------------------------------------------------
    // ResolveSource
    // -------------------------------------------------------------------------

    [Fact]
    public void ResolveSource_WithoutSecondaryServer_UsesOwnerEndpointAndTargetDatabase()
    {
        var owner = BuildOwner("primary-host");
        var provider = new MetadataProviderOptions();

        var source = SecondaryConnectionBuilder.ResolveSource(owner, provider, "AppDb", SecondaryConnectionBuilder.MetadataApplicationName);

        Assert.False(source.IsDedicated);
        Assert.Equal("primary-host", source.Endpoint);
        Assert.Equal("AppDb", source.SourceDatabase);
    }

    [Fact]
    public void ResolveSource_WithExplicitProviderDatabase_UsesItOnOwnerEndpoint()
    {
        var owner = BuildOwner("primary-host");
        var provider = new MetadataProviderOptions { Database = "MetaDb" };

        var source = SecondaryConnectionBuilder.ResolveSource(owner, provider, "AppDb", SecondaryConnectionBuilder.MetadataApplicationName);

        Assert.False(source.IsDedicated);
        Assert.Equal("primary-host", source.Endpoint);
        Assert.Equal("MetaDb", source.SourceDatabase);
    }

    [Fact]
    public void ResolveSource_WithDedicatedServer_RequiresExplicitDatabase()
    {
        var owner = BuildOwner();
        var provider = new AnonymizationRulesOptions { Server = "rules-host" };

        var exception = Assert.Throws<InvalidOperationException>(() => SecondaryConnectionBuilder.ResolveSource(
            owner, provider, "AppDb", SecondaryConnectionBuilder.AnonymizationRulesApplicationName));

        Assert.Contains("requires an explicitly configured database", exception.Message);
    }

    [Fact]
    public void ResolveSource_WithDedicatedServerAndDatabase_UsesProviderSource()
    {
        var owner = BuildOwner();
        var provider = new AnonymizationRulesOptions { Server = "rules-host", Database = "RulesDb" };

        var source = SecondaryConnectionBuilder.ResolveSource(owner, provider, "AppDb", SecondaryConnectionBuilder.AnonymizationRulesApplicationName);

        Assert.True(source.IsDedicated);
        Assert.Equal("rules-host", source.Endpoint);
        Assert.Equal("RulesDb", source.SourceDatabase);
    }

    // -------------------------------------------------------------------------
    // CreateForOwner — fallback path
    // -------------------------------------------------------------------------

    [Fact]
    public void CreateForOwner_WithoutSecondaryServer_ContactsTargetDatabaseOnOwnerEndpoint()
    {
        var owner = BuildOwner();
        var provider = new MetadataProviderOptions();
        var fallback = new CountingFallbackFactory();

        using var connection = SecondaryConnectionBuilder.CreateForOwner(
            Target("AppDb"), owner, provider, SecondaryConnectionBuilder.MetadataApplicationName, fallback);

        Assert.Equal(["AppDb"], fallback.TargetDatabases);
        Assert.Equal(["test"], fallback.TargetAliases);
    }

    [Fact]
    public void CreateForOwner_WithExplicitProviderDatabase_ContactsProviderDatabaseOnOwnerEndpoint()
    {
        var owner = BuildOwner();
        var provider = new MetadataProviderOptions { Database = "MetaDb" };
        var fallback = new CountingFallbackFactory();

        using var connection = SecondaryConnectionBuilder.CreateForOwner(
            Target("AppDb"), owner, provider, SecondaryConnectionBuilder.MetadataApplicationName, fallback);

        Assert.Equal(["MetaDb"], fallback.TargetDatabases);
        Assert.Equal(["test"], fallback.TargetAliases);
    }

    // -------------------------------------------------------------------------
    // CreateForOwner — dedicated path
    // -------------------------------------------------------------------------

    [Fact]
    public void CreateForOwner_WithDedicatedServer_BuildsStandaloneConnectionAndNeverUsesPrimary()
    {
        var owner = BuildOwner();
        var provider = new AnonymizationRulesOptions
        {
            Server = "rules-host",
            Database = "RulesDb",
            UserId = "rules-user",
            Password = "rules-password",
            IntegratedSecurity = false,
            TrustServerCertificate = true,
            CommandTimeoutSeconds = 15
        };
        var fallback = new CountingFallbackFactory();

        using DbConnection connection = SecondaryConnectionBuilder.CreateForOwner(
            Target("AppDb"), owner, provider, SecondaryConnectionBuilder.AnonymizationRulesApplicationName, fallback);

        var sqlConnection = Assert.IsType<SqlConnection>(connection);
        var builder = new SqlConnectionStringBuilder(sqlConnection.ConnectionString);
        Assert.Equal("rules-host", builder.DataSource);
        Assert.Equal("RulesDb", builder.InitialCatalog);
        Assert.Equal("rules-user", builder.UserID);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, builder.Encrypt);
        Assert.True(builder.TrustServerCertificate);
        Assert.Equal(15, builder.ConnectTimeout);
        Assert.Empty(fallback.TargetDatabases);
    }

    [Fact]
    public void CreateForOwner_WithDedicatedServerAndIntegratedSecurity_UsesIntegratedSecurity()
    {
        var owner = BuildOwner();
        var provider = new AnonymizationRulesOptions
        {
            Server = "rules-host",
            Database = "RulesDb",
            IntegratedSecurity = true,
            TrustServerCertificate = false
        };
        var fallback = new CountingFallbackFactory();

        using DbConnection connection = SecondaryConnectionBuilder.CreateForOwner(
            Target("AppDb"), owner, provider, SecondaryConnectionBuilder.AnonymizationRulesApplicationName, fallback);

        var builder = new SqlConnectionStringBuilder(((SqlConnection)connection).ConnectionString);
        Assert.True(builder.IntegratedSecurity);
        Assert.Equal("RulesDb", builder.InitialCatalog);
        Assert.Empty(fallback.TargetDatabases);
    }

    [Fact]
    public void CreateForOwner_WithDedicatedServerMissingDatabase_ThrowsAndNeverUsesPrimary()
    {
        var owner = BuildOwner();
        var provider = new AnonymizationRulesOptions { Server = "rules-host" };
        var fallback = new CountingFallbackFactory();

        Assert.Throws<InvalidOperationException>(() => SecondaryConnectionBuilder.CreateForOwner(
            Target("AppDb"), owner, provider, SecondaryConnectionBuilder.AnonymizationRulesApplicationName, fallback));

        Assert.Empty(fallback.TargetDatabases);
    }

    [Theory]
    [InlineData("App/Db")]
    [InlineData("ÜnïcöDé/Db")]
    public void CreateForOwner_PreservesUnusualDatabaseNamesEndToEnd(string rawDatabaseName)
    {
        var owner = BuildOwner();
        var provider = new MetadataProviderOptions();
        var fallback = new CountingFallbackFactory();

        using var connection = SecondaryConnectionBuilder.CreateForOwner(
            Target(rawDatabaseName), owner, provider, SecondaryConnectionBuilder.MetadataApplicationName, fallback);

        Assert.Equal([rawDatabaseName], fallback.TargetDatabases);
    }
}
