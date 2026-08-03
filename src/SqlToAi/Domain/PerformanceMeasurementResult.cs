#nullable enable

using System.Text.Json.Serialization;

namespace SqlToAi.Domain;

/// <summary>
/// Hard performance metrics captured from SQL Server STATISTICS IO and TIME.
/// </summary>
public sealed record PerformanceMetrics(
    [property: JsonPropertyName("cpu_time_ms")] long CpuTimeMs,
    [property: JsonPropertyName("elapsed_time_ms")] long ElapsedTimeMs,
    [property: JsonPropertyName("logical_reads")] long LogicalReads,
    [property: JsonPropertyName("physical_reads")] long PhysicalReads,
    [property: JsonPropertyName("read_ahead_reads")] long ReadAheadReads);

/// <summary>
/// A single warning or insight extracted from an actual execution plan XML.
/// </summary>
public sealed record PerformancePlanWarning(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("impact")] double? Impact);

/// <summary>
/// Overall result of measuring a query's performance on SQL Server.
/// </summary>
public sealed record PerformanceMeasurementResult(
    [property: JsonPropertyName("database")] string Database,
    [property: JsonPropertyName("runs_evaluated")] int RunsEvaluated,
    [property: JsonPropertyName("warmup_runs")] int WarmupRuns,
    [property: JsonPropertyName("metrics")] PerformanceMetrics Metrics,
    [property: JsonPropertyName("warnings")] IReadOnlyList<PerformancePlanWarning> Warnings,
    [property: JsonPropertyName("has_showplan_permission")] bool HasShowplanPermission,
    [property: JsonPropertyName("showplan_note")] string? ShowplanNote);
