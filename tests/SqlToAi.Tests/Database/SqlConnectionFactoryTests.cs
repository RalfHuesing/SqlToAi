using SqlToAi.Configuration;
using SqlToAi.Database;
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
    public void CreateConnection_ShouldThrow_WhenServerIsMissing()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.SqlServer.Server = "";
        var factory = new SqlConnectionFactory(Options.Create(options));

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => factory.CreateConnection());
    }

    [Fact]
    public void CreateConnection_ShouldUseOptions()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.SqlServer.Server = "localhost\\MSSQLSERVER";
        options.SqlServer.UserId = "test-user";
        options.SqlServer.Password = "secret";
        options.SqlServer.IntegratedSecurity = false;
        var factory = new SqlConnectionFactory(Options.Create(options));

        // Act
        using var connection = factory.CreateConnection("MyDb");

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
    public void CreateConnection_ShouldThrow_WhenIntegratedSecurityIsFalseAndCredentialsMissing()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.SqlServer.Server = "localhost";
        options.SqlServer.IntegratedSecurity = false;
        options.SqlServer.UserId = "";
        options.SqlServer.Password = "";
        var factory = new SqlConnectionFactory(Options.Create(options));

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => factory.CreateConnection("MyDb"));
    }

    [Fact]
    public void CreateConnection_ShouldUseIntegratedSecurity_WhenEnabled()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.SqlServer.Server = "localhost";
        options.SqlServer.IntegratedSecurity = true;
        var factory = new SqlConnectionFactory(Options.Create(options));

        // Act
        using var connection = factory.CreateConnection("MyDb");

        // Assert
        Assert.Contains("Integrated Security=True", connection.ConnectionString);
        Assert.Contains("Initial Catalog=MyDb", connection.ConnectionString);
        var builder = new SqlConnectionStringBuilder(connection.ConnectionString);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, builder.Encrypt);
        Assert.False(builder.TrustServerCertificate);
    }

    [Fact]
    public void CreateConnection_ShouldAllowExplicitTrustException_WithoutDisablingEncryption()
    {
        var options = new SqlToAiOptions();
        options.SqlServer.Server = "localhost";
        options.SqlServer.IntegratedSecurity = true;
        options.SqlServer.TrustServerCertificate = true;
        var factory = new SqlConnectionFactory(Options.Create(options));

        using var connection = factory.CreateConnection("MyDb");

        var builder = new SqlConnectionStringBuilder(connection.ConnectionString);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, builder.Encrypt);
        Assert.True(builder.TrustServerCertificate);
    }

    [Fact]
    public void CreateConnection_ShouldUseConfiguredConnectTimeout()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.SqlServer.Server = "localhost";
        options.SqlServer.IntegratedSecurity = true;
        options.SqlServer.ConnectTimeoutSeconds = 7;
        var factory = new SqlConnectionFactory(Options.Create(options));

        // Act
        using var connection = factory.CreateConnection("MyDb");

        // Assert
        Assert.Contains("Connect Timeout=7", connection.ConnectionString);
    }
}
