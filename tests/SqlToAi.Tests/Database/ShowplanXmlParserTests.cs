using System;
using System.Collections.Generic;
using System.Text.Json;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Mcp;
using Xunit;

namespace SqlToAi.Tests.Database;

public sealed class ShowplanXmlParserTests
{
    private readonly ShowplanXmlParser _parser = new();
    private const string TestTarget = "primary/DemoDB";

    // -------------------------------------------------------------------------
    // 1. Error Mapping & Security (DTD, Malformed, Missing Elements)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Parse_EmptyOrWhitespace_FailsWithCatalogError(string emptyXml)
    {
        var result = _parser.Parse(emptyXml, TestTarget);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Contains("empty", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MalformedXml_FailsWithCatalogError()
    {
        const string malformedXml = "<ShowPlanXML><BatchSequence><unclosed></ShowPlanXML>";

        var result = _parser.Parse(malformedXml, TestTarget);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Contains("malformed", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_DtdProhibited_RejectsExternalDtdAndFailsWithCatalogError()
    {
        const string dtdXml = """
            <?xml version="1.0"?>
            <!DOCTYPE ShowPlanXML [
              <!ENTITY xxe SYSTEM "file:///etc/passwd">
            ]>
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence><Batch><Statements><StmtSimple StatementType="SELECT"/></Statements></Batch></BatchSequence>
            </ShowPlanXML>
            """;

        var result = _parser.Parse(dtdXml, TestTarget);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Contains("prohibited", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MissingShowPlanXmlRoot_FailsWithCatalogError()
    {
        const string wrongRootXml = "<AnotherRoot><BatchSequence/></AnotherRoot>";

        var result = _parser.Parse(wrongRootXml, TestTarget);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Contains("missing root", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MissingSelectStatementPlan_FailsWithCatalogError()
    {
        const string noSelectXml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementType="ASSIGN" />
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var result = _parser.Parse(noSelectXml, TestTarget);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Contains("SELECT statement", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // 2. Select Statement Filtering in Batch (Ignores DECLARE)
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_BatchWithLeadingDeclare_IgnoresDeclareAndReturnsSelectPlanWithRootParentNull()
    {
        const string batchXml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementType="ASSIGN" StatementText="DECLARE @id int = 42" />
                    <StmtSimple StatementType="SELECT" StatementText="SELECT * FROM dbo.Orders WHERE Id = @id">
                      <QueryPlan>
                        <RelOp NodeId="0" PhysicalOp="Clustered Index Scan" LogicalOp="Clustered Index Scan" EstimateRows="10.0" EstimatedTotalSubtreeCost="0.00328">
                          <IndexScan>
                            <Object Schema="[dbo]" Table="[Orders]" Index="[PK_Orders]" />
                          </IndexScan>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var result = _parser.Parse(batchXml, TestTarget);

        Assert.True(result.IsSuccess);
        Assert.Equal(TestTarget, result.Value.Target);
        Assert.Equal("estimated", result.Value.PlanKind);
        Assert.False(result.Value.Truncated);

        var node = Assert.Single(result.Value.Nodes);
        Assert.Equal(0, node.NodeId);
        Assert.Null(node.ParentId);
        Assert.Equal("Clustered Index Scan", node.PhysicalOp);
        Assert.Equal("Clustered Index Scan", node.LogicalOp);
        Assert.Equal(10.0, node.EstimatedRows);
        Assert.Equal(0.00328, node.SubtreeCost);
        Assert.NotNull(node.Object);
        var obj = node.Object!;
        Assert.Equal("dbo", obj.Schema);
        Assert.Equal("Orders", obj.Table);
        Assert.Equal("PK_Orders", obj.Index);
    }

    // -------------------------------------------------------------------------
    // 3. Hierarchical Operators (Joins, Scans, ParentId Hierarchy, Object Scoping)
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_NestedLoopJoin_ProducesParentIdHierarchyAndCorrectObjectScoping()
    {
        const string joinPlanXml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementType="SELECT">
                      <QueryPlan>
                        <RelOp NodeId="0" PhysicalOp="Nested Loops" LogicalOp="Inner Join" EstimateRows="50" EstimatedTotalSubtreeCost="0.05">
                          <NestedLoops>
                            <RelOp NodeId="1" PhysicalOp="Clustered Index Scan" LogicalOp="Clustered Index Scan" EstimateRows="10" EstimatedTotalSubtreeCost="0.02">
                              <IndexScan>
                                <Object Schema="[dbo]" Table="[Orders]" Index="[PK_Orders]" />
                              </IndexScan>
                            </RelOp>
                            <RelOp NodeId="2" PhysicalOp="Index Seek" LogicalOp="Index Seek" EstimateRows="5" EstimatedTotalSubtreeCost="0.01">
                              <IndexScan>
                                <Object Schema="[dbo]" Table="[Customers]" Index="[IX_Cust_Id]" />
                              </IndexScan>
                            </RelOp>
                          </NestedLoops>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var result = _parser.Parse(joinPlanXml, TestTarget);

        Assert.True(result.IsSuccess);
        var nodes = result.Value.Nodes;
        Assert.Equal(3, nodes.Count);

        // Root: Nested Loops
        Assert.Equal(0, nodes[0].NodeId);
        Assert.Null(nodes[0].ParentId);
        Assert.Equal("Nested Loops", nodes[0].PhysicalOp);
        Assert.Equal("Inner Join", nodes[0].LogicalOp);
        Assert.Null(nodes[0].Object); // Root must NOT pull Object from descendants!
        Assert.Null(nodes[0].OmittedChildren);

        // Child 1: Orders scan
        Assert.Equal(1, nodes[1].NodeId);
        Assert.Equal(0, nodes[1].ParentId);
        Assert.Equal("Clustered Index Scan", nodes[1].PhysicalOp);
        Assert.NotNull(nodes[1].Object);
        var obj1 = nodes[1].Object!;
        Assert.Equal("dbo", obj1.Schema);
        Assert.Equal("Orders", obj1.Table);
        Assert.Equal("PK_Orders", obj1.Index);

        // Child 2: Customers seek
        Assert.Equal(2, nodes[2].NodeId);
        Assert.Equal(0, nodes[2].ParentId);
        Assert.Equal("Index Seek", nodes[2].PhysicalOp);
        Assert.NotNull(nodes[2].Object);
        var obj2 = nodes[2].Object!;
        Assert.Equal("dbo", obj2.Schema);
        Assert.Equal("Customers", obj2.Table);
        Assert.Equal("IX_Cust_Id", obj2.Index);
    }

    [Fact]
    public void Parse_WorktableWithoutTableOrMultiObject_DoesNotAssignObject()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementType="SELECT">
                      <QueryPlan>
                        <RelOp NodeId="0" PhysicalOp="Table Spool" LogicalOp="Lazy Spool" EstimateRows="1" EstimatedTotalSubtreeCost="0.01">
                          <Spool>
                            <Object Database="[tempdb]" /> <!-- Internal worktable without Table attribute -->
                          </Spool>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var result = _parser.Parse(xml, TestTarget);

        Assert.True(result.IsSuccess);
        var node = Assert.Single(result.Value.Nodes);
        Assert.Null(node.Object);
    }

    // -------------------------------------------------------------------------
    // 4. Warnings (Missing Index, Plan-Affecting Convert, Scans Not Extracted)
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_MissingIndexWarning_ExtractsAllowlistedFields_NoCreateDdl()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementType="SELECT">
                      <QueryPlan>
                        <MissingIndexes>
                          <MissingIndexGroup Impact="85.5">
                            <MissingIndex Database="[DemoDB]" Schema="[dbo]" Table="[Orders]">
                              <ColumnGroup Usage="EQUALITY">
                                <Column Name="[CustomerId]" />
                              </ColumnGroup>
                              <ColumnGroup Usage="INEQUALITY">
                                <Column Name="[OrderDate]" />
                              </ColumnGroup>
                              <ColumnGroup Usage="INCLUDE">
                                <Column Name="[Amount]" />
                                <Column Name="[Status]" />
                              </ColumnGroup>
                            </MissingIndex>
                          </MissingIndexGroup>
                        </MissingIndexes>
                        <RelOp NodeId="0" PhysicalOp="Table Scan" LogicalOp="Table Scan" EstimateRows="100" EstimatedTotalSubtreeCost="0.05">
                          <TableScan>
                            <Object Schema="[dbo]" Table="[Orders]" />
                          </TableScan>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var result = _parser.Parse(xml, TestTarget);

        Assert.True(result.IsSuccess);
        var warning = Assert.Single(result.Value.Warnings);
        Assert.Equal("missing_index", warning.Kind);
        Assert.Equal("dbo", warning.Schema);
        Assert.Equal("Orders", warning.Table);
        Assert.Equal(85.5, warning.Impact);
        Assert.Equal(["CustomerId", "OrderDate"], warning.KeyColumns);
        Assert.Equal(["Amount", "Status"], warning.IncludeColumns);

        // Verify no CREATE INDEX DDL is present in serialized output
        string json = JsonSerializer.Serialize(result.Value, McpJsonContext.Default.ExplainQueryResult);
        Assert.DoesNotContain("CREATE INDEX", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE NONCLUSTERED INDEX", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_PlanAffectingConvert_ExtractsConversionKindAndColumnIdentity_NoRawExpressionOrLiterals()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementType="SELECT">
                      <QueryPlan>
                        <Warnings>
                          <PlanAffectingConvert ConvertIssue="Seek Plan" Expression="CONVERT_IMPLICIT(nvarchar(50),[DemoDB].[dbo].[Orders].[CustomerId],0)" />
                        </Warnings>
                        <RelOp NodeId="0" PhysicalOp="Index Scan" LogicalOp="Index Scan" EstimateRows="10" EstimatedTotalSubtreeCost="0.01">
                          <IndexScan>
                            <Object Schema="[dbo]" Table="[Orders]" Index="[IX_Orders]" />
                          </IndexScan>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var result = _parser.Parse(xml, TestTarget);

        Assert.True(result.IsSuccess);
        var warning = Assert.Single(result.Value.Warnings);
        Assert.Equal("plan_affecting_convert", warning.Kind);
        Assert.Equal("Seek Plan", warning.ConversionKind);
        Assert.Equal("DemoDB.dbo.Orders.CustomerId", warning.Column);

        // Verify raw expression and literals do NOT appear in serialized output
        string json = JsonSerializer.Serialize(result.Value, McpJsonContext.Default.ExplainQueryResult);
        Assert.DoesNotContain("CONVERT_IMPLICIT", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nvarchar(50)", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_ScansAreNotExtractedAsWarnings()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementType="SELECT">
                      <QueryPlan>
                        <RelOp NodeId="0" PhysicalOp="Table Scan" LogicalOp="Table Scan" EstimateRows="10" EstimatedTotalSubtreeCost="0.01">
                          <TableScan>
                            <Object Schema="[dbo]" Table="[Orders]" />
                          </TableScan>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var result = _parser.Parse(xml, TestTarget);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Warnings);
        var node = Assert.Single(result.Value.Nodes);
        Assert.Equal("Table Scan", node.PhysicalOp);
    }

    [Fact]
    public void Parse_WarningCap_CapsAt25AndSetsTruncatedTrue()
    {
        var missingIndexesXml = new System.Text.StringBuilder();
        missingIndexesXml.Append("""
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementType="SELECT">
                      <QueryPlan>
                        <MissingIndexes>
            """);

        for (int i = 0; i < 30; i++)
        {
            missingIndexesXml.Append($"""
                <MissingIndexGroup Impact="50.0">
                  <MissingIndex Table="[dbo].[Table_{i}]">
                    <ColumnGroup Usage="EQUALITY">
                      <Column Name="[Col_{i}]" />
                    </ColumnGroup>
                  </MissingIndex>
                </MissingIndexGroup>
                """);
        }

        missingIndexesXml.Append("""
                        </MissingIndexes>
                        <RelOp NodeId="0" PhysicalOp="Compute Scalar" LogicalOp="Compute Scalar" EstimateRows="1" EstimatedTotalSubtreeCost="0.001" />
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """);

        var result = _parser.Parse(missingIndexesXml.ToString(), TestTarget);

        Assert.True(result.IsSuccess);
        Assert.Equal(25, result.Value.Warnings.Count);
        Assert.True(result.Value.Truncated);
    }

    // -------------------------------------------------------------------------
    // 5. Bounds & Truncation (MaxOperators, MaxDepth, MaxResponseBytes)
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_MaxOperators_TruncatesInBfsOrder_ReportsOmittedChildrenAndTruncated()
    {
        // Tree:
        // Node 0 (Root)
        //   ├── Node 1
        //   ├── Node 2
        //   └── Node 3
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementType="SELECT">
                      <QueryPlan>
                        <RelOp NodeId="0" PhysicalOp="Concat" LogicalOp="Concat" EstimateRows="3" EstimatedTotalSubtreeCost="0.01">
                          <Concat>
                            <RelOp NodeId="1" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="1" EstimatedTotalSubtreeCost="0.001" />
                            <RelOp NodeId="2" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="1" EstimatedTotalSubtreeCost="0.001" />
                            <RelOp NodeId="3" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="1" EstimatedTotalSubtreeCost="0.001" />
                          </Concat>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var options = new ExplainQueryOptions { MaxOperators = 2 };
        var result = _parser.Parse(xml, TestTarget, options);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Truncated);

        var nodes = result.Value.Nodes;
        Assert.Equal(2, nodes.Count);

        // Root accepted
        Assert.Equal(0, nodes[0].NodeId);
        Assert.Null(nodes[0].ParentId);
        Assert.Equal(2, nodes[0].OmittedChildren); // Nodes 2 and 3 omitted

        // Child 1 accepted
        Assert.Equal(1, nodes[1].NodeId);
        Assert.Equal(0, nodes[1].ParentId);
        Assert.Null(nodes[1].OmittedChildren);
    }

    [Fact]
    public void Parse_MaxDepth_TruncatesSubtreeBeyondMaxDepth_ReportsOmittedChildrenAndTruncated()
    {
        // Tree:
        // Node 0 (Root, depth 1)
        //   └── Node 1 (depth 2)
        //         ├── Node 2 (depth 3)
        //         └── Node 3 (depth 3)
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementType="SELECT">
                      <QueryPlan>
                        <RelOp NodeId="0" PhysicalOp="Compute Scalar" LogicalOp="Compute Scalar" EstimateRows="1" EstimatedTotalSubtreeCost="0.03">
                          <ComputeScalar>
                            <RelOp NodeId="1" PhysicalOp="Nested Loops" LogicalOp="Inner Join" EstimateRows="1" EstimatedTotalSubtreeCost="0.02">
                              <NestedLoops>
                                <RelOp NodeId="2" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="1" EstimatedTotalSubtreeCost="0.001" />
                                <RelOp NodeId="3" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="1" EstimatedTotalSubtreeCost="0.001" />
                              </NestedLoops>
                            </RelOp>
                          </ComputeScalar>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var options = new ExplainQueryOptions { MaxDepth = 2 };
        var result = _parser.Parse(xml, TestTarget, options);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Truncated);

        var nodes = result.Value.Nodes;
        Assert.Equal(2, nodes.Count);

        // Root (depth 1)
        Assert.Equal(0, nodes[0].NodeId);
        Assert.Null(nodes[0].OmittedChildren);

        // Child 1 (depth 2): both direct children (Nodes 2 & 3) cut off because depth 3 > MaxDepth 2
        Assert.Equal(1, nodes[1].NodeId);
        Assert.Equal(2, nodes[1].OmittedChildren);
    }

    [Fact]
    public void Parse_MaxResponseBytes_DropsNodesInReverseBfsOrder_AndReportsOmittedChildren()
    {
        // Tree:
        // Node 0 (Root)
        //   ├── Node 1
        //   └── Node 2
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementType="SELECT">
                      <QueryPlan>
                        <RelOp NodeId="0" PhysicalOp="Nested Loops" LogicalOp="Inner Join" EstimateRows="2" EstimatedTotalSubtreeCost="0.01">
                          <NestedLoops>
                            <RelOp NodeId="1" PhysicalOp="Index Scan" LogicalOp="Index Scan" EstimateRows="1" EstimatedTotalSubtreeCost="0.005">
                              <IndexScan>
                                <Object Schema="[dbo]" Table="[TableA]" Index="[IX_A]" />
                              </IndexScan>
                            </RelOp>
                            <RelOp NodeId="2" PhysicalOp="Index Scan" LogicalOp="Index Scan" EstimateRows="1" EstimatedTotalSubtreeCost="0.005">
                              <IndexScan>
                                <Object Schema="[dbo]" Table="[TableB]" Index="[IX_B]" />
                              </IndexScan>
                            </RelOp>
                          </NestedLoops>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        // Check normal size first
        var normalResult = _parser.Parse(xml, TestTarget);
        Assert.True(normalResult.IsSuccess);
        byte[] fullBytes = JsonSerializer.SerializeToUtf8Bytes(normalResult.Value, McpJsonContext.Default.ExplainQueryResult);

        // Set MaxResponseBytes strictly below the full bytes, but enough to hold Root + Node 1
        // Note: ApplyByteLimitTruncation has Math.Max(512, options.MaxResponseBytes)
        // If fullBytes is e.g. 550 bytes, setting MaxResponseBytes to 512 forces dropping Node 2
        var options = new ExplainQueryOptions { MaxResponseBytes = 512 };
        var truncatedResult = _parser.Parse(xml, TestTarget, options);

        Assert.True(truncatedResult.IsSuccess);
        byte[] truncatedBytes = JsonSerializer.SerializeToUtf8Bytes(truncatedResult.Value, McpJsonContext.Default.ExplainQueryResult);

        Assert.True(truncatedBytes.Length <= 512);
        if (fullBytes.Length > 512)
        {
            Assert.True(truncatedResult.Value.Truncated);
            Assert.True(truncatedResult.Value.Nodes.Count < 3);

            // If Node 0 is still present, its OmittedChildren should reflect the dropped child(ren)
            var root = truncatedResult.Value.Nodes[0];
            Assert.Equal(0, root.NodeId);
            Assert.NotNull(root.OmittedChildren);
            Assert.True(root.OmittedChildren > 0);
        }
    }

    // -------------------------------------------------------------------------
    // 6. Strict Literal Exclusion & Plan Data Protection
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_LiteralExclusion_SensitiveLiteralsAndRawPlanAttributesNeverAppearInJson()
    {
        const string secretLiteral = "SecretPassword123!";
        const string secretParam = "SuperSecretKey456";
        const string secretScalar = "HiddenToken789";

        string xml = $"""
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementType="SELECT" StatementText="SELECT * FROM [Users] WHERE [Password] = '{secretLiteral}'" ParameterCompiledValue="'{secretParam}'">
                      <QueryPlan>
                        <Warnings>
                          <PlanAffectingConvert ConvertIssue="Cardinality Estimate" Expression="CONVERT_IMPLICIT(int,[DemoDB].[dbo].[Users].[Age],0)" />
                        </Warnings>
                        <RelOp NodeId="0" PhysicalOp="Index Seek" LogicalOp="Index Seek" EstimateRows="1" EstimatedTotalSubtreeCost="0.003" ScalarString="{secretScalar}">
                          <IndexScan>
                            <Object Schema="[dbo]" Table="[Users]" Index="[PK_Users]" />
                          </IndexScan>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var result = _parser.Parse(xml, TestTarget);

        Assert.True(result.IsSuccess);

        // Serialize result through the production source generator context
        string json = JsonSerializer.Serialize(result.Value, McpJsonContext.Default.ExplainQueryResult);

        // Sensitive literals must NEVER leak into the serialized output
        Assert.DoesNotContain(secretLiteral, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secretParam, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secretScalar, json, StringComparison.OrdinalIgnoreCase);

        // Plan XML raw attributes must NEVER be copied into the output
        Assert.DoesNotContain("StatementText", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ScalarString", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ParameterCompiledValue", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CONVERT_IMPLICIT", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE INDEX", json, StringComparison.OrdinalIgnoreCase);
    }
}
