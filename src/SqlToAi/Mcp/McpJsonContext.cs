using System.Text.Json;
using System.Text.Json.Serialization;
using SqlToAi.Database;

namespace SqlToAi.Mcp;

/// <summary>
/// Source generator context for JSON serialization/deserialization to support Native AOT.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(ToolDefinition))]
[JsonSerializable(typeof(ToolInputSchema))]
[JsonSerializable(typeof(ToolParameterDefinition))]
[JsonSerializable(typeof(ToolCallParams))]
[JsonSerializable(typeof(ToolCallResult))]
[JsonSerializable(typeof(ToolContent))]
[JsonSerializable(typeof(SchemaExportToolResponse))]
[JsonSerializable(typeof(DataExportResult))]
[JsonSerializable(typeof(Domain.DatabaseEnvironmentResult))]
[JsonSerializable(typeof(Domain.ExplainQueryResult))]
[JsonSerializable(typeof(Domain.ExplainPlanNode))]
[JsonSerializable(typeof(Domain.ExplainPlanObject))]
[JsonSerializable(typeof(Domain.ExplainPlanWarning))]
[JsonSerializable(typeof(IReadOnlyList<Domain.ExplainPlanNode>))]
[JsonSerializable(typeof(IReadOnlyList<Domain.ExplainPlanWarning>))]
[JsonSerializable(typeof(Domain.ExplainPlanNode[]))]
[JsonSerializable(typeof(Domain.ExplainPlanWarning[]))]
[JsonSerializable(typeof(ExportColumnInfo))]
[JsonSerializable(typeof(IReadOnlyList<ExportColumnInfo>))]
[JsonSerializable(typeof(ExportColumnInfo[]))]


[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(string[]))]
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
internal sealed partial class McpJsonContext : JsonSerializerContext;
