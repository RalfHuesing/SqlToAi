using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Mcp;
using Xunit;

namespace SqlToAi.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(SqlServerCollectionFixture.Name)]
public sealed class ShowplanXmlParserIntegrationTests
{
    private readonly SqlServerFixture _fx;
    private readonly string _target;

    public ShowplanXmlParserIntegrationTests(SqlServerFixture fx)
    {
        _fx = fx;
        _target = TestConstants.QualifiedDatabaseName;
    }

    [Fact]
    public async Task Parse_RealSqlServerPlan_ForDeclareAndSelect_ReturnsValidNodesAndNoSensitiveData()
    {
        const string secretLiteral = "SensitiveProjectName42";
        var query = $"DECLARE @name nvarchar(50) = '{secretLiteral}'; SELECT ProjectName, Mandant FROM dbo.FakeProjects WHERE ProjectName = @name";

        var execResult = await _fx.ShowplanExecutionService.ExecuteShowplanAsync(
            _target,
            query,
            null,
            TestContext.Current.CancellationToken);

        Assert.True(execResult.IsSuccess, IntegrationAssertions.FormatFailure(execResult));
        Assert.NotNull(execResult.Value);

        var parseResult = _fx.ShowplanXmlParser.Parse(execResult.Value, _target);

        Assert.True(parseResult.IsSuccess, parseResult.IsFailure ? parseResult.Error.Message : null);
        var plan = parseResult.Value;

        Assert.Equal(_target, plan.Target);
        Assert.Equal("estimated", plan.PlanKind);
        Assert.False(plan.Truncated);
        Assert.NotEmpty(plan.Nodes);

        // Root must have parent_id = null
        var root = plan.Nodes[0];
        Assert.Null(root.ParentId);

        // The query touches FakeProjects; at least one node must identify that object
        var tableNode = plan.Nodes.FirstOrDefault(n => n.Object?.Table == "FakeProjects");
        Assert.NotNull(tableNode);
        Assert.Equal("dbo", tableNode.Object?.Schema);

        // Strict plan data protection check on real SQL Server output
        string json = JsonSerializer.Serialize(plan, McpJsonContext.Default.ExplainQueryResult);
        Assert.DoesNotContain(secretLiteral, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StatementText", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ParameterCompiledValue", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@name", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Parse_RealSqlServerPlan_ForJoinQuery_BuildsMultiNodeHierarchy()
    {
        var query = """
            SELECT p1.ProjectName, p1.Mandant, p2.Status
            FROM dbo.FakeProjects p1
            INNER JOIN dbo.FakeProjects p2 ON p1.ProjectName = p2.ProjectName
            """;

        var execResult = await _fx.ShowplanExecutionService.ExecuteShowplanAsync(
            _target,
            query,
            null,
            TestContext.Current.CancellationToken);

        Assert.True(execResult.IsSuccess, IntegrationAssertions.FormatFailure(execResult));
        Assert.NotNull(execResult.Value);

        var parseResult = _fx.ShowplanXmlParser.Parse(execResult.Value, _target);

        Assert.True(parseResult.IsSuccess, parseResult.IsFailure ? parseResult.Error.Message : null);
        var plan = parseResult.Value;

        Assert.True(plan.Nodes.Count >= 2, $"Expected at least 2 nodes in join plan, got {plan.Nodes.Count}");
        var root = plan.Nodes[0];
        Assert.Null(root.ParentId);

        // All child nodes must reference valid visible parents
        var nodeIds = plan.Nodes.Select(n => n.NodeId).ToHashSet();
        foreach (var node in plan.Nodes.Skip(1))
        {
            Assert.NotNull(node.ParentId);
            Assert.Contains(node.ParentId.Value, nodeIds);
        }
    }
}
