#nullable enable

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Metadata;
using SqlToAi.Security;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Unit tests for <see cref="SchemaService"/> detail queries (ForeignKeys, Indexes, Constraints, Triggers, References, Parameters).
/// </summary>
public sealed class SchemaServiceDetailsTests
{
    private static SchemaService BuildService(SqlToAiOptions? options = null, DummyConnectionFactory? factory = null)
    {
        options ??= new SqlToAiOptions();
        factory ??= new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(factory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(factory, Options.Create(options), NullLogger<MetadataProvider>.Instance);
        var policyResolver = new AlwaysAllowPolicyResolver();

        return new SchemaService(
            factory, securityGuard, accessLevelProvider, metadataProvider,
            policyResolver, Options.Create(options), NullLogger<SchemaService>.Instance);
    }

    [Fact]
    public async Task GetSchemaForeignKeysAsync_ShouldReturnForeignKeys()
    {
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = [TestConstants.DatabaseName];
        var service = BuildService(options);

        var result = await service.GetSchemaForeignKeysAsync(TestConstants.DatabaseName, "dbo.Orders", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains("FK_Orders_Customers", result.Value);
        Assert.Contains("dbo.Orders.CustomerId", result.Value);
    }

    [Fact]
    public async Task GetSchemaIndexesAsync_ShouldReturnIndexes()
    {
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = [TestConstants.DatabaseName];
        var service = BuildService(options);

        var result = await service.GetSchemaIndexesAsync(TestConstants.DatabaseName, "dbo.Customers", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains("PK_Customers", result.Value);
        Assert.Contains("CLUSTERED", result.Value);
    }

    [Fact]
    public async Task GetSchemaConstraintsAsync_ShouldReturnConstraints()
    {
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = [TestConstants.DatabaseName];
        var service = BuildService(options);

        var result = await service.GetSchemaConstraintsAsync(TestConstants.DatabaseName, "dbo.Customers", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains("DF_Customers_Created", result.Value);
        Assert.Contains("DEFAULT", result.Value);
    }

    [Fact]
    public async Task GetTriggerDefinitionAsync_ShouldReturnTriggerDDL()
    {
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = [TestConstants.DatabaseName];
        var service = BuildService(options);

        var result = await service.GetTriggerDefinitionAsync(TestConstants.DatabaseName, "dbo.Customers", "trg_Audit", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains("CREATE PROCEDURE GetCustomers", result.Value);
    }

    [Fact]
    public async Task GetObjectReferencesAsync_ShouldReturnReferences()
    {
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = [TestConstants.DatabaseName];
        var service = BuildService(options);

        var result = await service.GetObjectReferencesAsync(TestConstants.DatabaseName, "dbo.Customers", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains("GetCustomers", result.Value);
        Assert.Contains("OBJECT_OR_COLUMN", result.Value);
    }

    [Fact]
    public async Task GetRoutineParametersAsync_ShouldReturnParameters()
    {
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = [TestConstants.DatabaseName];
        var service = BuildService(options);

        var result = await service.GetRoutineParametersAsync(TestConstants.DatabaseName, "dbo.GetCustomersProc", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains("@CustomerId", result.Value);
        Assert.Contains("int", result.Value);
    }

    [Fact]
    public async Task GetSchemaForeignKeysAsync_ShouldReturnError_WhenObjectIsRoutine()
    {
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = [TestConstants.DatabaseName];
        var service = BuildService(options);

        var result = await service.GetSchemaForeignKeysAsync(TestConstants.DatabaseName, "dbo.GetCustomersProc", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidDetailQueryTypeCode, result.Error.Code);
    }

    [Fact]
    public async Task GetSchemaIndexesAsync_ShouldReturnError_WhenObjectIsRoutine()
    {
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = [TestConstants.DatabaseName];
        var service = BuildService(options);

        var result = await service.GetSchemaIndexesAsync(TestConstants.DatabaseName, "dbo.GetCustomersProc", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidDetailQueryTypeCode, result.Error.Code);
    }

    [Fact]
    public async Task GetSchemaConstraintsAsync_ShouldReturnError_WhenObjectIsRoutine()
    {
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = [TestConstants.DatabaseName];
        var service = BuildService(options);

        var result = await service.GetSchemaConstraintsAsync(TestConstants.DatabaseName, "dbo.GetCustomersProc", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidDetailQueryTypeCode, result.Error.Code);
    }

    [Fact]
    public async Task ExecuteDetailQueryAsync_ShouldPropagateAccessFailure_WithoutOpeningConnection()
    {
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = ["SalesDb"];
        var mockFactory = new DummyConnectionFactory();
        var service = BuildService(options, mockFactory);

        var result = await service.GetSchemaForeignKeysAsync("BlockedDb", "dbo.Orders", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
        Assert.Equal(0, mockFactory.ConnectionCreatedCount);
    }
}
