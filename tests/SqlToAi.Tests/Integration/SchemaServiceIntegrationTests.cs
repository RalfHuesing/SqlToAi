using SqlToAi.Database;
using SqlToAi.Domain;
using Dapper;
using System.Text;
using System.Text.Json;

namespace SqlToAi.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(SqlServerCollectionFixture.Name)]
public sealed class SchemaServiceIntegrationTests
{
    private readonly SqlServerFixture _fx;
    private readonly string _server;
    private readonly string _db;
    private readonly string _target;

    public SchemaServiceIntegrationTests(SqlServerFixture fx)
    {
        _fx = fx;
        _server = TestTargets.ServerAlias;
        _db = TestConstants.DatabaseName;
        _target = TestConstants.QualifiedDatabaseName;
    }

    [Fact]
    public async Task ListDatabasesAsync_ShouldIncludeConfiguredDefault()
    {
        var result = await _fx.SchemaService.ListDatabasesAsync(_server, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
        Assert.Contains(_target, result.Value);
    }

    [Fact]
    public async Task SearchDatabasesAsync_ShouldFindDatabase_ByPartialName()
    {
        // Take a 3-letter prefix of the database name and search for it.
        string prefix = _db.Substring(0, Math.Min(3, _db.Length));
        var result = await _fx.SchemaService.SearchDatabasesAsync(_server, prefix, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
        Assert.Contains(_target, result.Value);
    }

    [Fact]
    public async Task SearchObjectsAsync_ShouldFindKnownTable()
    {
        var result = await _fx.SchemaService.SearchObjectsAsync(_target, "FakeProjects", null, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
        Assert.Contains("FakeProjects", result.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetExportObjectsAsync_ShouldReturnTypedCatalog()
    {
        var result = await _fx.SchemaService.GetExportObjectsAsync(_target, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.Contains(result.Value, item => item.Identity.SchemaName == "dbo"
            && item.Identity.ObjectName == "FakeProjects" && item.Identity.Kind == SchemaObjectKind.Table);
        Assert.Contains(result.Value, item => item.Identity.SchemaName == "dbo"
            && item.Identity.ObjectName == "vewFakeProjectList" && item.Identity.Kind == SchemaObjectKind.View);
        Assert.All(result.Value, item => Assert.True(item.Identity.ObjectId > 0));

        var table = Assert.Single(result.Value, item => item.Identity.SchemaName == "dbo" && item.Identity.ObjectName == "FakeProjects");
        var invalidTrigger = new SchemaObject(table.Identity with { Kind = SchemaObjectKind.Trigger }, table.Identity);
        var definition = await _fx.SchemaService.GetExportTriggerDefinitionAsync(_target, invalidTrigger, TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.ObjectNotFound(table.Identity.DisplayName), definition.Error);
    }

    [Fact]
    public async Task SelectedObjects_ResolveQuotedAliasesAndFollowDatabaseCatalogComparison()
    {
        string[] names = ["dbo.FakeProjects", "[dbo].[FakeProjects]", "\"dbo\".\"FakeProjects\"", "DBO.FAKEPROJECTS"];
        var result = await _fx.SchemaService.ResolveObjectsAsync(_target, names, false, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.Equal(names, result.Value.Select(entry => entry.ObjectName));
        var first = result.Value[0].Object.Value;
        Assert.Same(first, result.Value[1].Object.Value);
        Assert.Same(first, result.Value[2].Object.Value);

        await using var connection = _fx.ConnectionFactory.CreateConnection(_db);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var matchingIds = (await connection.QueryAsync<int>(new CommandDefinition(
            "SELECT object_id FROM sys.objects WHERE is_ms_shipped = 0 AND schema_name(schema_id) = @SchemaName AND name = @ObjectName",
            new { SchemaName = "DBO", ObjectName = "FAKEPROJECTS" }, cancellationToken: TestContext.Current.CancellationToken))).ToArray();
        var cased = result.Value[3].Object;
        if (matchingIds.Length == 0)
        {
            Assert.Equal(SqlToAiError.ObjectNotFound(names[3]), cased.Error);
        }
        else if (matchingIds.Length == 1)
        {
            Assert.Equal(matchingIds[0], cased.Value.Identity.ObjectId);
            if (matchingIds[0] == first.Identity.ObjectId) Assert.Same(first, cased.Value);
        }
        else
        {
            Assert.Equal(SqlToAiError.InvalidParametersCode, cased.Error.Code);
        }
    }

    [Fact]
    public async Task GetSchemaAsync_ShouldReturnMarkdown_ForKnownTable()
    {
        var result = await _fx.SchemaService.GetSchemaAsync(_target, "dbo.FakeProjects", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
        // Markdown should mention the table itself and at least one column
        Assert.Contains("FakeProjects", result.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("|", result.Value); // pipe = markdown table column separator
    }

    [Fact]
    public async Task SchemaBatch_RetainsInteractiveScalarMarkdownAliasesAndSuccessfulSiblings()
    {
        string[] names = ["[dbo].[FakeProjects]", "dbo.FakeProjects", "DoesNotExist_Batch_ZZZZ", "[dbo].[vewFakeProjectList]"];
        var batch = await _fx.SchemaService.GetSchemaBatchAsync(_target,
            objectNames: JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(names)), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(batch.IsSuccess, IntegrationAssertions.FormatFailure(batch));
        Assert.False(batch.Value.Complete);
        using var json = JsonDocument.Parse(batch.Value.Json);
        var entries = json.RootElement.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(names, entries.Select(item => item.GetProperty("object_name").GetString()));
        Assert.Equal(["ok", "ok", "error", "ok"], entries.Select(item => item.GetProperty("status").GetString()));
        Assert.Equal(entries[0].GetProperty("markdown").GetString(), entries[1].GetProperty("markdown").GetString());
        var scalar = await _fx.SchemaService.GetSchemaAsync(_target, names[0], TestContext.Current.CancellationToken);
        Assert.True(scalar.IsSuccess, IntegrationAssertions.FormatFailure(scalar));
        Assert.Equal(scalar.Value, entries[0].GetProperty("markdown").GetString());
        Assert.Contains("Anonymized", scalar.Value);
        Assert.Contains("Discovery Index", scalar.Value);
        Assert.Contains("View Definition", entries[3].GetProperty("markdown").GetString());
        Assert.Equal(SqlToAiError.ObjectNotFoundCode, entries[2].GetProperty("error").GetProperty("code").GetString());
        Assert.True(Encoding.UTF8.GetByteCount(batch.Value.Json) <= _fx.Options.SchemaBatch.MaxResponseBytes);
    }

    [Fact]
    public async Task GetSchemaAsync_ShouldReturnMarkdown_ForKnownView()
    {
        var result = await _fx.SchemaService.GetSchemaAsync(_target, "dbo.vewFakeProjectList", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
    }

    [Fact]
    public async Task GetSchemaAsync_ShouldFailCleanly_ForNonExistentObject()
    {
        var result = await _fx.SchemaService.GetSchemaAsync(_target, "dbo.DoesNotExist_ZZZZ", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.ObjectNotFoundCode, result.Error.Code);
    }

    private static int CountMarkdownDataRows(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return 0;
        int headerRows = 0; // any line that starts with '|' AND contains '---' is the header separator
        int pipeRows = 0;  // total lines that start with '|'
        foreach (var line in markdown.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.TrimStart().StartsWith('|')) continue;
            pipeRows++;
            if (line.Contains("---")) headerRows++;
        }
        // Total pipe rows include the column header and (if present) the separator.
        return Math.Max(0, pipeRows - headerRows - 1);
    }
}
