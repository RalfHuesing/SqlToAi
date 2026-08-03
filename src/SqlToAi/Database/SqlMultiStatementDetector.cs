#nullable enable

using System;
using System.Collections.Generic;

namespace SqlToAi.Database;

/// <summary>
/// Detects multiple SQL statements by scanning for semicolons outside string literals
/// (<c>'...'</c>), bracket identifiers (<c>[...]</c>), and comments (<c>--</c>, <c>/* */</c>).
/// Allows T-SQL <c>DECLARE</c> variable declarations before a single main query while rejecting
/// multiple main queries or mutating batches.
/// </summary>
internal static class SqlMultiStatementDetector
{
    public static bool ContainsMultipleStatements(string query)
    {
        var semicolonIndices = GetSemicolonIndices(query);
        if (semicolonIndices.Count == 0)
        {
            return false;
        }

        var segments = GetQuerySegments(query, semicolonIndices);
        int lastNonEmptyIndex = GetLastNonEmptySegmentIndex(segments);

        if (lastNonEmptyIndex <= 0)
        {
            return false;
        }

        for (int i = 0; i < lastNonEmptyIndex; i++)
        {
            if (string.IsNullOrWhiteSpace(segments[i]))
            {
                continue;
            }

            if (!IsDeclareStatement(segments[i]))
            {
                return true;
            }
        }

        return false;
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

    private static List<string> GetQuerySegments(string query, List<int> semicolonIndices)
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

    private static int GetLastNonEmptySegmentIndex(List<string> segments)
    {
        int index = segments.Count - 1;
        while (index >= 0 && string.IsNullOrWhiteSpace(segments[index]))
        {
            index--;
        }
        return index;
    }

    private static bool IsDeclareStatement(string statement)
    {
        string trimmed = StripLeadingCommentsAndWhitespace(statement);
        if (trimmed.StartsWith("DECLARE", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.Length == 7 || char.IsWhiteSpace(trimmed[7]) || trimmed[7] == '@';
        }

        return false;
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
