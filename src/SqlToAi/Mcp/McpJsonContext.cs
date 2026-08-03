#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlToAi.Mcp;

/// <summary>
/// Source generator context for JSON serialization/deserialization to support Native AOT.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(JsonRpcRequest))]
[JsonSerializable(typeof(JsonRpcResponse))]
[JsonSerializable(typeof(JsonRpcErrorResponse))]
[JsonSerializable(typeof(JsonRpcError))]
[JsonSerializable(typeof(JsonRpcNotification))]
[JsonSerializable(typeof(InitializeParams))]
[JsonSerializable(typeof(InitializeResult))]
[JsonSerializable(typeof(ClientInfo))]
[JsonSerializable(typeof(ClientCapabilities))]
[JsonSerializable(typeof(ServerInfo))]
[JsonSerializable(typeof(ServerCapabilities))]
[JsonSerializable(typeof(ToolsCapability))]
[JsonSerializable(typeof(ToolListResult))]
[JsonSerializable(typeof(ToolDefinition))]
[JsonSerializable(typeof(ToolInputSchema))]
[JsonSerializable(typeof(ToolParameterDefinition))]
[JsonSerializable(typeof(ToolCallParams))]
[JsonSerializable(typeof(ToolCallResult))]
[JsonSerializable(typeof(ToolContent))]
[JsonSerializable(typeof(EmptyResult))]
[JsonSerializable(typeof(McpCallRecordShape))]
[JsonSerializable(typeof(Domain.QueryComparisonResult))]
[JsonSerializable(typeof(Domain.QueryComparisonArgs))]
[JsonSerializable(typeof(Domain.PerformanceMetrics))]
[JsonSerializable(typeof(Domain.PerformancePlanWarning))]
[JsonSerializable(typeof(Domain.PerformanceMeasurementResult))]
[JsonSerializable(typeof(Domain.QueryPerformanceArgs))]
[JsonSerializable(typeof(Domain.MetricDelta))]
[JsonSerializable(typeof(Domain.BenchmarkMetricsDelta))]
[JsonSerializable(typeof(Domain.OptimizationBenchmarkResult))]
[JsonSerializable(typeof(Domain.QueryBenchmarkArgs))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonElement?))]
[JsonSerializable(typeof(short))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(byte))]
internal partial class McpJsonContext : JsonSerializerContext;
