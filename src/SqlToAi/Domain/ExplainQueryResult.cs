using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SqlToAi.Domain;

public sealed record ExplainQueryResult(
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("plan_kind")] string PlanKind,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("nodes")] IReadOnlyList<ExplainPlanNode> Nodes,
    [property: JsonPropertyName("warnings")] IReadOnlyList<ExplainPlanWarning> Warnings);

public sealed record ExplainPlanNode(
    [property: JsonPropertyName("node_id")] int NodeId,
    [property: JsonPropertyName("parent_id"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? ParentId,
    [property: JsonPropertyName("physical_op")] string PhysicalOp,
    [property: JsonPropertyName("logical_op")] string LogicalOp,
    [property: JsonPropertyName("estimated_rows")] double EstimatedRows,
    [property: JsonPropertyName("subtree_cost")] double SubtreeCost,
    [property: JsonPropertyName("object")] ExplainPlanObject? Object = null,
    [property: JsonPropertyName("omitted_children")] int? OmittedChildren = null);

public sealed record ExplainPlanObject(
    [property: JsonPropertyName("schema")] string? Schema = null,
    [property: JsonPropertyName("table")] string? Table = null,
    [property: JsonPropertyName("index")] string? Index = null);

public sealed record ExplainPlanWarning(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("schema")] string? Schema = null,
    [property: JsonPropertyName("table")] string? Table = null,
    [property: JsonPropertyName("impact")] double? Impact = null,
    [property: JsonPropertyName("key_columns")] IReadOnlyList<string>? KeyColumns = null,
    [property: JsonPropertyName("include_columns")] IReadOnlyList<string>? IncludeColumns = null,
    [property: JsonPropertyName("conversion_kind")] string? ConversionKind = null,
    [property: JsonPropertyName("column")] string? Column = null);
