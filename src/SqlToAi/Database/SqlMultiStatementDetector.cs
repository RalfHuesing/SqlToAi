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
        var semicolonIndices = SqlCharScanner.GetSemicolonIndices(query);
        if (semicolonIndices.Count == 0)
        {
            return false;
        }

        var segments = SqlCharScanner.SplitIntoSegments(query, semicolonIndices);
        int lastNonEmptyIndex = SqlCharScanner.GetLastNonEmptySegmentIndex(segments);

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

    private static bool IsDeclareStatement(string statement)
    {
        string trimmed = SqlCharScanner.StripLeadingCommentsAndWhitespace(statement);
        if (trimmed.StartsWith("DECLARE", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.Length == 7 || char.IsWhiteSpace(trimmed[7]) || trimmed[7] == '@';
        }

        return false;
    }
}
