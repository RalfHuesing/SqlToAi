#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlToAi.Database;

internal readonly record struct DeconstructedQuery(string Preamble, string Ctes, string MainSelect);

/// <summary>
/// Deconstructs SQL queries into Preamble (DECLARE, SET statements), CTE definitions (WITH clauses),
/// and the main SELECT statement using AST navigation for database-side query comparison and subquery wrapping.
/// </summary>
internal static class QueryDeconstructor
{
    public static DeconstructedQuery Deconstruct(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new DeconstructedQuery(string.Empty, string.Empty, string.Empty);
        }

        var parseResult = SqlScriptDomParser.ParseScript(query);
        if (parseResult.Script is null || parseResult.Script.Batches.Count == 0)
        {
            return new DeconstructedQuery(string.Empty, string.Empty, CleanMainSelect(query));
        }

        var allStatements = new List<TSqlStatement>();
        foreach (var batch in parseResult.Script.Batches)
        {
            allStatements.AddRange(batch.Statements);
        }

        if (allStatements.Count == 0)
        {
            return new DeconstructedQuery(string.Empty, string.Empty, CleanMainSelect(query));
        }

        int mainIndex = allStatements.Count - 1;
        string preamble = ExtractPreamble(query, allStatements, mainIndex);
        var mainStatement = allStatements[mainIndex];

        if (mainStatement is SelectStatement selectStatement && selectStatement.WithCtesAndXmlNamespaces is not null)
        {
            var withClause = selectStatement.WithCtesAndXmlNamespaces;
            string ctes = query.Substring(withClause.StartOffset, withClause.FragmentLength).Trim();

            int selectStart = withClause.StartOffset + withClause.FragmentLength;
            int selectLength = (mainStatement.StartOffset + mainStatement.FragmentLength) - selectStart;
            string mainSelect = CleanMainSelect(query.Substring(selectStart, selectLength));

            return new DeconstructedQuery(preamble, ctes, mainSelect);
        }

        string singleMainSelect = CleanMainSelect(query.Substring(mainStatement.StartOffset, mainStatement.FragmentLength));
        return new DeconstructedQuery(preamble, string.Empty, singleMainSelect);
    }

    public static string CombinePreambles(string preambleA, string preambleB)
    {
        if (string.IsNullOrWhiteSpace(preambleA)) return preambleB;
        if (string.IsNullOrWhiteSpace(preambleB)) return preambleA;

        var lines = new List<string>();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in new[] { preambleA, preambleB })
        {
            var parts = p.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (!string.IsNullOrWhiteSpace(part) && set.Add(part))
                {
                    lines.Add(part + ";");
                }
            }
        }

        return string.Join("\n", lines);
    }

    public static string CombineCtes(string ctesA, string ctesB)
    {
        if (string.IsNullOrWhiteSpace(ctesA)) return ctesB;
        if (string.IsNullOrWhiteSpace(ctesB)) return ctesA;

        string bodyA = StripWithPrefix(ctesA);
        string bodyB = StripWithPrefix(ctesB);

        return $"WITH {bodyA}, {bodyB}";
    }

    private static string ExtractPreamble(string query, List<TSqlStatement> statements, int mainIndex)
    {
        var preambleParts = new List<string>();
        for (int i = 0; i < mainIndex; i++)
        {
            var stmt = statements[i];
            string stmtText = query.Substring(stmt.StartOffset, stmt.FragmentLength).Trim();
            if (!string.IsNullOrWhiteSpace(stmtText))
            {
                if (!stmtText.EndsWith(';'))
                {
                    stmtText += ";";
                }
                preambleParts.Add(stmtText);
            }
        }

        return string.Join("\n", preambleParts);
    }

    private static string CleanMainSelect(string sql)
    {
        return sql.TrimEnd(';', ' ', '\t', '\r', '\n').Trim();
    }

    private static string StripWithPrefix(string ctes)
    {
        string trimmed = ctes.Trim();
        if (trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[4..].Trim();
        }
        return trimmed;
    }
}
