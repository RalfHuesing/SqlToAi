using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SqlToAi.Domain;

/// <summary>
/// Result of an estimated query plan analysis containing operator hierarchy and compiler warnings.
/// </summary>
public sealed record ExplainQueryResult(
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("plan_kind")] string PlanKind,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("nodes")] IReadOnlyList<ExplainPlanNode> Nodes,
    [property: JsonPropertyName("warnings")] IReadOnlyList<ExplainPlanWarning> Warnings);

/// <summary>
/// A single operator node within the estimated execution plan tree.
/// </summary>
public sealed record ExplainPlanNode(
    [property: JsonPropertyName("node_id")] int NodeId,
    [property: JsonPropertyName("parent_id"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? ParentId,
    [property: JsonPropertyName("physical_op")] string PhysicalOp,
    [property: JsonPropertyName("logical_op")] string LogicalOp,
    [property: JsonPropertyName("estimated_rows")] double EstimatedRows,
    [property: JsonPropertyName("subtree_cost")] double SubtreeCost,
    [property: JsonPropertyName("object")] ExplainPlanObject? Object = null,
    [property: JsonPropertyName("omitted_children")] int? OmittedChildren = null);

/// <summary>
/// Identifying schema, table, and index metadata for an operator referencing a database object.
/// </summary>
public sealed record ExplainPlanObject(
    [property: JsonPropertyName("schema")] string? Schema = null,
    [property: JsonPropertyName("table")] string? Table = null,
    [property: JsonPropertyName("index")] string? Index = null);

/// <summary>
/// Compiler warning extracted from showplan XML, such as missing indexes or plan-affecting conversions.
/// </summary>
public sealed record ExplainPlanWarning(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("schema")] string? Schema = null,
    [property: JsonPropertyName("table")] string? Table = null,
    [property: JsonPropertyName("impact")] double? Impact = null,
    [property: JsonPropertyName("key_columns")] IReadOnlyList<string>? KeyColumns = null,
    [property: JsonPropertyName("include_columns")] IReadOnlyList<string>? IncludeColumns = null,
    [property: JsonPropertyName("conversion_kind")] string? ConversionKind = null,
    [property: JsonPropertyName("column")] string? Column = null);
