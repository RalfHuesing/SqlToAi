#nullable enable

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Metadata;
using SqlToAi.Security;
using Dapper;

namespace SqlToAi.Tests.Database;

#pragma warning disable CS8765

// @covers SqlToAi.Database.SchemaService
public sealed class SchemaServiceTests
{
    private static readonly Type TargetType = typeof(SchemaService);

    [Fact]
    public async Task ListDatabasesAsync_ShouldReturnAllowedDatabases_WhenQuerySucceeds()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.ListDatabasesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Contains("DemoDb", result.Value);
        Assert.Contains("SalesDb", result.Value);
    }

    [Fact]
    public async Task SearchObjectsAsync_ShouldReturnSecurityError_WhenDbBlocked()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["SalesDb"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.SearchObjectsAsync("BlockedDb", "cust", null, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    [Fact]
    public async Task SearchObjectsAsync_ShouldReturnMarkdownTable_WhenSucceeds()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.SearchObjectsAsync("DemoDb", "cust", null, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("Customers", result.Value);
        Assert.Contains("USER_TABLE", result.Value);
    }

    [Fact]
    public async Task GetSchemaAsync_ShouldReturnTableSchema_WhenObjectIsTable()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetSchemaAsync("DemoDb", "dbo.Customers", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("# Schema for Table/View: `dbo.Customers`", result.Value);
        Assert.Contains("CustomerId", result.Value);
        Assert.Contains("trg_Audit", result.Value);
        Assert.Contains("Discovery Index", result.Value);
    }

    [Fact]
    public async Task GetSchemaAsync_ShouldReturnRoutineSchema_WhenObjectIsProcedure()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetSchemaAsync("DemoDb", "dbo.GetCustomersProc", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("# DDL Definition for Stored Procedure/Function: `dbo.GetCustomersProc`", result.Value);
        Assert.Contains("CREATE PROCEDURE GetCustomers", result.Value);
    }

    [Fact]
    public async Task GetSchemaAsync_ShouldIncludeViewDefinition_WhenObjectIsView()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];

        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetSchemaAsync("DemoDb", "dbo.CustomersView", TestContext.Current.CancellationToken);

        // Assert — views get both the column list (like tables) and their SQL body (like routines).
        Assert.True(result.IsSuccess);
        Assert.Contains("# Schema for Table/View: `dbo.CustomersView`", result.Value);
        Assert.Contains("CustomerId", result.Value);
        Assert.Contains("## View Definition", result.Value);
        Assert.Contains("CREATE PROCEDURE GetCustomers", result.Value);
    }

    [Fact]
    public async Task GetSchemaForeignKeysAsync_ShouldReturnKeys()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetSchemaForeignKeysAsync("DemoDb", "dbo.Orders", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("FK_Orders_Customers", result.Value);
        Assert.Contains("dbo.Orders.CustomerId", result.Value);
    }

    [Fact]
    public async Task GetSchemaIndexesAsync_ShouldReturnIndexes()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetSchemaIndexesAsync("DemoDb", "dbo.Customers", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("PK_Customers", result.Value);
        Assert.Contains("CLUSTERED", result.Value);
    }

    [Fact]
    public async Task GetSchemaConstraintsAsync_ShouldReturnConstraints()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetSchemaConstraintsAsync("DemoDb", "dbo.Customers", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("DF_Customers_Created", result.Value);
        Assert.Contains("DEFAULT", result.Value);
    }

    [Fact]
    public async Task GetTriggerDefinitionAsync_ShouldReturnTriggerDDL()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetTriggerDefinitionAsync("DemoDb", "dbo.Customers", "trg_Audit", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("CREATE PROCEDURE GetCustomers", result.Value);
    }

    [Fact]
    public async Task GetObjectReferencesAsync_ShouldReturnReferences()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetObjectReferencesAsync("DemoDb", "dbo.Customers", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("GetCustomers", result.Value);
        Assert.Contains("OBJECT_OR_COLUMN", result.Value);
    }

    [Fact]
    public async Task GetRoutineParametersAsync_ShouldReturnParameters()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetRoutineParametersAsync("DemoDb", "dbo.GetCustomersProc", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("@CustomerId", result.Value);
        Assert.Contains("int", result.Value);
    }

}
