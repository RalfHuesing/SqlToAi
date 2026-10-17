using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Metadata;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Milestone 3.3 acceptance tests: verifies server discovery (sql_list_servers),
/// explicit server-qualified database listing (sql_list_databases), and partial matching (sql_search_databases).
/// Verifies zero database calls for server discovery, strict pre-check validation, and cross-server catalog isolation.
/// </summary>
public sealed class ServerDiscoveryTargetIsolationTests
{
    private sealed class DiscoveryTrackingConnectionFactory : IDatabaseConnectionFactory
    {
        public List<string?> OpenedCatalogServers { get; } = [];
        public int CatalogConnectionCount { get; private set; }
        public bool FailCatalogQuery { get; set; }

        public DbConnection CreateTargetConnection(DatabaseTargetContext target) => throw new NotSupportedException();

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Returned fake connection is owned by caller.")]
        public Result<DbConnection> CreateCatalogConnection(string? serverAlias)
        {
            CatalogConnectionCount++;
            OpenedCatalogServers.Add(serverAlias);
            return Result<DbConnection>.Success(new FakeDbConnection(conn => new FakeDbCommand(conn, new FakeDbCommandHandlers(
                ExecuteReader: cmd =>
                {
                    if (FailCatalogQuery)
                    {
                        throw new InvalidOperationException("Simulated catalog query failure");
                    }

                    if (cmd.CommandText.Contains("sys.databases", StringComparison.OrdinalIgnoreCase))
                    {
                        return new FakeDbDataReader(
                            ["name"],
                            [["CustomersDb"], ["OrdersDb"], ["InventoryDb"], ["FinanceDb"]]);
                    }

                    return new FakeDbDataReader(["val"], []);
                }))));
        }

        public DbConnection CreateConnection(string? databaseName = null) => throw new NotSupportedException();
    }

    private static (SchemaService Service, DiscoveryTrackingConnectionFactory Factory) CreateService(
        Action<SqlToAiOptions>? configure = null)
    {
        var options = new SqlToAiOptions();
        options.Servers["alpha"] = new NamedServerOptions { Server = "srv-alpha", TrustServerCertificate = true };
        options.Servers["beta"] = new NamedServerOptions { Server = "srv-beta", TrustServerCertificate = true };
        options.Databases.ReadWrite = ["alpha/CustomersDb", "alpha/OrdersDb", "beta/FinanceDb"];

        configure?.Invoke(options);

        var factory = new DiscoveryTrackingConnectionFactory();
        var opts = Options.Create(options);
        var security = new SecurityGuard(opts);
        var access = new AccessLevelProvider(factory, opts, NullLogger<AccessLevelProvider>.Instance);
        var metadata = new MetadataProvider(factory, opts, NullLogger<MetadataProvider>.Instance);
        var policy = new AlwaysAllowPolicyResolver();
        var service = new SchemaService(factory, security, access, metadata, policy, opts, NullLogger<SchemaService>.Instance);
        return (service, factory);
    }

    [Fact]
    public void ListServers_ReturnsCanonicalConfiguredAliases_WithoutDatabaseConnection()
    {
        var (service, factory) = CreateService();

        var serversResult = service.ListServers();

        Assert.True(serversResult.IsSuccess);
        var servers = serversResult.Value;
        Assert.Equal(2, servers.Count);
        Assert.Contains("alpha", servers);
        Assert.Contains("beta", servers);
        Assert.Equal(0, factory.CatalogConnectionCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ListDatabasesAsync_EmptyServer_FailsBeforeConnection(string emptyServer)
    {
        var (service, factory) = CreateService();

        var result = await service.ListDatabasesAsync(emptyServer, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Equal(0, factory.CatalogConnectionCount);
    }

    [Fact]
    public async Task ListDatabasesAsync_UnknownServer_FailsBeforeConnection()
    {
        var (service, factory) = CreateService();

        var result = await service.ListDatabasesAsync("unknown_srv", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Contains("unknown_srv", result.Error.Message);
        Assert.Equal(0, factory.CatalogConnectionCount);
    }

    [Fact]
    public async Task ListDatabasesAsync_RoutesOnlyToRequestedServer_AndAppliesTargetGrants()
    {
        var (service, factory) = CreateService();

        var resultAlpha = await service.ListDatabasesAsync("alpha", TestContext.Current.CancellationToken);
        Assert.True(resultAlpha.IsSuccess);
        Assert.Equal(2, resultAlpha.Value.Count);
        Assert.Contains("CustomersDb", resultAlpha.Value);
        Assert.Contains("OrdersDb", resultAlpha.Value);
        Assert.DoesNotContain("FinanceDb", resultAlpha.Value);

        var resultBeta = await service.ListDatabasesAsync("beta", TestContext.Current.CancellationToken);
        Assert.True(resultBeta.IsSuccess);
        Assert.Single(resultBeta.Value);
        Assert.Contains("FinanceDb", resultBeta.Value);
        Assert.DoesNotContain("CustomersDb", resultBeta.Value);
        Assert.DoesNotContain("OrdersDb", resultBeta.Value);

        Assert.Equal(2, factory.CatalogConnectionCount);
        Assert.Equal(["alpha", "beta"], factory.OpenedCatalogServers);
    }

    [Fact]
    public async Task ListDatabasesAsync_CatalogQueryFailure_FallsBackToStaticAllowedDatabases_ForRequestedServerOnly()
    {
        var (service, factory) = CreateService();
        factory.FailCatalogQuery = true;

        var resultAlpha = await service.ListDatabasesAsync("alpha", TestContext.Current.CancellationToken);
        Assert.True(resultAlpha.IsSuccess);
        Assert.Equal(2, resultAlpha.Value.Count);
        Assert.Contains("CustomersDb", resultAlpha.Value);
        Assert.Contains("OrdersDb", resultAlpha.Value);
        Assert.DoesNotContain("FinanceDb", resultAlpha.Value);

        var resultBeta = await service.ListDatabasesAsync("beta", TestContext.Current.CancellationToken);
        Assert.True(resultBeta.IsSuccess);
        Assert.Single(resultBeta.Value);
        Assert.Contains("FinanceDb", resultBeta.Value);
        Assert.DoesNotContain("CustomersDb", resultBeta.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchDatabasesAsync_EmptySearchTerm_FailsBeforeConnection(string emptyTerm)
    {
        var (service, factory) = CreateService();

        var result = await service.SearchDatabasesAsync("alpha", emptyTerm, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Equal(0, factory.CatalogConnectionCount);
    }

    [Fact]
    public async Task SearchDatabasesAsync_CaseInsensitiveSubstringMatch_OnTargetServer()
    {
        var (service, factory) = CreateService();

        var matchOrders = await service.SearchDatabasesAsync("alpha", "ord", TestContext.Current.CancellationToken);
        Assert.True(matchOrders.IsSuccess);
        Assert.Single(matchOrders.Value);
        Assert.Equal("OrdersDb", matchOrders.Value[0]);

        var matchAll = await service.SearchDatabasesAsync("alpha", "db", TestContext.Current.CancellationToken);
        Assert.True(matchAll.IsSuccess);
        Assert.Equal(2, matchAll.Value.Count);
        Assert.Contains("CustomersDb", matchAll.Value);
        Assert.Contains("OrdersDb", matchAll.Value);

        var matchNone = await service.SearchDatabasesAsync("alpha", "Finance", TestContext.Current.CancellationToken);
        Assert.True(matchNone.IsSuccess);
        Assert.Empty(matchNone.Value);
    }
}
