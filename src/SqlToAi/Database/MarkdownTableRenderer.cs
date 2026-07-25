#nullable enable

using System.Text;

namespace SqlToAi.Database;

/// <summary>
/// Renders an in-memory table (headers + rows of cell strings) as a GitHub-flavored
/// Markdown pipe-table, with the only required escaping being the pipe character
/// inside cell values (newlines and other Markdown-significant characters are
/// intentionally NOT escaped — cell content is trusted, single-line content).
/// <para>
/// This is the single source of truth for the table format used by
/// <see cref="SchemaService"/>, <see cref="DetailSchemaRenderer"/>, and
/// <see cref="TableSchemaRenderer"/> — all three previously carried a textually
/// identical private <c>RenderMarkdownTable</c> copy, which is now consolidated here
/// to prevent accidental format divergence.
/// </para>
/// </summary>
internal static class MarkdownTableRenderer
{
    /// <summary>
    /// Builds a Markdown pipe-table from the given headers and rows. The output is
    /// byte-identical to the three former private copies (same header line, same
    /// <c>---</c> separator, same row layout, same <c>|</c>-escaping inside cells).
    /// </summary>
    /// <param name="headers">Column headers, in column order. Not escaped.</param>
    /// <param name="rows">Cell rows; each inner array must have the same length as
    /// <paramref name="headers"/>. <c>null</c> cells are rendered as empty strings.</param>
    /// <returns>The full table including the trailing newline of the last row.</returns>
    public static string Render(string[] headers, List<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append("| ").Append(string.Join(" | ", headers)).AppendLine(" |");
        sb.Append("| ").Append(string.Join(" | ", headers.Select(_ => "---"))).AppendLine(" |");
        foreach (var row in rows)
        {
            sb.Append("| ").Append(string.Join(" | ", row.Select(r => r?.Replace("|", "\\|") ?? ""))).AppendLine(" |");
        }
        return sb.ToString();
    }
}
