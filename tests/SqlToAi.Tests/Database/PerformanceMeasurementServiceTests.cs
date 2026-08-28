#nullable enable

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Unit tests for <see cref="PerformanceMeasurementService"/>, focused on the ShowPlan XML
/// parsing logic. The pure pipeline outcomes (empty parameters, blocked database, access level,
/// mutating-keyword detection, multi-statement detection) are covered end-to-end in the dedicated
/// <c>QuerySafetyValidatorTests</c> class (step-003 / DRY-T3). The 7 of 8 ShowPlan XML fixtures
/// are built via <see cref="ShowPlanTestHelper.BuildShowPlanXml"/> (DRY-T2); the 8th fixture
/// (<c>ParseExecutionPlanXml_MissingIndexAndImplicitConversion_ParsesWarningsCorrectly</c>) keeps
/// its hand-rolled XML block because it exercises non-<c>&lt;MissingIndex&gt;</c> paths
/// (<c>&lt;RelOp&gt;</c>, <c>&lt;Warnings&gt;</c>, <c>&lt;PlanAffectingConvert&gt;</c>) that the
/// helper does not model.
/// </summary>
public sealed class PerformanceMeasurementServiceTests
{
    private static PerformanceMeasurementService BuildService(
        bool isAllowed = true,
        AccessLevel accessLevel = AccessLevel.ReadOnly,
        SqlToAiError? error = null)
    {
        var options = new SqlToAiOptions();

        return new PerformanceMeasurementService(
            new ValidationMockConnectionFactory(),
            FakeQuerySafetyValidator.Create(isAllowed, accessLevel, error),
            Options.Create(options),
            NullLogger<PerformanceMeasurementService>.Instance);
    }

    [Fact]
    public void ParseExecutionPlanXml_MissingIndexAndImplicitConversion_ParsesWarningsCorrectly()
    {
        // Intentional hand-rolled XML block: this is the one ShowPlan fixture that exercises
        // non-<MissingIndex> paths (<RelOp LogicalOp="Table Scan">, <Warnings>,
        // <PlanAffectingConvert>) outside the scope of ShowPlanTestHelper. The other 7 fixtures
        // are built via the helper.
        string sampleXml = """
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
          <BatchSequence>
            <Batch>
              <Statements>
                <StmtSimple>
                  <QueryPlan>
                    <MissingIndexes>
                      <MissingIndexGroup Impact="85.4">
                        <MissingIndex Table="[dbo].[Orders]" />
                      </MissingIndexGroup>
                    </MissingIndexes>
                    <RelOp LogicalOp="Table Scan">
                      <Warnings>
                        <PlanAffectingConvert Expression="CONVERT_IMPLICIT(int, OrderDate)" />
                      </Warnings>
                    </RelOp>
                  </QueryPlan>
                </StmtSimple>
              </Statements>
            </Batch>
          </BatchSequence>
        </ShowPlanXML>
        """;

        var warnings = PerformanceMeasurementService.ParseExecutionPlanXml(sampleXml);

        Assert.Equal(3, warnings.Count);
        Assert.Contains(warnings, w => w.Type == "MissingIndex" && w.Impact > 80);
        Assert.Contains(warnings, w => w.Type == "ImplicitConversion");
        Assert.Contains(warnings, w => w.Type == "TableScan");
    }

    [Fact]
    public void ParseExecutionPlanXml_MissingIndex_EqualityOnly_BuildsStatement()
    {
        string sampleXml = ShowPlanTestHelper.BuildShowPlanXml(
            impact: 72.5,
            table: "[dbo].[Orders]",
            columns: [new ColumnSpec("CustomerId", "EQUALITY")]);

        var warnings = PerformanceMeasurementService.ParseExecutionPlanXml(sampleXml);

        var missing = Assert.Single(warnings, w => w.Type == "MissingIndex");
        Assert.NotNull(missing.MissingIndexStatement);
        Assert.Contains("CREATE NONCLUSTERED INDEX", missing.MissingIndexStatement);
        Assert.Contains("IX_Orders_CustomerId", missing.MissingIndexStatement);
        Assert.Contains("ON [dbo].[Orders]", missing.MissingIndexStatement);
        Assert.Contains("(CustomerId)", missing.MissingIndexStatement);
        Assert.EndsWith(";", missing.MissingIndexStatement);
    }

    [Fact]
    public void ParseExecutionPlanXml_MissingIndex_EqualityPlusInequalityPlusInclude_BuildsFullStatement()
    {
        string sampleXml = ShowPlanTestHelper.BuildShowPlanXml(
            impact: 85.7,
            table: "[dbo].[Orders]",
            columns:
            [
                new ColumnSpec("CustomerId", "EQUALITY"),
                new ColumnSpec("OrderDate", "INEQUALITY"),
                new ColumnSpec("Amount", "INCLUDE"),
                new ColumnSpec("Status", "INCLUDE"),
            ]);

        var warnings = PerformanceMeasurementService.ParseExecutionPlanXml(sampleXml);

        var missing = Assert.Single(warnings, w => w.Type == "MissingIndex");
        Assert.NotNull(missing.MissingIndexStatement);
        Assert.Contains("CREATE NONCLUSTERED INDEX", missing.MissingIndexStatement);
        Assert.Contains("ON [dbo].[Orders]", missing.MissingIndexStatement);
        Assert.Contains("(CustomerId, OrderDate)", missing.MissingIndexStatement);
        Assert.Contains("INCLUDE (Amount, Status)", missing.MissingIndexStatement);
        Assert.EndsWith(";", missing.MissingIndexStatement);
    }

    [Fact]
    public void ParseExecutionPlanXml_MissingIndex_EqualityOnlyWithInclude_BuildsStatementWithInclude()
    {
        string sampleXml = ShowPlanTestHelper.BuildShowPlanXml(
            impact: 60.0,
            table: "[dbo].[Orders]",
            columns:
            [
                new ColumnSpec("CustomerId", "EQUALITY"),
                new ColumnSpec("Amount", "INCLUDE"),
                new ColumnSpec("Status", "INCLUDE"),
            ]);

        var warnings = PerformanceMeasurementService.ParseExecutionPlanXml(sampleXml);

        var missing = Assert.Single(warnings, w => w.Type == "MissingIndex");
        Assert.NotNull(missing.MissingIndexStatement);
        Assert.Contains("ON [dbo].[Orders] (CustomerId)", missing.MissingIndexStatement);
        Assert.Contains("INCLUDE (Amount, Status)", missing.MissingIndexStatement);
        Assert.EndsWith(";", missing.MissingIndexStatement);
    }

    [Fact]
    public void ParseExecutionPlanXml_MissingIndex_DescendingColumn_RendersDescSuffix()
    {
        string sampleXml = ShowPlanTestHelper.BuildShowPlanXml(
            impact: 75.0,
            table: "[dbo].[Orders]",
            columns:
            [
                new ColumnSpec("CustomerId", "EQUALITY"),
                new ColumnSpec("OrderDate", "INEQUALITY", Descending: true),
                new ColumnSpec("Amount", "INCLUDE"),
            ]);

        var warnings = PerformanceMeasurementService.ParseExecutionPlanXml(sampleXml);

        var missing = Assert.Single(warnings, w => w.Type == "MissingIndex");
        Assert.NotNull(missing.MissingIndexStatement);
        Assert.Contains("(CustomerId, OrderDate DESC)", missing.MissingIndexStatement);
        Assert.Contains("INCLUDE (Amount)", missing.MissingIndexStatement);
        Assert.DoesNotContain("CustomerId DESC", missing.MissingIndexStatement);
        Assert.EndsWith(";", missing.MissingIndexStatement);
    }

    [Fact]
    public void ParseExecutionPlanXml_MissingIndex_DescendingFalse_IsAscendingLikeBefore()
    {
        string sampleXml = ShowPlanTestHelper.BuildShowPlanXml(
            impact: 60.0,
            table: "[dbo].[Orders]",
            columns:
            [
                new ColumnSpec("CustomerId", "EQUALITY", Descending: false),
                new ColumnSpec("Amount", "INCLUDE"),
            ]);

        var warnings = PerformanceMeasurementService.ParseExecutionPlanXml(sampleXml);

        var missing = Assert.Single(warnings, w => w.Type == "MissingIndex");
        Assert.NotNull(missing.MissingIndexStatement);
        Assert.Contains("ON [dbo].[Orders] (CustomerId)", missing.MissingIndexStatement);
        Assert.DoesNotContain("CustomerId DESC", missing.MissingIndexStatement);
        Assert.EndsWith(";", missing.MissingIndexStatement);
    }

    [Fact]
    public void ParseExecutionPlanXml_MissingIndex_AllColumnsDescending_RendersAllDesc()
    {
        string sampleXml = ShowPlanTestHelper.BuildShowPlanXml(
            impact: 80.0,
            table: "[dbo].[Orders]",
            columns:
            [
                new ColumnSpec("ColA", "EQUALITY", Descending: true),
                new ColumnSpec("ColB", "EQUALITY", Descending: true),
            ]);

        var warnings = PerformanceMeasurementService.ParseExecutionPlanXml(sampleXml);

        var missing = Assert.Single(warnings, w => w.Type == "MissingIndex");
        Assert.NotNull(missing.MissingIndexStatement);
        Assert.Contains("(ColA DESC, ColB DESC)", missing.MissingIndexStatement);
        Assert.EndsWith(";", missing.MissingIndexStatement);
    }

    [Fact]
    public void ParseExecutionPlanXml_MissingIndex_DescendingInInclude_IsIgnored()
    {
        string sampleXml = ShowPlanTestHelper.BuildShowPlanXml(
            impact: 55.0,
            table: "[dbo].[Orders]",
            columns:
            [
                new ColumnSpec("CustomerId", "EQUALITY"),
                new ColumnSpec("Amount", "INCLUDE", Descending: true),
            ]);

        var warnings = PerformanceMeasurementService.ParseExecutionPlanXml(sampleXml);

        var missing = Assert.Single(warnings, w => w.Type == "MissingIndex");
        Assert.NotNull(missing.MissingIndexStatement);
        Assert.Contains("INCLUDE (Amount)", missing.MissingIndexStatement);
        Assert.DoesNotContain("Amount DESC", missing.MissingIndexStatement);
        Assert.EndsWith(";", missing.MissingIndexStatement);
    }
}
