#nullable enable

namespace SqlToAi.Domain;

/// <summary>
/// Arguments container for comparing two SQL queries.
/// </summary>
/// <param name="DatabaseName">Target database name.</param>
/// <param name="QueryA">Baseline SQL query (Query A).</param>
/// <param name="QueryB">Candidate SQL query (Query B).</param>
/// <param name="ParametersA">Optional parameters dictionary for Query A.</param>
/// <param name="ParametersB">Optional parameters dictionary for Query B.</param>
/// <param name="SharedParameters">Optional shared parameters dictionary for both queries.</param>
/// <param name="MaxDiffRows">Maximum example diff rows to return when queries differ.</param>
public sealed record QueryComparisonArgs(
    string DatabaseName,
    string QueryA,
    string QueryB,
    object? ParametersA = null,
    object? ParametersB = null,
    object? SharedParameters = null,
    int MaxDiffRows = 5);
