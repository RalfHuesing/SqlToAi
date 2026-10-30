using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Mcp;
using SqlToAi.Security;
using Xunit;

namespace SqlToAi.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(SqlServerCollectionFixture.Name)]
public sealed class ExplainQueryServiceIntegrationTests
{
    private readonly SqlServerFixture _fx;
    private readonly string _target;

    public ExplainQueryServiceIntegrationTests(SqlServerFixture fx)
    {
        _fx = fx;
        _target = TestConstants.QualifiedDatabaseName;
    }

    [Fact]
    public async Task ExplainQueryAsync_DeclareAndSelect_ReturnsPlanOfSelectOnly()
    {
        const string query = "DECLARE @status nvarchar(20) = 'Active'; SELECT ProjectName, Mandant FROM dbo.FakeProjects WHERE Status = @status";

        var result = await _fx.ExplainQueryService.ExplainQueryAsync(
            _target,
            query,
            null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        var plan = result.Value;

        Assert.Equal(_target, plan.Target);
        Assert.Equal("estimated", plan.PlanKind);
        Assert.False(plan.Truncated);
        Assert.NotEmpty(plan.Nodes);
        Assert.Null(plan.Nodes[0].ParentId);

        var tableNode = plan.Nodes.FirstOrDefault(n => n.Object?.Table == "FakeProjects");
        Assert.NotNull(tableNode);
        Assert.Equal("dbo", tableNode.Object?.Schema);

        // Verify sensitive parameter and statement text are not leaked
        string json = JsonSerializer.Serialize(plan, McpJsonContext.Default.ExplainQueryResult);
        Assert.DoesNotContain("StatementText", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@status", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Active", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExplainQueryAsync_ParameterizedSelect_ProducesEstimatedPlan()
    {
        const string query = "DECLARE @Mandant int = 1; SELECT ProjectName, Mandant FROM dbo.FakeProjects WHERE Mandant = @Mandant";

        var result = await _fx.ExplainQueryService.ExplainQueryAsync(
            _target,
            query,
            new Dictionary<string, object?>(),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        var plan = result.Value;

        Assert.Equal(_target, plan.Target);
        Assert.Equal("estimated", plan.PlanKind);
        Assert.NotEmpty(plan.Nodes);
        Assert.Null(plan.Nodes[0].ParentId);

        var tableNode = plan.Nodes.FirstOrDefault(n => n.Object?.Table == "FakeProjects");
        Assert.NotNull(tableNode);
    }

    [Fact]
    public async Task ExplainQueryAsync_JoinQuery_ReturnsMultiNodeHierarchy()
    {
        const string query = """
            SELECT p1.ProjectName, p1.Mandant, p2.Status
            FROM dbo.FakeProjects p1
            INNER JOIN dbo.FakeProjects p2 ON p1.ProjectName = p2.ProjectName
            """;

        var result = await _fx.ExplainQueryService.ExplainQueryAsync(
            _target,
            query,
            null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        var plan = result.Value;

        Assert.True(plan.Nodes.Count >= 2, $"Expected at least 2 nodes, got {plan.Nodes.Count}");
        Assert.Null(plan.Nodes[0].ParentId);

        var nodeIds = plan.Nodes.Select(n => n.NodeId).ToHashSet();
        foreach (var node in plan.Nodes.Skip(1))
        {
            Assert.NotNull(node.ParentId);
            Assert.Contains(node.ParentId.Value, nodeIds);
        }
    }

    [Fact]
    public async Task ExplainQueryAsync_ScanOrSeek_IdentifiesTableObject()
    {
        const string query = "SELECT ProjectName, Mandant, Status, Description FROM dbo.FakeProjects";

        var result = await _fx.ExplainQueryService.ExplainQueryAsync(
            _target,
            query,
            null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        var plan = result.Value;

        var scanOrSeek = plan.Nodes.FirstOrDefault(n => n.Object is not null);
        Assert.NotNull(scanOrSeek);
        Assert.Equal("FakeProjects", scanOrSeek.Object?.Table);
        Assert.Equal("dbo", scanOrSeek.Object?.Schema);
    }

    [Fact]
    public async Task ExplainQueryAsync_WorksWithSchemaOnlyAccess()
    {
        var options = new SqlToAiOptions();
        foreach (var (k, v) in _fx.Options.Servers)
        {
            options.Servers[k] = v;
        }
        options.Databases.SchemaOnly = [_target];
        options.QueryExecution.CommandTimeoutSeconds = _fx.Options.QueryExecution.CommandTimeoutSeconds;

        var optionsWrapper = Options.Create(options);
        var guard = new SecurityGuard(optionsWrapper);
        var accessProvider = new AccessLevelProvider(optionsWrapper);
        var statementGuard = new ExplainQueryStatementGuard(guard, new ReadOnlyGuard());
        var service = new ExplainQueryService(
            _fx.ShowplanExecutionService,
            _fx.ShowplanXmlParser,
            statementGuard,
            guard,
            accessProvider,
            optionsWrapper,
            NullLogger<ExplainQueryService>.Instance);

        const string query = "SELECT ProjectName FROM dbo.FakeProjects";
        var result = await service.ExplainQueryAsync(_target, query, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.Equal(_target, result.Value.Target);
        Assert.NotEmpty(result.Value.Nodes);
    }

    [Fact]
    public async Task ExplainQueryAsync_TruncationOptions_SetsTruncatedFlag()
    {
        var options = new SqlToAiOptions();
        foreach (var (k, v) in _fx.Options.Servers)
        {
            options.Servers[k] = v;
        }
        options.Databases.ReadOnly = [_target];
        options.ExplainQuery.MaxOperators = 1;

        var optionsWrapper = Options.Create(options);
        var guard = new SecurityGuard(optionsWrapper);
        var accessProvider = new AccessLevelProvider(optionsWrapper);
        var statementGuard = new ExplainQueryStatementGuard(guard, new ReadOnlyGuard());
        var parser = new ShowplanXmlParser(optionsWrapper);
        var service = new ExplainQueryService(
            _fx.ShowplanExecutionService,
            parser,
            statementGuard,
            guard,
            accessProvider,
            optionsWrapper,
            NullLogger<ExplainQueryService>.Instance);

        const string query = """
            SELECT p1.ProjectName, p1.Mandant, p2.Status
            FROM dbo.FakeProjects p1
            INNER JOIN dbo.FakeProjects p2 ON p1.ProjectName = p2.ProjectName
            """;

        var result = await service.ExplainQueryAsync(_target, query, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        var plan = result.Value;

        Assert.True(plan.Truncated);
        Assert.Single(plan.Nodes);
        Assert.True(plan.Nodes[0].OmittedChildren > 0);
    }

    [Fact]
    public async Task ExplainQueryAsync_QueryWithMissingIndex_ReturnsMissingIndexWarning()
    {
        var resolver = new DatabaseTargetResolver(_fx.Options);
        var targetContext = resolver.Resolve(_target).Value;
        await using (var setupConn = _fx.ConnectionFactory.CreateTargetConnection(targetContext))
        {
            await setupConn.OpenAsync(TestContext.Current.CancellationToken);
            await using var cmd = setupConn.CreateCommand();
            cmd.CommandText = "UPDATE STATISTICS dbo.FakeProjects WITH ROWCOUNT = 500000, PAGECOUNT = 50000;";
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        try
        {
            const string query = "SELECT Description FROM dbo.FakeProjects WHERE Status = 'Active' AND Mandant = 1";
            var result = await _fx.ExplainQueryService.ExplainQueryAsync(
                _target,
                query,
                null,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
            var plan = result.Value;
            var warning = Assert.Single(plan.Warnings, w => w.Kind == "missing_index");
            Assert.Equal("dbo", warning.Schema);
            Assert.Equal("FakeProjects", warning.Table);
            Assert.NotNull(warning.Impact);
            Assert.True(warning.Impact.Value > 0);
            Assert.NotNull(warning.KeyColumns);
            Assert.Contains("Mandant", warning.KeyColumns);
            Assert.Contains("Status", warning.KeyColumns);
            Assert.NotNull(warning.IncludeColumns);
            Assert.Contains("Description", warning.IncludeColumns);

            string json = JsonSerializer.Serialize(plan, McpJsonContext.Default.ExplainQueryResult);
            Assert.Contains("\"kind\":\"missing_index\"", json, StringComparison.Ordinal);
            Assert.Contains("\"schema\":\"dbo\"", json, StringComparison.Ordinal);
            Assert.Contains("\"table\":\"FakeProjects\"", json, StringComparison.Ordinal);
        }
        finally
        {
            await using var cleanupConn = _fx.ConnectionFactory.CreateTargetConnection(targetContext);
            await cleanupConn.OpenAsync(CancellationToken.None);
            await using var cmd = cleanupConn.CreateCommand();
            cmd.CommandText = "UPDATE STATISTICS dbo.FakeProjects;";
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
