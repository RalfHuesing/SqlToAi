using System.Collections.Generic;
using System.Threading.Tasks;
using SqlToAi.Database;
using SqlToAi.Domain;
using Xunit;

namespace SqlToAi.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(SqlServerCollectionFixture.Name)]
public sealed class ShowplanExecutionServiceIntegrationTests
{
    private readonly SqlServerFixture _fx;
    private readonly string _target;

    public ShowplanExecutionServiceIntegrationTests(SqlServerFixture fx)
    {
        _fx = fx;
        _target = TestConstants.QualifiedDatabaseName;
    }

    [Fact]
    public async Task ExecuteShowplanAsync_ReturnsShowplanXml_ForParameterizedSelect()
    {
        var query = "DECLARE @name nvarchar(50) = 'DemoProject'; SELECT ProjectName, Mandant FROM dbo.FakeProjects WHERE ProjectName = @name";

        var result = await _fx.ShowplanExecutionService.ExecuteShowplanAsync(
            _target,
            query,
            null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotNull(result.Value);
        Assert.Contains("<ShowPlanXML", result.Value);
        Assert.Contains("FakeProjects", result.Value);
        Assert.Contains("@name", result.Value);
    }

    [Fact]
    public async Task ExecuteShowplanAsync_DoesNotExecuteQuery_ProvesNoExecutionAndNoDataRows()
    {
        // A query with an expression that would fail during execution (divide by zero at runtime)
        // is compiled without executing, returning valid showplan XML.
        var query = "SELECT ProjectName, 1 / (CASE WHEN Mandant >= 0 THEN 0 ELSE 1 END) AS RuntimeBomb FROM dbo.FakeProjects";

        var result = await _fx.ShowplanExecutionService.ExecuteShowplanAsync(
            _target,
            query,
            null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotNull(result.Value);
        Assert.Contains("<ShowPlanXML", result.Value);
        Assert.Contains("FakeProjects", result.Value);
    }
}
