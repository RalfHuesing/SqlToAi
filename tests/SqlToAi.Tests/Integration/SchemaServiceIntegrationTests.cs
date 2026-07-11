#nullable enable

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
        _db = fx.Options.Databases.Default;
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
        var result = await _fx.SchemaService.SearchObjectsAsync(_db, "BCSPjmProjekte", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
        Assert.Contains("BCSPjmProjekte", result.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSchemaAsync_ShouldReturnMarkdown_ForKnownTable()
    {
        var result = await _fx.SchemaService.GetSchemaAsync(_db, "dbo.BCSPjmProjekte", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
        // Markdown should mention the table itself and at least one column
        Assert.Contains("BCSPjmProjekte", result.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("|", result.Value); // pipe = markdown table column separator
    }

    [Fact]
    public async Task GetSchemaAsync_ShouldReturnMarkdown_ForKnownView()
    {
        var result = await _fx.SchemaService.GetSchemaAsync(_db, "dbo.vewBCSPjmProjektliste", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
    }

    [Fact]
    public async Task GetSchemaForeignKeysAsync_ShouldReturnResult_ForTable()
    {
        var result = await _fx.SchemaService.GetSchemaForeignKeysAsync(_db, "dbo.BCSPjmProjekte", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        // May be empty if the table has no FKs, but the call must succeed
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task GetSchemaIndexesAsync_ShouldReturnAtLeastOneIndex()
    {
        var result = await _fx.SchemaService.GetSchemaIndexesAsync(_db, "dbo.BCSPjmProjekte", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
    }

    [Fact]
    public async Task GetSchemaConstraintsAsync_ShouldNotFail()
    {
        var result = await _fx.SchemaService.GetSchemaConstraintsAsync(_db, "dbo.BCSPjmProjekte", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task GetRoutineParametersAsync_ShouldReturnParameters_ForKnownProcedure()
    {
        var result = await _fx.SchemaService.GetRoutineParametersAsync(_db, "dbo.spSysTan", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
    }

    [Fact]
    public async Task GetObjectReferencesAsync_ShouldReturnResult_ForKnownTable()
    {
        var result = await _fx.SchemaService.GetObjectReferencesAsync(_db, "dbo.BCSPjmProjekte", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task GetSchemaAsync_ShouldFailCleanly_ForNonExistentObject()
    {
        var result = await _fx.SchemaService.GetSchemaAsync(_db, "dbo.DoesNotExist_ZZZZ", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.ObjectNotFoundCode, result.Error.Code);
    }

    [Fact]
    public async Task SearchObjectsAsync_ShouldRespectMaxResults()
    {
        var result = await _fx.SchemaService.SearchObjectsAsync(_db, "BCS", 2, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        // The markdown has a header row plus N data rows. Subtract the header to get the
        // actual number of object matches and confirm maxResults was honored.
        int dataRows = CountMarkdownDataRows(result.Value);
        Assert.True(dataRows <= 2, $"Expected at most 2 data rows, got {dataRows}");
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
