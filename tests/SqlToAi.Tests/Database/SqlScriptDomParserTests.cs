#nullable enable

using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlToAi.Database;
using Xunit;

namespace SqlToAi.Tests.Database;

// @covers SqlToAi.Database.SqlScriptDomParser
public sealed class SqlScriptDomParserTests
{
    private static readonly System.Type TargetType = typeof(SqlScriptDomParser);

    [Fact]
    public void Parse_NullOrWhitespace_ReturnsNullAndEmptyErrors()
    {
        var resultNull = SqlScriptDomParser.Parse(null, out var errorsNull);
        var resultEmpty = SqlScriptDomParser.Parse("   ", out var errorsEmpty);

        Assert.Null(resultNull);
        Assert.Empty(errorsNull);
        Assert.Null(resultEmpty);
        Assert.Empty(errorsEmpty);
    }

    [Fact]
    public void ParseScript_ValidSelect_ReturnsTSqlScriptWithNoErrors()
    {
        const string sql = "SELECT Id, [Name] FROM dbo.Users WHERE Active = 1";
        var script = SqlScriptDomParser.ParseScript(sql, out var errors);

        Assert.NotNull(script);
        Assert.Empty(errors);
        Assert.Single(script.Batches);
        Assert.Single(script.Batches[0].Statements);
    }

    [Fact]
    public void ParseScript_InvalidSql_ReturnsErrors()
    {
        const string sql = "SELECT FROM WHERE";
        var script = SqlScriptDomParser.ParseScript(sql, out var errors);

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void CreateParser_ReturnsConfiguredTSql150Parser()
    {
        var parser = SqlScriptDomParser.CreateParser();
        Assert.NotNull(parser);
        Assert.IsType<TSql150Parser>(parser);
    }
}
