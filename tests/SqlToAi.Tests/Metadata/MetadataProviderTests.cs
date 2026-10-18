using System.Data;
using System.Data.Common;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SqlToAi.Tests.Database;
using SqlToAi.Tests.TestSupport;
using System.Net;
using System.Net.Sockets;

namespace SqlToAi.Tests.Metadata;

#pragma warning disable CS8765 // Nullability of parameter doesn't match overridden member

public sealed class MetadataProviderTests
{
    private static readonly Type TargetType = typeof(MetadataProvider);
    private const string ServerAlias = "primary";
    private static readonly DatabaseTargetContext Target = new(ServerAlias, "TargetDb");
    private static readonly DatabaseTargetContext TestDbTarget = new(ServerAlias, TestConstants.DatabaseName);
    public static string? LastTableNameParameter { get; set; }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task Lookups_ApplyProviderCommandTimeoutToBothCommands(int timeout)
    {
        var commands = new List<FakeDbCommand>();
        using var connection = new FakeDbConnection(conn => new FakeDbCommand(conn, new FakeDbCommandHandlers(
            ExecuteReader: command =>
            {
                commands.Add(command);
                return command.CommandText.Contains("sys.columns", StringComparison.Ordinal)
                    ? new FakeDbDataReader(["ColumnName", "Description"], [["Value", "column description"]])
                    : new FakeDbDataReader(["Description"], [["table description"]]);
            })));
        var options = new SqlToAiOptions().WithNamedServers(ServerAlias);
        options.Servers[ServerAlias].MetadataProvider.Enabled = true;
        options.Servers[ServerAlias].MetadataProvider.CommandTimeoutSeconds = timeout;
        var provider = new MetadataProvider(new DummyConnectionFactory(connection), Options.Create(options), NullLogger<MetadataProvider>.Instance);

        Assert.Equal("table description", await provider.GetTableDescriptionAsync(Target, "dbo.Table", TestContext.Current.CancellationToken));
        Assert.Equal("column description", (await provider.GetColumnDescriptionsAsync(Target, "dbo.Table", TestContext.Current.CancellationToken))["Value"]);
        Assert.Equal(2, commands.Count);
        Assert.All(commands, command => Assert.Equal(timeout, command.CommandTimeout));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ColumnLookup_PreservesCustomAliasesNullValuesAndLastRowWins(bool omitDescriptionAlias)
    {
        using var connection = new FakeDbConnection(conn => new FakeDbCommand(conn, new FakeDbCommandHandlers(
            ExecuteReader: _ => new FakeDbDataReader(
                [omitDescriptionAlias ? "Other" : "dEsCrIpTiOn", "COLUMNNAME"],
                [["first", "Value"], [DBNull.Value, "Value"], [42, "Number"], ["", "Empty"],
                    ["ignored", DBNull.Value], ["ignored", " "], ["last", "VALUE"]]))));
        var options = new SqlToAiOptions().WithNamedServers(ServerAlias);
        options.Servers[ServerAlias].MetadataProvider.Enabled = true;
        options.Servers[ServerAlias].MetadataProvider.ColumnMetadataQuery = "SELECT custom aliases";
        var provider = new MetadataProvider(new DummyConnectionFactory(connection), Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var descriptions = await provider.GetColumnDescriptionsAsync(Target, "dbo.Table", TestContext.Current.CancellationToken);
        if (omitDescriptionAlias)
        {
            Assert.Empty(descriptions);
        }
        else
        {
            Assert.Equal(3, descriptions.Count);
            Assert.Equal("last", descriptions["value"]);
            Assert.Equal("42", descriptions["Number"]);
            Assert.Equal("", descriptions["Empty"]);
        }
    }

    [Theory]
    [InlineData("table")]
    [InlineData("columns")]
    public async Task Lookup_TlsOpenFailure_PreservesMissingResultAndLogsOnlySafeDiagnostic(string lookup)
    {
        const string marker = "synthetic-secret-marker";
        var failure = SqlExceptionFactory.Create(unchecked((int)0x80090325),
            $"Untrusted certificate Password={marker};Server=raw-connection-marker;", severity: 20,
            inner: new InvalidOperationException($"inner {marker};Server=raw-connection-marker;"));
        var connection = new FakeDbConnection(_ => throw new NotSupportedException(), new FakeDbConnectionOptions(OpenFailure: failure));
        var factory = new DummyConnectionFactory(connection);
        var logger = new CapturingLogger<MetadataProvider>();
        var options = new SqlToAiOptions().WithNamedServers(ServerAlias);
        options.Servers[ServerAlias].MetadataProvider.Enabled = true;
        var provider = new MetadataProvider(factory, Options.Create(options), logger);

        if (lookup == "table")
        {
            Assert.Null(await provider.GetTableDescriptionAsync(Target, "dbo.Customers", TestContext.Current.CancellationToken));
        }
        else
        {
            Assert.Empty(await provider.GetColumnDescriptionsAsync(Target, "dbo.Customers", TestContext.Current.CancellationToken));
        }

        Assert.Equal(1, factory.ConnectionCreatedCount);
        Assert.Equal(1, connection.OpenCount);
        Assert.NotEmpty(logger.Entries);
        foreach (var (message, exception) in logger.Entries)
        {
            Assert.Contains("MetadataProvider", message);
            Assert.Contains("TLS/certificate connection failure", message);
            Assert.DoesNotContain(marker, message);
            Assert.DoesNotContain("raw-connection-marker", message);
            Assert.Null(exception);
        }
    }

    [Theory]
    [InlineData("table")]
    [InlineData("columns")]
    public async Task DedicatedLookup_OpenFailureDoesNotRetryPrimaryFactory(string lookup)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task accepted = Task.Run(async () =>
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);

        var factory = new DummyConnectionFactory();
        var logger = new CapturingLogger<MetadataProvider>();
        var options = new SqlToAiOptions().WithNamedServers(ServerAlias);
        var metadata = options.Servers[ServerAlias].MetadataProvider;
        metadata.Enabled = true;
        metadata.Server = $"127.0.0.1,{port}";
        metadata.Database = "MetadataDb";
        metadata.UserId = "metadata-user-marker";
        metadata.Password = "metadata-password-marker";
        metadata.IntegratedSecurity = false;
        metadata.TrustServerCertificate = false;
        metadata.CommandTimeoutSeconds = 2;
        var provider = new MetadataProvider(factory, Options.Create(options), logger);

        if (lookup == "table")
        {
            Assert.Null(await provider.GetTableDescriptionAsync(Target, "dbo.Customers", TestContext.Current.CancellationToken));
        }
        else
        {
            Assert.Empty(await provider.GetColumnDescriptionsAsync(Target, "dbo.Customers", TestContext.Current.CancellationToken));
        }
        await accepted.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(0, factory.ConnectionCreatedCount);
        Assert.NotEmpty(logger.Entries);
        foreach (var (message, exception) in logger.Entries)
        {
            Assert.Contains("MetadataProvider", message);
            Assert.Contains("connection", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("metadata-password-marker", message);
            Assert.Null(exception);
        }
    }

    [Fact]
    public async Task GetTableDescriptionAsync_ShouldReturnNull_WhenDisabled()
    {
        // Arrange
        var options = new SqlToAiOptions().WithNamedServers(ServerAlias);
        options.Servers[ServerAlias].MetadataProvider.Enabled = false;

        var mockFactory = new DummyConnectionFactory();
        var provider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        // Act
        var desc = await provider.GetTableDescriptionAsync(TestDbTarget, "dbo.Customers", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(desc);
        Assert.Equal(0, mockFactory.ConnectionCreatedCount);
    }

    [Fact]
    public async Task GetTableDescriptionAsync_ShouldReturnDescription_WhenEnabled()
    {
        // Arrange
        var options = new SqlToAiOptions().WithNamedServers(ServerAlias);
        options.Servers[ServerAlias].MetadataProvider.Enabled = true;

        var mockConn = MockMetadataConnection.Create(tableDesc: "Test Table Description");
        var mockFactory = new DummyConnectionFactory(mockConn);
        var provider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        // Act
        var desc = await provider.GetTableDescriptionAsync(TestDbTarget, "dbo.Customers", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Test Table Description", desc);
        Assert.Equal(1, mockFactory.ConnectionCreatedCount);
    }

    [Fact]
    public async Task GetColumnDescriptionsAsync_ShouldReturnDescriptions_WhenEnabled()
    {
        // Arrange
        var options = new SqlToAiOptions().WithNamedServers(ServerAlias);
        options.Servers[ServerAlias].MetadataProvider.Enabled = true;

        var columnsData = new Dictionary<string, string>
        {
            { "Id", "Primary Key" },
            { "Name", "Customer Name" }
        };
        var mockConn = MockMetadataConnection.Create(columnDescs: columnsData);
        var mockFactory = new DummyConnectionFactory(mockConn);
        var provider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        // Act
        var result = await provider.GetColumnDescriptionsAsync(TestDbTarget, "dbo.Customers", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("Primary Key", result["Id"]);
        Assert.Equal("Customer Name", result["Name"]);
    }

    [Fact]
    public void CreateConnection_ShouldUseConfiguredDatabase_WhenDatabaseIsSpecified()
    {
        // Arrange
        var options = new SqlToAiOptions().WithNamedServers(ServerAlias);
        options.Servers[ServerAlias].MetadataProvider.Enabled = true;
        options.Servers[ServerAlias].MetadataProvider.Database = "CustomMetadataDb";

        var mockFactory = new DummyConnectionFactory();

        // Act
        using var connection = SecondaryConnectionBuilder.CreateForOwner(
            Target, options.Servers[ServerAlias], options.Servers[ServerAlias].MetadataProvider, SecondaryConnectionBuilder.MetadataApplicationName, mockFactory);

        // Assert
        Assert.NotNull(connection);
        Assert.Equal(1, mockFactory.ConnectionCreatedCount);
        Assert.Equal("CustomMetadataDb", mockFactory.LastDatabaseName);
    }

    [Fact]
    public async Task GetTableDescriptionAsync_ShouldStripSchemaPrefix_WhenCustomQueryIsUsed()
    {
        // Arrange
        var options = new SqlToAiOptions().WithNamedServers(ServerAlias);
        options.Servers[ServerAlias].MetadataProvider.Enabled = true;
        options.Servers[ServerAlias].MetadataProvider.TableMetadataQuery = "SELECT Description FROM TableMetadata WHERE TableName = @TableName";

        var mockConn = MockMetadataConnection.Create(tableDesc: "Metadata description");
        var mockFactory = new DummyConnectionFactory(mockConn);
        var provider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        LastTableNameParameter = null;

        // Act
        var desc = await provider.GetTableDescriptionAsync(TestDbTarget, "dbo.Customers", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Metadata description", desc);
        Assert.Equal("Customers", LastTableNameParameter);
    }

    [Fact]
    public async Task GetColumnDescriptionsAsync_ShouldStripSchemaPrefix_WhenCustomQueryIsUsed()
    {
        // Arrange
        var options = new SqlToAiOptions().WithNamedServers(ServerAlias);
        options.Servers[ServerAlias].MetadataProvider.Enabled = true;
        options.Servers[ServerAlias].MetadataProvider.ColumnMetadataQuery = "SELECT ColumnName, Description FROM ColumnMetadata WHERE TableName = @TableName";

        var mockConn = MockMetadataConnection.Create(columnDescs: new Dictionary<string, string> { { "Col1", "Desc1" } });
        var mockFactory = new DummyConnectionFactory(mockConn);
        var provider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        LastTableNameParameter = null;

        // Act
        var result = await provider.GetColumnDescriptionsAsync(TestDbTarget, "dbo.Customers", TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
        Assert.Equal("Desc1", result["Col1"]);
        Assert.Equal("Customers", LastTableNameParameter);
    }

    [Fact]
    public void CreateConnection_ShouldUseIndividualCredentials_WhenServerIsSpecified()
    {
        // Arrange
        var options = new SqlToAiOptions().WithNamedServers(ServerAlias);
        var metadata = options.Servers[ServerAlias].MetadataProvider;
        metadata.Enabled = true;
        metadata.Server = "custom-metadata-server";
        metadata.Database = "CustomMetadataDb";
        metadata.UserId = "meta-user";
        metadata.Password = "meta-pass";
        metadata.CommandTimeoutSeconds = 45;

        var mockFactory = new DummyConnectionFactory();

        // Act
        using var connection = (SqlConnection)SecondaryConnectionBuilder.CreateForOwner(
            Target, options.Servers[ServerAlias], metadata, SecondaryConnectionBuilder.MetadataApplicationName, mockFactory);

        // Assert
        Assert.NotNull(connection);
        Assert.Contains("Data Source=custom-metadata-server", connection.ConnectionString);
        Assert.Contains("Initial Catalog=CustomMetadataDb", connection.ConnectionString);
        Assert.Contains("User ID=meta-user", connection.ConnectionString);
        Assert.Contains("Password=meta-pass", connection.ConnectionString);
        Assert.Contains("Connect Timeout=45", connection.ConnectionString);
    }

    [Fact]
    public void CreateConnection_ShouldUseIntegratedSecurity_WhenEnabledForMetadata()
    {
        // Arrange
        var options = new SqlToAiOptions().WithNamedServers(ServerAlias);
        var metadata = options.Servers[ServerAlias].MetadataProvider;
        metadata.Enabled = true;
        metadata.Server = "custom-metadata-server";
        metadata.Database = "CustomMetadataDb";
        metadata.IntegratedSecurity = true;

        var mockFactory = new DummyConnectionFactory();

        // Act
        using var connection = (SqlConnection)SecondaryConnectionBuilder.CreateForOwner(
            Target, options.Servers[ServerAlias], metadata, SecondaryConnectionBuilder.MetadataApplicationName, mockFactory);

        // Assert
        Assert.NotNull(connection);
        Assert.Contains("Data Source=custom-metadata-server", connection.ConnectionString);
        Assert.Contains("Initial Catalog=CustomMetadataDb", connection.ConnectionString);
        Assert.Contains("Integrated Security=True", connection.ConnectionString);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateConnection_ShouldUseBoundMetadataTrustSetting_IndependentlyOfPrimary(bool trustMetadataCertificate)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SqlToAi:Servers:primary:Server"] = "metadata-primary",
                ["SqlToAi:Servers:primary:TrustServerCertificate"] = (!trustMetadataCertificate).ToString(),
                ["SqlToAi:Servers:primary:MetadataProvider:Server"] = "metadata-server",
                ["SqlToAi:Servers:primary:MetadataProvider:Database"] = "MetadataDb",
                ["SqlToAi:Servers:primary:MetadataProvider:IntegratedSecurity"] = "true",
                ["SqlToAi:Servers:primary:MetadataProvider:TrustServerCertificate"] = trustMetadataCertificate.ToString()
            })
            .Build();
        var options = configuration.GetSection("SqlToAi").Get<SqlToAiOptions>()!;
        var server = options.Servers["primary"];
        var target = new DatabaseTargetContext("primary", "TargetDb");
        using var connection = (SqlConnection)SecondaryConnectionBuilder.CreateForOwner(
            target, server, server.MetadataProvider, SecondaryConnectionBuilder.MetadataApplicationName, new DummyConnectionFactory());

        var builder = new SqlConnectionStringBuilder(connection.ConnectionString);
        Assert.Equal("MetadataDb", builder.InitialCatalog);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, builder.Encrypt);
        Assert.Equal(trustMetadataCertificate, builder.TrustServerCertificate);
    }

    [Fact]
    public void CreateConnection_ShouldThrow_WhenMetadataServerSetButIntegratedSecurityFalseAndCredentialsMissing()
    {
        // Arrange
        var options = new SqlToAiOptions().WithNamedServers(ServerAlias);
        var metadata = options.Servers[ServerAlias].MetadataProvider;
        metadata.Enabled = true;
        metadata.Server = "custom-metadata-server";
        metadata.Database = "CustomMetadataDb";
        metadata.IntegratedSecurity = false;
        metadata.UserId = "";
        metadata.Password = "";

        var mockFactory = new DummyConnectionFactory();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => SecondaryConnectionBuilder.CreateForOwner(
            Target, options.Servers[ServerAlias], metadata, SecondaryConnectionBuilder.MetadataApplicationName, mockFactory));
    }

}

