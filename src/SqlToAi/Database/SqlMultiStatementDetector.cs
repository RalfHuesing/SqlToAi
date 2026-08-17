#nullable enable

using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlToAi.Database;

/// <summary>
/// Detects multiple SQL statements using the AST parser.
/// Allows T-SQL preamble statements (such as DECLARE, SET, USE) before a single main query,
/// while rejecting multiple main queries or multi-statement batches.
/// </summary>
internal static class SqlMultiStatementDetector
{
    /// <summary>
    /// Checks whether the given SQL query contains more than one non-preamble statement across all batches.
    /// </summary>
    /// <param name="query">The SQL query string.</param>
    /// <returns>True if more than one main statement is detected; otherwise, false.</returns>
    public static bool ContainsMultipleStatements(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var parseResult = SqlScriptDomParser.ParseScript(query);
        var script = parseResult.Script;
        if (script == null)
        {
            return false;
        }

        int nonPreambleCount = 0;
        foreach (var batch in script.Batches)
        {
            foreach (var statement in batch.Statements)
            {
                if (!IsPreambleStatement(statement))
                {
                    nonPreambleCount++;
                    if (nonPreambleCount > 1)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool IsPreambleStatement(TSqlStatement statement)
    {
        return statement is DeclareVariableStatement
            or SetVariableStatement
            or PredicateSetStatement
            or SetTransactionIsolationLevelStatement
            or SetCommandStatement
            or UseStatement;
    }
}
