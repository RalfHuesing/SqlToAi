#nullable enable

namespace SqlToAi.Domain;

/// <summary>
/// Arguments container for measuring a SQL query's performance.
/// </summary>
/// <param name="DatabaseName">Target database name.</param>
/// <param name="Query">SQL query to measure.</param>
/// <param name="Parameters">Optional parameter object.</param>
/// <param name="WarmupRuns">Number of initial unmeasured warmup runs (default 1).</param>
/// <param name="ExecutionRuns">Number of measured runs to average (default 1).</param>
/// <param name="IncludePlanAnalysis">Whether to attempt XML plan parsing if permissions allow (default true).</param>
public sealed record QueryPerformanceArgs(
    string DatabaseName,
    string Query,
    object? Parameters = null,
    int WarmupRuns = 1,
    int ExecutionRuns = 1,
    bool IncludePlanAnalysis = true);
