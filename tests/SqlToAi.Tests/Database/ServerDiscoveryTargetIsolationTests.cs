using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
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
        public Exception? CatalogQueryFailure { get; set; }
        public Action? BeforeCatalogQueryFailure { get; set; }
        public int CatalogQueryCount { get; private set; }
        public FakeDbConnection? LastCatalogConnection { get; private set; }

        public DbConnection CreateTargetConnection(DatabaseTargetContext target) => throw new NotSupportedException();

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Returned fake connection is owned by caller.")]
        public Result<DbConnection> CreateCatalogConnection(string? serverAlias)
        {
            CatalogConnectionCount++;
            OpenedCatalogServers.Add(serverAlias);
            LastCatalogConnection = new FakeDbConnection(conn => new FakeDbCommand(conn, new FakeDbCommandHandlers(
                ExecuteReader: cmd =>
                {
                    CatalogQueryCount++;
                    if (CatalogQueryFailure is not null)
                    {
                        BeforeCatalogQueryFailure?.Invoke();
                        throw CatalogQueryFailure;
                    }
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
                })));
            return Result<DbConnection>.Success(LastCatalogConnection);
        }

        public DbConnection CreateConnection(string? databaseName = null) => throw new NotSupportedException();
    }

    private static (SchemaService Service, DiscoveryTrackingConnectionFactory Factory) CreateService(
        Action<SqlToAiOptions>? configure = null,
        CapturingLogger<SchemaService>? logger = null)
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
        var service = new SchemaService(factory, security, access, metadata, policy, opts,
            (ILogger<SchemaService>?)logger ?? NullLogger<SchemaService>.Instance);
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
        Assert.Contains("alpha/CustomersDb", resultAlpha.Value);
        Assert.Contains("alpha/OrdersDb", resultAlpha.Value);
        Assert.DoesNotContain("beta/FinanceDb", resultAlpha.Value);

        var resultBeta = await service.ListDatabasesAsync("beta", TestContext.Current.CancellationToken);
        Assert.True(resultBeta.IsSuccess);
        Assert.Single(resultBeta.Value);
        Assert.Contains("beta/FinanceDb", resultBeta.Value);
        Assert.DoesNotContain("alpha/CustomersDb", resultBeta.Value);
        Assert.DoesNotContain("alpha/OrdersDb", resultBeta.Value);

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
        Assert.Contains("alpha/CustomersDb", resultAlpha.Value);
        Assert.Contains("alpha/OrdersDb", resultAlpha.Value);
        Assert.DoesNotContain("beta/FinanceDb", resultAlpha.Value);

        var resultBeta = await service.ListDatabasesAsync("beta", TestContext.Current.CancellationToken);
        Assert.True(resultBeta.IsSuccess);
        Assert.Single(resultBeta.Value);
        Assert.Contains("beta/FinanceDb", resultBeta.Value);
        Assert.DoesNotContain("alpha/CustomersDb", resultBeta.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListDatabasesAsync_ReadWriteAnonymizedGrants_PreserveServerAndExclusionFilters(bool catalogFails)
    {
        var (service, factory) = CreateService(options =>
        {
            options.Databases.ReadWrite = [];
            options.Databases.ReadWriteAnonymized = ["alpha/CustomersDb", "alpha/OrdersDb", "beta/FinanceDb"];
            options.Servers["alpha"].ExcludedDatabases = ["Orders*"];
        });
        factory.FailCatalogQuery = catalogFails;

        var alpha = await service.ListDatabasesAsync("alpha", TestContext.Current.CancellationToken);
        var beta = await service.ListDatabasesAsync("beta", TestContext.Current.CancellationToken);

        Assert.True(alpha.IsSuccess);
        Assert.Equal("alpha/CustomersDb", Assert.Single(alpha.Value));
        Assert.True(beta.IsSuccess);
        Assert.Equal("beta/FinanceDb", Assert.Single(beta.Value));
        Assert.Equal(["alpha", "beta"], factory.OpenedCatalogServers);
    }

    [Theory]
    [InlineData("list", "sql-timeout", SqlToAiError.TimeoutCode)]
    [InlineData("search", "nested-timeout", SqlToAiError.TimeoutCode)]
    [InlineData("list", "tls-certificate", SqlToAiError.InfrastructureErrorCode)]
    public async Task CatalogQueryTimeoutOrTlsFailure_ReturnsSafeErrorAfterOpenWithoutFallback(
        string operation, string failureKind, string expectedCode)
    {
        const string marker = "synthetic-secret-marker";
        var logger = new CapturingLogger<SchemaService>();
        var (service, factory) = CreateService(logger: logger);
        factory.CatalogQueryFailure = failureKind switch
        {
            "sql-timeout" => SqlExceptionFactory.Create(-2, $"Timeout; SSL Provider Password={marker};Server=raw-marker;"),
            "nested-timeout" => new InvalidOperationException($"outer {marker}", new TimeoutException($"inner {marker}")),
            "tls-certificate" => SqlExceptionFactory.Create(unchecked((int)0x80090325), $"Certificate failure Password={marker};Server=raw-marker;"),
            _ => throw new ArgumentOutOfRangeException(nameof(failureKind))
        };

        var result = operation == "list"
            ? await service.ListDatabasesAsync("alpha", TestContext.Current.CancellationToken)
            : await service.SearchDatabasesAsync("alpha", "Customer", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.Contains("SqlToAi:Servers:alpha", result.Error.Message);
        Assert.DoesNotContain(marker, result.Error.Message);
        Assert.DoesNotContain("raw-marker", result.Error.Message);
        Assert.DoesNotContain("ConnectTimeout", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, factory.CatalogConnectionCount);
        Assert.Equal(1, factory.LastCatalogConnection!.OpenCount);
        Assert.Equal(1, factory.CatalogQueryCount);
        Assert.NotEmpty(logger.Entries);
        Assert.All(logger.Entries, entry =>
        {
            Assert.Contains("SqlToAi:Servers:alpha", entry.Message);
            Assert.DoesNotContain(marker, entry.Message);
            Assert.Null(entry.Exception);
        });
    }

    [Fact]
    public async Task CatalogQueryOrdinaryFailure_FallsBackToStaticGrantsWithSafeLog()
    {
        const string marker = "synthetic-secret-marker";
        var logger = new CapturingLogger<SchemaService>();
        var (service, factory) = CreateService(logger: logger);
        factory.CatalogQueryFailure = new InvalidOperationException($"query failed Password={marker};Server=raw-marker;");

        var result = await service.ListDatabasesAsync("alpha", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(["alpha/CustomersDb", "alpha/OrdersDb"], result.Value);
        Assert.Equal(1, factory.LastCatalogConnection!.OpenCount);
        Assert.Equal(1, factory.CatalogQueryCount);
        var entry = Assert.Single(logger.Entries);
        Assert.Contains("SqlToAi:Servers:alpha", entry.Message);
        Assert.Contains("Falling back", entry.Message);
        Assert.DoesNotContain(marker, entry.Message);
        Assert.DoesNotContain("raw-marker", entry.Message);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task CatalogReaderRequestedCancellation_PropagatesWithoutFallback()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var logger = new CapturingLogger<SchemaService>();
        var (service, factory) = CreateService(logger: logger);
        factory.CatalogQueryFailure = new OperationCanceledException(cancellation.Token);
        factory.BeforeCatalogQueryFailure = () => cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ListDatabasesAsync("alpha", cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, factory.LastCatalogConnection!.OpenCount);
        Assert.Equal(1, factory.CatalogQueryCount);
        Assert.Empty(logger.Entries);
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
        Assert.Equal("alpha/OrdersDb", matchOrders.Value[0]);

        var matchAll = await service.SearchDatabasesAsync("alpha", "db", TestContext.Current.CancellationToken);
        Assert.True(matchAll.IsSuccess);
        Assert.Equal(2, matchAll.Value.Count);
        Assert.Contains("alpha/CustomersDb", matchAll.Value);
        Assert.Contains("alpha/OrdersDb", matchAll.Value);

        var matchNone = await service.SearchDatabasesAsync("alpha", "Finance", TestContext.Current.CancellationToken);
        Assert.True(matchNone.IsSuccess);
        Assert.Empty(matchNone.Value);

        // Searching for server alias does not match because search operates on raw DB name only
        var matchAlias = await service.SearchDatabasesAsync("alpha", "alpha", TestContext.Current.CancellationToken);
        Assert.True(matchAlias.IsSuccess);
        Assert.Empty(matchAlias.Value);
    }
}
