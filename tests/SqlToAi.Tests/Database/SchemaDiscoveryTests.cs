using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Metadata;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

public sealed class SchemaDiscoveryTests
{
    [Fact]
    public async Task ExportDiscovery_ReturnsEverySupportedKindAboveInteractiveLimit_WithParents()
    {
        var catalog = Enumerable.Range(1, 125)
            .Select(index => new object?[] { index, "dbo", $"Table{index:D3}", "USER_TABLE", null, null, null, null }).ToList();
        catalog.AddRange([
            [201, "sales", "Overview", "VIEW", null, null, null, null],
            [202, "sales", "UpdateOrders", "SQL_STORED_PROCEDURE", null, null, null, null],
            [203, "sales", "Scalar", "SQL_SCALAR_FUNCTION", null, null, null, null],
            [204, "sales", "Inline", "SQL_INLINE_TABLE_VALUED_FUNCTION", null, null, null, null],
            [205, "sales", "Multi", "SQL_TABLE_VALUED_FUNCTION", null, null, null, null],
            [301, "audit", "Changed", "SQL_TRIGGER", 1, "dbo", "Table001", "USER_TABLE"],
            [302, "sales", "Changed", "SQL_TRIGGER", 201, "sales", "Overview", "VIEW"]
        ]);
        var factory = new ReaderConnectionFactory(command =>
        {
            Assert.Contains("o.is_ms_shipped = 0", command.CommandText);
            Assert.Contains("o.type IN ('U', 'V', 'P', 'FN', 'IF', 'TF', 'TR')", command.CommandText);
            Assert.Contains("o.type <> 'TR' OR parent.type IN ('U', 'V')", command.CommandText);
            Assert.Contains("(@TypeFilter IS NULL OR o.type_desc LIKE @TypeFilter)", command.CommandText);
            Assert.Contains("WHEN 'SQL_TRIGGER' THEN 3", command.CommandText);
            Assert.Contains("schema_name(o.schema_id), o.name", command.CommandText);
            bool export = (bool)Parameter(command, "ExportOnly")!;
            if (export)
            {
                Assert.DoesNotContain("TOP", command.CommandText);
                Assert.DoesNotContain(command.Parameters.Cast<DbParameter>(), parameter => parameter.ParameterName == "Limit");
            }
            else
            {
                Assert.Equal(100, Parameter(command, "Limit"));
            }
            return new FakeDbDataReader(
                ["ObjectId", "SchemaName", "ObjectName", "TypeDescription", "ParentObjectId", "ParentSchemaName", "ParentObjectName", "ParentTypeDescription"],
                export ? catalog : catalog.Take(100).ToList());
        });
        var service = BuildService(factory);
        var before = await service.SearchObjectsAsync("DemoDB", "", cancellationToken: TestContext.Current.CancellationToken);
        var result = await service.GetExportObjectsAsync("DemoDB", TestContext.Current.CancellationToken);
        var after = await service.SearchObjectsAsync("DemoDB", "", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(132, result.Value.Count);
        Assert.Equal(Enum.GetValues<SchemaObjectKind>().Order(), result.Value.Select(item => item.Identity.Kind).Distinct().Order());
        Assert.Equal(Enumerable.Range(1, 125), result.Value.Where(item => item.Identity.Kind == SchemaObjectKind.Table).Select(item => item.Identity.ObjectId));
        Assert.Equal(new SchemaObjectIdentity(1, "dbo", "Table001", SchemaObjectKind.Table), result.Value.Single(item => item.Identity.ObjectId == 301).Parent);
        Assert.Equal(new SchemaObjectIdentity(201, "sales", "Overview", SchemaObjectKind.View), result.Value.Single(item => item.Identity.ObjectId == 302).Parent);
        Assert.All(result.Value.Where(item => item.Identity.Kind != SchemaObjectKind.Trigger), item => Assert.Null(item.Parent));
        string expectedSearch = "| Schema | Name | Type |\n| --- | --- | --- |\n"
            + string.Concat(Enumerable.Range(1, 100).Select(index => $"| dbo | Table{index:D3} | USER_TABLE |\n"));
        Assert.Equal(expectedSearch.Replace("\n", Environment.NewLine, StringComparison.Ordinal), before.Value);
        Assert.Equal(before.Value, after.Value);
    }

    [Fact]
    public async Task ExportDiscovery_PreservesDeniedAccessAndQueryErrors()
    {
        var factory = new ReaderConnectionFactory(_ => throw new InvalidOperationException("fixed discovery failure"));
        var deniedService = BuildService(factory, allowed: false);
        var denied = await deniedService.GetExportObjectsAsync("DemoDB", TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.SafetyCheckFailed("Database 'DemoDB' is blocked by security policies (static whitelist)."), denied.Error);
        Assert.Equal(0, factory.ConnectionCount);

        var failed = await BuildService(factory).GetExportObjectsAsync("DemoDB", TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.QueryError("fixed discovery failure"), failed.Error);
    }

    [Theory]
    [InlineData("sales", 301, "U", SchemaObjectKind.Table)]
    [InlineData("audit", 302, "V", SchemaObjectKind.View)]
    public async Task ExportTrigger_ResolvesSameNamedTriggersByValidatedIdentity(string schema, int id, string parentType, SchemaObjectKind parentKind)
    {
        var trigger = new SchemaObject(new SchemaObjectIdentity(id, schema, "Changed", SchemaObjectKind.Trigger),
            new SchemaObjectIdentity(id + 100, schema, "Orders", parentKind));
        string definition = $"CREATE TRIGGER [{schema}].[Changed] ON [{schema}].[Orders] AFTER INSERT AS\n-- original SQL comment\nSELECT {id};";
        var factory = new ReaderConnectionFactory(command =>
        {
            Assert.Equal(id, Parameter(command, "ObjectId"));
            Assert.DoesNotContain("OBJECT_ID", command.CommandText);
            if (command.CommandText.Contains("sys.triggers", StringComparison.Ordinal))
            {
                Assert.Contains("t.parent_class = 1", command.CommandText);
                Assert.Contains("t.parent_id = @ParentObjectId", command.CommandText);
                Assert.Contains("o.schema_id = SCHEMA_ID(@SchemaName)", command.CommandText);
                Assert.Contains("parent.schema_id = SCHEMA_ID(@ParentSchemaName)", command.CommandText);
                Assert.Contains("parent.type = @ParentType", command.CommandText);
                Assert.Equal(schema, Parameter(command, "SchemaName"));
                Assert.Equal("Changed", Parameter(command, "ObjectName"));
                Assert.Equal(id + 100, Parameter(command, "ParentObjectId"));
                Assert.Equal(schema, Parameter(command, "ParentSchemaName"));
                Assert.Equal("Orders", Parameter(command, "ParentObjectName"));
                Assert.Equal(parentType, Parameter(command, "ParentType"));
                return new FakeDbDataReader(["object_id"], [[id]]);
            }
            return new FakeDbDataReader(["definition"], [[definition]]);
        });
        var result = await BuildService(factory).GetExportTriggerDefinitionAsync("DemoDB", trigger, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal($"# Trigger Definition: `{schema}.Changed` (on table `{schema}.Orders`)\n\n```sql\n{definition}\n```", result.Value);
        Assert.Equal(2, factory.CommandCount);
    }

    [Fact]
    public async Task ExportTrigger_RejectsMismatchedParentWithoutRetrievingDefinition()
    {
        var factory = new ReaderConnectionFactory(command =>
        {
            Assert.Contains("sys.triggers", command.CommandText);
            Assert.Equal(999, Parameter(command, "ParentObjectId"));
            return new FakeDbDataReader(["object_id"], []);
        });
        var trigger = new SchemaObject(new SchemaObjectIdentity(301, "sales", "Changed", SchemaObjectKind.Trigger),
            new SchemaObjectIdentity(999, "sales", "WrongParent", SchemaObjectKind.Table));
        var result = await BuildService(factory).GetExportTriggerDefinitionAsync("DemoDB", trigger, TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.ObjectNotFound("sales.Changed"), result.Error);
        Assert.Equal(1, factory.CommandCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task ExportTrigger_UnavailableDefinitionRemainsSuccessful(string? definition)
    {
        var trigger = new SchemaObject(new SchemaObjectIdentity(301, "sales", "Changed", SchemaObjectKind.Trigger),
            new SchemaObjectIdentity(1, "sales", "Orders", SchemaObjectKind.Table));
        var factory = new ReaderConnectionFactory(command => command.CommandText.Contains("sys.triggers", StringComparison.Ordinal)
            ? new FakeDbDataReader(["object_id"], [[301]])
            : new FakeDbDataReader(["definition"], [[definition]]));
        var result = await BuildService(factory).GetExportTriggerDefinitionAsync("DemoDB", trigger, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal("*Definition for trigger `sales.Changed` not available.* *Definition not available — either the object is encrypted, or the configured login lacks VIEW DEFINITION permission on it.*", result.Value);
    }

    [Fact]
    public async Task ExportTrigger_RejectsInvalidIdentityAndPreservesAccessAndQueryFailures()
    {
        var factory = new ReaderConnectionFactory(_ => throw new InvalidOperationException("fixed trigger failure"));
        var trigger = new SchemaObject(new SchemaObjectIdentity(301, "sales", "Changed", SchemaObjectKind.Trigger));
        var service = BuildService(factory);
        var invalid = await service.GetExportTriggerDefinitionAsync("DemoDB", trigger, TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.InvalidParameters("Export trigger identity requires a table or view parent."), invalid.Error);
        Assert.Equal(0, factory.CommandCount);

        trigger = trigger with { Parent = new SchemaObjectIdentity(1, "sales", "Orders", SchemaObjectKind.Table) };
        var denied = await BuildService(factory, allowed: false).GetExportTriggerDefinitionAsync("DemoDB", trigger, TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.SafetyCheckFailed("Database 'DemoDB' is blocked by security policies (static whitelist)."), denied.Error);
        Assert.Equal(0, factory.CommandCount);
        var failed = await service.GetExportTriggerDefinitionAsync("DemoDB", trigger, TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.QueryError("fixed trigger failure"), failed.Error);
    }

    [Fact]
    public void QualifiedIdentity_EscapesSqlDelimitersWithoutChangingDisplayNames()
    {
        var identity = new SchemaObjectIdentity(1, "sales]", "Order.Detail]", SchemaObjectKind.Table);
        Assert.Equal("[sales]]].[Order.Detail]]]", identity.QualifiedName);
        Assert.Equal("sales].Order.Detail]", identity.DisplayName);
    }

    [Fact]
    public async Task ExportTrigger_PropagatesDefinitionQueryFailureAfterParentValidation()
    {
        var trigger = new SchemaObject(new SchemaObjectIdentity(301, "sales", "Changed", SchemaObjectKind.Trigger),
            new SchemaObjectIdentity(1, "sales", "Orders", SchemaObjectKind.Table));
        var factory = new ReaderConnectionFactory(command => command.CommandText.Contains("sys.triggers", StringComparison.Ordinal)
            ? new FakeDbDataReader(["object_id"], [[301]])
            : throw new InvalidOperationException("fixed definition failure"));
        var result = await BuildService(factory).GetExportTriggerDefinitionAsync("DemoDB", trigger, TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.QueryError("fixed definition failure"), result.Error);
        Assert.Equal(2, factory.CommandCount);
    }

    [Fact]
    public async Task TriggerCalls_KeepLegacyResolutionAndExportIdentitiesIsolatedWhenInterleaved()
    {
        var salesTrigger = new SchemaObject(new SchemaObjectIdentity(301, "sales", "Changed", SchemaObjectKind.Trigger),
            new SchemaObjectIdentity(1, "sales", "Orders", SchemaObjectKind.Table));
        var auditTrigger = new SchemaObject(new SchemaObjectIdentity(302, "audit", "Changed", SchemaObjectKind.Trigger),
            new SchemaObjectIdentity(2, "audit", "Orders", SchemaObjectKind.Table));
        var factory = new ReaderConnectionFactory(command =>
        {
            bool hasIdentity = command.Parameters.Cast<DbParameter>().Any(parameter => parameter.ParameterName == "ObjectId");
            if (command.CommandText.Contains("sys.triggers", StringComparison.Ordinal))
            {
                return hasIdentity
                    ? new FakeDbDataReader(["object_id"], [[Parameter(command, "ObjectId")]])
                    : new FakeDbDataReader(["name"], [["Changed"]]);
            }
            return new FakeDbDataReader(["definition"], [[hasIdentity
                ? $"CREATE TRIGGER {(int)Parameter(command, "ObjectId")! switch { 301 => "sales", 302 => "audit", _ => throw new InvalidOperationException() }}.Changed AS SELECT 1;"
                : "CREATE TRIGGER dbo.Changed AS SELECT 0;"]]);
        });
        var service = BuildService(factory);
        var before = await service.GetTriggerDefinitionAsync("DemoDB", "sales.Orders", "Changed", TestContext.Current.CancellationToken);
        var sales = await service.GetExportTriggerDefinitionAsync("DemoDB", salesTrigger, TestContext.Current.CancellationToken);
        var audit = await service.GetExportTriggerDefinitionAsync("DemoDB", auditTrigger, TestContext.Current.CancellationToken);
        var after = await service.GetTriggerDefinitionAsync("DemoDB", "sales.Orders", "Changed", TestContext.Current.CancellationToken);

        Assert.Equal("# Trigger Definition: `Changed` (on table `sales.Orders`)\n\n```sql\nCREATE TRIGGER dbo.Changed AS SELECT 0;\n```", before.Value);
        Assert.Equal("# Trigger Definition: `sales.Changed` (on table `sales.Orders`)\n\n```sql\nCREATE TRIGGER sales.Changed AS SELECT 1;\n```", sales.Value);
        Assert.Equal("# Trigger Definition: `audit.Changed` (on table `audit.Orders`)\n\n```sql\nCREATE TRIGGER audit.Changed AS SELECT 1;\n```", audit.Value);
        Assert.Equal(before.Value, after.Value);
    }

    [Fact]
    public async Task SearchObjects_PreservesCompleteMarkdownAndQueryInputs()
    {
        var factory = new ReaderConnectionFactory(command =>
        {
            Assert.Contains("SELECT TOP (@Limit)", command.CommandText);
            Assert.Equal(7, Parameter(command, "Limit"));
            Assert.Equal("%cust%", Parameter(command, "SearchPattern"));
            Assert.Equal("%TABLE%", Parameter(command, "TypeFilter"));
            return new FakeDbDataReader(["SchemaName", "ObjectName", "TypeDescription"],
                [["sales", "Customers", "USER_TABLE"], ["sales", "CustomerView", "VIEW"]]);
        });
        var result = await BuildService(factory).SearchObjectsAsync("DemoDB", "cust", 7, "%TABLE%", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("| Schema | Name | Type |\n| --- | --- | --- |\n| sales | Customers | USER_TABLE |\n| sales | CustomerView | VIEW |\n".Replace("\n", Environment.NewLine, StringComparison.Ordinal), result.Value);
    }

    [Fact]
    public async Task SearchObjects_PreservesEmptyNote()
    {
        var factory = new ReaderConnectionFactory(_ => new FakeDbDataReader(["SchemaName", "ObjectName", "TypeDescription"], []));
        var result = await BuildService(factory).SearchObjectsAsync("DemoDB", "missing", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal("No objects found matching 'missing' in database 'DemoDB'.", result.Value);
    }

    [Fact]
    public async Task SearchObjects_PreservesQueryFailure()
    {
        var factory = new ReaderConnectionFactory(_ => throw new InvalidOperationException("fixed discovery failure"));
        var result = await BuildService(factory).SearchObjectsAsync("DemoDB", "cust", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.QueryError("fixed discovery failure"), result.Error);
    }

    [Theory]
    [InlineData("CREATE TRIGGER dbo.Audit ON dbo.Customers AFTER INSERT AS SELECT 1;", "# Trigger Definition: `Audit` (on table `sales.Customers`)\n\n```sql\nCREATE TRIGGER dbo.Audit ON dbo.Customers AFTER INSERT AS SELECT 1;\n```")]
    [InlineData(null, "*Definition for trigger 'Audit' not available.* *Definition not available — either the object is encrypted, or the configured login lacks VIEW DEFINITION permission on it.*")]
    [InlineData("  ", "*Definition for trigger 'Audit' not available.* *Definition not available — either the object is encrypted, or the configured login lacks VIEW DEFINITION permission on it.*")]
    public async Task LegacyTrigger_PreservesCompleteDefinitionAndUnavailableNotes(string? definition, string expected)
    {
        var factory = new ReaderConnectionFactory(command =>
        {
            Assert.Equal("Audit", Parameter(command, "TriggerName"));
            if (command.CommandText.Contains("sys.triggers", StringComparison.Ordinal))
            {
                Assert.Equal("sales.Customers", Parameter(command, "TableName"));
                return new FakeDbDataReader(["name"], [["Audit"]]);
            }

            // Preserve the legacy mismatch: even after validating sales.Audit, resolution
            // uses the simple name and can return dbo.Audit's definition.
            Assert.Equal("SELECT definition FROM sys.sql_modules WHERE object_id = OBJECT_ID(@TriggerName)", command.CommandText);
            return new FakeDbDataReader(["definition"], [[definition]]);
        });
        var result = await BuildService(factory).GetTriggerDefinitionAsync("DemoDB", "sales.Customers", "Audit", TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public async Task LegacyTrigger_PreservesNotFoundFailure()
    {
        var factory = new ReaderConnectionFactory(_ => new FakeDbDataReader(["name"], []));
        var result = await BuildService(factory).GetTriggerDefinitionAsync("DemoDB", "sales.Customers", "missing", TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.ObjectNotFound("missing"), result.Error);
        Assert.Equal(1, factory.CommandCount);
    }

    [Fact]
    public async Task LegacyTrigger_PreservesQueryFailure()
    {
        var factory = new ReaderConnectionFactory(_ => throw new InvalidOperationException("fixed trigger failure"));
        var result = await BuildService(factory).GetTriggerDefinitionAsync("DemoDB", "sales.Customers", "Audit", TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.QueryError("fixed trigger failure"), result.Error);
    }

    [Fact]
    public async Task LegacyOperations_PreserveDeniedAccessFailures()
    {
        var factory = new ReaderConnectionFactory(_ => throw new InvalidOperationException("Must not query"));
        var service = BuildService(factory, allowed: false);
        var search = await service.SearchObjectsAsync("DemoDB", "cust", cancellationToken: TestContext.Current.CancellationToken);
        var trigger = await service.GetTriggerDefinitionAsync("DemoDB", "sales.Customers", "Audit", TestContext.Current.CancellationToken);
        var expected = SqlToAiError.SafetyCheckFailed("Database 'DemoDB' is blocked by security policies (static whitelist).");
        Assert.Equal(expected, search.Error);
        Assert.Equal(expected, trigger.Error);
        Assert.Equal(0, factory.ConnectionCount);
    }

    private static SchemaService BuildService(ReaderConnectionFactory factory, bool allowed = true)
    {
        var options = new SqlToAiOptions();
        if (allowed) options.Databases.SchemaOnly = ["DemoDB"];
        var boundOptions = Options.Create(options);
        return new SchemaService(factory, new SecurityGuard(boundOptions),
            new AccessLevelProvider(factory, boundOptions, NullLogger<AccessLevelProvider>.Instance),
            new MetadataProvider(factory, boundOptions, NullLogger<MetadataProvider>.Instance),
            new AlwaysAllowPolicyResolver(), boundOptions, NullLogger<SchemaService>.Instance);
    }

    private static object? Parameter(FakeDbCommand command, string name) =>
        command.Parameters.Cast<DbParameter>().Single(parameter => parameter.ParameterName == name).Value;

    private sealed class ReaderConnectionFactory(Func<FakeDbCommand, DbDataReader> reader) : IDatabaseConnectionFactory
    {
        public int ConnectionCount { get; private set; }
        public int CommandCount { get; private set; }

        public DbConnection CreateConnection(string? databaseName = null)
        {
            ConnectionCount++;
            return new FakeDbConnection(connection => new FakeDbCommand(connection,
                new FakeDbCommandHandlers(ExecuteReader: command =>
                {
                    CommandCount++;
                    return reader(command);
                })));
        }
    }
}
