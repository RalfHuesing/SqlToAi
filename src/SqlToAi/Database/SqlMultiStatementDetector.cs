#nullable enable

namespace SqlToAi.Database;

/// <summary>
/// Detects multiple SQL statements by scanning for semicolons outside string literals
/// (<c>'...'</c>), bracket identifiers (<c>[...]</c>), and comments (<c>--</c>, <c>/* */</c>).
/// Extracted from <see cref="QueryExecutionService"/> so that file stays within the project's
/// line-count budget; this scanner has no dependency on the rest of that class.
/// </summary>
internal static class SqlMultiStatementDetector
{
    public static bool ContainsMultipleStatements(string query)
    {
        foreach (var ev in SqlCharScanner.Scan(query))
        {
            if (ev.State == SqlCharState.Normal && ev.Character == ';')
            {
                // Allow trailing semicolon at end (after trimming whitespace)
                string remaining = query[(ev.Index + 1)..].TrimEnd();
                if (remaining.Length > 0)
                {
                    return true; // text after semicolon → second statement
                }
            }
        }

        return false;
    }
}
