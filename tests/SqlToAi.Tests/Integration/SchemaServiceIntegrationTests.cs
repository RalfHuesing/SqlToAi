using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(SqlServerCollectionFixture.Name)]
public sealed class SchemaServiceIntegrationTests
{
    private readonly SqlServerFixture _fx;
    private readonly string _db;

    public SchemaServiceIntegrationTests(SqlServerFixture fx)
    {
        _fx = fx;
        _db = TestConstants.DatabaseName;
    }

    [Fact]
    public async Task ListDatabasesAsync_ShouldIncludeConfiguredDefault()
    {
        var result = await _fx.SchemaService.ListDatabasesAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
        Assert.Contains(_db, result.Value);
    }

    [Fact]
    public async Task SearchDatabasesAsync_ShouldFindDatabase_ByPartialName()
    {
        // Take a 3-letter prefix of the database name and search for it.
        string prefix = _db.Substring(0, Math.Min(3, _db.Length));
        var result = await _fx.SchemaService.SearchDatabasesAsync(prefix, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
    }

    [Fact]
    public async Task SearchObjectsAsync_ShouldFindKnownTable()
    {
        var result = await _fx.SchemaService.SearchObjectsAsync(_db, "FakeProjects", null, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
        Assert.Contains("FakeProjects", result.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetExportObjectsAsync_ShouldReturnTypedCatalog()
    {
        var result = await _fx.SchemaService.GetExportObjectsAsync(_db, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.Contains(result.Value, item => item.Identity.SchemaName == "dbo"
            && item.Identity.ObjectName == "FakeProjects" && item.Identity.Kind == SchemaObjectKind.Table);
        Assert.Contains(result.Value, item => item.Identity.SchemaName == "dbo"
            && item.Identity.ObjectName == "vewFakeProjectList" && item.Identity.Kind == SchemaObjectKind.View);
        Assert.All(result.Value, item => Assert.True(item.Identity.ObjectId > 0));

        var table = Assert.Single(result.Value, item => item.Identity.SchemaName == "dbo" && item.Identity.ObjectName == "FakeProjects");
        var invalidTrigger = new SchemaObject(table.Identity with { Kind = SchemaObjectKind.Trigger }, table.Identity);
        var definition = await _fx.SchemaService.GetExportTriggerDefinitionAsync(_db, invalidTrigger, TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.ObjectNotFound(table.Identity.DisplayName), definition.Error);
    }

    [Fact]
    public async Task GetSchemaAsync_ShouldReturnMarkdown_ForKnownTable()
    {
        var result = await _fx.SchemaService.GetSchemaAsync(_db, "dbo.FakeProjects", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
        // Markdown should mention the table itself and at least one column
        Assert.Contains("FakeProjects", result.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("|", result.Value); // pipe = markdown table column separator
    }

    [Fact]
    public async Task GetSchemaAsync_ShouldReturnMarkdown_ForKnownView()
    {
        var result = await _fx.SchemaService.GetSchemaAsync(_db, "dbo.vewFakeProjectList", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
    }

    [Fact]
    public async Task GetSchemaAsync_ShouldFailCleanly_ForNonExistentObject()
    {
        var result = await _fx.SchemaService.GetSchemaAsync(_db, "dbo.DoesNotExist_ZZZZ", TestContext.Current.CancellationToken);

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
