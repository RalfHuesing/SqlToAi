using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Database;

public sealed class SchemaPresentationRegressionTests
{
    internal const string TableOutput = """
        # Schema for Table/View: `sales.Customers`
        *Description:* Fixed table description

        | Column Name | Type | Nullable | Key/Identity | Anonymized | Description |
        | --- | --- | --- | --- | --- | --- |
        | Id | int | No | PK, Identity | No |  |
        | Email | nvarchar(40) | Yes |  | Yes | Fixed column description |

        ## Triggers
        | Trigger Name | Insert | Update | Delete | Status |
        | --- | --- | --- | --- | --- |
        | Changed | ✓ | ✓ |  | Disabled |

        ## Discovery Index
        - **Foreign Keys:** 2 (run `sql_get_schema_foreign_keys` to view details)
        - **Indexes:** 2 (run `sql_get_schema_indexes` to view details)
        - **Constraints:** 2 (run `sql_get_schema_constraints` to view details)
        - **Triggers:** 1 active — use `sql_get_trigger_definition` with: `Changed`
        """;

    [Theory]
    [InlineData("U")]
    [InlineData("V")]
    public async Task McpTableAndView_PreserveEntireEnrichedOutput(string type)
    {
        var fixture = new SchemaPresentationFixture { ObjectType = type };
        var result = await fixture.CreateService().GetSchemaAsync("DemoDB", "sales.Customers", TestContext.Current.CancellationToken);
        string expected = Lines(TableOutput);
        if (type == "V") expected += Lines("## View Definition\n```sql") + fixture.Definition + Environment.NewLine + Lines("```");
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
        Assert.Equal(2, fixture.MetadataCalls);
        Assert.Equal(1, fixture.PolicyCalls);
        Assert.Equal(1, fixture.RuleCalls);
    }

    [Theory]
    [InlineData("P")]
    [InlineData("FN")]
    [InlineData("IF")]
    [InlineData("TF")]
    public async Task McpRoutine_PreservesEntireDefinitionAndDiscovery(string type)
    {
        var fixture = new SchemaPresentationFixture { ObjectType = type };
        var result = await fixture.CreateService().GetSchemaAsync("DemoDB", "sales.Routine", TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(Lines("# DDL Definition for Stored Procedure/Function: `sales.Routine`\n\n*Discovery:* This routine accepts `2` parameter(s). Run `sql_get_routine_parameters` to view them.\n\n```sql") + fixture.Definition + Environment.NewLine + Lines("```"), result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task McpViewAndRoutine_PreserveEntireUnavailableNotes(string? definition)
    {
        var fixture = new SchemaPresentationFixture { ObjectType = "V", Definition = definition };
        var service = fixture.CreateService();
        var view = await service.GetSchemaAsync("DemoDB", "sales.Customers", TestContext.Current.CancellationToken);
        Assert.Equal(Lines(TableOutput) + Lines("## View Definition\n" + DetailSchemaRenderer.DdlUnavailableNote), view.Value);
        fixture.ObjectType = "P";
        var routine = await service.GetSchemaAsync("DemoDB", "sales.Routine", TestContext.Current.CancellationToken);
        Assert.Equal(Lines("# DDL Definition for Stored Procedure/Function: `sales.Routine`\n\n*Discovery:* This routine accepts `2` parameter(s). Run `sql_get_routine_parameters` to view them.\n\n" + DetailSchemaRenderer.DdlUnavailableNote), routine.Value);
    }

    [Fact]
    public async Task McpSchema_PreservesExactFailures()
    {
        var fixture = new SchemaPresentationFixture();
        var denied = await fixture.CreateService(false).GetSchemaAsync("DemoDB", "sales.Customers", TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.SafetyCheckFailed("Database 'DemoDB' is blocked by security policies (static whitelist)."), denied.Error);
        Assert.Equal(0, fixture.Connections);
        var service = fixture.CreateService();
        fixture.ObjectType = null;
        var missing = await service.GetSchemaAsync("DemoDB", "sales.Customers", TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.ObjectNotFound("sales.Customers"), missing.Error);
        fixture.FailQuery = true;
        var failure = await service.GetSchemaAsync("DemoDB", "sales.Customers", TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.QueryError("fixed schema failure"), failure.Error);
    }

    [Fact]
    public async Task McpDetailNavigation_PreservesEntireTables()
    {
        var service = new SchemaPresentationFixture().CreateService();
        var fk = await service.GetSchemaForeignKeysAsync("DemoDB", "sales.Customers", TestContext.Current.CancellationToken);
        Assert.Equal("# Foreign Keys for `sales.Customers`\n\n" + Lines("| FK Name | Source Column | Dir | Reference Column |\n| --- | --- | --- | --- |\n| FK_Customer | sales.Orders.CustomerId | → | sales.Customers.Id |"), fk.Value);
        var refs = await service.GetObjectReferencesAsync("DemoDB", "sales.Customers", TestContext.Current.CancellationToken);
        Assert.Equal("# Referencing Entities for `sales.Customers`\n\n" + Lines("| Schema | Entity Name | Type |\n| --- | --- | --- |\n| sales | Overview | OBJECT_OR_COLUMN |"), refs.Value);
    }

    internal static string Lines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", Environment.NewLine, StringComparison.Ordinal) + Environment.NewLine;
}
