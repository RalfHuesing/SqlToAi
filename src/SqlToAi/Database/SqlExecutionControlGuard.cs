using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlToAi.Domain;

namespace SqlToAi.Database;

internal static class SqlExecutionControlGuard
{
    public static SqlToAiError? Validate(string sql, bool validationOnly)
    {
        var parsed = SqlScriptDomParser.Parse(sql);
        if (!parsed.Success)
        {
            return SqlToAiError.QueryError("SQL syntax could not be parsed safely before execution.");
        }

        var visitor = new ControlVisitor(validationOnly);
        parsed.Fragment!.Accept(visitor);
        return visitor.Rejected
            ? SqlToAiError.SafetyCheckFailed(validationOnly
                ? "Caller NOEXEC, PARSEONLY and FMTONLY settings are not permitted during validation."
                : "Explicit transaction control and IMPLICIT_TRANSACTIONS settings are not permitted in a server-managed script transaction.")
            : null;
    }

    private sealed class ControlVisitor(bool validationOnly) : TSqlFragmentVisitor
    {
        public bool Rejected { get; private set; }

        public override void Visit(TSqlFragment node)
        {
            // Traverse nested blocks and module bodies; string literals and comments are not statements.
            if (!validationOnly && node is TransactionStatement)
            {
                Rejected = true;
            }

            if (node is PredicateSetStatement setting)
            {
                SetOptions forbidden = validationOnly
                    ? SetOptions.NoExec | SetOptions.ParseOnly | SetOptions.FmtOnly
                    : SetOptions.ImplicitTransactions;
                Rejected |= (setting.Options & forbidden) != 0;
            }
        }
    }
}
