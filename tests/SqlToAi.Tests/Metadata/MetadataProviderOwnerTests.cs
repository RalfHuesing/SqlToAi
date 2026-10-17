using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Metadata;
using SqlToAi.Tests.Database;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Metadata;

/// <summary>
/// Verifies owner-scoped metadata enrichment: per-server provider settings, source resolution,
/// disabled providers making no connection, failure and cancellation semantics.
/// </summary>
public sealed class MetadataProviderOwnerTests
{
    private sealed class TargetConnectionFactory : IDatabaseConnectionFactory
    {
        public List<string> TargetDatabases { get; } = [];
        public int TargetConnectionCount { get; private set; }
        public string TableDescription { get; set; } = "owner table description";
        public Dictionary<string, string> ColumnDescriptions { get; set; } = new() { ["Email"] = "owner column description" };
        public Exception? OpenFailure { get; set; }

        public DbConnection CreateTargetConnection(DatabaseTargetContext target)
        {
            TargetDatabases.Add(target.DatabaseName);
            TargetConnectionCount++;
            if (OpenFailure is not null) throw OpenFailure;
            return MockMetadataConnection.Create(TableDescription, ColumnDescriptions);
        }

        public Result<DbConnection> CreateCatalogConnection(string? serverAlias) => throw new NotSupportedException();

        public DbConnection CreateConnection(string? databaseName = null) => throw new NotSupportedException();
    }

    private static SqlToAiOptions BuildOptions(bool metadataEnabled = true)
    {
        var options = new SqlToAiOptions();
        var server = new NamedServerOptions
        {
            Server = "test-primary-host",
            IntegratedSecurity = true,
            TrustServerCertificate = false,
            ConnectTimeoutSeconds = 30
        };
        server.MetadataProvider = new MetadataProviderOptions { Enabled = metadataEnabled };
        options.Servers["test"] = server;
        return options;
    }

    private static (MetadataProvider Provider, TargetConnectionFactory Factory) BuildProvider(SqlToAiOptions options)
    {
        var factory = new TargetConnectionFactory();
        return (new MetadataProvider(factory, Options.Create(options), NullLogger<MetadataProvider>.Instance), factory);
    }

    private static DatabaseTargetContext ResolveTarget(SqlToAiOptions options, string qualifiedTarget) =>
        new DatabaseTargetResolver(options).Resolve(qualifiedTarget).Value;

    private static readonly SchemaObjectIdentity Identity = new(1, "dbo", "Customers", SchemaObjectKind.Table);

    [Fact]
    public async Task GetTableDescriptionAsync_DisabledProvider_MakesNoConnection()
    {
        var options = BuildOptions(metadataEnabled: false);
        var (provider, factory) = BuildProvider(options);

        string? description = await provider.GetTableDescriptionAsync(ResolveTarget(options, "test/AppDb"), Identity, TestContext.Current.CancellationToken);

        Assert.Null(description);
        Assert.Equal(0, factory.TargetConnectionCount);
    }

    [Fact]
    public async Task GetTableDescriptionAsync_UnknownOwner_KeepsNativeDescriptionsWithoutConnection()
    {
        var options = BuildOptions();
        var extra = new NamedServerOptions { Server = "other-host", IntegratedSecurity = true };
        options.Servers["other"] = extra; // resolvable alias only for building the context
        DatabaseTargetContext target = ResolveTarget(options, "other/AppDb");
        options.Servers.Remove("other"); // the provider must not find the owner anymore
        var (provider, factory) = BuildProvider(options);

        string? description = await provider.GetTableDescriptionAsync(target, Identity, TestContext.Current.CancellationToken);

        Assert.Null(description);
        Assert.Equal(0, factory.TargetConnectionCount);
    }

    [Fact]
    public async Task GetTableDescriptionAsync_EnabledProvider_ReturnsOwnerDescriptionOnTargetDatabase()
    {
        var options = BuildOptions();
        var (provider, factory) = BuildProvider(options);

        string? description = await provider.GetTableDescriptionAsync(ResolveTarget(options, "test/AppDb"), Identity, TestContext.Current.CancellationToken);

        Assert.Equal("owner table description", description);
        Assert.Equal(["AppDb"], factory.TargetDatabases);
    }

    [Fact]
    public async Task GetColumnDescriptionsAsync_EnabledProvider_ReturnsOwnerDescriptions()
    {
        var options = BuildOptions();
        var (provider, factory) = BuildProvider(options);

        IReadOnlyDictionary<string, string> descriptions = await provider.GetColumnDescriptionsAsync(
            ResolveTarget(options, "test/AppDb"), Identity, TestContext.Current.CancellationToken);

        Assert.Equal("owner column description", descriptions["Email"]);
        Assert.Equal(["AppDb"], factory.TargetDatabases);
    }

    [Fact]
    public async Task GetTableDescriptionAsync_LoadFailure_ReturnsNullWithoutDescription()
    {
        var options = BuildOptions();
        var (provider, factory) = BuildProvider(options);
        factory.OpenFailure = new InvalidOperationException("connection failed");

        string? description = await provider.GetTableDescriptionAsync(ResolveTarget(options, "test/AppDb"), Identity, TestContext.Current.CancellationToken);

        Assert.Null(description);
    }

    [Theory]
    [InlineData("table")]
    [InlineData("columns")]
    public async Task ProviderFailureLogsOwnerSettingsPathWithoutExceptionPayload(string lookup)
    {
        const string marker = "provider-secret-marker";
        var options = BuildOptions();
        options.Servers["prod"] = new NamedServerOptions
        {
            Server = "prod-primary-host",
            MetadataProvider = new MetadataProviderOptions { Enabled = true }
        };
        var factory = new TargetConnectionFactory { OpenFailure = new InvalidOperationException($"Password={marker};Server=raw-marker;") };
        var logger = new CapturingLogger<MetadataProvider>();
        var provider = new MetadataProvider(factory, Options.Create(options), logger);

        foreach (string alias in new[] { "test", "prod" })
        {
            var target = ResolveTarget(options, $"{alias}/AppDb");
            if (lookup == "table")
            {
                Assert.Null(await provider.GetTableDescriptionAsync(target, Identity, TestContext.Current.CancellationToken));
            }
            else
            {
                Assert.Empty(await provider.GetColumnDescriptionsAsync(target, Identity, TestContext.Current.CancellationToken));
            }
        }

        Assert.Equal(2, logger.Entries.Count);
        foreach (string alias in new[] { "test", "prod" })
        {
            var entry = Assert.Single(logger.Entries, item => item.Message.Contains($"SqlToAi:Servers:{alias}:MetadataProvider", StringComparison.Ordinal));
            Assert.Contains("SQL connection or provider failure", entry.Message);
            Assert.DoesNotContain(marker, entry.Message);
            Assert.DoesNotContain("raw-marker", entry.Message);
            Assert.Null(entry.Exception);
        }
    }

    [Fact]
    public async Task GetColumnDescriptionsAsync_LoadFailure_ReturnsEmptyDescriptions()
    {
        var options = BuildOptions();
        var (provider, factory) = BuildProvider(options);
        factory.OpenFailure = new InvalidOperationException("connection failed");

        IReadOnlyDictionary<string, string> descriptions = await provider.GetColumnDescriptionsAsync(
            ResolveTarget(options, "test/AppDb"), Identity, TestContext.Current.CancellationToken);

        Assert.Empty(descriptions);
    }

    [Fact]
    public async Task GetTableDescriptionAsync_RequestedCancellation_Propagates()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var options = BuildOptions();
        var (provider, factory) = BuildProvider(options);
        factory.OpenFailure = new OperationCanceledException(cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetTableDescriptionAsync(
            ResolveTarget(options, "test/AppDb"), Identity, cancellation.Token));
    }
}
