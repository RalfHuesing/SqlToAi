using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlToAi.Database;

internal static class SqlColumnReferenceDetector
{
    public static bool IsColumnIndependent(string? query)
    {
        var parsed = SqlScriptDomParser.Parse(query);
        if (!parsed.Success)
        {
            return false;
        }

        var visitor = new ColumnReferenceVisitor();
        parsed.Fragment!.Accept(visitor);
        return !visitor.Found;
    }

    private sealed class ColumnReferenceVisitor : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }

        public override void Visit(ColumnReferenceExpression node) => Found = true;

        public override void Visit(SelectStarExpression node) => Found = true;
    }
}
