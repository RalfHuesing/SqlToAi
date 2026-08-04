#nullable enable

using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Retrieves server-wide cumulative missing-index recommendations from the
/// <c>sys.dm_db_missing_index_*</c> DMVs, prioritized by
/// <c>improvement_score</c>, with graceful degradation when the login lacks
/// <c>VIEW SERVER STATE</c>.
/// </summary>
public interface IIndexSuggestionService
{
    /// <summary>
    /// Retrieves missing-index recommendations using a parameter object.
    /// </summary>
    /// <param name="args">Suggestion options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Result{T}"/> containing a Markdown document with a
    /// restart-hint preamble and a table of recommendations (score, table, equality/
    /// inequality/include columns, seeks, scans, last-seek timestamp). On a
    /// permission failure, the document is replaced with a structured note.</returns>
    Task<Result<string>> SuggestIndexesAsync(
        IndexSuggestionArgs args,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Convenience overload that builds the args record from individual parameters.
    /// </summary>
    Task<Result<string>> SuggestIndexesAsync(
        string databaseName,
        string? tableName = null,
        double? minScore = null,
        int? top = null,
        CancellationToken cancellationToken = default);
}
