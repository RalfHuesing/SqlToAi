#nullable enable

using System.Text.RegularExpressions;

namespace SqlToAi.Security;

/// <summary>
/// Validates SQL queries to ensure they are strictly read-only and contain no mutating commands.
/// </summary>
public sealed class ReadOnlyGuard : IReadOnlyGuard
{
    private static readonly Regex MutatingKeywordsRegex = new(
        @"\b(insert|update|delete|drop|alter|truncate|create|merge|grant|revoke|reconfigure|checkpoint|backup|restore|dbcc|exec|execute|into)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));

    /// <summary>
    /// Checks if a query is safe for read-only execution by stripping comments and verifying against mutating keywords.
    /// </summary>
    /// <param name="query">The SQL query string to evaluate.</param>
    /// <returns>True if the query is safe (read-only); otherwise, false.</returns>
    public bool IsQuerySafe(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        try
        {
            string cleanQuery = StripSqlComments(query);
            return !MutatingKeywordsRegex.IsMatch(cleanQuery);
        }
        catch (RegexMatchTimeoutException)
        {
            // Fail secure: If regex times out, assume unsafe
            return false;
        }
    }

    private static string StripSqlComments(string sql)
    {
        // Remove multi-line comments /* ... */
        string noMultiLine = Regex.Replace(
            sql, 
            @"/\*.*?\*/", 
            string.Empty, 
            RegexOptions.Singleline, 
            TimeSpan.FromMilliseconds(200));

        // Remove single-line comments -- ...
        return Regex.Replace(
            noMultiLine, 
            @"--.*$", 
            string.Empty, 
            RegexOptions.Multiline, 
            TimeSpan.FromMilliseconds(200));
    }
}
