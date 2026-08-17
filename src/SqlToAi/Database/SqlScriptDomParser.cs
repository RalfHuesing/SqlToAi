#nullable enable

using System.Collections.Generic;
using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlToAi.Database;

/// <summary>
/// Provides shared AST parsing methods using Microsoft.SqlServer.TransactSql.ScriptDom with TSql150 compatibility.
/// </summary>
internal static class SqlScriptDomParser
{
    /// <summary>
    /// Creates a configured TSql150Parser instance with quoted identifiers enabled and all SQL engine types supported.
    /// </summary>
    public static TSql150Parser CreateParser()
    {
        return new TSql150Parser(initialQuotedIdentifiers: true, SqlEngineType.All);
    }

    /// <summary>
    /// Parses a SQL string into a TSqlFragment AST.
    /// </summary>
    /// <param name="sql">The SQL query string.</param>
    /// <param name="errors">Output collection of parse errors.</param>
    /// <returns>The root TSqlFragment, or null if the input is null or whitespace.</returns>
    public static TSqlFragment? Parse(string? sql, out IList<ParseError> errors)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            errors = [];
            return null;
        }

        var parser = CreateParser();
        using var reader = new StringReader(sql);
        return parser.Parse(reader, out errors);
    }

    /// <summary>
    /// Parses a SQL string specifically into a TSqlScript AST.
    /// </summary>
    /// <param name="sql">The SQL query string.</param>
    /// <param name="errors">Output collection of parse errors.</param>
    /// <returns>The root TSqlScript if parsed, or null if empty or incompatible.</returns>
    public static TSqlScript? ParseScript(string? sql, out IList<ParseError> errors)
    {
        var fragment = Parse(sql, out errors);
        return fragment as TSqlScript;
    }
}
