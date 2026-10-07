using System.CommandLine;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using SqlToAi.Cli;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Tests.Database;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Cli;

public sealed class SchemaExportTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static SchemaObject Object(int id, string name, SchemaObjectKind kind = SchemaObjectKind.Table, string schema = "dbo")
        => new(new SchemaObjectIdentity(id, schema, name, kind));

    private static (SchemaExportService Export, ExportSchemaProxy Proxy) Create(params SchemaObject[] objects)
    {
        var schema = DispatchProxy.Create<ISchemaService, ExportSchemaProxy>();
        var proxy = (ExportSchemaProxy)schema;
        proxy.Objects = objects;
        return (new SchemaExportService(schema, NullLogger<SchemaExportService>.Instance), proxy);
    }

    [Theory]
    [InlineData("export-schema")]
    [InlineData("export-schema --database DemoDB")]
    [InlineData("export-schema --output dump")]
    public async Task MissingRequiredInputs_DoNotInvokeExport(string input)
    {
        bool invoked = false;
        var root = new RootCommand();
        var command = SchemaExportCommand.Build((_, _, _) =>
        {
            invoked = true;
            return Task.FromResult(Result.Success());
        });
        root.Add(command);
        Assert.Equal(["--database", "--output"], command.Options.Select(option => option.Name));
        Assert.NotEqual(0, await root.Parse(input).InvokeAsync(cancellationToken: Token));
        Assert.False(invoked);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task Command_PropagatesResultAndInputs(bool success, int expected)
    {
        var root = new RootCommand();
        root.Add(SchemaExportCommand.Build((database, output, _) =>
        {
            Assert.Equal("DemoDB", database);
            Assert.Equal("a path", output);
            return Task.FromResult(success ? Result.Success() : Result.Failure(SqlToAiError.QueryError("fixed")));
        }));
        Assert.Equal(expected, await root.Parse(["export-schema", "--database", "DemoDB", "--output", "a path"]).InvokeAsync(cancellationToken: Token));
    }

    [Fact]
    public async Task AllSevenKinds_AboveInteractiveLimit_ComposeFilesAndNavigation()
    {
        using var temp = TestTempDirectory.Create();
        var parent = Object(1, "Parent # [x]");
        var objects = Enumerable.Range(2, 106).Select(id => Object(id, "Table" + id)).Prepend(parent).ToList();
        objects.Add(Object(201, "Overview", SchemaObjectKind.View));
        objects.Add(Object(202, "Routine", SchemaObjectKind.Procedure));
        objects.Add(Object(203, "Scalar", SchemaObjectKind.ScalarFunction));
        objects.Add(Object(204, "Inline", SchemaObjectKind.InlineTableValuedFunction));
        objects.Add(Object(205, "Valued", SchemaObjectKind.TableValuedFunction));
        var trigger = new SchemaObject(new SchemaObjectIdentity(301, "audit", "Changed", SchemaObjectKind.Trigger), parent.Identity);
        var secondTrigger = new SchemaObject(new SchemaObjectIdentity(302, "sales", "Changed", SchemaObjectKind.Trigger), objects[107].Identity);
        objects.AddRange([trigger, secondTrigger]);
        var (export, proxy) = Create(objects.ToArray());
        string root = temp.GetPath("dump");
        Assert.True((await export.ExportAsync("DemoDB", root, Token)).IsSuccess);
        var paths = SchemaExportPaths.Create(objects);
        Assert.Equal(objects.Count + 1, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
        foreach (SchemaObject item in objects)
        {
            string text = await File.ReadAllTextAsync(Path.Combine(root, paths[item.Identity]), Token);
            if (item.Identity.Kind is SchemaObjectKind.Table or SchemaObjectKind.View)
            {
                Assert.Contains("foreign keys", text);
                Assert.Contains("indexes " + item.Identity.QualifiedName, text);
                Assert.Contains("constraints " + item.Identity.QualifiedName, text);
                Assert.Contains("references", text);
                Assert.Contains("[Changed]", text);
                if (item.Identity.Kind == SchemaObjectKind.View) Assert.Contains("CREATE", text);
            }
            else if (item.Identity.Kind != SchemaObjectKind.Trigger)
            {
                Assert.Contains("CREATE", text);
                Assert.Contains("parameters " + item.Identity.QualifiedName, text);
            }
        }
        string triggerText = await File.ReadAllTextAsync(Path.Combine(root, paths[trigger.Identity]), Token);
        Assert.Contains("Parent: " + SchemaRenderingContext.RelativeLink(paths[trigger.Identity], paths[parent.Identity], parent.Identity.DisplayName), triggerText);
        Assert.Contains("trigger 301", triggerText);
        Assert.Contains("trigger 302", await File.ReadAllTextAsync(Path.Combine(root, paths[secondTrigger.Identity]), Token));
        string overview = await File.ReadAllTextAsync(Path.Combine(root, "README.md"), Token);
        foreach (SchemaObject item in objects)
            Assert.Contains(SchemaRenderingContext.RelativeLink("README.md", paths[item.Identity], item.Identity.DisplayName), overview);
        Assert.Equal(0, proxy.InteractiveCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyDatabase_NewOrEmptyTarget_WritesOnlyOverview(bool existing)
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.GetPath("dump");
        if (existing) Directory.CreateDirectory(root);
        var (export, _) = Create();
        Assert.True((await export.ExportAsync("DemoDB", root, Token)).IsSuccess);
        Assert.Single(Directory.GetFileSystemEntries(root));
        Assert.True(File.Exists(Path.Combine(root, "README.md")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonemptyTarget_IsRejectedWithoutDiscovery(bool directory)
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.GetPath("dump");
        Directory.CreateDirectory(root);
        if (directory) Directory.CreateDirectory(Path.Combine(root, "child"));
        else await File.WriteAllTextAsync(Path.Combine(root, "existing.md"), "keep", Token);
        var (export, proxy) = Create(Object(1, "One"));
        Assert.True((await export.ExportAsync("DemoDB", root, Token)).IsFailure);
        Assert.Empty(proxy.Calls);
        Assert.Single(Directory.GetFileSystemEntries(root));
    }

    [Theory]
    [InlineData("Name", "name")]
    [InlineData("S", "s")]
    public async Task CaseCollision_IsRejectedBeforeFirstFileOrDirectory(string first, string second)
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.GetPath("dump");
        var (export, proxy) = Create(Object(1, first), Object(2, second));
        var result = await export.ExportAsync("DemoDB", root, Token);
        Assert.True(result.IsFailure);
        Assert.Contains("collision", result.Error.Message);
        Assert.False(Directory.Exists(root));
        Assert.Single(proxy.Calls);
    }

    [Fact]
    public async Task SafeNames_EscapingIsStableDistinctAndLinksReachEveryFile()
    {
        using var temp = TestTempDirectory.Create();
        var objects = new[] { Object(1, "CON", schema: "CON"), Object(2, "a<>:\"/\\|?*.", schema: "sales"),
            Object(3, "trailing "), Object(4, "x~003F"), Object(5, "x?"), Object(6, "Name # [x] (y) %"),
            Object(7, new string('?', 128), schema: new string('?', 128)), Object(8, "one.two"),
            Object(9, "two", schema: "dbo.one"), Object(10, new string('x', 30) + "😀" + new string('x', 98), schema: new string('s', 128)) };
        var paths = SchemaExportPaths.Create(objects);
        Assert.Equal(paths.Values, SchemaExportPaths.Create(objects).Values);
        Assert.Equal(objects.Length, paths.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (string relative in paths.Values)
        {
            string filename = Path.GetFileName(relative);
            Assert.True(filename.Length <= 255);
            Assert.DoesNotContain(filename, value => "<>:\"/\\|?*".Contains(value, StringComparison.Ordinal));
            Assert.False(filename.StartsWith("CON.", StringComparison.OrdinalIgnoreCase));
        }
        var (export, _) = Create(objects);
        string root = temp.GetPath("dump");
        Assert.True((await export.ExportAsync("DemoDB", root, Token)).IsSuccess);
        string overview = await File.ReadAllTextAsync(Path.Combine(root, "README.md"), Token);
        foreach (var item in objects)
        {
            string link = SchemaRenderingContext.RelativeLink("README.md", paths[item.Identity], item.Identity.DisplayName);
            Assert.Contains(link, overview);
            string url = link[(link.LastIndexOf("](", StringComparison.Ordinal) + 2)..^1];
            Assert.True(File.Exists(Path.Combine(root, Uri.UnescapeDataString(url))));
        }
        Assert.Contains("%23%20%5Bx%5D%20%28y%29%20%25", overview);
    }

    [Theory]
    [InlineData("GetExportObjectsAsync")]
    [InlineData("GetExportSchemaAsync")]
    [InlineData("GetExportSchemaForeignKeysAsync")]
    [InlineData("GetSchemaIndexesAsync")]
    [InlineData("GetSchemaConstraintsAsync")]
    [InlineData("GetExportObjectReferencesAsync")]
    [InlineData("GetRoutineParametersAsync")]
    [InlineData("GetExportTriggerDefinitionAsync")]
    public async Task FailedServiceResult_StopsAtFailureAndPreservesError(string operation)
    {
        using var temp = TestTempDirectory.Create();
        var kind = operation == "GetRoutineParametersAsync" ? SchemaObjectKind.Procedure : SchemaObjectKind.Table;
        var parent = Object(1, "One", kind);
        var objects = operation == "GetExportTriggerDefinitionAsync"
            ? new[] { new SchemaObject(new SchemaObjectIdentity(2, "audit", "T", SchemaObjectKind.Trigger), parent.Identity), parent }
            : [parent, Object(3, "Never")];
        var (export, proxy) = Create(objects);
        proxy.FailOperation = operation;
        var result = await export.ExportAsync("DemoDB", temp.GetPath("dump"), Token);
        Assert.True(result.IsFailure);
        Assert.Equal(proxy.Failure, result.Error);
        Assert.Equal(operation, proxy.Calls.Last());
        Assert.Empty(Directory.Exists(temp.GetPath("dump")) ? Directory.GetFiles(temp.GetPath("dump"), "*", SearchOption.AllDirectories) : []);
    }

    [Fact]
    public async Task LaterServiceFailure_LeavesPartialOutput_AndRetryIsRejected()
    {
        using var temp = TestTempDirectory.Create();
        var (export, proxy) = Create(Object(1, "First"), Object(2, "Second"));
        proxy.BeforePrimary = context => { if (context.Source.ObjectId == 2) proxy.FailOperation = "GetExportSchemaAsync"; };
        string root = temp.GetPath("dump");
        Assert.True((await export.ExportAsync("DemoDB", root, Token)).IsFailure);
        Assert.Single(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        string first = await File.ReadAllTextAsync(Path.Combine(root, "tables/dbo.First.md"), Token);
        int calls = proxy.Calls.Count;
        Assert.True((await export.ExportAsync("DemoDB", root, Token)).IsFailure);
        Assert.Equal(calls, proxy.Calls.Count);
        Assert.Equal(first, await File.ReadAllTextAsync(Path.Combine(root, "tables/dbo.First.md"), Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteFailure_StopsAndKeepsEarlierFile_WithoutOverwriting(bool existingFile)
    {
        using var temp = TestTempDirectory.Create();
        string root = temp.GetPath("dump");
        var (export, proxy) = Create(Object(1, "First"), Object(2, "Second"), Object(3, "Never"));
        proxy.BeforePrimary = context =>
        {
            if (context.Source.ObjectId == 2)
            {
                string blockedPath = Path.Combine(root, "tables/dbo.Second.md");
                if (existingFile) File.WriteAllText(blockedPath, "keep existing file");
                else Directory.CreateDirectory(blockedPath);
            }
        };
        var result = await export.ExportAsync("DemoDB", root, Token);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InfrastructureErrorCode, result.Error.Code);
        Assert.True(File.Exists(Path.Combine(root, "tables/dbo.First.md")));
        Assert.False(File.Exists(Path.Combine(root, "tables/dbo.Never.md")));
        Assert.False(File.Exists(Path.Combine(root, "README.md")));
        if (existingFile) Assert.Equal("keep existing file", await File.ReadAllTextAsync(Path.Combine(root, "tables/dbo.Second.md"), Token));
        Assert.True((await export.ExportAsync("DemoDB", root, Token)).IsFailure);
    }

    [Theory]
    [InlineData(SchemaObjectKind.Procedure)]
    [InlineData(SchemaObjectKind.View)]
    [InlineData(SchemaObjectKind.Trigger)]
    public async Task UnavailableDefinition_SuccessfulNoteDoesNotFailExport(SchemaObjectKind kind)
    {
        using var temp = TestTempDirectory.Create();
        var source = Object(1, "Unavailable", kind);
        var objects = kind == SchemaObjectKind.Trigger
            ? new[] { source with { Parent = Object(2, "Parent").Identity }, Object(2, "Parent") }
            : [source];
        var (export, proxy) = Create(objects);
        proxy.PrimaryText = DetailSchemaRenderer.DdlUnavailableNote;
        proxy.TriggerText = DetailSchemaRenderer.DdlUnavailableNote;
        Assert.True((await export.ExportAsync("DemoDB", temp.GetPath("dump"), Token)).IsSuccess);
        Assert.Contains(DetailSchemaRenderer.DdlUnavailableNote,
            await File.ReadAllTextAsync(temp.GetPath("dump/" + SchemaExportPaths.Create(objects)[source.Identity]), Token));
    }

    [Theory]
    [InlineData("U", "tables")]
    [InlineData("V", "views")]
    [InlineData("P", "procedures")]
    public async Task ExportEntry_UsesRealOfflinePresentation_SkipsProvidersAndRetainsSqlComments(string type, string directory)
    {
        using var temp = TestTempDirectory.Create();
        var fixture = new SchemaPresentationFixture { ObjectType = type, RejectEnrichment = true, ExportDiscovery = true };
        var exporter = new SchemaExportService(fixture.CreateService(), NullLogger<SchemaExportService>.Instance);
        var result = await exporter.ExportAsync("DemoDB", temp.GetPath("dump"), Token);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        string text = await File.ReadAllTextAsync(temp.GetPath($"dump/{directory}/sales.Exported.md"), Token);
        if (type != "U") Assert.Contains(fixture.Definition!, text);
        Assert.DoesNotContain("Fixed table description", text);
        Assert.DoesNotContain("Fixed column description", text);
        Assert.DoesNotContain("Run `sql_get_", text);
        Assert.DoesNotContain("Anonymized", text);
        Assert.Equal(0, fixture.MetadataCalls + fixture.PolicyCalls + fixture.RuleCalls);
    }

    public class ExportSchemaProxy : DispatchProxy
    {
        internal IReadOnlyList<SchemaObject> Objects { get; set; } = [];
        internal List<string> Calls { get; } = [];
        internal string? FailOperation { get; set; }
        internal SqlToAiError Failure { get; } = SqlToAiError.QueryError("fixed export failure");
        internal Action<SchemaRenderingContext>? BeforePrimary { get; set; }
        internal string? PrimaryText { get; set; }
        internal string? TriggerText { get; set; }
        internal int InteractiveCalls { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            string method = targetMethod!.Name;
            Calls.Add(method);
            if (method == "GetExportObjectsAsync")
                return Task.FromResult(FailOperation == method ? Result<IReadOnlyList<SchemaObject>>.Failure(Failure) : Result<IReadOnlyList<SchemaObject>>.Success(Objects));
            if (method == "GetExportSchemaAsync") BeforePrimary?.Invoke((SchemaRenderingContext)args![1]!);
            if (FailOperation == method) return Task.FromResult(Result<string>.Failure(Failure));
            string text;
            switch (method)
            {
                case "GetExportSchemaAsync":
                    var context = (SchemaRenderingContext)args![1]!;
                    text = PrimaryText ?? "# " + context.Source.DisplayName + "\nCREATE -- SQL comment\n";
                    foreach (SchemaObject trigger in Objects.Where(item => item.Identity.Kind == SchemaObjectKind.Trigger))
                        text += context.ObjectLink(trigger.Identity, trigger.Identity.ObjectName) + "\n";
                    break;
                case "GetExportSchemaForeignKeysAsync": text = "foreign keys"; break;
                case "GetSchemaIndexesAsync": text = "indexes " + args![1]; break;
                case "GetSchemaConstraintsAsync": text = "constraints " + args![1]; break;
                case "GetExportObjectReferencesAsync": text = "references"; break;
                case "GetRoutineParametersAsync": text = "parameters " + args![1]; break;
                case "GetExportTriggerDefinitionAsync": text = TriggerText ?? "trigger " + ((SchemaObject)args![1]!).Identity.ObjectId; break;
                default: InteractiveCalls++; throw new InvalidOperationException("Unexpected interactive call " + method);
            }
            return Task.FromResult(Result<string>.Success(text));
        }
    }
}
