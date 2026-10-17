using System.CommandLine;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SqlToAi.Cli;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Tests.Database;
using SqlToAi.Tests.TestSupport;
using static SqlToAi.Tests.Database.SchemaObjectSelectionTests;

namespace SqlToAi.Tests.Cli;

public sealed class SchemaExportOutcomeTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);
    private static SchemaObject Object(int id, string name, SchemaObjectKind kind = SchemaObjectKind.Table) =>
        new(new SchemaObjectIdentity(id, "dbo", name, kind));

    private static (SchemaExportService Export, SchemaExportTests.ExportSchemaProxy Proxy) Create(
        IReadOnlyList<SchemaObject> objects, Func<string, Stream>? openFile = null)
    {
        var schema = DispatchProxy.Create<ISchemaService, SchemaExportTests.ExportSchemaProxy>();
        var proxy = (SchemaExportTests.ExportSchemaProxy)schema;
        proxy.Objects = objects;
        var logger = NullLogger<SchemaExportService>.Instance;
        return (openFile is null ? new SchemaExportService(schema, logger) : new SchemaExportService(schema, logger, openFile), proxy);
    }

    [Fact]
    public async Task Selected_NormalizesAndResolvesBeforeMapping_DeduplicatesIdsAndDoesNotExpand()
    {
        using var temp = TestTempDirectory.Create();
        var catalog = new CatalogFactory([
            new(1, "dbo", "Name"), new(2, "dbo", "name"), new(3, "dbo", "Routine", "SQL_STORED_PROCEDURE"),
            new(4, "dbo", "Changed", "SQL_TRIGGER", "VIEW")]);
        var selectedTable = Object(1, "Name");
        var otherTable = Object(2, "name");
        var routine = Object(3, "Routine", SchemaObjectKind.Procedure);
        var trigger = new SchemaObject(new SchemaObjectIdentity(4, "dbo", "Changed", SchemaObjectKind.Trigger),
            new SchemaObjectIdentity(123, "dbo", "Parent", SchemaObjectKind.View));
        var (export, proxy) = Create([selectedTable, otherTable, routine, trigger]);
        proxy.ResolveSelected = (names, token) => catalog.Service().GetSelectedExportObjectsAsync("DemoDB", names, token);
        string root = temp.GetPath("dump");
        var result = await export.ExportAsync("DemoDB", root, SchemaExportScope.Selected,
            Json("\" dbo.Name \""), Json("[null,\" \",\"[dbo].[Name]\",\"dbo.Changed\",\"Routine\",\"dbo.Name\"]"), Token);

        Assert.Equal(SchemaExportStatus.Completed, result.Status);
        Assert.Equal(root, result.OutputPath);
        Assert.False(result.PartialOutput);
        Assert.Null(result.Error);
        Assert.Equal(3, catalog.Commands.Count);
        Assert.Equal(4, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
        Assert.Equal("dbo.Name.md", Path.GetFileName(Assert.Single(Directory.GetFiles(Path.Combine(root, "tables")))));
        Assert.False(File.Exists(Path.Combine(root, "views/dbo.Parent.md")));
        Assert.Equal(1, proxy.Calls.Count(call => call == "GetExportSchemaForeignKeysAsync"));
        Assert.Equal(1, proxy.Calls.Count(call => call == "GetExportTriggerDefinitionAsync"));
        Assert.DoesNotContain("GetExportObjectsAsync", proxy.Calls);
        string triggerText = await File.ReadAllTextAsync(Path.Combine(root, "triggers/dbo.Changed.md"), Token);
        Assert.Contains("Parent: dbo.Parent", triggerText);
        Assert.DoesNotContain("../views/", triggerText);
        Assert.DoesNotContain("dbo.name", await File.ReadAllTextAsync(Path.Combine(root, "README.md"), Token));
    }

    [Theory]
    [InlineData("Missing", "SQL-AI-0103")]
    [InlineData("Orders", "SQL-AI-0001")]
    [InlineData("dbo.CheckOrders", "SQL-AI-0001")]
    [InlineData("dbo.ServerTrigger", "SQL-AI-0001")]
    public async Task Selected_InvalidSelectionResolvesWholeRequestWithoutWrites(string invalid, string code)
    {
        using var temp = TestTempDirectory.Create();
        var catalog = new CatalogFactory([
            new(1, "dbo", "Good"), new(2, "dbo", "Last"), new(3, "dbo", "Orders"),
            new(4, "sales", "Orders"), new(5, "dbo", "CheckOrders", "CHECK_CONSTRAINT"),
            new(6, "dbo", "ServerTrigger", "SQL_TRIGGER")]);
        var exporter = new SchemaExportService(catalog.Service(), NullLogger<SchemaExportService>.Instance);
        string root = temp.GetPath("dump");
        var result = await exporter.ExportAsync("DemoDB", root, SchemaExportScope.Selected,
            objectNames: JsonSerializer.SerializeToElement(new[] { "dbo.Good", invalid, "dbo.Last" }), cancellationToken: Token);
        Assert.Equal(SchemaExportStatus.Failed, result.Status);
        Assert.Equal(code, result.Error!.Code);
        Assert.Contains(invalid, result.Error.Message);
        Assert.Equal(root, result.OutputPath);
        Assert.False(result.PartialOutput);
        Assert.False(Directory.Exists(root));
        Assert.Equal(3, catalog.Commands.Count);
    }

    [Fact]
    public async Task Selected_AllSupportedKindsAndBothTriggerParentKinds_ExportOnceWithoutBatchLimits()
    {
        using var temp = TestTempDirectory.Create();
        var catalog = new CatalogFactory([
            new(1, "dbo", "TableOne"), new(2, "dbo", "ViewOne", "VIEW"),
            new(3, "dbo", "ProcedureOne", "SQL_STORED_PROCEDURE"), new(4, "dbo", "ScalarOne", "SQL_SCALAR_FUNCTION"),
            new(5, "dbo", "InlineOne", "SQL_INLINE_TABLE_VALUED_FUNCTION"), new(6, "dbo", "ValuedOne", "SQL_TABLE_VALUED_FUNCTION"),
            new(7, "dbo", "TableChanged", "SQL_TRIGGER", "USER_TABLE"), new(8, "dbo", "ViewChanged", "SQL_TRIGGER", "VIEW")]);
        var (export, proxy) = Create([]);
        proxy.ResolveSelected = async (names, token) =>
        {
            var resolved = await catalog.Service().GetSelectedExportObjectsAsync("DemoDB", names, token);
            if (resolved.IsSuccess) proxy.Objects = resolved.Value;
            return resolved;
        };
        string[] names = ["TableOne", "ViewOne", "ProcedureOne", "ScalarOne", "InlineOne", "ValuedOne", "TableChanged", "ViewChanged"];
        var result = await export.ExportAsync("DemoDB", temp.GetPath("dump"), SchemaExportScope.Selected,
            objectNames: JsonSerializer.SerializeToElement(names.Concat(names).ToArray()), cancellationToken: Token);
        Assert.Equal(SchemaExportStatus.Completed, result.Status);
        Assert.False(result.PartialOutput);
        Assert.Equal(8, catalog.Commands.Count);
        Assert.Equal(9, Directory.GetFiles(result.OutputPath!, "*", SearchOption.AllDirectories).Length);
        Assert.Equal(2, proxy.Calls.Count(call => call == "GetExportTriggerDefinitionAsync"));
        Assert.Equal(4, proxy.Calls.Count(call => call == "GetExportRoutineParametersAsync"));
        Assert.Equal(2, proxy.Calls.Count(call => call == "GetExportSchemaForeignKeysAsync"));
        Assert.Equal(0, proxy.InteractiveCalls);
    }

    [Theory]
    [InlineData(true, "null", "[]")]
    [InlineData(true, "\" \"", "[null,\"\"]")]
    [InlineData(true, "42", "[\"Good\"]")]
    [InlineData(true, "null", "\"Good\"")]
    [InlineData(false, "\"Good\"", "[]")]
    [InlineData(false, "null", "[\"Good\"]")]
    public async Task InvalidInputs_DoNotDiscoverOrWrite(bool selected, string scalar, string list)
    {
        using var temp = TestTempDirectory.Create();
        var (export, proxy) = Create([]);
        var result = await export.ExportAsync("DemoDB", temp.GetPath("dump"), selected ? SchemaExportScope.Selected : SchemaExportScope.All, Json(scalar), Json(list), Token);
        Assert.Equal(SchemaExportStatus.Failed, result.Status);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error!.Code);
        Assert.False(result.PartialOutput);
        Assert.Empty(proxy.Calls);
        Assert.False(Directory.Exists(temp.GetPath("dump")));
    }

    [Fact]
    public async Task All_EmptySelectionAndCatalog_WritesOverviewAndReportsCompleted()
    {
        using var temp = TestTempDirectory.Create();
        var (export, _) = Create([]);
        var result = await export.ExportAsync("DemoDB", temp.GetPath("dump"), SchemaExportScope.All,
            Json("\" \""), Json("[null,\"\",\" \"]"), Token);
        Assert.Equal(SchemaExportStatus.Completed, result.Status);
        Assert.False(result.PartialOutput);
        Assert.Equal("README.md", Path.GetFileName(Assert.Single(Directory.GetFiles(result.OutputPath!))));
    }

    [Fact]
    public async Task All_TriggerOnlyCatalog_WritesDefinitionWithoutPrimarySchemaCalls()
    {
        using var temp = TestTempDirectory.Create();
        var parent = new SchemaObjectIdentity(1, "sales", "Orders", SchemaObjectKind.Table);
        var trigger = new SchemaObject(new SchemaObjectIdentity(2, "audit", "Changed", SchemaObjectKind.Trigger), parent);
        var (export, proxy) = Create([trigger]);
        string root = temp.GetPath("dump");

        var result = await export.ExportAsync("DemoDB", root, SchemaExportScope.All, cancellationToken: Token);

        Assert.Equal(SchemaExportStatus.Completed, result.Status);
        Assert.Equal(["GetExportObjectsAsync", "GetExportTriggerDefinitionAsync"], proxy.Calls);
        Assert.Equal("# Trigger: `audit.Changed`\n\nParent: sales.Orders\n\ntrigger 2",
            await File.ReadAllTextAsync(Path.Combine(root, "triggers/audit.Changed.md"), Token));
        Assert.Equal(2, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Selected_DeniedAccessNeverCreatesOutput(bool excluded)
    {
        using var temp = TestTempDirectory.Create();
        var catalog = new CatalogFactory([new(1, "dbo", "Good")]);
        var exporter = new SchemaExportService(catalog.Service(allowed: excluded, excluded: excluded), NullLogger<SchemaExportService>.Instance);
        var result = await exporter.ExportAsync("DemoDB", temp.GetPath("dump"), SchemaExportScope.Selected,
            Json("\"Good\""), cancellationToken: Token);
        Assert.Equal(SchemaExportStatus.Failed, result.Status);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error!.Code);
        Assert.False(result.PartialOutput);
        Assert.Equal(0, catalog.ConnectionCount);
        Assert.False(Directory.Exists(temp.GetPath("dump")));
    }

    [Fact]
    public async Task Selected_RealOfflineRenderingLeavesExternalReferencesReadableWithoutLinks()
    {
        using var temp = TestTempDirectory.Create();
        var fixture = new SchemaPresentationFixture { ExportDiscovery = true, RejectEnrichment = true };
        var export = new SchemaExportService(fixture.CreateService(), NullLogger<SchemaExportService>.Instance);
        var result = await export.ExportAsync("DemoDB", temp.GetPath("dump"), SchemaExportScope.Selected,
            Json("\"sales.Exported\""), cancellationToken: Token);
        Assert.Equal(SchemaExportStatus.Completed, result.Status);
        string text = await File.ReadAllTextAsync(temp.GetPath("dump/tables/sales.Exported.md"), Token);
        Assert.Contains("sales.Customers", text);
        Assert.Contains("Overview", text);
        Assert.Contains("Changed", text);
        Assert.DoesNotContain(".md)", text);
        Assert.DoesNotContain("Anonymized", text);
        Assert.Equal(0, fixture.MetadataCalls + fixture.PolicyCalls + fixture.RuleCalls);
        Assert.Equal(2, Directory.GetFiles(result.OutputPath!, "*", SearchOption.AllDirectories).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompositionFailure_ReportsRetainedOutputAndStops(bool afterWrite)
    {
        using var temp = TestTempDirectory.Create();
        var (export, proxy) = Create([Object(1, "First"), Object(2, "Second"), Object(3, "Never")]);
        proxy.BeforePrimary = context =>
        {
            if (context.Source.ObjectId == (afterWrite ? 2 : 1)) proxy.FailOperation = "GetExportSchemaAsync";
        };
        var result = await export.ExportAsync("DemoDB", temp.GetPath("dump"), SchemaExportScope.All, cancellationToken: Token);
        Assert.Equal(SchemaExportStatus.Failed, result.Status);
        Assert.Equal(proxy.Failure, result.Error);
        Assert.Equal(afterWrite, result.PartialOutput);
        Assert.Equal(afterWrite ? 1 : 0, Directory.GetFiles(result.OutputPath!, "*", SearchOption.AllDirectories).Length);
        Assert.DoesNotContain("Never", string.Join(',', Directory.GetFileSystemEntries(result.OutputPath!)));
        if (!afterWrite)
        {
            proxy.BeforePrimary = null;
            proxy.FailOperation = null;
            Assert.Equal(SchemaExportStatus.Completed,
                (await export.ExportAsync("DemoDB", result.OutputPath!, SchemaExportScope.All, cancellationToken: Token)).Status);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationEvenWhenServiceReturnsFailure_TakesPrecedenceAndStops(bool afterWrite)
    {
        using var temp = TestTempDirectory.Create();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var (export, proxy) = Create([Object(1, "First"), Object(2, "Second"), Object(3, "Never")]);
        proxy.BeforePrimary = context =>
        {
            if (context.Source.ObjectId != (afterWrite ? 2 : 1)) return;
            cancellation.Cancel();
            proxy.FailOperation = "GetExportSchemaAsync";
        };
        var result = await export.ExportAsync("DemoDB", temp.GetPath("dump"), SchemaExportScope.All, cancellationToken: cancellation.Token);
        Assert.Equal(SchemaExportStatus.Cancelled, result.Status);
        Assert.Equal("cancelled", result.Reason);
        Assert.Null(result.Error);
        Assert.Equal(afterWrite, result.PartialOutput);
        Assert.Equal(afterWrite ? 1 : 0, Directory.GetFiles(result.OutputPath!, "*", SearchOption.AllDirectories).Length);
        Assert.Equal(afterWrite ? 2 : 1, proxy.Calls.Count(call => call == "GetExportSchemaAsync"));
    }

    [Fact]
    public async Task PreCancelled_ReturnsResolvedPathWithoutDiscoveryOrRoot()
    {
        using var temp = TestTempDirectory.Create();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var (export, proxy) = Create([]);
        var result = await export.ExportAsync("DemoDB", temp.GetPath("dump"), SchemaExportScope.All, cancellationToken: cancellation.Token);
        Assert.Equal(SchemaExportStatus.Cancelled, result.Status);
        Assert.Equal(temp.GetPath("dump"), result.OutputPath);
        Assert.False(result.PartialOutput);
        Assert.Empty(proxy.Calls);
        Assert.False(Directory.Exists(result.OutputPath));
    }

    [Fact]
    public async Task DiscoveryCancellationReturnedAsFailure_IsCancelledBeforeWrites()
    {
        using var temp = TestTempDirectory.Create();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var (export, proxy) = Create([]);
        proxy.ResolveSelected = async (_, _) =>
        {
            await cancellation.CancelAsync();
            return Result<IReadOnlyList<SchemaObject>>.Failure(proxy.Failure);
        };
        var result = await export.ExportAsync("DemoDB", temp.GetPath("dump"), SchemaExportScope.Selected,
            Json("\"Selected\""), cancellationToken: cancellation.Token);
        Assert.Equal(SchemaExportStatus.Cancelled, result.Status);
        Assert.False(result.PartialOutput);
        Assert.False(Directory.Exists(result.OutputPath));
    }

    [Fact]
    public async Task CancellationAfterLastFileClosed_NeverReportsCompleted()
    {
        using var temp = TestTempDirectory.Create();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var (export, _) = Create([], path => new CancelOnDisposeStream(path, cancellation));
        var result = await export.ExportAsync("DemoDB", temp.GetPath("dump"), SchemaExportScope.All, cancellationToken: cancellation.Token);
        Assert.Equal(SchemaExportStatus.Cancelled, result.Status);
        Assert.True(result.PartialOutput);
        Assert.Equal("README.md", Path.GetFileName(Assert.Single(Directory.GetFiles(result.OutputPath!))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FileCreationFailure_EmptyRootIsFalseButChildDirectoryIsPartial(bool withObject)
    {
        using var temp = TestTempDirectory.Create();
        var (export, _) = Create(withObject ? [Object(1, "First")] : [], _ => throw new IOException("fixed write failure"));
        var result = await export.ExportAsync("DemoDB", temp.GetPath("dump"), SchemaExportScope.All, cancellationToken: Token);
        Assert.Equal(SchemaExportStatus.Failed, result.Status);
        Assert.Equal(SqlToAiError.InfrastructureErrorCode, result.Error!.Code);
        Assert.Equal(withObject, result.PartialOutput);
        Assert.Empty(Directory.GetFiles(result.OutputPath!, "*", SearchOption.AllDirectories));
        Assert.Equal(withObject ? 1 : 0, Directory.GetDirectories(result.OutputPath!).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteFile_IsRetainedAndReportedAfterWriteFailureOrCancellation(bool cancelled)
    {
        using var temp = TestTempDirectory.Create();
        var (export, _) = Create([], path => new PartialWriteStream(path, cancelled));
        var result = await export.ExportAsync("DemoDB", temp.GetPath("dump"), SchemaExportScope.All, cancellationToken: Token);
        Assert.Equal(cancelled ? SchemaExportStatus.Cancelled : SchemaExportStatus.Failed, result.Status);
        Assert.True(result.PartialOutput);
        string file = Assert.Single(Directory.GetFiles(result.OutputPath!));
        Assert.Equal("README.md", Path.GetFileName(file));
        Assert.True(new FileInfo(file).Length > 0);
        Assert.True((await export.ExportAsync("DemoDB", result.OutputPath!, Token)).IsFailure);
    }

    [Fact]
    public async Task Cli_RelativeOutputStillExportsAllWithOriginalExitBehavior()
    {
        using var temp = TestTempDirectory.Create();
        var (export, _) = Create([Object(1, "First"), Object(2, "Second")]);
        string output = Path.GetRelativePath(Environment.CurrentDirectory, temp.GetPath("dump"));
        Assert.False(Path.IsPathFullyQualified(output));
        var command = new RootCommand();
        command.Add(SchemaExportCommand.Build(export.ExportAsync));
        Assert.Equal(0, await command.Parse(["export-schema", "--database", "DemoDB", "--output", output]).InvokeAsync(cancellationToken: Token));
        Assert.Equal(3, Directory.GetFiles(temp.GetPath("dump"), "*", SearchOption.AllDirectories).Length);
        Assert.Equal(1, await command.Parse(["export-schema", "--database", "DemoDB", "--output", output]).InvokeAsync(cancellationToken: Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad\0path")]
    public async Task InvalidPath_DoesNotReportAResolvedLocation(string path)
    {
        var (export, proxy) = Create([]);
        var result = await export.ExportAsync("DemoDB", path, SchemaExportScope.All, cancellationToken: Token);
        Assert.Equal(SchemaExportStatus.Failed, result.Status);
        Assert.Null(result.OutputPath);
        Assert.False(result.PartialOutput);
        Assert.Empty(proxy.Calls);
    }

    private sealed class PartialWriteStream(string path, bool cancelled)
        : FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true)
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await base.WriteAsync(buffer[..1], cancellationToken);
            await base.FlushAsync(cancellationToken);
            if (cancelled) throw new OperationCanceledException("fixed write cancellation", cancellationToken);
            throw new IOException("fixed incomplete write");
        }
    }

    private sealed class CancelOnDisposeStream(string path, CancellationTokenSource cancellation)
        : FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true)
    {
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await cancellation.CancelAsync();
        }
    }
}
