#nullable enable

using System;
using System.Collections.Generic;

namespace SqlToAi.Database;

internal readonly record struct DeconstructedQuery(string Preamble, string Ctes, string MainSelect);

/// <summary>
/// Deconstructs SQL queries into Preamble (DECLARE statements), CTE definitions (WITH clauses),
/// and the main SELECT statement for database-side query comparison and subquery wrapping.
/// </summary>
internal static class QueryDeconstructor
{
    public static DeconstructedQuery Deconstruct(string query)
    {
        var (preamble, body) = ExtractPreambleAndBody(query);

        string trimmedBody = SqlCharScanner.StripLeadingCommentsAndWhitespace(body);
        if (!trimmedBody.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            return new DeconstructedQuery(preamble, string.Empty, body);
        }

        int selectIndex = FindMainSelectIndex(body);
        if (selectIndex <= 0)
        {
            return new DeconstructedQuery(preamble, string.Empty, body);
        }

        string ctes = body[..selectIndex].Trim();
        string mainSelect = body[selectIndex..].Trim();

        return new DeconstructedQuery(preamble, ctes, mainSelect);
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

    private static (string Preamble, string Body) ExtractPreambleAndBody(string query)
    {
        var semicolonIndices = SqlCharScanner.GetSemicolonIndices(query);
        if (semicolonIndices.Count == 0)
        {
            return (string.Empty, query.Trim());
        }

        var segments = SqlCharScanner.SplitIntoSegments(query, semicolonIndices);
        int lastNonEmptyIndex = SqlCharScanner.GetLastNonEmptySegmentIndex(segments);

        if (lastNonEmptyIndex <= 0)
        {
            return (string.Empty, query.Trim());
        }

        return BuildPreambleAndBody(segments, lastNonEmptyIndex);
    }

    private static (string Preamble, string Body) BuildPreambleAndBody(List<string> segments, int lastNonEmptyIndex)
    {
        var preambleParts = new List<string>();
        for (int i = 0; i < lastNonEmptyIndex; i++)
        {
            if (!string.IsNullOrWhiteSpace(segments[i]))
            {
                preambleParts.Add(segments[i].Trim() + ";");
            }
        }

        var bodyParts = new List<string>();
        for (int i = lastNonEmptyIndex; i < segments.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(segments[i]))
            {
                bodyParts.Add(segments[i].Trim());
            }
        }

        return (string.Join("\n", preambleParts), string.Join(";\n", bodyParts));
    }

    private static int FindMainSelectIndex(string sql)
    {
        int depth = 0;
        foreach (var ev in SqlCharScanner.Scan(sql))
        {
            if (ev.State == SqlCharState.Normal)
            {
                if (ev.Character == '(')
                {
                    depth++;
                }
                else if (ev.Character == ')')
                {
                    if (depth > 0) depth--;
                }
                else if (depth == 0 && IsWordAt(sql, ev.Index, "SELECT"))
                {
                    return ev.Index;
                }
            }
        }

        return -1;
    }

    private static bool IsWordAt(string sql, int index, string word)
    {
        if (index + word.Length > sql.Length) return false;

        if (string.Compare(sql, index, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0)
        {
            return false;
        }

        if (index > 0 && (char.IsLetterOrDigit(sql[index - 1]) || sql[index - 1] == '_'))
        {
            return false;
        }

        int nextIndex = index + word.Length;
        if (nextIndex < sql.Length && (char.IsLetterOrDigit(sql[nextIndex]) || sql[nextIndex] == '_'))
        {
            return false;
        }

        return true;
    }

    private static string StripWithPrefix(string ctes)
    {
        string trimmed = SqlCharScanner.StripLeadingCommentsAndWhitespace(ctes);
        if (trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[4..].Trim();
        }
        return ctes;
    }
}
