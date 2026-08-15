#nullable enable

using System.Data;
using System.Data.Common;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace SqlToAi.Tests.Metadata;

#pragma warning disable CS8765 // Nullability of parameter doesn't match overridden member

// @covers SqlToAi.Metadata.MetadataProvider
public sealed class MetadataProviderTests
{
    private static readonly Type TargetType = typeof(MetadataProvider);
    public static string? LastTableNameParameter { get; set; }

    [Fact]
    public async Task GetTableDescriptionAsync_ShouldReturnNull_WhenDisabled()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.MetadataProvider.Enabled = false;

        var mockFactory = new DummyConnectionFactory();
        var provider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        // Act
        var desc = await provider.GetTableDescriptionAsync(TestConstants.DatabaseName, "dbo.Customers", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(desc);
        Assert.Equal(0, mockFactory.ConnectionCreatedCount);
    }

    [Fact]
    public async Task GetTableDescriptionAsync_ShouldReturnDescription_WhenEnabled()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.MetadataProvider.Enabled = true;

        var mockConn = MockMetadataConnection.Create(tableDesc: "Test Table Description");
        var mockFactory = new DummyConnectionFactory(mockConn);
        var provider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        // Act
        var desc = await provider.GetTableDescriptionAsync(TestConstants.DatabaseName, "dbo.Customers", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Test Table Description", desc);
        Assert.Equal(1, mockFactory.ConnectionCreatedCount);
    }

    [Fact]
    public async Task GetColumnDescriptionsAsync_ShouldReturnDescriptions_WhenEnabled()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.MetadataProvider.Enabled = true;

        var columnsData = new Dictionary<string, string>
        {
            { "Id", "Primary Key" },
            { "Name", "Customer Name" }
        };
        var mockConn = MockMetadataConnection.Create(columnDescs: columnsData);
        var mockFactory = new DummyConnectionFactory(mockConn);
        var provider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        // Act
        var result = await provider.GetColumnDescriptionsAsync(TestConstants.DatabaseName, "dbo.Customers", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("Primary Key", result["Id"]);
        Assert.Equal("Customer Name", result["Name"]);
    }

    [Fact]
    public void CreateConnection_ShouldUseConfiguredDatabase_WhenDatabaseIsSpecified()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.MetadataProvider.Enabled = true;
        options.MetadataProvider.Database = "CustomMetadataDb";

        var mockFactory = new DummyConnectionFactory();
        var provider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        // Act
        var method = typeof(MetadataProvider).GetMethod("CreateConnection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        using var connection = (DbConnection)method.Invoke(provider, new object[] { "TargetDb" })!;

        // Assert
        Assert.NotNull(connection);
        Assert.Equal(1, mockFactory.ConnectionCreatedCount);
        Assert.Equal("CustomMetadataDb", mockFactory.LastDatabaseName);
    }

    [Fact]
    public async Task GetTableDescriptionAsync_ShouldStripSchemaPrefix_WhenCustomQueryIsUsed()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.MetadataProvider.Enabled = true;
        options.MetadataProvider.TableMetadataQuery = "SELECT Description FROM TableMetadata WHERE TableName = @TableName";

        var mockConn = MockMetadataConnection.Create(tableDesc: "Metadata description");
        var mockFactory = new DummyConnectionFactory(mockConn);
        var provider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        LastTableNameParameter = null;

        // Act
        var desc = await provider.GetTableDescriptionAsync(TestConstants.DatabaseName, "dbo.Customers", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Metadata description", desc);
        Assert.Equal("Customers", LastTableNameParameter);
    }

    [Fact]
    public async Task GetColumnDescriptionsAsync_ShouldStripSchemaPrefix_WhenCustomQueryIsUsed()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.MetadataProvider.Enabled = true;
        options.MetadataProvider.ColumnMetadataQuery = "SELECT ColumnName, Description FROM ColumnMetadata WHERE TableName = @TableName";

        var mockConn = MockMetadataConnection.Create(columnDescs: new Dictionary<string, string> { { "Col1", "Desc1" } });
        var mockFactory = new DummyConnectionFactory(mockConn);
        var provider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        LastTableNameParameter = null;

        // Act
        var result = await provider.GetColumnDescriptionsAsync(TestConstants.DatabaseName, "dbo.Customers", TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
        Assert.Equal("Desc1", result["Col1"]);
        Assert.Equal("Customers", LastTableNameParameter);
    }

    [Fact]
    public void CreateConnection_ShouldUseIndividualCredentials_WhenServerIsSpecified()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.MetadataProvider.Enabled = true;
        options.MetadataProvider.Server = "custom-metadata-server";
        options.MetadataProvider.UserId = "meta-user";
        options.MetadataProvider.Password = "meta-pass";
        options.MetadataProvider.CommandTimeoutSeconds = 45;

        var mockFactory = new DummyConnectionFactory();
        var provider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        // Act
        var method = typeof(MetadataProvider).GetMethod("CreateConnection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        using var connection = (DbConnection)method.Invoke(provider, new object[] { "TargetDb" })!;

        // Assert
        Assert.NotNull(connection);
        Assert.Contains("Data Source=custom-metadata-server", connection.ConnectionString);
        Assert.Contains("Initial Catalog=TargetDb", connection.ConnectionString);
        Assert.Contains("User ID=meta-user", connection.ConnectionString);
        Assert.Contains("Password=meta-pass", connection.ConnectionString);
        Assert.Contains("Connect Timeout=45", connection.ConnectionString);
    }

    [Fact]
    public void CreateConnection_ShouldUseIntegratedSecurity_WhenEnabledForMetadata()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.MetadataProvider.Enabled = true;
        options.MetadataProvider.Server = "custom-metadata-server";
        options.MetadataProvider.IntegratedSecurity = true;

        var mockFactory = new DummyConnectionFactory();
        var provider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        // Act
        var method = typeof(MetadataProvider).GetMethod("CreateConnection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        using var connection = (DbConnection)method.Invoke(provider, new object[] { "TargetDb" })!;

        // Assert
        Assert.NotNull(connection);
        Assert.Contains("Data Source=custom-metadata-server", connection.ConnectionString);
        Assert.Contains("Initial Catalog=TargetDb", connection.ConnectionString);
        Assert.Contains("Integrated Security=True", connection.ConnectionString);
    }

    [Fact]
    public void CreateConnection_ShouldThrow_WhenMetadataServerSetButIntegratedSecurityFalseAndCredentialsMissing()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.MetadataProvider.Enabled = true;
        options.MetadataProvider.Server = "custom-metadata-server";
        options.MetadataProvider.IntegratedSecurity = false;
        options.MetadataProvider.UserId = "";
        options.MetadataProvider.Password = "";

        var mockFactory = new DummyConnectionFactory();
        var provider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        // Act
        var method = typeof(MetadataProvider).GetMethod("CreateConnection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        // Assert
        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(provider, new object[] { "TargetDb" }));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

}

