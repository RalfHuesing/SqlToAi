using System.Data.Common;
using System.Text.Json;
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
/// Milestone 3.1 acceptance tests: verifies database target qualification, security policy pre-checks,
/// and cross-server same-database isolation for <see cref="SchemaService"/>.
/// </summary>
public sealed class SchemaServiceTargetIsolationTests
{
    private sealed class TrackingConnectionFactory : IDatabaseConnectionFactory
    {
        public List<DatabaseTargetContext> OpenedTargets { get; } = [];
        public List<string> ExecutedSql { get; } = [];
        public int ConnectionCount { get; private set; }

        public DbConnection CreateTargetConnection(DatabaseTargetContext target)
        {
            ConnectionCount++;
            OpenedTargets.Add(target);
            return new FakeDbConnection(conn => new FakeDbCommand(conn, new FakeDbCommandHandlers(
                ExecuteReader: cmd => HandleReader(cmd),
                ExecuteScalar: cmd => HandleScalar(cmd))));
        }

        public Result<DbConnection> CreateCatalogConnection(string? serverAlias) => throw new NotSupportedException();

        public DbConnection CreateConnection(string? databaseName = null) => throw new NotSupportedException();

        private FakeDbDataReader HandleReader(FakeDbCommand cmd)
        {
            ExecutedSql.Add(cmd.CommandText);
            if (cmd.CommandText.Contains("o.type_desc AS TypeDescription", StringComparison.Ordinal))
            {
                return new FakeDbDataReader(
                    ["ObjectId", "SchemaName", "ObjectName", "TypeDescription", "ParentObjectId", "ParentSchemaName", "ParentObjectName", "ParentTypeDescription"],
                    [[1, "dbo", "Customers", "USER_TABLE", null, null, null, null]]);
            }
            if (cmd.CommandText.Contains("FROM sys.columns c", StringComparison.Ordinal))
            {
                return new FakeDbDataReader(
                    ["ColumnName", "DataType", "IsNullable", "DefaultDefinition", "IsIdentity", "IsPrimaryKey"],
                    [["Id", "int", false, null, true, true], ["Name", "nvarchar(100)", true, null, false, false]]);
            }
            if (cmd.CommandText.Contains("sys.foreign_keys", StringComparison.Ordinal))
            {
                return new FakeDbDataReader(
                    ["ForeignKeyName", "ParentTable", "ParentColumn", "ReferencedTable", "ReferencedColumn"],
                    []);
            }
            if (cmd.CommandText.Contains("sys.indexes", StringComparison.Ordinal))
            {
                return new FakeDbDataReader(
                    ["IndexName", "IndexType", "IsUnique", "IsPrimaryKey", "Columns", "FilterDefinition"],
                    []);
            }
            if (cmd.CommandText.Contains("sys.check_constraints", StringComparison.Ordinal))
            {
                return new FakeDbDataReader(
                    ["ConstraintName", "ConstraintType", "Definition"],
                    []);
            }
            if (cmd.CommandText.Contains("definition FROM sys.sql_modules", StringComparison.Ordinal))
            {
                return new FakeDbDataReader(
                    ["definition"],
                    [["CREATE TRIGGER trg ON dbo.Customers AFTER INSERT AS BEGIN END"]]);
            }
            if (cmd.CommandText.Contains("SELECT RTRIM(type) FROM sys.objects", StringComparison.Ordinal))
            {
                return new FakeDbDataReader(["type"], [["U"]]);
            }
            if (cmd.CommandText.Contains("sys.objects", StringComparison.Ordinal) && cmd.CommandText.Contains("name LIKE @Pattern", StringComparison.Ordinal))
            {
                return new FakeDbDataReader(
                    ["SchemaName", "ObjectName", "TypeDescription"],
                    [["dbo", "Customers", "USER_TABLE"]]);
            }
            return new FakeDbDataReader(["CountValue"], [[0]]);
        }

        private object? HandleScalar(FakeDbCommand cmd)
        {
            ExecutedSql.Add(cmd.CommandText);
            if (cmd.CommandText.Contains("SELECT RTRIM(type) FROM sys.objects", StringComparison.Ordinal))
            {
                return "U";
            }
            return 1;
        }
    }

    private static (SchemaService Service, TrackingConnectionFactory Factory) CreateService(Action<SqlToAiOptions>? configure = null)
    {
        var options = new SqlToAiOptions()
            .WithNamedServers("srv1", "srv2");
        options.Databases.SchemaOnly = ["srv1/SharedDb", "srv2/SharedDb", "srv1/WhitelistedDb"];
        configure?.Invoke(options);

        var bound = Options.Create(options);
        var factory = new TrackingConnectionFactory();
        var guard = new SecurityGuard(bound);
        var accessProvider = new AccessLevelProvider(factory, bound, NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(factory, bound, NullLogger<MetadataProvider>.Instance);
        var policy = new AlwaysAllowPolicyResolver();
        var service = new SchemaService(factory, guard, accessProvider, metadataProvider, policy, bound, NullLogger<SchemaService>.Instance);
        return (service, factory);
    }

    [Theory]
    [InlineData("SharedDb")]
    [InlineData("dbo.Customers")]
    [InlineData("unknown_alias/SharedDb")]
    [InlineData("srv1//SharedDb")]
    [InlineData("   ")]
    public async Task SchemaService_RejectsUnqualifiedOrUnknownTargetBeforeConnecting(string invalidTarget)
    {
        var (service, factory) = CreateService();

        var result = await service.GetSchemaAsync(invalidTarget, "dbo.Customers", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(0, factory.ConnectionCount);
        Assert.Empty(factory.OpenedTargets);
    }

    [Fact]
    public async Task SchemaService_RejectsTargetBlockedByStaticWhitelistBeforeConnecting()
    {
        var (service, factory) = CreateService();

        var result = await service.GetSchemaAsync("srv1/BlockedDb", "dbo.Customers", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
        Assert.Contains("blocked by security policies", result.Error.Message);
        Assert.Equal(0, factory.ConnectionCount);
    }

    [Fact]
    public async Task SchemaService_SameNamedDatabaseOnDifferentServers_RoutesToCorrectContext()
    {
        var (service, factory) = CreateService();

        var srv1Result = await service.GetSchemaAsync("srv1/SharedDb", "dbo.Customers", TestContext.Current.CancellationToken);
        var srv2Result = await service.GetSchemaAsync("srv2/SharedDb", "dbo.Customers", TestContext.Current.CancellationToken);

        Assert.True(srv1Result.IsSuccess);
        Assert.True(srv2Result.IsSuccess);
        Assert.Equal(6, factory.ConnectionCount);
        Assert.Equal(6, factory.OpenedTargets.Count);

        Assert.All(factory.OpenedTargets.Take(3), target => Assert.Equal(new DatabaseTargetContext("srv1", "SharedDb"), target));
        Assert.All(factory.OpenedTargets.Skip(3), target => Assert.Equal(new DatabaseTargetContext("srv2", "SharedDb"), target));
    }

    [Fact]
    public async Task SchemaService_DetailQueries_RouteWithContextAndRejectUnqualified()
    {
        var (service, factory) = CreateService();

        // 1. Unqualified target rejection
        var unqFk = await service.GetSchemaForeignKeysAsync("SharedDb", "dbo.Customers", TestContext.Current.CancellationToken);
        var unqIdx = await service.GetSchemaIndexesAsync("SharedDb", "dbo.Customers", TestContext.Current.CancellationToken);
        var unqChk = await service.GetSchemaConstraintsAsync("SharedDb", "dbo.Customers", TestContext.Current.CancellationToken);
        var unqTrg = await service.GetTriggerDefinitionAsync("SharedDb", "dbo.Customers", "trg", TestContext.Current.CancellationToken);
        var unqSearch = await service.SearchObjectsAsync("SharedDb", "Cust", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(unqFk.IsFailure);
        Assert.True(unqIdx.IsFailure);
        Assert.True(unqChk.IsFailure);
        Assert.True(unqTrg.IsFailure);
        Assert.True(unqSearch.IsFailure);
        Assert.Equal(0, factory.ConnectionCount);

        // 2. Qualified target routing
        var qFk = await service.GetSchemaForeignKeysAsync("srv2/SharedDb", "dbo.Customers", TestContext.Current.CancellationToken);
        var qIdx = await service.GetSchemaIndexesAsync("srv2/SharedDb", "dbo.Customers", TestContext.Current.CancellationToken);
        var qChk = await service.GetSchemaConstraintsAsync("srv2/SharedDb", "dbo.Customers", TestContext.Current.CancellationToken);
        var qTrg = await service.GetTriggerDefinitionAsync("srv2/SharedDb", "dbo.Customers", "trg", TestContext.Current.CancellationToken);
        var qSearch = await service.SearchObjectsAsync("srv2/SharedDb", "Cust", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(qFk.IsSuccess);
        Assert.True(qIdx.IsSuccess);
        Assert.True(qChk.IsSuccess);
        Assert.True(qTrg.IsSuccess);
        Assert.True(qSearch.IsSuccess);

        Assert.All(factory.OpenedTargets, target => Assert.Equal(new DatabaseTargetContext("srv2", "SharedDb"), target));
    }

    [Fact]
    public async Task SchemaService_BatchSchema_RoutesWithContextAndUsesRawIdentifiersInSql()
    {
        var (service, factory) = CreateService();

        var batchResult = await service.GetSchemaBatchAsync(
            "srv1/SharedDb",
            JsonSerializer.Deserialize<JsonElement>("\"dbo.Customers\""),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(batchResult.IsSuccess);
        Assert.NotEmpty(factory.OpenedTargets);
        Assert.All(factory.OpenedTargets, target => Assert.Equal(new DatabaseTargetContext("srv1", "SharedDb"), target));

        // DDL / SQL commands in the factory should contain raw names, not qualified server aliases
        Assert.All(factory.ExecutedSql, sql =>
        {
            Assert.DoesNotContain("srv1/SharedDb", sql);
            Assert.DoesNotContain("srv1", sql);
        });
    }
}
