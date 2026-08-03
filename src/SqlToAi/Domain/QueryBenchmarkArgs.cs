#nullable enable

namespace SqlToAi.Domain;

/// <summary>
/// Arguments container for running a combined optimization benchmark between two SQL queries.
/// </summary>
/// <param name="DatabaseName">Target database name.</param>
/// <param name="QueryA">Baseline SQL query (Query A).</param>
/// <param name="QueryB">Candidate SQL query (Query B).</param>
/// <param name="ParametersA">Optional parameters dictionary for Query A.</param>
/// <param name="ParametersB">Optional parameters dictionary for Query B.</param>
/// <param name="SharedParameters">Optional shared parameters dictionary for both queries.</param>
/// <param name="WarmupRuns">Number of initial unmeasured warmup runs for performance testing (default 1).</param>
/// <param name="ExecutionRuns">Number of measured runs to average for performance testing (default 1).</param>
public sealed record QueryBenchmarkArgs(
    string DatabaseName,
    string QueryA,
    string QueryB,
    object? ParametersA = null,
    object? ParametersB = null,
    object? SharedParameters = null,
    int WarmupRuns = 1,
    int ExecutionRuns = 1);
