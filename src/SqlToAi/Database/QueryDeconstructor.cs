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

        string trimmedBody = StripLeadingCommentsAndWhitespace(body);
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
        var semicolonIndices = GetSemicolonIndices(query);
        if (semicolonIndices.Count == 0)
        {
            return (string.Empty, query.Trim());
        }

        var segments = GetSegmentsFromIndices(query, semicolonIndices);
        int lastNonEmptyIndex = GetLastNonEmptyIndex(segments);

        if (lastNonEmptyIndex <= 0)
        {
            return (string.Empty, query.Trim());
        }

        return BuildPreambleAndBody(segments, lastNonEmptyIndex);
    }

    private static List<int> GetSemicolonIndices(string query)
    {
        var indices = new List<int>();
        foreach (var ev in SqlCharScanner.Scan(query))
        {
            if (ev.State == SqlCharState.Normal && ev.Character == ';')
            {
                indices.Add(ev.Index);
            }
        }
        return indices;
    }

    private static List<string> GetSegmentsFromIndices(string query, List<int> semicolonIndices)
    {
        var segments = new List<string>();
        int lastIndex = 0;
        foreach (int idx in semicolonIndices)
        {
            segments.Add(query[lastIndex..idx]);
            lastIndex = idx + 1;
        }
        if (lastIndex <= query.Length)
        {
            segments.Add(query[lastIndex..]);
        }
        return segments;
    }

    private static int GetLastNonEmptyIndex(List<string> segments)
    {
        int index = segments.Count - 1;
        while (index >= 0 && string.IsNullOrWhiteSpace(segments[index]))
        {
            index--;
        }
        return index;
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
        string trimmed = StripLeadingCommentsAndWhitespace(ctes);
        if (trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[4..].Trim();
        }
        return ctes;
    }

    private static string StripLeadingCommentsAndWhitespace(string sql)
    {
        int index = 0;
        while (index < sql.Length)
        {
            if (char.IsWhiteSpace(sql[index]))
            {
                index++;
                continue;
            }

            if (TrySkipComment(sql, ref index))
            {
                continue;
            }

            break;
        }

        return sql[index..];
    }

    private static bool TrySkipComment(string sql, ref int index)
    {
        return TrySkipLineComment(sql, ref index) || TrySkipBlockComment(sql, ref index);
    }

    private static bool TrySkipLineComment(string sql, ref int index)
    {
        if (index + 1 >= sql.Length || sql[index] != '-' || sql[index + 1] != '-')
        {
            return false;
        }

        index += 2;
        while (index < sql.Length && sql[index] != '\n')
        {
            index++;
        }
        if (index < sql.Length && sql[index] == '\n')
        {
            index++;
        }
        return true;
    }

    private static bool TrySkipBlockComment(string sql, ref int index)
    {
        if (index + 1 >= sql.Length || sql[index] != '/' || sql[index + 1] != '*')
        {
            return false;
        }

        index += 2;
        while (index + 1 < sql.Length && !(sql[index] == '*' && sql[index + 1] == '/'))
        {
            index++;
        }
        if (index + 1 < sql.Length)
        {
            index += 2;
        }
        return true;
    }
}
