using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Database;

public sealed class OfflineSchemaPresentationTests
{
    private static readonly SchemaObjectIdentity Customers = new(101, "sales", "Customers", SchemaObjectKind.Table);
    private static readonly SchemaObjectIdentity Orders = new(102, "sales", "Orders", SchemaObjectKind.Table);
    private static readonly SchemaObjectIdentity Overview = new(201, "sales", "Overview", SchemaObjectKind.View);
    private static readonly SchemaObjectIdentity Trigger = new(301, "audit", "Changed", SchemaObjectKind.Trigger);

    [Theory]
    [InlineData("U")]
    [InlineData("V")]
    public async Task OfflineTableAndView_SkipAllEnrichmentAndRenderStructuredTriggerLinks(string type)
    {
        var fixture = new SchemaPresentationFixture { ObjectType = type, RejectEnrichment = true };
        var context = Context(Customers);
        var result = await fixture.CreateService().GetExportSchemaAsync("DemoDB", context, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        string expected = SchemaPresentationRegressionTests.Lines("""
            # Schema for Table/View: `sales.Customers`
            | Column Name | Type | Nullable | Key/Identity |
            | --- | --- | --- | --- |
            | Id | int | No | PK, Identity |
            | Email | nvarchar(40) | Yes |  |

            ## Triggers
            | Trigger Name | Insert | Update | Delete | Status |
            | --- | --- | --- | --- | --- |
            | [Changed](../triggers/audit.Changed%20%23%5B1%5D.md) | ✓ | ✓ |  | Disabled |

            """);
        if (type == "V") expected += SchemaPresentationRegressionTests.Lines("## View Definition\n```sql") + fixture.Definition + Environment.NewLine + SchemaPresentationRegressionTests.Lines("```");
        Assert.Equal(expected, result.Value);
        Assert.Equal(0, fixture.MetadataCalls);
        Assert.Equal(0, fixture.PolicyCalls);
        Assert.Equal(0, fixture.RuleCalls);
        Assert.DoesNotContain("Description", result.Value);
        Assert.DoesNotContain("Anonymized", result.Value);
        Assert.DoesNotContain("Discovery Index", result.Value);
    }

    [Theory]
    [InlineData("P", SchemaObjectKind.Procedure)]
    [InlineData("FN", SchemaObjectKind.ScalarFunction)]
    [InlineData("IF", SchemaObjectKind.InlineTableValuedFunction)]
    [InlineData("TF", SchemaObjectKind.TableValuedFunction)]
    public async Task OfflineRoutine_RetainsOriginalSqlWithoutGeneratedToolInstructions(string type, SchemaObjectKind kind)
    {
        var source = new SchemaObjectIdentity(501, "sales", "Routine", kind);
        var fixture = new SchemaPresentationFixture { ObjectType = type, RejectEnrichment = true };
        var result = await fixture.CreateService().GetExportSchemaAsync("DemoDB", Context(source), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(SchemaPresentationRegressionTests.Lines("# DDL Definition for Stored Procedure/Function: `sales.Routine`\n\n*Parameters:* This routine accepts `2` parameter(s).\n\n```sql")
            + fixture.Definition + Environment.NewLine + SchemaPresentationRegressionTests.Lines("```"), result.Value);
        Assert.Contains("-- SQL comment: run sql_get_schema\nCREATE VIEW", result.Value);
        Assert.DoesNotContain("Run `sql_get_routine_parameters`", result.Value);
        Assert.Equal(0, fixture.MetadataCalls + fixture.PolicyCalls + fixture.RuleCalls);
    }

    [Theory]
    [InlineData("V", null)]
    [InlineData("V", "  ")]
    [InlineData("P", null)]
    [InlineData("P", "  ")]
    public async Task OfflineDefinitions_RetainSuccessfulUnavailableNotes(string type, string? definition)
    {
        var fixture = new SchemaPresentationFixture { ObjectType = type, Definition = definition, RejectEnrichment = true };
        var result = await fixture.CreateService().GetExportSchemaAsync("DemoDB", Context(Customers), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.EndsWith(SchemaPresentationRegressionTests.Lines(DetailSchemaRenderer.DdlUnavailableNote), result.Value);
        Assert.DoesNotContain("sql_get_", result.Value);
    }

    [Fact]
    public async Task OfflineRelationships_LinkStructuredTargetsAndRetainMissingTargetsAsText()
    {
        var fixture = new SchemaPresentationFixture { RejectEnrichment = true };
        var service = fixture.CreateService();
        var context = Context(Customers);
        var fk = await service.GetExportSchemaForeignKeysAsync("DemoDB", context, TestContext.Current.CancellationToken);
        Assert.Equal("# Foreign Keys for `sales.Customers`\n\n" + SchemaPresentationRegressionTests.Lines("| FK Name | Source Column | Dir | Reference Column |\n| --- | --- | --- | --- |\n| FK_Customer | [sales.Orders.CustomerId](sales.Orders.md) | → | [sales.Customers.Id](sales.Customers.md) |"), fk.Value);
        var refs = await service.GetExportObjectReferencesAsync("DemoDB", context, TestContext.Current.CancellationToken);
        Assert.Equal("# Referencing Entities for `sales.Customers`\n\n" + SchemaPresentationRegressionTests.Lines("| Schema | Entity Name | Type |\n| --- | --- | --- |\n| sales | [Overview](../views/sales.Overview.md) | OBJECT_OR_COLUMN |"), refs.Value);
        var missing = new SchemaRenderingContext(Customers, new Dictionary<SchemaObjectIdentity, string> { [Customers] = "tables/sales.Customers.md" });
        var unlinked = await service.GetExportObjectReferencesAsync("DemoDB", missing, TestContext.Current.CancellationToken);
        Assert.Contains("| sales | Overview | OBJECT_OR_COLUMN |", unlinked.Value);
        Assert.Equal(0, fixture.MetadataCalls + fixture.PolicyCalls + fixture.RuleCalls);
    }

    [Fact]
    public async Task OfflineForeignKeyLink_UsesActualReferencedSchemaWithoutChangingMcpLabel()
    {
        var target = new SchemaObjectIdentity(701, "archive", "Customers.v2", SchemaObjectKind.Table);
        var fixture = new SchemaPresentationFixture { ReferenceSchema = target.SchemaName, ReferenceName = target.ObjectName };
        var service = fixture.CreateService();
        var context = Context(Customers, target);
        var offline = await service.GetExportSchemaForeignKeysAsync("DemoDB", context, TestContext.Current.CancellationToken);
        Assert.Contains("[archive.Customers.v2.Id](sales.Extra.md)", offline.Value);
        var mcp = await service.GetSchemaForeignKeysAsync("DemoDB", "sales.Customers", TestContext.Current.CancellationToken);
        Assert.Contains("| sales.Customers.Id |", mcp.Value);
    }

    [Fact]
    public async Task InterleavedOfflineCalls_LeaveFullMcpContentNotesAndErrorsUnchanged()
    {
        var fixture = new SchemaPresentationFixture();
        var service = fixture.CreateService();
        foreach (string type in new[] { "U", "V", "P", "FN", "IF", "TF" })
        {
            fixture.ObjectType = type;
            var before = await service.GetSchemaAsync("DemoDB", "sales.Customers", TestContext.Current.CancellationToken);
            int metadata = fixture.MetadataCalls, policy = fixture.PolicyCalls, rules = fixture.RuleCalls;
            fixture.RejectEnrichment = true;
            var offline = await service.GetExportSchemaAsync("DemoDB", Context(Customers), TestContext.Current.CancellationToken);
            Assert.True(offline.IsSuccess);
            Assert.Equal(metadata, fixture.MetadataCalls);
            Assert.Equal(policy, fixture.PolicyCalls);
            Assert.Equal(rules, fixture.RuleCalls);
            fixture.RejectEnrichment = false;
            var after = await service.GetSchemaAsync("DemoDB", "sales.Customers", TestContext.Current.CancellationToken);
            Assert.Equal(before.Value, after.Value);
        }
        fixture.Definition = null;
        fixture.ObjectType = "P";
        var noteBefore = await service.GetSchemaAsync("DemoDB", "sales.Customers", TestContext.Current.CancellationToken);
        await service.GetExportSchemaAsync("DemoDB", Context(Customers), TestContext.Current.CancellationToken);
        var noteAfter = await service.GetSchemaAsync("DemoDB", "sales.Customers", TestContext.Current.CancellationToken);
        Assert.Equal(noteBefore.Value, noteAfter.Value);
        fixture.FailQuery = true;
        var errorBefore = await service.GetSchemaAsync("DemoDB", "sales.Customers", TestContext.Current.CancellationToken);
        var offlineError = await service.GetExportSchemaAsync("DemoDB", Context(Customers), TestContext.Current.CancellationToken);
        var errorAfter = await service.GetSchemaAsync("DemoDB", "sales.Customers", TestContext.Current.CancellationToken);
        Assert.Equal(errorBefore.Error, offlineError.Error);
        Assert.Equal(errorBefore.Error, errorAfter.Error);
    }

    [Fact]
    public async Task InterleavedOfflineNavigation_LeavesFullMcpTablesUnchanged()
    {
        var service = new SchemaPresentationFixture().CreateService();
        var fkBefore = await service.GetSchemaForeignKeysAsync("DemoDB", "sales.Customers", TestContext.Current.CancellationToken);
        var refsBefore = await service.GetObjectReferencesAsync("DemoDB", "sales.Customers", TestContext.Current.CancellationToken);
        await service.GetExportSchemaForeignKeysAsync("DemoDB", Context(Customers), TestContext.Current.CancellationToken);
        await service.GetExportObjectReferencesAsync("DemoDB", Context(Customers), TestContext.Current.CancellationToken);
        var fkAfter = await service.GetSchemaForeignKeysAsync("DemoDB", "sales.Customers", TestContext.Current.CancellationToken);
        var refsAfter = await service.GetObjectReferencesAsync("DemoDB", "sales.Customers", TestContext.Current.CancellationToken);
        Assert.Equal(fkBefore.Value, fkAfter.Value);
        Assert.Equal(refsBefore.Value, refsAfter.Value);
    }

    [Fact]
    public void TriggerNavigation_UsesIdentityForSameNamedTriggersInDifferentSchemas()
    {
        var other = new SchemaObjectIdentity(302, "sales", "Changed", SchemaObjectKind.Trigger);
        var paths = new Dictionary<SchemaObjectIdentity, string>
        {
            [Customers] = "tables/sales.Customers.md", [Trigger] = "triggers/audit.Changed.md", [other] = "triggers/sales.Changed.md"
        };
        var context = new SchemaRenderingContext(Customers, paths);
        Assert.Equal("[Changed](../triggers/audit.Changed.md)", context.TriggerLink(Trigger.ObjectId, "Changed"));
        Assert.Equal("[Changed](../triggers/sales.Changed.md)", context.TriggerLink(other.ObjectId, "Changed"));
    }

    [Theory]
    [InlineData("is_identity")]
    [InlineData("sys.triggers")]
    [InlineData("sys.sql_modules")]
    [InlineData("sys.parameters")]
    public async Task OfflineQueryFailures_AbortWithoutEnrichment(string query)
    {
        var fixture = new SchemaPresentationFixture { FailQueryTerm = query, RejectEnrichment = true, ObjectType = query == "sys.parameters" ? "P" : "V" };
        var result = await fixture.CreateService().GetExportSchemaAsync("DemoDB", Context(Customers), TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.QueryError("fixed schema failure"), result.Error);
        Assert.Equal(0, fixture.MetadataCalls + fixture.PolicyCalls + fixture.RuleCalls);
    }

    [Fact]
    public async Task OfflineServiceEntries_PreserveAccessTypeAndQueryFailures()
    {
        var fixture = new SchemaPresentationFixture { RejectEnrichment = true };
        var deniedService = fixture.CreateService(false);
        var context = Context(Customers);
        var deniedSchema = await deniedService.GetExportSchemaAsync("DemoDB", context, TestContext.Current.CancellationToken);
        var deniedFk = await deniedService.GetExportSchemaForeignKeysAsync("DemoDB", context, TestContext.Current.CancellationToken);
        var deniedRefs = await deniedService.GetExportObjectReferencesAsync("DemoDB", context, TestContext.Current.CancellationToken);
        var error = SqlToAiError.SafetyCheckFailed("Database 'DemoDB' is blocked by security policies (static whitelist).");
        Assert.Equal(error, deniedSchema.Error);
        Assert.Equal(error, deniedFk.Error);
        Assert.Equal(error, deniedRefs.Error);
        Assert.Equal(0, fixture.Connections);
        var service = fixture.CreateService();
        fixture.ObjectType = "P";
        var badFk = await service.GetExportSchemaForeignKeysAsync("DemoDB", context, TestContext.Current.CancellationToken);
        var badRefs = await service.GetExportObjectReferencesAsync("DemoDB", context, TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.InvalidDetailQueryType(Customers.QualifiedName), badFk.Error);
        Assert.Equal(SqlToAiError.InvalidReferenceType(Customers.QualifiedName), badRefs.Error);
        fixture.FailQueryTerm = "sys.foreign_keys";
        fixture.ObjectType = "U";
        var fkFailure = await service.GetExportSchemaForeignKeysAsync("DemoDB", context, TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.QueryError("fixed schema failure"), fkFailure.Error);
        fixture.FailQueryTerm = "sys.dm_sql_referencing_entities";
        var refsFailure = await service.GetExportObjectReferencesAsync("DemoDB", context, TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.QueryError("fixed schema failure"), refsFailure.Error);
    }

    [Fact]
    public void RenderingContext_CopiesMappingAndUsesEscapedRelativeLinks()
    {
        var paths = new Dictionary<SchemaObjectIdentity, string> { [Customers] = "tables/sales.Customers.md", [Trigger] = "triggers/audit.Changed #1.md" };
        var context = new SchemaRenderingContext(Customers, paths);
        paths[Trigger] = "wrong.md";
        Assert.Equal("[Changed \\[x\\]](../triggers/audit.Changed%20%231.md)", context.TriggerLink(301, "Changed [x]"));
        Assert.Equal("[sales.Customers](tables/sales.Customers.md)", SchemaRenderingContext.RelativeLink("README.md", "tables/sales.Customers.md", Customers.DisplayName));
        Assert.Equal("Unknown", context.TriggerLink(999, "Unknown"));
        Assert.Throws<ArgumentException>(() => new SchemaRenderingContext(Overview, paths));
    }

    private static SchemaRenderingContext Context(SchemaObjectIdentity source, SchemaObjectIdentity? extra = null)
    {
        var paths = new Dictionary<SchemaObjectIdentity, string>
        {
            [Customers] = "tables/sales.Customers.md", [Orders] = "tables/sales.Orders.md",
            [Overview] = "views/sales.Overview.md", [Trigger] = "triggers/audit.Changed #[1].md"
        };
        if (!paths.ContainsKey(source)) paths[source] = "functions/sales.Routine.md";
        if (extra is not null) paths[extra] = "tables/sales.Extra.md";
        return new SchemaRenderingContext(source, paths);
    }
}
