using System.Data.Common;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Mcp;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

public sealed class EnvironmentServiceTests
{
    private static readonly string[] EnvironmentColumns =
    [
        "ProductVersion",
        "ProductMajorVersion",
        "Edition",
        "EngineEdition",
        "CompatibilityLevel",
        "Collation",
        "StateDesc",
        "IsReadOnly"
    ];

    private sealed class TrackingConnectionFactory : IDatabaseConnectionFactory
    {
        public List<DatabaseTargetContext> OpenedTargets { get; } = [];
        public List<string> ExecutedSql { get; } = [];
        public int ConnectionCount { get; private set; }
        public Func<FakeDbCommand, FakeDbDataReader>? ReaderHandler { get; set; }
        public Func<DatabaseTargetContext, DbConnection>? ConnectionFactoryOverride { get; set; }

        public DbConnection CreateTargetConnection(DatabaseTargetContext target)
        {
            ConnectionCount++;
            OpenedTargets.Add(target);

            if (ConnectionFactoryOverride is not null)
            {
                return ConnectionFactoryOverride(target);
            }

            return new FakeDbConnection(conn => new FakeDbCommand(conn, new FakeDbCommandHandlers(
                ExecuteReader: cmd =>
                {
                    ExecutedSql.Add(cmd.CommandText);
                    return ReaderHandler is not null
                        ? ReaderHandler(cmd)
                        : new FakeDbDataReader(EnvironmentColumns,
                            [["16.0.4180.1", 16, "Developer Edition (64-bit)", 3, (byte)160, "Latin1_General_CI_AS", "ONLINE", false]]);
                })));
        }

        public Result<DbConnection> CreateCatalogConnection(string? serverAlias) => throw new NotSupportedException();
        public DbConnection CreateConnection(string? databaseName = null) => throw new NotSupportedException();
    }

    private static (EnvironmentService Service, TrackingConnectionFactory Factory) CreateService(Action<SqlToAiOptions>? configure = null)
    {
        var options = new SqlToAiOptions()
            .WithNamedServers("srv1", "srv2");
        options.Databases.SchemaOnly = ["srv1/SharedDb", "srv2/SharedDb", "srv1/WhitelistedDb"];
        options.Servers["srv1"].ExcludedDatabases = ["ExcludedDb"];
        configure?.Invoke(options);

        var bound = Options.Create(options);
        var factory = new TrackingConnectionFactory();
        var guard = new SecurityGuard(bound);
        var accessProvider = new AccessLevelProvider(factory, bound, NullLogger<AccessLevelProvider>.Instance);
        var service = new EnvironmentService(factory, guard, accessProvider, bound, NullLogger<EnvironmentService>.Instance);
        return (service, factory);
    }

    [Theory]
    [InlineData("SharedDb")]
    [InlineData("dbo.Customers")]
    [InlineData("unknown_alias/SharedDb")]
    [InlineData("123invalid/SharedDb")]
    [InlineData("/SharedDb")]
    [InlineData("srv1/")]
    [InlineData("   ")]
    [InlineData("")]
    public async Task GetEnvironmentAsync_RejectsUnqualifiedOrUnknownTargetBeforeConnecting(string invalidTarget)
    {
        var (service, factory) = CreateService();

        var result = await service.GetEnvironmentAsync(invalidTarget, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Equal(0, factory.ConnectionCount);
        Assert.Empty(factory.OpenedTargets);
    }

    [Fact]
    public async Task GetEnvironmentAsync_RejectsTargetBlockedByStaticWhitelistBeforeConnecting()
    {
        var (service, factory) = CreateService();

        var result = await service.GetEnvironmentAsync("srv1/BlockedDb", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
        Assert.Contains("blocked by security policies", result.Error.Message);
        Assert.Equal(0, factory.ConnectionCount);
    }

    [Fact]
    public async Task GetEnvironmentAsync_RejectsTargetExcludedBeforeConnecting()
    {
        var (service, factory) = CreateService();

        var result = await service.GetEnvironmentAsync("srv1/ExcludedDb", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
        Assert.Contains("blocked by security policies", result.Error.Message);
        Assert.Equal(0, factory.ConnectionCount);
    }

    [Fact]
    public async Task GetEnvironmentAsync_RejectsAccessLevelNoneBeforeConnecting()
    {
        var options = new SqlToAiOptions().WithNamedServers("srv1");
        options.Databases.SchemaOnly = ["srv1/SharedDb"];
        var bound = Options.Create(options);
        var factory = new TrackingConnectionFactory();
        var guard = new SecurityGuard(bound);
        var customAccessProvider = new DelegatingAccessLevelProvider(_ => AccessLevel.None);
        var service = new EnvironmentService(factory, guard, customAccessProvider, bound, NullLogger<EnvironmentService>.Instance);

        var result = await service.GetEnvironmentAsync("srv1/SharedDb", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
        Assert.Contains("access was denied (AccessLevel: None)", result.Error.Message);
        Assert.Equal(0, factory.ConnectionCount);
    }

    private sealed class DelegatingAccessLevelProvider(Func<DatabaseTargetContext, AccessLevel> getLevel) : IAccessLevelProvider
    {
        public Task<AccessLevel> GetTargetAccessLevelAsync(DatabaseTargetContext target, CancellationToken cancellationToken = default)
            => Task.FromResult(getLevel(target));
    }

    [Fact]
    public async Task GetEnvironmentAsync_AllowsSchemaOnly()
    {
        var (service, factory) = CreateService();

        var result = await service.GetEnvironmentAsync("srv1/SharedDb", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, factory.ConnectionCount);
        Assert.Equal("srv1/SharedDb", result.Value.Target);
    }

    [Fact]
    public async Task GetEnvironmentAsync_AllowsReadOnlyAndReadWrite()
    {
        var (service, factory) = CreateService(opts =>
        {
            opts.Databases.ReadOnly = ["srv1/ReadOnlyDb"];
            opts.Databases.ReadWrite = ["srv1/ReadWriteDb"];
        });

        var roResult = await service.GetEnvironmentAsync("srv1/ReadOnlyDb", TestContext.Current.CancellationToken);
        var rwResult = await service.GetEnvironmentAsync("srv1/ReadWriteDb", TestContext.Current.CancellationToken);

        Assert.True(roResult.IsSuccess);
        Assert.True(rwResult.IsSuccess);
        Assert.Equal(2, factory.ConnectionCount);
    }

    [Fact]
    public async Task GetEnvironmentAsync_SameNamedDatabaseOnDifferentServers_RoutesToCorrectContext()
    {
        var (service, factory) = CreateService();

        var srv1Result = await service.GetEnvironmentAsync("srv1/SharedDb", TestContext.Current.CancellationToken);
        var srv2Result = await service.GetEnvironmentAsync("srv2/SharedDb", TestContext.Current.CancellationToken);

        Assert.True(srv1Result.IsSuccess);
        Assert.True(srv2Result.IsSuccess);
        Assert.Equal(2, factory.ConnectionCount);
        Assert.Equal("srv1", factory.OpenedTargets[0].ServerAlias);
        Assert.Equal("srv2", factory.OpenedTargets[1].ServerAlias);
        Assert.Equal("srv1/SharedDb", srv1Result.Value.Target);
        Assert.Equal("srv2/SharedDb", srv2Result.Value.Target);
    }

    [Fact]
    public async Task GetEnvironmentAsync_MapsAllAllowlistedFieldsCorrectly()
    {
        var (service, factory) = CreateService();
        factory.ReaderHandler = _ => new FakeDbDataReader(EnvironmentColumns,
            [["16.0.4180.1", 16, "Developer Edition (64-bit)", 3, (byte)160, "Latin1_General_CI_AS", "ONLINE", false]]);

        var result = await service.GetEnvironmentAsync("srv1/SharedDb", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var env = result.Value;
        Assert.Equal("srv1/SharedDb", env.Target);
        Assert.Equal("16.0.4180.1", env.ProductVersion);
        Assert.Equal(16, env.ProductMajorVersion);
        Assert.Equal("Developer Edition (64-bit)", env.Edition);
        Assert.Equal(3, env.EngineEdition);
        Assert.Equal((byte)160, env.CompatibilityLevel);
        Assert.Equal("Latin1_General_CI_AS", env.Collation);
        Assert.Equal("ONLINE", env.StateDesc);
        Assert.False(env.IsReadOnly);
        Assert.Empty(env.UnavailableFields);
    }

    [Fact]
    public async Task GetEnvironmentAsync_MissingFieldsAreReportedInUnavailableFieldsAndNeverAsZeroOrFalse()
    {
        var (service, factory) = CreateService();
        // ProductMajorVersion is null, IsReadOnly is null, Edition is null
        factory.ReaderHandler = _ => new FakeDbDataReader(EnvironmentColumns,
            [["16.0.4180.1", null, null, 3, (byte)160, "Latin1_General_CI_AS", "ONLINE", null]]);

        var result = await service.GetEnvironmentAsync("srv1/SharedDb", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var env = result.Value;
        Assert.Equal("srv1/SharedDb", env.Target);
        Assert.Equal("16.0.4180.1", env.ProductVersion);
        Assert.Null(env.ProductMajorVersion);
        Assert.Null(env.Edition);
        Assert.Equal(3, env.EngineEdition);
        Assert.Equal((byte)160, env.CompatibilityLevel);
        Assert.Equal("Latin1_General_CI_AS", env.Collation);
        Assert.Equal("ONLINE", env.StateDesc);
        Assert.Null(env.IsReadOnly);

        Assert.Contains("product_major_version", env.UnavailableFields);
        Assert.Contains("edition", env.UnavailableFields);
        Assert.Contains("is_read_only", env.UnavailableFields);
        Assert.Equal(3, env.UnavailableFields.Count);
    }

    [Fact]
    public async Task GetEnvironmentAsync_AllFieldsNull_ReportsAllUnavailableFields()
    {
        var (service, factory) = CreateService();
        factory.ReaderHandler = _ => new FakeDbDataReader(EnvironmentColumns,
            [[null, null, null, null, null, null, null, null]]);

        var result = await service.GetEnvironmentAsync("srv1/SharedDb", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var env = result.Value;
        Assert.Equal("srv1/SharedDb", env.Target);
        Assert.Null(env.ProductVersion);
        Assert.Null(env.ProductMajorVersion);
        Assert.Null(env.Edition);
        Assert.Null(env.EngineEdition);
        Assert.Null(env.CompatibilityLevel);
        Assert.Null(env.Collation);
        Assert.Null(env.StateDesc);
        Assert.Null(env.IsReadOnly);

        Assert.Equal(8, env.UnavailableFields.Count);
        Assert.Equal(
            ["product_version", "product_major_version", "edition", "engine_edition", "compatibility_level", "collation", "state_desc", "is_read_only"],
            env.UnavailableFields);
    }

    [Fact]
    public async Task GetEnvironmentAsync_JsonSerialization_OmitsNullValuesAndSerializesUnavailableFields()
    {
        var env = new DatabaseEnvironmentResult(
            Target: "srv1/SharedDb",
            ProductVersion: "16.0.4180.1",
            ProductMajorVersion: null,
            Edition: null,
            EngineEdition: 3,
            CompatibilityLevel: 160,
            Collation: "Latin1_General_CI_AS",
            StateDesc: "ONLINE",
            IsReadOnly: null,
            UnavailableFields: ["product_major_version", "edition", "is_read_only"]);

        // Test with McpJsonContext
        string json = JsonSerializer.Serialize(env, McpJsonContext.Default.DatabaseEnvironmentResult);
        using (var doc = JsonDocument.Parse(json))
        {
            var root = doc.RootElement;
            Assert.Equal("srv1/SharedDb", root.GetProperty("target").GetString());
            Assert.Equal("16.0.4180.1", root.GetProperty("product_version").GetString());
            Assert.Equal(3, root.GetProperty("engine_edition").GetInt32());
            Assert.Equal(160, root.GetProperty("compatibility_level").GetByte());
            Assert.Equal("Latin1_General_CI_AS", root.GetProperty("collation").GetString());
            Assert.Equal("ONLINE", root.GetProperty("state_desc").GetString());

            // Nullable omitted properties must NOT exist in JSON (never 0 or false!)
            Assert.False(root.TryGetProperty("product_major_version", out _));
            Assert.False(root.TryGetProperty("edition", out _));
            Assert.False(root.TryGetProperty("is_read_only", out _));

            var unavailableArray = root.GetProperty("unavailable_fields").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Equal(["product_major_version", "edition", "is_read_only"], unavailableArray);
        }

        // Test with McpAnalysisJsonContext as well
        string analysisJson = JsonSerializer.Serialize(env, McpAnalysisJsonContext.Default.DatabaseEnvironmentResult);
        using (var doc = JsonDocument.Parse(analysisJson))
        {
            var root = doc.RootElement;
            Assert.False(root.TryGetProperty("product_major_version", out _));
            Assert.False(root.TryGetProperty("is_read_only", out _));
        }
    }

    [Fact]
    public async Task GetEnvironmentAsync_HandlesConnectionFailure_ReturnsCatalogErrorNeverAllNull()
    {
        var (service, factory) = CreateService();
        factory.ConnectionFactoryOverride = target =>
            new FakeDbConnection(conn => throw new PrimaryConnectionException(
                new SqlToAiError(SqlToAiError.InfrastructureErrorCode, "Connection refused"), false));

        var result = await service.GetEnvironmentAsync("srv1/SharedDb", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InfrastructureErrorCode, result.Error.Code);
        Assert.Contains("Connection refused", result.Error.Message);
    }

    [Fact]
    public async Task GetEnvironmentAsync_HandlesTimeout_ReturnsCatalogErrorNeverAllNull()
    {
        var (service, factory) = CreateService();
        factory.ReaderHandler = _ => throw new TimeoutException("Operation timed out.");

        var result = await service.GetEnvironmentAsync("srv1/SharedDb", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.TimeoutCode, result.Error.Code);
    }

    [Fact]
    public async Task GetEnvironmentAsync_HandlesPermissionError_ReturnsCatalogErrorNeverAllNull()
    {
        var (service, factory) = CreateService();
        factory.ReaderHandler = _ => throw SqlExceptionFactory.Create(229, "The SELECT permission was denied on the object 'databases', database 'master', schema 'sys'.");

        var result = await service.GetEnvironmentAsync("srv1/SharedDb", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
        Assert.Contains("Permission denied", result.Error.Message);
    }

    [Fact]
    public async Task GetEnvironmentAsync_NoRowsReturned_ReturnsQueryError()
    {
        var (service, factory) = CreateService();
        factory.ReaderHandler = _ => new FakeDbDataReader(EnvironmentColumns, []);

        var result = await service.GetEnvironmentAsync("srv1/SharedDb", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Contains("No environment metadata returned", result.Error.Message);
    }
}
