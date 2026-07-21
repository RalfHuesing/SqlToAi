#nullable enable

using System.Data.Common;
using Microsoft.Data.SqlClient;
using SqlToAi.Database;

namespace SqlToAi.Tests.Database;

// @covers SqlToAi.Database.SecondaryConnectionBuilder
// @covers SqlToAi.Database.SecondaryConnectionSettings
public sealed class SecondaryConnectionBuilderTests
{
    private static readonly Type TargetType = typeof(SecondaryConnectionBuilder);

    [Fact]
    public void Create_ShouldUseFallbackFactory_WhenServerIsNotConfigured()
    {
        var settings = new SecondaryConnectionSettings(null, null, null, null, false, 30);
        var fallback = new FakeConnectionFactory();

        var connection = SecondaryConnectionBuilder.Create(settings, "SqlToAi-Test", "FallbackDb", fallback);

        Assert.Same(fallback.ReturnedConnection, connection);
        Assert.Equal("FallbackDb", fallback.LastDatabaseName);
    }

    [Fact]
    public void Create_ShouldPreferConfiguredDatabase_OverFallbackDatabaseName()
    {
        var settings = new SecondaryConnectionSettings(null, "ConfiguredDb", null, null, false, 30);
        var fallback = new FakeConnectionFactory();

        SecondaryConnectionBuilder.Create(settings, "SqlToAi-Test", "FallbackDb", fallback);

        Assert.Equal("ConfiguredDb", fallback.LastDatabaseName);
    }

    [Fact]
    public void Create_ShouldBuildDedicatedConnection_WhenServerIsConfigured()
    {
        var settings = new SecondaryConnectionSettings("my-server", "MyDb", "user", "pass", false, 15);
        var fallback = new FakeConnectionFactory();

        using DbConnection connection = SecondaryConnectionBuilder.Create(settings, "SqlToAi-Test", "FallbackDb", fallback);

        var sqlConnection = Assert.IsType<SqlConnection>(connection);
        var builder = new SqlConnectionStringBuilder(sqlConnection.ConnectionString);
        Assert.Equal("my-server", builder.DataSource);
        Assert.Equal("MyDb", builder.InitialCatalog);
        Assert.Equal("user", builder.UserID);
        Assert.Equal(0, fallback.ConnectionCreatedCount);
    }

    [Fact]
    public void Create_ShouldThrow_WhenServerConfiguredButNoDatabaseAndNoFallback()
    {
        var settings = new SecondaryConnectionSettings("my-server", null, "user", "pass", false, 15);
        var fallback = new FakeConnectionFactory();

        Assert.Throws<InvalidOperationException>(() =>
            SecondaryConnectionBuilder.Create(settings, "SqlToAi-Test", "", fallback));
    }

    [Fact]
    public void Create_ShouldThrow_WhenCredentialsMissing_AndIntegratedSecurityIsFalse()
    {
        var settings = new SecondaryConnectionSettings("my-server", "MyDb", null, null, false, 15);
        var fallback = new FakeConnectionFactory();

        Assert.Throws<InvalidOperationException>(() =>
            SecondaryConnectionBuilder.Create(settings, "SqlToAi-Test", "FallbackDb", fallback));
    }

    [Fact]
    public void Create_ShouldUseIntegratedSecurity_WhenConfigured()
    {
        var settings = new SecondaryConnectionSettings("my-server", "MyDb", null, null, true, 15);
        var fallback = new FakeConnectionFactory();

        using DbConnection connection = SecondaryConnectionBuilder.Create(settings, "SqlToAi-Test", "FallbackDb", fallback);

        var sqlConnection = Assert.IsType<SqlConnection>(connection);
        var builder = new SqlConnectionStringBuilder(sqlConnection.ConnectionString);
        Assert.True(builder.IntegratedSecurity);
    }

    private sealed class FakeConnectionFactory : IDatabaseConnectionFactory
    {
        public int ConnectionCreatedCount { get; private set; }
        public string? LastDatabaseName { get; private set; }
        public DbConnection ReturnedConnection { get; } = new SqlConnection();

        public DbConnection CreateConnection(string? databaseName = null)
        {
            ConnectionCreatedCount++;
            LastDatabaseName = databaseName;
            return ReturnedConnection;
        }
    }
}
