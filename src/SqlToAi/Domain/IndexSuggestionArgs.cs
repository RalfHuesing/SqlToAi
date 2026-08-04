#nullable enable

namespace SqlToAi.Domain;

/// <summary>
/// Arguments container for retrieving server-wide cumulative missing-index
/// recommendations from <c>sys.dm_db_missing_index_*</c> DMVs.
/// </summary>
/// <param name="DatabaseName">Target database name.</param>
/// <param name="TableName">Optional LIKE substring filter applied to the DMV <c>statement</c> column (e.g. <c>Orders</c> or <c>dbo.%</c>).</param>
/// <param name="MinScore">Optional minimum <c>improvement_score</c> threshold; rows below it are excluded. <c>null</c> = no threshold.</param>
/// <param name="Top">Maximum number of recommendations to return (default 10).</param>
public sealed record IndexSuggestionArgs(
    string DatabaseName,
    string? TableName = null,
    double? MinScore = null,
    int Top = 10);
