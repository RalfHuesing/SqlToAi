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
/// Unit tests for <see cref="PerformanceMeasurementService"/>, verifying security guards,
/// empty parameter validation, and XML plan parsing logic.
/// </summary>
public sealed class PerformanceMeasurementServiceTests
{
    private static PerformanceMeasurementService BuildService(
        bool isAllowed = true,
        AccessLevel accessLevel = AccessLevel.ReadOnly,
        SqlToAiError? error = null)
    {
        var options = new SqlToAiOptions();

        IQuerySafetyValidator safetyValidator = error != null
            ? new FakeQuerySafetyValidator(error)
            : new FakeQuerySafetyValidator(
                new FakeSecurityGuard(isAllowed),
                new FakeAccessLevelProvider(accessLevel),
                new ReadOnlyGuard());

        return new PerformanceMeasurementService(
            new ValidationMockConnectionFactory(),
            safetyValidator,
            Options.Create(options),
            NullLogger<PerformanceMeasurementService>.Instance);
    }

    [Fact]
    public async Task MeasurePerformanceAsync_EmptyDatabase_ReturnsInvalidParameters()
    {
        var service = BuildService();
        var result = await service.MeasurePerformanceAsync("", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task MeasurePerformanceAsync_EmptyQuery_ReturnsInvalidParameters()
    {
        var service = BuildService();
        var result = await service.MeasurePerformanceAsync("TestDb", "", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task MeasurePerformanceAsync_DatabaseNotAllowed_ReturnsSafetyCheckFailed()
    {
        var service = BuildService(isAllowed: false);
        var result = await service.MeasurePerformanceAsync("ForbiddenDb", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    [Fact]
    public async Task MeasurePerformanceAsync_AccessLevelNone_ReturnsWriteOperationBlocked()
    {
        var service = BuildService(accessLevel: AccessLevel.None);
        var result = await service.MeasurePerformanceAsync("TestDb", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Fact]
    public async Task MeasurePerformanceAsync_MutatingQuery_ReturnsWriteOperationBlocked()
    {
        var service = BuildService(accessLevel: AccessLevel.ReadOnly);
        var result = await service.MeasurePerformanceAsync("TestDb", "DROP TABLE Users", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Fact]
    public async Task MeasurePerformanceAsync_MultiStatement_ReturnsMultipleStatementsForbidden()
    {
        var service = BuildService(accessLevel: AccessLevel.ReadOnly);
        var result = await service.MeasurePerformanceAsync("TestDb", "SELECT 1; SELECT 2", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    [Fact]
    public void ParseExecutionPlanXml_MissingIndexAndImplicitConversion_ParsesWarningsCorrectly()
    {
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
        string sampleXml = """
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
          <BatchSequence>
            <Batch>
              <Statements>
                <StmtSimple>
                  <QueryPlan>
                    <MissingIndexes>
                      <MissingIndexGroup Impact="72.5">
                        <MissingIndex Table="[dbo].[Orders]">
                          <ColumnGroup Usage="EQUALITY">
                            <Column Name="CustomerId" />
                          </ColumnGroup>
                        </MissingIndex>
                      </MissingIndexGroup>
                    </MissingIndexes>
                  </QueryPlan>
                </StmtSimple>
              </Statements>
            </Batch>
          </BatchSequence>
        </ShowPlanXML>
        """;

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
        string sampleXml = """
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
          <BatchSequence>
            <Batch>
              <Statements>
                <StmtSimple>
                  <QueryPlan>
                    <MissingIndexes>
                      <MissingIndexGroup Impact="85.7">
                        <MissingIndex Table="[dbo].[Orders]">
                          <ColumnGroup Usage="EQUALITY">
                            <Column Name="CustomerId" />
                          </ColumnGroup>
                          <ColumnGroup Usage="INEQUALITY">
                            <Column Name="OrderDate" />
                          </ColumnGroup>
                          <ColumnGroup Usage="INCLUDE">
                            <Column Name="Amount" />
                            <Column Name="Status" />
                          </ColumnGroup>
                        </MissingIndex>
                      </MissingIndexGroup>
                    </MissingIndexes>
                  </QueryPlan>
                </StmtSimple>
              </Statements>
            </Batch>
          </BatchSequence>
        </ShowPlanXML>
        """;

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
        string sampleXml = """
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
          <BatchSequence>
            <Batch>
              <Statements>
                <StmtSimple>
                  <QueryPlan>
                    <MissingIndexes>
                      <MissingIndexGroup Impact="60.0">
                        <MissingIndex Table="[dbo].[Orders]">
                          <ColumnGroup Usage="EQUALITY">
                            <Column Name="CustomerId" />
                          </ColumnGroup>
                          <ColumnGroup Usage="INCLUDE">
                            <Column Name="Amount" />
                            <Column Name="Status" />
                          </ColumnGroup>
                        </MissingIndex>
                      </MissingIndexGroup>
                    </MissingIndexes>
                  </QueryPlan>
                </StmtSimple>
              </Statements>
            </Batch>
          </BatchSequence>
        </ShowPlanXML>
        """;

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
        string sampleXml = """
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
          <BatchSequence>
            <Batch>
              <Statements>
                <StmtSimple>
                  <QueryPlan>
                    <MissingIndexes>
                      <MissingIndexGroup Impact="75.0">
                        <MissingIndex Table="[dbo].[Orders]">
                          <ColumnGroup Usage="EQUALITY">
                            <Column Name="CustomerId" />
                          </ColumnGroup>
                          <ColumnGroup Usage="INEQUALITY">
                            <Column Name="OrderDate" Descending="True" />
                          </ColumnGroup>
                          <ColumnGroup Usage="INCLUDE">
                            <Column Name="Amount" />
                          </ColumnGroup>
                        </MissingIndex>
                      </MissingIndexGroup>
                    </MissingIndexes>
                  </QueryPlan>
                </StmtSimple>
              </Statements>
            </Batch>
          </BatchSequence>
        </ShowPlanXML>
        """;

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
        string sampleXml = """
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
          <BatchSequence>
            <Batch>
              <Statements>
                <StmtSimple>
                  <QueryPlan>
                    <MissingIndexes>
                      <MissingIndexGroup Impact="60.0">
                        <MissingIndex Table="[dbo].[Orders]">
                          <ColumnGroup Usage="EQUALITY">
                            <Column Name="CustomerId" Descending="False" />
                          </ColumnGroup>
                          <ColumnGroup Usage="INCLUDE">
                            <Column Name="Amount" />
                          </ColumnGroup>
                        </MissingIndex>
                      </MissingIndexGroup>
                    </MissingIndexes>
                  </QueryPlan>
                </StmtSimple>
              </Statements>
            </Batch>
          </BatchSequence>
        </ShowPlanXML>
        """;

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
        string sampleXml = """
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
          <BatchSequence>
            <Batch>
              <Statements>
                <StmtSimple>
                  <QueryPlan>
                    <MissingIndexes>
                      <MissingIndexGroup Impact="80.0">
                        <MissingIndex Table="[dbo].[Orders]">
                          <ColumnGroup Usage="EQUALITY">
                            <Column Name="ColA" Descending="True" />
                            <Column Name="ColB" Descending="True" />
                          </ColumnGroup>
                        </MissingIndex>
                      </MissingIndexGroup>
                    </MissingIndexes>
                  </QueryPlan>
                </StmtSimple>
              </Statements>
            </Batch>
          </BatchSequence>
        </ShowPlanXML>
        """;

        var warnings = PerformanceMeasurementService.ParseExecutionPlanXml(sampleXml);

        var missing = Assert.Single(warnings, w => w.Type == "MissingIndex");
        Assert.NotNull(missing.MissingIndexStatement);
        Assert.Contains("(ColA DESC, ColB DESC)", missing.MissingIndexStatement);
        Assert.EndsWith(";", missing.MissingIndexStatement);
    }

    [Fact]
    public void ParseExecutionPlanXml_MissingIndex_DescendingInInclude_IsIgnored()
    {
        string sampleXml = """
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
          <BatchSequence>
            <Batch>
              <Statements>
                <StmtSimple>
                  <QueryPlan>
                    <MissingIndexes>
                      <MissingIndexGroup Impact="55.0">
                        <MissingIndex Table="[dbo].[Orders]">
                          <ColumnGroup Usage="EQUALITY">
                            <Column Name="CustomerId" />
                          </ColumnGroup>
                          <ColumnGroup Usage="INCLUDE">
                            <Column Name="Amount" Descending="True" />
                          </ColumnGroup>
                        </MissingIndex>
                      </MissingIndexGroup>
                    </MissingIndexes>
                  </QueryPlan>
                </StmtSimple>
              </Statements>
            </Batch>
          </BatchSequence>
        </ShowPlanXML>
        """;

        var warnings = PerformanceMeasurementService.ParseExecutionPlanXml(sampleXml);

        var missing = Assert.Single(warnings, w => w.Type == "MissingIndex");
        Assert.NotNull(missing.MissingIndexStatement);
        Assert.Contains("INCLUDE (Amount)", missing.MissingIndexStatement);
        Assert.DoesNotContain("Amount DESC", missing.MissingIndexStatement);
        Assert.EndsWith(";", missing.MissingIndexStatement);
    }
}

