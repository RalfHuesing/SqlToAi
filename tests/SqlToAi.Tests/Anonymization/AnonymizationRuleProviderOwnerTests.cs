using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Tests.Database;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Anonymization;

/// <summary>
/// Verifies owner-scoped central-rule behavior: per-server provider settings, source resolution,
/// cache partitioning by policy owner and effective source, failure and cancellation semantics.
/// All cases are offline: connections are simulated through the shared fake connection plumbing.
/// </summary>
public sealed class AnonymizationRuleProviderOwnerTests
{
    private const string DefaultRulesTable = "dbo.AnonymizationRules";

    /// <summary>Factory that serves one fresh mock connection per requested target context.</summary>
    private sealed class TargetConnectionFactory(List<RuleRowData> rows) : IDatabaseConnectionFactory
    {
        public List<string> TargetDatabases { get; } = [];
        public List<string> TargetAliases { get; } = [];
        public int TargetConnectionCount { get; private set; }
        public int FailFirstConnections { get; init; }

        public DbConnection CreateTargetConnection(DatabaseTargetContext target)
        {
            TargetAliases.Add(target.ServerAlias);
            TargetDatabases.Add(target.DatabaseName);
            TargetConnectionCount++;
            if (TargetConnectionCount <= FailFirstConnections)
            {
                throw new InvalidOperationException("connection failed");
            }
            return MockConnection.Create(rows);
        }

        public Result<DbConnection> CreateCatalogConnection(string? serverAlias) => throw new NotSupportedException();

        public DbConnection CreateConnection(string? databaseName = null) => throw new NotSupportedException();
    }

    private static SqlToAiOptions BuildOptions()
    {
        var options = new SqlToAiOptions();
        AddServer(options, "test", rulesEnabled: true);
        return options;
    }

    private static NamedServerOptions AddServer(SqlToAiOptions options, string alias, bool rulesEnabled)
    {
        var server = new NamedServerOptions
        {
            Server = $"{alias}-primary-host",
            IntegratedSecurity = true,
            TrustServerCertificate = false,
            ConnectTimeoutSeconds = 30
        };
        server.AnonymizationRules = new AnonymizationRulesOptions
        {
            Enabled = rulesEnabled,
            TableName = DefaultRulesTable,
            CacheTtlSeconds = 300
        };
        options.Servers[alias] = server;
        return server;
    }

    private static AnonymizationRuleProvider BuildProvider(SqlToAiOptions options, IDatabaseConnectionFactory factory) =>
        new(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

    private static DatabaseTargetContext ResolveTarget(SqlToAiOptions options, string qualifiedTarget) =>
        new DatabaseTargetResolver(options).Resolve(qualifiedTarget).Value;

    // -------------------------------------------------------------------------
    // Disabled providers
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IsExcludedAsync_DisabledRules_MakeNoConnection()
    {
        var options = BuildOptions();
        AddServer(options, "test", rulesEnabled: false);
        var factory = new TargetConnectionFactory([new("%", "Customers", "Email", false)]);
        var provider = BuildProvider(options, factory);

        bool excluded = await provider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), "dbo", "Customers", "Email", TestContext.Current.CancellationToken);

        Assert.False(excluded);
        Assert.Equal(0, factory.TargetConnectionCount);
    }

    [Fact]
    public async Task IsExcludedAsync_UnknownOwner_KeepsDefaultMaskingWithoutConnection()
    {
        var options = BuildOptions();
        AddServer(options, "unknown", rulesEnabled: true); // resolvable alias only for building the context
        DatabaseTargetContext target = ResolveTarget(options, "unknown/AppDb");
        options.Servers.Remove("unknown"); // the provider must not find the owner anymore
        var factory = new TargetConnectionFactory([new("%", "Customers", "Email", false)]);
        var provider = BuildProvider(options, factory);

        bool excluded = await provider.IsExcludedAsync(target, "dbo", "Customers", "Email", TestContext.Current.CancellationToken);

        Assert.False(excluded);
        Assert.Equal(0, factory.TargetConnectionCount);
    }

    // -------------------------------------------------------------------------
    // Success and matching inside the owner's context
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IsExcludedAsync_RuleDatabasePattern_MatchesRawTargetDatabaseName()
    {
        var options = BuildOptions();
        var factory = new TargetConnectionFactory([new("App/Db", "%", "Email", false)]);
        var provider = BuildProvider(options, factory);

        bool excludedSlashDb = await provider.IsExcludedAsync(ResolveTarget(options, "test/App/Db"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);
        bool excludedOtherDb = await provider.IsExcludedAsync(ResolveTarget(options, "test/OtherDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);

        Assert.True(excludedSlashDb);
        Assert.False(excludedOtherDb);
    }

    [Fact]
    public async Task IsExcludedAsync_MaskedAndExemptValues_AreCorrectForOneOwner()
    {
        var options = BuildOptions();
        var factory = new TargetConnectionFactory(
        [
            new("%", "Customers", "Email", false),
            new("%", "Customers", "Name", true)
        ]);
        var provider = BuildProvider(options, factory);

        bool emailExcluded = await provider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), "dbo", "Customers", "Email", TestContext.Current.CancellationToken);
        bool nameExcluded = await provider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), "dbo", "Customers", "Name", TestContext.Current.CancellationToken);

        Assert.True(emailExcluded);
        Assert.False(nameExcluded);
    }

    // -------------------------------------------------------------------------
    // Cache partitioning
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IsExcludedAsync_SameEndpointDifferentAliases_RemainDistinctOwners()
    {
        var options = BuildOptions();
        AddServer(options, "prod", rulesEnabled: true);
        options.Servers["prod"].Server = options.Servers["test"].Server; // same physical endpoint
        var factory = new TargetConnectionFactory([new("%", "Customers", "Email", false)]);
        var provider = BuildProvider(options, factory);

        bool testExcluded = await provider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);
        bool prodExcluded = await provider.IsExcludedAsync(ResolveTarget(options, "prod/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);

        Assert.True(testExcluded);
        Assert.True(prodExcluded);
        Assert.Equal(2, factory.TargetConnectionCount); // both owners loaded their own rules
    }

    [Fact]
    public async Task IsExcludedAsync_PerTargetFallback_DifferentTargetDatabasesDoNotShareCache()
    {
        var options = BuildOptions();
        var factory = new TargetConnectionFactory([new("%", "Customers", "Email", false)]);
        var provider = BuildProvider(options, factory);

        bool appDbExcluded = await provider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);
        bool otherDbExcluded = await provider.IsExcludedAsync(ResolveTarget(options, "test/OtherDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);

        Assert.True(appDbExcluded);
        Assert.True(otherDbExcluded);
        Assert.Equal(["AppDb", "OtherDb"], factory.TargetDatabases);
        Assert.Equal(2, factory.TargetConnectionCount); // no cross-target cache reuse
    }

    // -------------------------------------------------------------------------
    // Dedicated sources (offline: an unbound loopback port fails fast on connect)
    // -------------------------------------------------------------------------

    private static string UnreachableEndpoint()
    {
        // Reserve a port and immediately release it: connections to it are refused quickly.
        using System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return $"127.0.0.1,{port}";
    }

    private static void ConfigureDedicatedRules(NamedServerOptions server, string endpoint)
    {
        server.AnonymizationRules = new AnonymizationRulesOptions
        {
            Enabled = true,
            Server = endpoint,
            Database = "RulesDb",
            TableName = DefaultRulesTable,
            CacheTtlSeconds = 300,
            CommandTimeoutSeconds = 2
        };
    }

    [Fact]
    public async Task IsExcludedAsync_SharedDedicatedSource_LoadsPerOwnerAndFailsSafePerOwner()
    {
        var options = BuildOptions();
        string unreachable = UnreachableEndpoint();
        AddServer(options, "prod", rulesEnabled: false);
        ConfigureDedicatedRules(options.Servers["test"], unreachable);
        ConfigureDedicatedRules(options.Servers["prod"], unreachable); // explicitly configured for both owners
        var factory = new TargetConnectionFactory([new("%", "Customers", "Email", false)]);
        var provider = BuildProvider(options, factory);

        bool testExcluded = await provider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);
        bool prodExcluded = await provider.IsExcludedAsync(ResolveTarget(options, "prod/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);

        Assert.False(testExcluded); // dedicated load fails offline -> default masking
        Assert.False(prodExcluded); // no foreign exemptions borrowed
        Assert.Empty(factory.TargetDatabases); // neither owner fell back to the primary factory
    }

    [Fact]
    public async Task IsExcludedAsync_DedicatedSourceDoesNotAffectFallbackOwner()
    {
        var options = BuildOptions();
        AddServer(options, "prod", rulesEnabled: true); // per-target fallback via the factory
        ConfigureDedicatedRules(options.Servers["test"], UnreachableEndpoint());
        var factory = new TargetConnectionFactory([new("%", "Customers", "Email", false)]);
        var provider = BuildProvider(options, factory);

        bool testExcluded = await provider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);
        bool prodExcluded = await provider.IsExcludedAsync(ResolveTarget(options, "prod/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);

        Assert.False(testExcluded); // dedicated source unavailable -> masking
        Assert.True(prodExcluded); // fallback owner loaded its own exemptions
        Assert.Equal(["AppDb"], factory.TargetDatabases);
        Assert.Equal(["prod"], factory.TargetAliases);
    }

    [Fact]
    public async Task IsExcludedAsync_DifferentRulesTables_AreSeparateCacheEntries()
    {
        var options = BuildOptions();
        var factory = new TargetConnectionFactory([new("%", "Customers", "Email", false)]);
        var provider = BuildProvider(options, factory);

        bool first = await provider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);
        Assert.True(first);
        Assert.Equal(1, factory.TargetConnectionCount);

        options.Servers["test"].AnonymizationRules.TableName = "dbo.OtherRules";

        bool second = await provider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);
        Assert.True(second);
        Assert.Equal(2, factory.TargetConnectionCount); // different rules table -> different cache identity
    }

    // -------------------------------------------------------------------------
    // Failure semantics
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IsExcludedAsync_FailedLoad_RetainsDefaultMaskingAndDoesNotBorrowForeignRules()
    {
        var options = BuildOptions();
        var factory = new TargetConnectionFactory([new("%", "Customers", "Email", false)]) { FailFirstConnections = 1 };
        var provider = BuildProvider(options, factory);

        bool failed = await provider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);
        Assert.False(failed); // default masking

        // A second target with a working source must not be affected and must load its own rules.
        bool otherDb = await provider.IsExcludedAsync(ResolveTarget(options, "test/OtherDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);
        Assert.True(otherDb);
        Assert.Equal(["AppDb", "OtherDb"], factory.TargetDatabases);
        Assert.Equal(2, factory.TargetConnectionCount);
    }

    [Fact]
    public async Task IsExcludedAsync_DedicatedServerWithoutDatabase_FailsSafeToMasking()
    {
        var options = BuildOptions();
        NamedServerOptions server = AddServer(options, "test", rulesEnabled: true);
        server.AnonymizationRules.Server = "rules-host";
        server.AnonymizationRules.Database = null;
        var factory = new TargetConnectionFactory([new("%", "Customers", "Email", false)]);
        var provider = BuildProvider(options, factory);

        bool excluded = await provider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);

        Assert.False(excluded);
        Assert.Equal(0, factory.TargetConnectionCount);
    }

    [Fact]
    public async Task IsExcludedAsync_RequestedCancellation_PropagatesAndCachesNothing()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var options = BuildOptions();
        using var cancelledConnection = new FakeDbConnection(_ => throw new NotSupportedException(),
            new FakeDbConnectionOptions(OpenFailure: new OperationCanceledException(cancellation.Token)));
        using var successConnection = MockConnection.Create([new("%", "Customers", "Email", false)]);
        var served = new ServingFactory
        {
            Connections = [cancelledConnection, successConnection]
        };
        var cancelledProvider = BuildProvider(options, served);

        await cancellation.CancelAsync();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancelledProvider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);

        // Nothing was cached: the next call performs a fresh load and succeeds.
        bool excluded = await cancelledProvider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);
        Assert.True(excluded);
        Assert.Equal(2, served.TargetConnectionCount);
    }

    private sealed class ServingFactory : IDatabaseConnectionFactory
    {
        public List<DbConnection> Connections { get; init; } = [];
        public int TargetConnectionCount { get; private set; }

        public DbConnection CreateTargetConnection(DatabaseTargetContext target)
        {
            TargetConnectionCount++;
            return Connections[Math.Min(TargetConnectionCount, Connections.Count) - 1];
        }

        public Result<DbConnection> CreateCatalogConnection(string? serverAlias) => throw new NotSupportedException();

        public DbConnection CreateConnection(string? databaseName = null) => throw new NotSupportedException();
    }

    // -------------------------------------------------------------------------
    // Parallel requests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IsExcludedAsync_ParallelRequests_KeepOwnersIsolated()
    {
        var options = BuildOptions();
        AddServer(options, "prod", rulesEnabled: true);
        // Same endpoint for both owners, but only "test" exempts the Email column.
        options.Servers["prod"].Server = options.Servers["test"].Server;
        var factory = new TargetConnectionFactory([new("%", "Customers", "Email", false)]);
        var provider = BuildProvider(options, factory);

        var testTask = Task.Run(() => provider.IsExcludedAsync(
            ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken));
        var prodTask = Task.Run(() => provider.IsExcludedAsync(
            ResolveTarget(options, "prod/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken));

        Assert.True(await testTask);
        Assert.True(await prodTask);
        Assert.Equal(2, factory.TargetConnectionCount);
    }
}
