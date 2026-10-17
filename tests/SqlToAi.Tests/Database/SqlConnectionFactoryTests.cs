using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;

namespace SqlToAi.Tests.Database;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlConnectionFactoryCollectionFixture { public const string Name = "SqlConnectionFactory"; }

[Collection("SqlConnectionFactory")]
public sealed class SqlConnectionFactoryTests
{
    private static readonly Type TargetType = typeof(SqlConnectionFactory);

    [Fact]
    public void CreateTargetConnection_ShouldThrow_WhenServerIsMissing()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Servers["primary"] = new NamedServerOptions { Server = "" };
        var factory = new SqlConnectionFactory(Options.Create(options));
        var target = new DatabaseTargetContext("primary", "MyDb");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => factory.CreateTargetConnection(target));
    }

    [Fact]
    public void CreateTargetConnection_ShouldThrow_WhenServerAliasNotFound()
    {
        var options = new SqlToAiOptions();
        options.Servers["primary"] = new NamedServerOptions { Server = "localhost" };
        var factory = new SqlConnectionFactory(Options.Create(options));
        var target = new DatabaseTargetContext("unknown", "MyDb");

        Assert.Throws<InvalidOperationException>(() => factory.CreateTargetConnection(target));
    }

    [Fact]
    public void CreateTargetConnection_ShouldUseOptions()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Servers["primary"] = new NamedServerOptions
        {
            Server = "localhost\\MSSQLSERVER",
            UserId = "test-user",
            Password = "secret",
            IntegratedSecurity = false
        };
        var factory = new SqlConnectionFactory(Options.Create(options));
        var target = new DatabaseTargetContext("primary", "MyDb");

        // Act
        using var connection = factory.CreateTargetConnection(target);

        // Assert
        Assert.Contains("Data Source=localhost\\MSSQLSERVER", connection.ConnectionString);
        Assert.Contains("Initial Catalog=MyDb", connection.ConnectionString);
        Assert.Contains("User ID=test-user", connection.ConnectionString);
        Assert.Contains("Password=secret", connection.ConnectionString);
        Assert.DoesNotContain("Application Intent=ReadOnly", connection.ConnectionString);
        var builder = new SqlConnectionStringBuilder(connection.ConnectionString);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, builder.Encrypt);
        Assert.False(builder.TrustServerCertificate);
    }

    [Fact]
    public void CreateTargetConnection_ShouldThrow_WhenIntegratedSecurityIsFalseAndCredentialsMissing()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Servers["primary"] = new NamedServerOptions
        {
            Server = "localhost",
            IntegratedSecurity = false,
            UserId = "",
            Password = ""
        };
        var factory = new SqlConnectionFactory(Options.Create(options));
        var target = new DatabaseTargetContext("primary", "MyDb");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => factory.CreateTargetConnection(target));
    }

    [Fact]
    public void CreateTargetConnection_ShouldUseIntegratedSecurity_WhenEnabled()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Servers["primary"] = new NamedServerOptions
        {
            Server = "localhost",
            IntegratedSecurity = true
        };
        var factory = new SqlConnectionFactory(Options.Create(options));
        var target = new DatabaseTargetContext("primary", "MyDb");

        // Act
        using var connection = factory.CreateTargetConnection(target);

        // Assert
        Assert.Contains("Integrated Security=True", connection.ConnectionString);
        Assert.Contains("Initial Catalog=MyDb", connection.ConnectionString);
        var builder = new SqlConnectionStringBuilder(connection.ConnectionString);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, builder.Encrypt);
        Assert.False(builder.TrustServerCertificate);
    }

    [Fact]
    public void CreateTargetConnection_ShouldAllowExplicitTrustException_WithoutDisablingEncryption()
    {
        var options = new SqlToAiOptions();
        options.Servers["primary"] = new NamedServerOptions
        {
            Server = "localhost",
            IntegratedSecurity = true,
            TrustServerCertificate = true
        };
        var factory = new SqlConnectionFactory(Options.Create(options));
        var target = new DatabaseTargetContext("primary", "MyDb");

        using var connection = factory.CreateTargetConnection(target);

        var builder = new SqlConnectionStringBuilder(connection.ConnectionString);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, builder.Encrypt);
        Assert.True(builder.TrustServerCertificate);
    }

    [Fact]
    public void CreateTargetConnection_ShouldUseConfiguredConnectTimeout()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Servers["primary"] = new NamedServerOptions
        {
            Server = "localhost",
            IntegratedSecurity = true,
            ConnectTimeoutSeconds = 7
        };
        var factory = new SqlConnectionFactory(Options.Create(options));
        var target = new DatabaseTargetContext("primary", "MyDb");

        // Act
        using var connection = factory.CreateTargetConnection(target);

        // Assert
        Assert.Contains("Connect Timeout=7", connection.ConnectionString);
    }

    [Fact]
    public void CreateCatalogConnection_WithServerAlias_OmitInitialCatalog()
    {
        var options = new SqlToAiOptions();
        options.Servers["primary"] = new NamedServerOptions
        {
            Server = "localhost",
            IntegratedSecurity = true
        };
        var factory = new SqlConnectionFactory(Options.Create(options));

        var result = factory.CreateCatalogConnection("primary");

        Assert.True(result.IsSuccess);
        using var connection = result.Value;
        var builder = new SqlConnectionStringBuilder(connection.ConnectionString);
        Assert.Empty(builder.InitialCatalog);
        Assert.Equal("localhost", builder.DataSource);
    }
}
