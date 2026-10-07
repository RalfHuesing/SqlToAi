using Microsoft.Extensions.Logging.Abstractions;
using SqlToAi.Cli;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

public sealed class OfflineSqlNameTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string, string, string> Names => new()
    {
        { "One\n\nTwo", @"One\\n\\nTwo", @"`sales.One\n\nTwo`" },
        { "One\r\n\r\nTwo", @"One\\r\\n\\r\\nTwo", @"`sales.One\r\n\r\nTwo`" },
        { "A`B", @"A\`B", "``sales.A`B``" },
        { "A&amp;B", @"A\&amp;B", "`sales.A&amp;B`" },
        { @"One\nTwo", @"One\\\\nTwo", @"`sales.One\\nTwo`" }
    };

    public static IEnumerable<object[]> ExportCases => Names.SelectMany(row => new[]
    {
        ("U", SchemaObjectKind.Table), ("V", SchemaObjectKind.View), ("P", SchemaObjectKind.Procedure),
        ("FN", SchemaObjectKind.ScalarFunction), ("IF", SchemaObjectKind.InlineTableValuedFunction),
        ("TF", SchemaObjectKind.TableValuedFunction), ("TR", SchemaObjectKind.Trigger)
    }.Select(kind => new object[] { row.Data.Item1, row.Data.Item2, row.Data.Item3, kind.Item1, kind.Item2 }));

    [Theory]
    [MemberData(nameof(ExportCases))]
    public async Task RealExport_FormatsNamesAndNavigationWithoutChangingIdentityOrSql(string name, string textName, string codeName, string type, SchemaObjectKind kind)
    {
        using var temp = TestTempDirectory.Create();
        var identity = new SchemaObjectIdentity(kind == SchemaObjectKind.Trigger ? 301 : 501, "sales", name, kind);
        var parent = new SchemaObjectIdentity(101, "sales", name + "Parent", SchemaObjectKind.Table);
        var source = new SchemaObject(identity, kind == SchemaObjectKind.Trigger ? parent : null);
        // Include a trigger parent to exercise both linked labels and primary/detail rendering.
        SchemaObject[] objects = kind == SchemaObjectKind.Trigger ? [new(parent), source] : [source];
        string sql = $"-- Original SQL name/comment: {name}\r\nCREATE VIEW sales.X AS SELECT 1;";
        var fixture = new SchemaPresentationFixture
        {
            ExportDiscovery = true, ExportObjects = objects, ObjectType = kind == SchemaObjectKind.Trigger ? "U" : type,
            StructuredName = name, ReferenceSchema = "sales", ReferenceName = name, RejectEnrichment = true, Definition = sql
        };
        var exporter = new SchemaExportService(fixture.CreateService(), NullLogger<SchemaExportService>.Instance);
        string root = temp.GetPath("dump");
        var result = await exporter.ExportAsync("DemoDB", root, Token);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var paths = SchemaExportPaths.Create(objects);
        string document = await File.ReadAllTextAsync(Path.Combine(root, paths[identity]), Token);
        string overview = await File.ReadAllTextAsync(Path.Combine(root, "README.md"), Token);
        string url = string.Join('/', paths[identity].Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
        Assert.Contains($"- [sales.{textName}]({url})" + Environment.NewLine, overview);
        Assert.True(File.Exists(Path.Combine(root, Uri.UnescapeDataString(url))));
        string heading = kind switch
        {
            SchemaObjectKind.Table or SchemaObjectKind.View => "# Schema for Table/View: ",
            SchemaObjectKind.Trigger => "# Trigger: ",
            _ => "# DDL Definition for Stored Procedure/Function: "
        };
        Assert.Equal(heading + codeName, document.Split('\n')[0].TrimEnd('\r'));
        if (kind != SchemaObjectKind.Table) Assert.Contains(sql, document);
        if (kind is SchemaObjectKind.Table or SchemaObjectKind.View)
        {
            foreach (string detail in new[] { "Foreign Keys for", "Indexes for", "Constraints for", "Referencing Entities for" })
                Assert.Contains($"# {detail} {codeName}\n\n", document);
            Assert.Contains($"| {textName} | int | No | PK, Identity |", document);
            Assert.Contains($"| {textName} | CLUSTERED | Primary Key | {textName} | {textName} |", document);
            Assert.Contains($"| {textName} | {textName} | DEFAULT | (N'A&amp;B`C') |", document);
            Assert.Contains($"[sales.{textName}.{textName}]", document);
            Assert.Contains($"| {textName} | {textName} | OBJECT_OR_COLUMN |", document); // missing target remains escaped text
        }
        else if (kind == SchemaObjectKind.Trigger)
        {
            Assert.Contains($"Parent: [sales.{textName}Parent](../tables/", document);
            Assert.Contains($"# Trigger Definition: {codeName} (on table ", document);
        }
        else
        {
            Assert.Contains($"# Parameters for Routine {codeName}\n\n", document);
            Assert.Contains($"| {textName} | int | 4 | No |", document);
        }
        Assert.All(fixture.QueriedObjectNames, queried => Assert.Contains(queried, new[] { identity.QualifiedName, identity.ObjectName, parent.QualifiedName }));
        Assert.Equal(name, identity.ObjectName);
        Assert.Equal(0, fixture.MetadataCalls + fixture.PolicyCalls + fixture.RuleCalls);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task SharedDetailCalls_PreserveFullMcpOutputsNotesAndFailuresAfterOfflineCalls(string name, string textName, string codeName)
    {
        var identity = new SchemaObjectIdentity(501, "sales", name, SchemaObjectKind.Table);
        var paths = new Dictionary<SchemaObjectIdentity, string> { [identity] = "tables/name.md" };
        var context = new SchemaRenderingContext(identity, paths);
        var fixture = new SchemaPresentationFixture { StructuredName = name };
        var service = fixture.CreateService();
        Func<Task<Result<string>>>[] mcp =
        [
            () => service.GetSchemaIndexesAsync("DemoDB", identity.QualifiedName, Token),
            () => service.GetSchemaConstraintsAsync("DemoDB", identity.QualifiedName, Token),
            () => service.GetRoutineParametersAsync("DemoDB", identity.QualifiedName, Token),
            () => service.GetSchemaForeignKeysAsync("DemoDB", identity.QualifiedName, Token),
            () => service.GetObjectReferencesAsync("DemoDB", identity.QualifiedName, Token)
        ];
        Func<Task<Result<string>>>[] offline =
        [
            () => service.GetExportSchemaIndexesAsync("DemoDB", context, Token),
            () => service.GetExportSchemaConstraintsAsync("DemoDB", context, Token),
            () => service.GetExportRoutineParametersAsync("DemoDB", context, Token),
            () => service.GetExportSchemaForeignKeysAsync("DemoDB", context, Token),
            () => service.GetExportObjectReferencesAsync("DemoDB", context, Token)
        ];
        string[] expected =
        [
            $"# Indexes for `{identity.QualifiedName}`\n\n| Index Name | Type | Property | Keys | Included Columns |{Environment.NewLine}| --- | --- | --- | --- | --- |{Environment.NewLine}| {name} | CLUSTERED | Primary Key | {name} | {name} |{Environment.NewLine}",
            $"# Constraints for `{identity.QualifiedName}`\n\n| Constraint Name | Column | Type | Definition |{Environment.NewLine}| --- | --- | --- | --- |{Environment.NewLine}| {name} | {name} | DEFAULT | (N'A&amp;B`C') |{Environment.NewLine}| {name} | {name} | CHECK | (N'A&amp;B`C') |{Environment.NewLine}",
            $"# Parameters for Routine `{identity.QualifiedName}`\n\n| Parameter Name | Type | Length | Output |{Environment.NewLine}| --- | --- | --- | --- |{Environment.NewLine}| {name} | int | 4 | No |{Environment.NewLine}",
            $"# Foreign Keys for `{identity.QualifiedName}`\n\n| FK Name | Source Column | Dir | Reference Column |{Environment.NewLine}| --- | --- | --- | --- |{Environment.NewLine}| {name} | sales.Orders.{name} | → | sales.Customers.{name} |{Environment.NewLine}",
            $"# Referencing Entities for `{identity.QualifiedName}`\n\n| Schema | Entity Name | Type |{Environment.NewLine}| --- | --- | --- |{Environment.NewLine}| {name} | {name} | OBJECT_OR_COLUMN |{Environment.NewLine}"
        ];
        for (int index = 0; index < mcp.Length; index++)
        {
            fixture.ObjectType = index == 2 ? "P" : "U";
            Assert.Equal(expected[index], (await mcp[index]()).Value);
            var rendered = await offline[index]();
            Assert.Contains(codeName + "\n\n", rendered.Value);
            Assert.Contains(textName, rendered.Value);
            Assert.Equal(expected[index], (await mcp[index]()).Value);
            fixture.EmptyDetails = true;
            var before = await mcp[index]();
            var note = await offline[index]();
            Assert.True(note.IsSuccess);
            Assert.Contains(codeName, note.Value);
            Assert.DoesNotContain('\n', note.Value);
            Assert.DoesNotContain('\r', note.Value);
            Assert.Equal(before.Value, (await mcp[index]()).Value);
            fixture.EmptyDetails = false;
            fixture.FailQuery = true;
            var failure = await mcp[index]();
            Assert.Equal(SqlToAiError.QueryError("fixed schema failure"), failure.Error);
            Assert.Equal(failure.Error, (await offline[index]()).Error);
            Assert.Equal(failure.Error, (await mcp[index]()).Error);
            fixture.FailQuery = false;
        }
        var denied = fixture.CreateService(false);
        Assert.Equal(SqlToAiError.SafetyCheckFailed("Database 'DemoDB' is blocked by security policies (static whitelist)."), (await denied.GetExportSchemaIndexesAsync("DemoDB", context, Token)).Error);
        Assert.True((await denied.GetExportSchemaConstraintsAsync("DemoDB", context, Token)).IsFailure);
        Assert.True((await denied.GetExportRoutineParametersAsync("DemoDB", context, Token)).IsFailure);
        fixture.ObjectType = "P";
        Assert.Equal(SqlToAiError.InvalidDetailQueryType(identity.QualifiedName), (await service.GetExportSchemaIndexesAsync("DemoDB", context, Token)).Error);
        Assert.Equal(SqlToAiError.InvalidDetailQueryType(identity.QualifiedName), (await service.GetExportSchemaConstraintsAsync("DemoDB", context, Token)).Error);
        fixture.ObjectType = "U";
        Assert.Equal(SqlToAiError.InvalidParameterType(identity.QualifiedName), (await service.GetExportRoutineParametersAsync("DemoDB", context, Token)).Error);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task PrimaryAndTriggerRendering_KeepFullLegacyOutputAndUnavailableNotes(string name, string textName, string codeName)
    {
        var identity = new SchemaObjectIdentity(501, "sales", name, SchemaObjectKind.Table);
        var trigger = new SchemaObject(new SchemaObjectIdentity(301, "sales", name, SchemaObjectKind.Trigger), identity);
        var context = new SchemaRenderingContext(identity, new Dictionary<SchemaObjectIdentity, string> { [identity] = "tables/name.md" });
        var fixture = new SchemaPresentationFixture();
        var service = fixture.CreateService();
        string tableExpected = SchemaPresentationRegressionTests.Lines(SchemaPresentationRegressionTests.TableOutput)
            .Replace("`sales.Customers`", $"`{identity.QualifiedName}`", StringComparison.Ordinal);
        Assert.Equal(tableExpected, (await service.GetSchemaAsync("DemoDB", identity.QualifiedName, Token)).Value);
        Assert.StartsWith("# Schema for Table/View: " + codeName, (await service.GetExportSchemaAsync("DemoDB", context, Token)).Value);
        Assert.Equal(tableExpected, (await service.GetSchemaAsync("DemoDB", identity.QualifiedName, Token)).Value);
        fixture.ObjectType = "P";
        string routineExpected = $"# DDL Definition for Stored Procedure/Function: `{identity.QualifiedName}`{Environment.NewLine}{Environment.NewLine}"
            + SchemaPresentationRegressionTests.Lines("*Discovery:* This routine accepts `2` parameter(s). Run `sql_get_routine_parameters` to view them.\n\n```sql")
            + fixture.Definition + Environment.NewLine + SchemaPresentationRegressionTests.Lines("```");
        Assert.Equal(routineExpected, (await service.GetSchemaAsync("DemoDB", identity.QualifiedName, Token)).Value);
        Assert.StartsWith("# DDL Definition for Stored Procedure/Function: " + codeName, (await service.GetExportSchemaAsync("DemoDB", context, Token)).Value);
        Assert.Equal(routineExpected, (await service.GetSchemaAsync("DemoDB", identity.QualifiedName, Token)).Value);
        string triggerExpected = $"# Trigger Definition: `{name}` (on table `{identity.QualifiedName}`)\n\n```sql\n{fixture.Definition}\n```";
        Assert.Equal(triggerExpected, (await service.GetTriggerDefinitionAsync("DemoDB", identity.QualifiedName, name, Token)).Value);
        Assert.StartsWith($"# Trigger Definition: {codeName} (on table {codeName})", (await service.GetExportTriggerDefinitionAsync("DemoDB", trigger, Token)).Value);
        Assert.Equal(triggerExpected, (await service.GetTriggerDefinitionAsync("DemoDB", identity.QualifiedName, name, Token)).Value);
        fixture.Definition = null;
        string legacyNote = $"*Definition for trigger '{name}' not available.* {DetailSchemaRenderer.DdlUnavailableNote}";
        Assert.Equal(legacyNote, (await service.GetTriggerDefinitionAsync("DemoDB", identity.QualifiedName, name, Token)).Value);
        Assert.Equal($"*Definition for trigger {codeName} not available.* {DetailSchemaRenderer.DdlUnavailableNote}", (await service.GetExportTriggerDefinitionAsync("DemoDB", trigger, Token)).Value);
        Assert.Equal(legacyNote, (await service.GetTriggerDefinitionAsync("DemoDB", identity.QualifiedName, name, Token)).Value);
        // Missing local targets use the same literal label formatting as navigation links.
        Assert.Equal(textName, context.TriggerLink(999, name));
        Assert.Equal(textName, context.ObjectLink("missing", name, name));
    }

    [Theory]
    [InlineData("`Edge``", "``` `Edge`` ```")]
    [InlineData(" A `B` ", "``  A `B`  ``")]
    [InlineData("A&amp;B", "`A&amp;B`")]
    [InlineData(" ", "` `")]
    public void CodeNames_UseLiteralCodeSpans(string name, string expected) => Assert.Equal(expected, OfflineSqlNameFormatter.Code(name));

    [Fact]
    public void PipeAndControlNames_AreSafeInTablesAndKeepLiteralCharactersDistinct()
    {
        Assert.Equal(@"A\~\~B", OfflineSqlNameFormatter.Text("A~~B"));
        Assert.Equal(@"A&#124;B\\tC\\u0001", OfflineSqlNameFormatter.Text("A|B\tC\u0001"));
        Assert.Equal(@"`A|B\tC\u0001`", OfflineSqlNameFormatter.Code("A|B\tC\u0001"));
        Assert.Equal("| Name |" + Environment.NewLine + "| --- |" + Environment.NewLine + "| A&#124;B |" + Environment.NewLine,
            MarkdownTableRenderer.Render(["Name"], [[OfflineSqlNameFormatter.Text("A|B")]]));
    }
}
