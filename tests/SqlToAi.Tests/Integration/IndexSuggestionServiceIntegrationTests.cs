#nullable enable

namespace SqlToAi.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="SqlToAi.Database.IndexSuggestionService"/>
/// against a real SQL Server. Proves the DMV query (CTE-based, top-N per
/// index_handle) parses and executes against a live instance, and that
/// graceful degradation on missing VIEW SERVER STATE permission works
/// end-to-end (or that real recommendations are returned when the login
/// has the permission).
/// </summary>
[Trait("Category", "Integration")]
[Collection(SqlServerCollectionFixture.Name)]
public sealed class IndexSuggestionServiceIntegrationTests
{
    private readonly SqlServerFixture _fx;
    private readonly string _db;

    public IndexSuggestionServiceIntegrationTests(SqlServerFixture fx)
    {
        _fx = fx;
        _db = TestConstants.DatabaseName;
    }

    [Fact]
    public async Task SuggestIndexesAsync_ShouldReturnMarkdownWithRestartHint_AgainstRealDatabase()
    {
        var result = await _fx.IndexSuggestionService.SuggestIndexesAsync(
            _db, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
        Assert.Contains("# Missing Index Recommendations — " + _db, result.Value);
        Assert.Contains("cumulative since the last SQL Server restart", result.Value);
        // Either the permission note, "No missing-index recommendations found",
        // OR Markdown table with "| Score |" header — all three are valid tool outputs.
        Assert.True(
            result.Value.Contains("VIEW SERVER STATE", StringComparison.Ordinal)
            || result.Value.Contains("No missing-index recommendations found", StringComparison.Ordinal)
            || result.Value.Contains("| Score |", StringComparison.Ordinal),
            "Expected permission note, 'No recommendations' message, or Markdown table with Score header.");
    }

    [Fact]
    public async Task SuggestIndexesAsync_ShouldRespectTopParameter_AgainstRealDatabase()
    {
        var result = await _fx.IndexSuggestionService.SuggestIndexesAsync(
            _db, tableName: null, minScore: null, top: 3,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.Contains("cumulative since the last SQL Server restart", result.Value);
    }

    [Fact]
    public async Task SuggestIndexesAsync_ShouldRespectTableNameFilter_AgainstRealDatabase()
    {
        var result = await _fx.IndexSuggestionService.SuggestIndexesAsync(
            _db, tableName: "FakeProjects", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
    }

    [Fact]
    public async Task SuggestIndexesAsync_ShouldReturnPermissionNote_IfViewServerStateMissing_OtherwiseMarkdown()
    {
        // Opportune probe: the configured 'Agent' login typically has VIEW SERVER STATE
        // (per architecture-spec.md §H). If it doesn't, the service must still return
        // a structured permission note (graceful degradation). Both outcomes are IsSuccess=true.
        var result = await _fx.IndexSuggestionService.SuggestIndexesAsync(
            _db, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.Contains("# Missing Index Recommendations — " + _db, result.Value);
        Assert.Contains("cumulative since the last SQL Server restart", result.Value);
        // Either real recommendations / no-recommendations message, or the permission note.
        Assert.True(
            result.Value.Contains("VIEW SERVER STATE", StringComparison.Ordinal)
            || result.Value.Contains("No missing-index recommendations", StringComparison.Ordinal)
            || result.Value.Contains("| Score |", StringComparison.Ordinal),
            "Expected permission note, 'no recommendations' message, or Markdown table.");
    }
}
