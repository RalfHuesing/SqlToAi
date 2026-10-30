using System.Text.Json;
using SqlToAi.Domain;
using SqlToAi.Mcp;

namespace SqlToAi.Tests.Domain;

public sealed class ExplainQueryResultSerializationTests
{
    [Fact]
    public void ExplainQueryResult_SerializesThroughMcpJsonContext_WithExpectedShape()
    {
        var rootNode = new ExplainPlanNode(
            NodeId: 0,
            ParentId: null, // Root node must have null parent_id
            PhysicalOp: "Compute Scalar",
            LogicalOp: "Compute Scalar",
            EstimatedRows: 1.0,
            SubtreeCost: 0.000001,
            Object: null,
            OmittedChildren: null);

        var childNode = new ExplainPlanNode(
            NodeId: 1,
            ParentId: 0,
            PhysicalOp: "Index Seek",
            LogicalOp: "Index Seek",
            EstimatedRows: 10.0,
            SubtreeCost: 0.00328,
            Object: new ExplainPlanObject(Schema: "dbo", Table: "Orders", Index: "IX_Orders_CustomerId"),
            OmittedChildren: 3);

        var missingIndexWarning = new ExplainPlanWarning(
            Kind: "missing_index",
            Schema: "dbo",
            Table: "Orders",
            Impact: 89.5,
            KeyColumns: ["CustomerId", "OrderDate"],
            IncludeColumns: ["TotalAmount"]);

        var conversionWarning = new ExplainPlanWarning(
            Kind: "plan_affecting_convert",
            ConversionKind: "implicit",
            Column: "CustomerId");

        var result = new ExplainQueryResult(
            Target: "primary/TestDb",
            PlanKind: "estimated",
            Truncated: true,
            Nodes: [rootNode, childNode],
            Warnings: [missingIndexWarning, conversionWarning]);

        // Act - McpJsonContext
        string json = JsonSerializer.Serialize(result, McpJsonContext.Default.ExplainQueryResult);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("primary/TestDb", root.GetProperty("target").GetString());
        Assert.Equal("estimated", root.GetProperty("plan_kind").GetString());
        Assert.True(root.GetProperty("truncated").GetBoolean());

        var nodes = root.GetProperty("nodes").EnumerateArray().ToList();
        Assert.Equal(2, nodes.Count);

        // Root node checks: parent_id must be serialized as null!
        var node0 = nodes[0];
        Assert.Equal(0, node0.GetProperty("node_id").GetInt32());
        Assert.Equal(JsonValueKind.Null, node0.GetProperty("parent_id").ValueKind);
        Assert.Equal("Compute Scalar", node0.GetProperty("physical_op").GetString());
        Assert.Equal("Compute Scalar", node0.GetProperty("logical_op").GetString());
        Assert.Equal(1.0, node0.GetProperty("estimated_rows").GetDouble());
        Assert.False(node0.TryGetProperty("object", out _), "null object must be omitted");
        Assert.False(node0.TryGetProperty("omitted_children", out _), "null omitted_children must be omitted");

        // Child node checks
        var node1 = nodes[1];
        Assert.Equal(1, node1.GetProperty("node_id").GetInt32());
        Assert.Equal(0, node1.GetProperty("parent_id").GetInt32());
        Assert.Equal("Index Seek", node1.GetProperty("physical_op").GetString());
        Assert.Equal("Index Seek", node1.GetProperty("logical_op").GetString());
        Assert.Equal(10.0, node1.GetProperty("estimated_rows").GetDouble());
        Assert.Equal(3, node1.GetProperty("omitted_children").GetInt32());

        var obj = node1.GetProperty("object");
        Assert.Equal("dbo", obj.GetProperty("schema").GetString());
        Assert.Equal("Orders", obj.GetProperty("table").GetString());
        Assert.Equal("IX_Orders_CustomerId", obj.GetProperty("index").GetString());

        // Warnings checks
        var warnings = root.GetProperty("warnings").EnumerateArray().ToList();
        Assert.Equal(2, warnings.Count);

        var w0 = warnings[0];
        Assert.Equal("missing_index", w0.GetProperty("kind").GetString());
        Assert.Equal("dbo", w0.GetProperty("schema").GetString());
        Assert.Equal("Orders", w0.GetProperty("table").GetString());
        Assert.Equal(89.5, w0.GetProperty("impact").GetDouble());
        Assert.Equal(["CustomerId", "OrderDate"], w0.GetProperty("key_columns").EnumerateArray().Select(e => e.GetString()).ToList());
        Assert.Equal(["TotalAmount"], w0.GetProperty("include_columns").EnumerateArray().Select(e => e.GetString()).ToList());
        Assert.False(w0.TryGetProperty("conversion_kind", out _));
        Assert.False(w0.TryGetProperty("column", out _));

        var w1 = warnings[1];
        Assert.Equal("plan_affecting_convert", w1.GetProperty("kind").GetString());
        Assert.Equal("implicit", w1.GetProperty("conversion_kind").GetString());
        Assert.Equal("CustomerId", w1.GetProperty("column").GetString());
        Assert.False(w1.TryGetProperty("impact", out _));
        Assert.False(w1.TryGetProperty("key_columns", out _));
    }

    [Fact]
    public void ExplainQueryResult_SerializesThroughMcpAnalysisJsonContext()
    {
        var node = new ExplainPlanNode(
            NodeId: 0,
            ParentId: null,
            PhysicalOp: "Clustered Index Scan",
            LogicalOp: "Clustered Index Scan",
            EstimatedRows: 500.0,
            SubtreeCost: 0.05,
            Object: new ExplainPlanObject(Schema: "dbo", Table: "Customers", Index: "PK_Customers"));

        var result = new ExplainQueryResult(
            Target: "primary/TestDb",
            PlanKind: "estimated",
            Truncated: false,
            Nodes: [node],
            Warnings: []);

        // Act - McpAnalysisJsonContext
        string json = JsonSerializer.Serialize(result, McpAnalysisJsonContext.Default.ExplainQueryResult);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("primary/TestDb", root.GetProperty("target").GetString());
        Assert.Equal("estimated", root.GetProperty("plan_kind").GetString());
        Assert.False(root.GetProperty("truncated").GetBoolean());
        Assert.Empty(root.GetProperty("warnings").EnumerateArray().ToList());

        var deserialized = JsonSerializer.Deserialize(json, McpAnalysisJsonContext.Default.ExplainQueryResult);
        Assert.NotNull(deserialized);
        Assert.Equal("primary/TestDb", deserialized.Target);
        Assert.Null(deserialized.Nodes[0].ParentId);
        Assert.Equal("PK_Customers", deserialized.Nodes[0].Object?.Index);
    }
}
