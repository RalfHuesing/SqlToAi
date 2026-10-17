using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlToAi.Database;

/// <summary>
/// Locates single-quoted value literals using SQL syntax, excluding comments and identifiers,
/// including single-quoted column aliases. Unparseable input has no substitutable literals.
/// </summary>
internal static class SqlLiteralScanner
{
    /// <summary>Returns the (start, length) of each literal's content, excluding the surrounding quotes.</summary>
    public static IReadOnlyList<(int Start, int Length)> GetLiteralContentRanges(string sql)
    {
        var parsed = SqlScriptDomParser.Parse(sql);
        if (!parsed.Success)
        {
            return [];
        }

        var visitor = new ValueLiteralVisitor(sql);
        parsed.Fragment!.Accept(visitor);
        visitor.Ranges.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        return visitor.Ranges;
    }

    private sealed class ValueLiteralVisitor(string sql) : TSqlFragmentVisitor
    {
        public List<(int Start, int Length)> Ranges { get; } = [];

        public override void ExplicitVisit(SelectScalarExpression node)
        {
            // ColumnName can itself be a StringLiteral in all three T-SQL alias forms.
            node.Expression?.Accept(this);
        }

        public override void ExplicitVisit(StringLiteral node)
        {
            int quoteOffset = node.IsNational ? 1 : 0;
            int openingQuote = node.StartOffset + quoteOffset;
            // QUOTED_IDENTIFIER OFF can produce double-quoted StringLiteral nodes. The
            // resolver's existing contract only substitutes single-quoted literal content.
            if (sql[openingQuote] == '\'')
            {
                Ranges.Add((openingQuote + 1, node.FragmentLength - quoteOffset - 2));
            }
        }
    }
}
