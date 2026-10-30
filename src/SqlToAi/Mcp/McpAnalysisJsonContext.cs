using System.Text.Json.Serialization;

namespace SqlToAi.Mcp;

/// <summary>
/// Dedicated source generator context for query analysis and benchmark results.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(Domain.QueryComparisonResult))]
[JsonSerializable(typeof(Domain.DatabaseEnvironmentResult))]
[JsonSerializable(typeof(Domain.ExplainQueryResult))]
[JsonSerializable(typeof(Domain.ExplainPlanNode))]
[JsonSerializable(typeof(Domain.ExplainPlanObject))]
[JsonSerializable(typeof(Domain.ExplainPlanWarning))]
[JsonSerializable(typeof(IReadOnlyList<Domain.ExplainPlanNode>))]
[JsonSerializable(typeof(IReadOnlyList<Domain.ExplainPlanWarning>))]
[JsonSerializable(typeof(Domain.ExplainPlanNode[]))]
[JsonSerializable(typeof(Domain.ExplainPlanWarning[]))]
[JsonSerializable(typeof(Domain.QueryComparisonArgs))]
[JsonSerializable(typeof(Domain.PerformanceMetrics))]
[JsonSerializable(typeof(Domain.PerformancePlanWarning))]
[JsonSerializable(typeof(Domain.PerformanceMeasurementResult))]
[JsonSerializable(typeof(Domain.QueryPerformanceArgs))]
[JsonSerializable(typeof(Domain.MetricDelta))]
[JsonSerializable(typeof(Domain.BenchmarkMetricsDelta))]
[JsonSerializable(typeof(Domain.OptimizationBenchmarkResult))]
[JsonSerializable(typeof(Domain.QueryBenchmarkArgs))]
internal sealed partial class McpAnalysisJsonContext : JsonSerializerContext;
