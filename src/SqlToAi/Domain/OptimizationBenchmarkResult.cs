#nullable enable

using System.Text.Json.Serialization;

namespace SqlToAi.Domain;

/// <summary>
/// Metric delta comparison between baseline (Query A) and candidate (Query B).
/// Negative percentage indicates performance improvement (reduction in resources).
/// </summary>
public sealed record MetricDelta(
    [property: JsonPropertyName("baseline_value")] long BaselineValue,
    [property: JsonPropertyName("candidate_value")] long CandidateValue,
    [property: JsonPropertyName("absolute_delta")] long AbsoluteDelta,
    [property: JsonPropertyName("percentage_delta")] double PercentageDelta);

/// <summary>
/// Consolidated metric deltas for all key database performance indicators.
/// </summary>
public sealed record BenchmarkMetricsDelta(
    [property: JsonPropertyName("cpu_time")] MetricDelta CpuTime,
    [property: JsonPropertyName("elapsed_time")] MetricDelta ElapsedTime,
    [property: JsonPropertyName("logical_reads")] MetricDelta LogicalReads,
    [property: JsonPropertyName("physical_reads")] MetricDelta PhysicalReads);

/// <summary>
/// Result of the combined optimization benchmark, evaluating equivalence, performance deltas, and recommendation verdict.
/// </summary>
public sealed record OptimizationBenchmarkResult(
    [property: JsonPropertyName("database")] string Database,
    [property: JsonPropertyName("verdict")] string Verdict,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("comparison")] QueryComparisonResult Comparison,
    [property: JsonPropertyName("performance_a")] PerformanceMeasurementResult PerformanceA,
    [property: JsonPropertyName("performance_b")] PerformanceMeasurementResult PerformanceB,
    [property: JsonPropertyName("deltas")] BenchmarkMetricsDelta Deltas);
