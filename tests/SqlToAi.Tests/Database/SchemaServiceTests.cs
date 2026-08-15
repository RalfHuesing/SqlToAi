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
        options.Databases.ReadWrite = [TestConstants.DatabaseName, "SalesDb"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);
        var policyResolver = new AlwaysAllowPolicyResolver();

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, policyResolver, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.ListDatabasesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Contains(TestConstants.DatabaseName, result.Value);
        Assert.Contains("SalesDb", result.Value);
    }

    [Fact]
    public async Task SearchObjectsAsync_ShouldReturnSecurityError_WhenDbBlocked()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = ["SalesDb"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);
        var policyResolver = new AlwaysAllowPolicyResolver();

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, policyResolver, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.SearchObjectsAsync("BlockedDb", "cust", null, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    [Fact]
    public async Task SearchObjectsAsync_ShouldReturnMarkdownTable_WhenSucceeds()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = [TestConstants.DatabaseName];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);
        var policyResolver = new AlwaysAllowPolicyResolver();

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, policyResolver, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.SearchObjectsAsync(TestConstants.DatabaseName, "cust", null, null, TestContext.Current.CancellationToken);

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
        options.Databases.ReadWrite = [TestConstants.DatabaseName];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);
        var policyResolver = new AlwaysAllowPolicyResolver();

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, policyResolver, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetSchemaAsync(TestConstants.DatabaseName, "dbo.Customers", TestContext.Current.CancellationToken);

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
        options.Databases.ReadWrite = [TestConstants.DatabaseName];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);
        var policyResolver = new AlwaysAllowPolicyResolver();

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, policyResolver, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetSchemaAsync(TestConstants.DatabaseName, "dbo.GetCustomersProc", TestContext.Current.CancellationToken);

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
        options.Databases.ReadWrite = [TestConstants.DatabaseName];

        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);
        var policyResolver = new AlwaysAllowPolicyResolver();

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, policyResolver, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetSchemaAsync(TestConstants.DatabaseName, "dbo.CustomersView", TestContext.Current.CancellationToken);

        // Assert — views get both the column list (like tables) and their SQL body (like routines).
        Assert.True(result.IsSuccess);
        Assert.Contains("# Schema for Table/View: `dbo.CustomersView`", result.Value);
        Assert.Contains("CustomerId", result.Value);
        Assert.Contains("## View Definition", result.Value);
        Assert.Contains("CREATE PROCEDURE GetCustomers", result.Value);
    }



    [Fact]
    public async Task SearchObjectsAsync_WithNullObjectType_ShouldReturnAllMatchingObjects()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = [TestConstants.DatabaseName];

        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);
        var policyResolver = new AlwaysAllowPolicyResolver();

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, policyResolver, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act — no objectType filter; all types should be returned
        var result = await service.SearchObjectsAsync(TestConstants.DatabaseName, "cust", null, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("Customers", result.Value);
    }

    [Fact]
    public async Task SearchObjectsAsync_WithObjectTypeFilter_ShouldPassFilterToQuery()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = [TestConstants.DatabaseName];

        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);
        var policyResolver = new AlwaysAllowPolicyResolver();

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, policyResolver, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act — pass "USER_TABLE" as objectType; mock DB returns Customers (USER_TABLE),
        // so we verify the call succeeds (SQL filter correctness is tested by the mock SQL).
        var result = await service.SearchObjectsAsync(TestConstants.DatabaseName, "cust", null, "USER_TABLE", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("Customers", result.Value);
    }



    [Fact]
    public async Task GetSchemaAsync_WithMoreThan200Columns_ShouldRenderAllColumns()
    {
        // Arrange — verifies there is no hidden column-count limit anywhere in the
        // rendering pipeline; sys.columns itself has no TOP/limit in its query.
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = [TestConstants.DatabaseName];

        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);
        var policyResolver = new AlwaysAllowPolicyResolver();

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, policyResolver, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act — the mock returns 250 columns for any table named "WideTable"
        var result = await service.GetSchemaAsync(TestConstants.DatabaseName, "dbo.WideTable", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("Column1", result.Value);
        Assert.Contains("Column250", result.Value);
    }

}
