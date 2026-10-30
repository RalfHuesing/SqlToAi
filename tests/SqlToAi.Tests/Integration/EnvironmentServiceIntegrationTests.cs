using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(SqlServerCollectionFixture.Name)]
public sealed class EnvironmentServiceIntegrationTests
{
    private readonly SqlServerFixture _fx;
    private readonly string _target;

    public EnvironmentServiceIntegrationTests(SqlServerFixture fx)
    {
        _fx = fx;
        _target = TestConstants.QualifiedDatabaseName;
    }

    [Fact]
    public async Task GetEnvironmentAsync_ReturnsValidServerAndDatabaseMetadata()
    {
        var result = await _fx.EnvironmentService.GetEnvironmentAsync(_target, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        var env = result.Value;

        Assert.Equal(_target, env.Target);
        Assert.False(string.IsNullOrWhiteSpace(env.ProductVersion), "ProductVersion should not be empty.");
        Assert.True(env.ProductMajorVersion is null or >= 10, "ProductMajorVersion should be >= 10 or unavailable.");
        Assert.False(string.IsNullOrWhiteSpace(env.Edition), "Edition should not be empty.");
        Assert.True(env.EngineEdition is > 0, "EngineEdition should be greater than zero.");
        Assert.True(env.CompatibilityLevel is >= 100, "CompatibilityLevel should be >= 100.");
        Assert.False(string.IsNullOrWhiteSpace(env.Collation), "Collation should not be empty.");
        Assert.Equal("ONLINE", env.StateDesc);
        Assert.NotNull(env.IsReadOnly);
    }

    [Fact]
    public async Task GetEnvironmentAsync_WorksWithSchemaOnlyAccess()
    {
        // Clone options with only SchemaOnly grant for the test target
        var options = new SqlToAiOptions();
        foreach (var (k, v) in _fx.Options.Servers)
        {
            options.Servers[k] = v;
        }
        options.Databases.SchemaOnly = [_target];
        options.QueryExecution.CommandTimeoutSeconds = _fx.Options.QueryExecution.CommandTimeoutSeconds;

        var optionsWrapper = Options.Create(options);
        var guard = new SecurityGuard(optionsWrapper);
        var accessProvider = new AccessLevelProvider(optionsWrapper);
        var service = new EnvironmentService(_fx.ConnectionFactory, guard, accessProvider, optionsWrapper, NullLogger<EnvironmentService>.Instance);

        var result = await service.GetEnvironmentAsync(_target, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.Equal(_target, result.Value.Target);
        Assert.Equal("ONLINE", result.Value.StateDesc);
    }

    [Fact]
    public async Task GetEnvironmentAsync_RejectsUnknownServerAliasBeforeConnecting()
    {
        var result = await _fx.EnvironmentService.GetEnvironmentAsync("unknown_server/SomeDb", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }
}
