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
/// All cases are offline: fallback connections are simulated through the shared fake connection
/// plumbing and dedicated connections through the provider's dedicated-connection test seam.
/// </summary>
public sealed class AnonymizationRuleProviderOwnerTests
{
    private const string DefaultRulesTable = "dbo.AnonymizationRules";

    /// <summary>
    /// Thread-safe, target-aware factory: rule rows are keyed by <c>alias/database</c> (a "*"-keyed
    /// entry serves every other target) and explicit failing targets simulate failed loads. Lists
    /// and counters are lock-protected because parallel owner requests create connections at the
    /// same time.
    /// </summary>
    private sealed class TargetConnectionFactory : IDatabaseConnectionFactory
    {
        private const string AnyTarget = "*";

        private readonly object _gate = new();
        private readonly Dictionary<string, List<RuleRowData>> _rowsByTarget;
        private readonly HashSet<string> _failingTargets;

        public List<string> TargetDatabases { get; } = [];
        public List<string> TargetAliases { get; } = [];
        public int TargetConnectionCount { get; private set; }
        public int FailFirstConnections { get; init; }

        /// <summary>Serves the same rows for every requested target context.</summary>
        public TargetConnectionFactory(List<RuleRowData> rows)
            : this(new Dictionary<string, List<RuleRowData>> { [AnyTarget] = rows })
        {
        }

        /// <summary>Serves per-target rows; unconfigured targets fall back to a "*"-keyed entry.</summary>
        public TargetConnectionFactory(Dictionary<string, List<RuleRowData>> rowsByTarget, HashSet<string>? failingTargets = null)
        {
            _rowsByTarget = rowsByTarget;
            _failingTargets = failingTargets ?? [];
        }

        public DbConnection CreateTargetConnection(DatabaseTargetContext target)
        {
            string targetKey = $"{target.ServerAlias}/{target.DatabaseName}";
            lock (_gate)
            {
                TargetAliases.Add(target.ServerAlias);
                TargetDatabases.Add(target.DatabaseName);
                TargetConnectionCount++;
            }

            if (TargetConnectionCount <= FailFirstConnections || _failingTargets.Contains(targetKey))
            {
                throw new InvalidOperationException("connection failed");
            }

            if (!_rowsByTarget.TryGetValue(targetKey, out List<RuleRowData>? rows))
            {
                _rowsByTarget.TryGetValue(AnyTarget, out rows);
            }
            return MockConnection.Create(rows ?? []);
        }

        public Result<DbConnection> CreateCatalogConnection(string? serverAlias) => throw new NotSupportedException();

        public DbConnection CreateConnection(string? databaseName = null) => throw new NotSupportedException();
    }

    /// <summary>
    /// Connection-creation seam for dedicated sources: records every dedicated load and serves
    /// queued per-load rule rows (successful loads) or one fixed failing connection.
    /// </summary>
    private sealed class DedicatedConnectionFactory
    {
        private readonly object _gate = new();
        private readonly Queue<List<RuleRowData>>? _queuedRows;
        private readonly FakeDbConnection? _fixedConnection;

        public int LoadCount { get; private set; }

        public DedicatedConnectionFactory(Queue<List<RuleRowData>> queuedRows)
        {
            _queuedRows = queuedRows;
        }

        public DedicatedConnectionFactory(FakeDbConnection fixedConnection)
        {
            _fixedConnection = fixedConnection;
        }

        public DbConnection CreateConnection()
        {
            lock (_gate)
            {
                LoadCount++;
                if (_fixedConnection is not null)
                {
                    return _fixedConnection;
                }
                return MockConnection.Create(_queuedRows!.Dequeue());
            }
        }
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

    private static void ConfigureDedicatedRules(NamedServerOptions server)
    {
        server.AnonymizationRules = new AnonymizationRulesOptions
        {
            Enabled = true,
            Server = "rules-host",
            Database = "RulesDb",
            TableName = DefaultRulesTable,
            CacheTtlSeconds = 300,
            CommandTimeoutSeconds = 2
        };
    }

    private static AnonymizationRuleProvider BuildProvider(SqlToAiOptions options, IDatabaseConnectionFactory factory) =>
        new(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

    private static AnonymizationRuleProvider BuildDedicatedProvider(
        SqlToAiOptions options, IDatabaseConnectionFactory factory, DedicatedConnectionFactory dedicated) =>
        new(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance, dedicated.CreateConnection);

    private static DatabaseTargetContext ResolveTarget(SqlToAiOptions options, string qualifiedTarget) =>
        new DatabaseTargetResolver(options).Resolve(qualifiedTarget).Value;

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
        // Contrasting rules: a shared cache entry would make both targets yield the same result.
        var factory = new TargetConnectionFactory(new Dictionary<string, List<RuleRowData>>
        {
            ["test/AppDb"] = [new("%", "Customers", "Email", false)],
            ["test/OtherDb"] = [new("%", "Customers", "Email", true)]
        });
        var provider = BuildProvider(options, factory);

        bool appDbExcluded = await provider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);
        bool otherDbExcluded = await provider.IsExcludedAsync(ResolveTarget(options, "test/OtherDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);

        Assert.True(appDbExcluded);
        Assert.False(otherDbExcluded);
        Assert.Equal(["AppDb", "OtherDb"], factory.TargetDatabases);
        Assert.Equal(2, factory.TargetConnectionCount); // no cross-target cache reuse
    }

    [Fact]
    public async Task IsExcludedAsync_SameOwnerSameSource_ReusesCacheWithoutReload()
    {
        var options = BuildOptions();
        var factory = new TargetConnectionFactory([new("%", "Customers", "Email", false)]);
        var provider = BuildProvider(options, factory);

        bool first = await provider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);
        bool second = await provider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);

        Assert.True(first);
        Assert.True(second);
        Assert.Equal(1, factory.TargetConnectionCount); // second call hit the same-owner cache entry
    }

    [Fact]
    public async Task IsExcludedAsync_SharedDedicatedSource_LoadsRulesIndependentlyPerOwner()
    {
        var options = BuildOptions();
        AddServer(options, "prod", rulesEnabled: false);
        ConfigureDedicatedRules(options.Servers["test"]);
        ConfigureDedicatedRules(options.Servers["prod"]); // explicitly configured for both owners
        var factory = new TargetConnectionFactory([new("%", "Customers", "Email", false)]);
        var dedicatedRows = new Queue<List<RuleRowData>>();
        dedicatedRows.Enqueue([new("AppDb", "Customers", "Email", false)]); // test owner's load: exempt
        dedicatedRows.Enqueue([new("AppDb", "Customers", "Email", true)]); // prod owner's load: protective
        var dedicated = new DedicatedConnectionFactory(dedicatedRows);
        var provider = BuildDedicatedProvider(options, factory, dedicated);

        bool testExcluded = await provider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);
        bool prodExcluded = await provider.IsExcludedAsync(ResolveTarget(options, "prod/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);

        // Independent per-owner cache loads against the same dedicated source: the two loads return
        // contrasting rules, so a shared cache entry would make both results identical.
        Assert.True(testExcluded);
        Assert.False(prodExcluded);
        Assert.Equal(2, dedicated.LoadCount);
        Assert.Equal(0, factory.TargetConnectionCount); // dedicated path never used the primary factory
    }

    [Fact]
    public async Task IsExcludedAsync_DedicatedSourceOpenFailure_DoesNotFallBackToPrimary()
    {
        var options = BuildOptions();
        ConfigureDedicatedRules(options.Servers["test"]);
        var factory = new TargetConnectionFactory([new("%", "Customers", "Email", false)]);
        using var failingConnection = new FakeDbConnection(_ => throw new NotSupportedException(),
            new FakeDbConnectionOptions(DataSource: "rules-host", OpenFailure: new InvalidOperationException("connection refused")));
        var dedicated = new DedicatedConnectionFactory(failingConnection);
        var provider = BuildDedicatedProvider(options, factory, dedicated);

        bool excluded = await provider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);

        Assert.False(excluded); // default masking after the failed dedicated open
        Assert.Equal(1, failingConnection.OpenCount); // the dedicated open was actually attempted
        Assert.Equal(0, factory.TargetConnectionCount); // no fallback to the primary factory
    }

    [Fact]
    public async Task IsExcludedAsync_DedicatedSourceDoesNotAffectFallbackOwner()
    {
        var options = BuildOptions();
        AddServer(options, "prod", rulesEnabled: true); // per-target fallback via the factory
        ConfigureDedicatedRules(options.Servers["test"]);
        var factory = new TargetConnectionFactory([new("%", "Customers", "Email", false)]);
        using var failingConnection = new FakeDbConnection(_ => throw new NotSupportedException(),
            new FakeDbConnectionOptions(DataSource: "rules-host", OpenFailure: new InvalidOperationException("connection refused")));
        var dedicated = new DedicatedConnectionFactory(failingConnection);
        var provider = BuildDedicatedProvider(options, factory, dedicated);

        bool testExcluded = await provider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);
        bool prodExcluded = await provider.IsExcludedAsync(ResolveTarget(options, "prod/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken);

        Assert.False(testExcluded); // dedicated open failed -> masking
        Assert.True(prodExcluded); // fallback owner loaded its own exemptions
        Assert.Equal(1, failingConnection.OpenCount);
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

    [Fact]
    public async Task IsExcludedAsync_FailedLoad_RetainsDefaultMaskingAndDoesNotBorrowForeignRules()
    {
        var options = BuildOptions();
        var factory = new TargetConnectionFactory(
            new Dictionary<string, List<RuleRowData>>
            {
                ["test/OtherDb"] = [new("%", "Customers", "Email", false)]
            },
            failingTargets: ["test/AppDb"]);
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
        private int _count;
        public int TargetConnectionCount => _count;

        public DbConnection CreateTargetConnection(DatabaseTargetContext target)
        {
            int index = Interlocked.Increment(ref _count);
            return Connections[Math.Min(index, Connections.Count) - 1];
        }

        public Result<DbConnection> CreateCatalogConnection(string? serverAlias) => throw new NotSupportedException();

        public DbConnection CreateConnection(string? databaseName = null) => throw new NotSupportedException();
    }

    [Fact]
    public async Task IsExcludedAsync_ParallelRequests_KeepOwnersIsolated()
    {
        var options = BuildOptions();
        AddServer(options, "prod", rulesEnabled: true);
        // Same physical endpoint for both owners, but only "test" exempts Email: a borrowed cache
        // entry would flip one of the two results.
        options.Servers["prod"].Server = options.Servers["test"].Server;
        var factory = new TargetConnectionFactory(new Dictionary<string, List<RuleRowData>>
        {
            ["test/AppDb"] = [new("%", "Customers", "Email", false)],
            ["prod/AppDb"] = [new("%", "Customers", "Email", true)]
        });
        var provider = BuildProvider(options, factory);

        var testTask = Task.Run(() => provider.IsExcludedAsync(
            ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken));
        var prodTask = Task.Run(() => provider.IsExcludedAsync(
            ResolveTarget(options, "prod/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken));

        Assert.True(await testTask);
        Assert.False(await prodTask);
        Assert.Equal(2, factory.TargetConnectionCount); // both owners loaded their own rules

        // Alternating calls afterwards keep hitting each owner's own cache entry.
        Assert.True(await provider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken));
        Assert.False(await provider.IsExcludedAsync(ResolveTarget(options, "prod/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken));
        Assert.Equal(2, factory.TargetConnectionCount); // cache reuse, no reload and no cross-owner borrow
    }

    [Fact]
    public async Task IsExcludedAsync_ParallelAndAlternatingCalls_FailedOwnerNeverBorrowsForeignExemptions()
    {
        var options = BuildOptions();
        AddServer(options, "prod", rulesEnabled: true);
        options.Servers["prod"].Server = options.Servers["test"].Server; // same physical endpoint
        var factory = new TargetConnectionFactory(
            new Dictionary<string, List<RuleRowData>>
            {
                ["test/AppDb"] = [new("%", "Customers", "Email", false)]
            },
            failingTargets: ["prod/AppDb"]);
        var provider = BuildProvider(options, factory);

        // Concurrent first calls: the failing owner must not borrow the successful owner's cache.
        var testTask = Task.Run(() => provider.IsExcludedAsync(
            ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken));
        var prodTask = Task.Run(() => provider.IsExcludedAsync(
            ResolveTarget(options, "prod/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken));

        Assert.True(await testTask);
        Assert.False(await prodTask);

        // Alternating calls afterwards: the failed owner keeps default masking while the foreign
        // owner keeps its cached exemptions.
        for (int round = 0; round < 3; round++)
        {
            Assert.False(await provider.IsExcludedAsync(ResolveTarget(options, "prod/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken));
            Assert.True(await provider.IsExcludedAsync(ResolveTarget(options, "test/AppDb"), string.Empty, "Customers", "Email", TestContext.Current.CancellationToken));
        }

        Assert.Contains("prod", factory.TargetAliases); // the failing owner's load was attempted
        Assert.Contains("test", factory.TargetAliases);
    }
}
