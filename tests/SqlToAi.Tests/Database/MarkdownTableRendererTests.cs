#nullable enable

using SqlToAi.Database;

namespace SqlToAi.Tests.Database;

// @covers SqlToAi.Database.MarkdownTableRenderer
public sealed class MarkdownTableRendererTests
{
    [Fact]
    public void Render_ShouldProduceHeaderAndSeparatorRow()
    {
        var result = MarkdownTableRenderer.Render(
            headers: ["Column A", "Column B"],
            rows: [["x", "y"]]);

        const string expected =
            "| Column A | Column B |\r\n" +
            "| --- | --- |\r\n" +
            "| x | y |\r\n";

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Render_ShouldEscapePipeCharacter_InCellValues()
    {
        var result = MarkdownTableRenderer.Render(
            headers: ["H"],
            rows: [["a|b"]]);

        // Pipe inside a cell value is escaped as `\|` so it does not break the column boundary.
        Assert.Contains("a\\|b", result);
        // The escaped value is still inside a single cell (one column on the row).
        Assert.Equal(
            "| H |\r\n| --- |\r\n| a\\|b |\r\n",
            result);
    }

    [Fact]
    public void Render_ShouldHandleEmptyRows()
    {
        var result = MarkdownTableRenderer.Render(
            headers: ["A", "B"],
            rows: []);

        // Only the header and separator line are emitted — no row lines.
        Assert.Equal(
            "| A | B |\r\n| --- | --- |\r\n",
            result);
    }

    [Fact]
    public void Render_ShouldHandleNullCell()
    {
        // The renderer's signature is `List<string[]>` (non-nullable items), but its
        // internal `r?.Replace("|", "\\|") ?? ""` explicitly handles a null reference
        // — a defense-in-depth for callers that may pass `null!`.
        string[] nullCellRow = new string[] { null! };
        var result = MarkdownTableRenderer.Render(
            headers: ["A"],
            rows: new List<string[]> { nullCellRow });

        // `null` cells are rendered as empty strings (matches the original
        // `r?.Replace("|", "\\|") ?? ""` semantics).
        Assert.Equal(
            "| A |\r\n| --- |\r\n|  |\r\n",
            result);
    }
}
