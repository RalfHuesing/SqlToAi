#nullable enable

using System.Collections.Generic;

namespace SqlToAi.Domain;

/// <summary>
/// Contains the query output data along with details of any anonymization applied.
/// </summary>
/// <param name="SearchableTokenColumns">
/// The subset of <paramref name="AnonymizedColumns"/> that received a reversible, searchable
/// token instead of a scramble/hash mask — the caller can reuse those exact values verbatim in a
/// later query's <c>WHERE</c>/<c>JOIN</c>/<c>LIKE</c>/range predicate.
/// </param>
public sealed record QueryExecutionResult(
    string Data,
    bool WasAnonymized,
    IReadOnlyList<string> AnonymizedColumns,
    string AnonymizationMode,
    IReadOnlyList<string> SearchableTokenColumns = null!)
{
    public IReadOnlyList<string> SearchableTokenColumns { get; init; } = SearchableTokenColumns ?? [];
}
