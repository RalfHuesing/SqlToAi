#nullable enable

using System.Globalization;
using System.Text;

namespace SqlToAi.Tests.TestSupport;

/// <summary>
/// Builder for SQL Server ShowPlan XML test fixtures. Replaces 7 of 8 hand-rolled XML literals in
/// <c>PerformanceMeasurementServiceTests</c> (step-003 / DRY-T2). The 8th literal stays in the test
/// file because it tests non-<c>&lt;MissingIndex&gt;</c> XML paths
/// (<c>&lt;RelOp&gt;</c>, <c>&lt;Warnings&gt;</c>, <c>&lt;PlanAffectingConvert&gt;</c>) that this
/// helper does not model. Whitespace may differ from the originals —
/// <c>PerformanceMeasurementService.ParseExecutionPlanXml</c> uses <c>XDocument.Parse</c> which is
/// whitespace-tolerant; the assertions in the tests check on substring matches inside
/// <c>MissingIndexStatement</c> and on warning count, not on literal XML bytes.
/// </summary>
internal static class ShowPlanTestHelper
{
    /// <summary>
    /// Builds a single-statement, single-MissingIndex ShowPlan XML document with the given impact,
    /// table, and ordered column groups. Pass <paramref name="columns"/> in the order the columns
    /// should appear in the CREATE INDEX statement (equality first, then inequality, then include).
    /// </summary>
    public static string BuildShowPlanXml(double impact, string table, IReadOnlyList<ColumnSpec> columns)
    {
        var sb = new StringBuilder();
        sb.Append("<ShowPlanXML xmlns=\"http://schemas.microsoft.com/sqlserver/2004/07/showplan\">\n");
        sb.Append("  <BatchSequence>\n");
        sb.Append("    <Batch>\n");
        sb.Append("      <Statements>\n");
        sb.Append("        <StmtSimple>\n");
        sb.Append("          <QueryPlan>\n");
        sb.Append("            <MissingIndexes>\n");
        sb.Append("              <MissingIndexGroup Impact=\"")
          .Append(impact.ToString("F1", CultureInfo.InvariantCulture))
          .Append("\">\n");
        sb.Append("                <MissingIndex Table=\"").Append(table).Append("\">\n");
        foreach (ColumnSpec col in columns)
        {
            sb.Append("                  <ColumnGroup Usage=\"").Append(col.Usage).Append("\">\n");
            sb.Append("                    <Column Name=\"").Append(col.Name).Append('"');
            if (col.Descending.HasValue)
            {
                sb.Append(" Descending=\"").Append(col.Descending.Value ? "True" : "False").Append('"');
            }
            sb.Append(" />\n");
            sb.Append("                  </ColumnGroup>\n");
        }
        sb.Append("                </MissingIndex>\n");
        sb.Append("              </MissingIndexGroup>\n");
        sb.Append("            </MissingIndexes>\n");
        sb.Append("          </QueryPlan>\n");
        sb.Append("        </StmtSimple>\n");
        sb.Append("      </Statements>\n");
        sb.Append("    </Batch>\n");
        sb.Append("  </BatchSequence>\n");
        sb.Append("</ShowPlanXML>\n");
        return sb.ToString();
    }
}
