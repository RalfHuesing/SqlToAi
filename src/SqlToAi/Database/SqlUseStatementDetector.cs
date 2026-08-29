#nullable enable

using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlToAi.Database;

/// <summary>
/// Detects <c>USE [database]</c> statements in SQL query text using AST parsing and token inspection.
/// </summary>
internal static class SqlUseStatementDetector
{
    public static bool ContainsUseStatement(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var parseResult = SqlScriptDomParser.Parse(query);
        if (parseResult.Fragment is null)
        {
            return false;
        }

        var visitor = new UseStatementVisitor();
        parseResult.Fragment.Accept(visitor);
        if (visitor.HasUseStatement)
        {
            return true;
        }

        if (parseResult.Errors.Count > 0 && parseResult.Fragment.ScriptTokenStream is not null)
        {
            return ContainsUseToken(parseResult.Fragment.ScriptTokenStream);
        }

        return false;
    }

    private static bool ContainsUseToken(IList<TSqlParserToken> tokens)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].TokenType == TSqlTokenType.Use)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class UseStatementVisitor : TSqlFragmentVisitor
    {
        public bool HasUseStatement { get; private set; }

        public override void Visit(UseStatement node)
        {
            HasUseStatement = true;
        }
    }
}
