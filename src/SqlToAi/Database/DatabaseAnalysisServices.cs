#nullable enable

namespace SqlToAi.Database;

/// <summary>
/// Bundles performance measurement, query comparison, optimization benchmark, and index suggestion services.
/// </summary>
public sealed record DatabaseAnalysisServices(
    IPerformanceMeasurementService PerformanceMeasurement,
    IQueryComparisonService QueryComparison,
    IOptimizationBenchmarkService Benchmark,
    IIndexSuggestionService IndexSuggestion);
