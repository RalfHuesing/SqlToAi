#nullable enable

using System.Collections.Generic;
using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlToAi.Database;

/// <summary>
/// Result of an AST parse operation containing the fragment (or null) and any parse errors.
/// </summary>
internal readonly record struct SqlParseResult(TSqlFragment? Fragment, IList<ParseError> Errors)
{
    public bool Success => Errors.Count == 0 && Fragment is not null;
}

/// <summary>
/// Result of a script parse operation containing the TSqlScript AST (or null) and any parse errors.
/// </summary>
internal readonly record struct SqlScriptParseResult(TSqlScript? Script, IList<ParseError> Errors)
{
    public bool Success => Errors.Count == 0 && Script is not null;
}

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
    /// <returns>A parse result containing the AST fragment and any parse errors.</returns>
    public static SqlParseResult Parse(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new SqlParseResult(null, []);
        }

        var parser = CreateParser();
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out IList<ParseError> errors);
        return new SqlParseResult(fragment, errors);
    }

    /// <summary>
    /// Parses a SQL string specifically into a TSqlScript AST.
    /// </summary>
    /// <param name="sql">The SQL query string.</param>
    /// <returns>A parse result containing the TSqlScript and any parse errors.</returns>
    public static SqlScriptParseResult ParseScript(string? sql)
    {
        var result = Parse(sql);
        return new SqlScriptParseResult(result.Fragment as TSqlScript, result.Errors);
    }
}
