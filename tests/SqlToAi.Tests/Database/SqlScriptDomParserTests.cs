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
        var resultNull = SqlScriptDomParser.Parse(null);
        var resultEmpty = SqlScriptDomParser.Parse("   ");

        Assert.Null(resultNull.Fragment);
        Assert.Empty(resultNull.Errors);
        Assert.False(resultNull.Success);

        Assert.Null(resultEmpty.Fragment);
        Assert.Empty(resultEmpty.Errors);
        Assert.False(resultEmpty.Success);
    }

    [Fact]
    public void ParseScript_ValidSelect_ReturnsTSqlScriptWithNoErrors()
    {
        const string sql = "SELECT Id, [Name] FROM dbo.Users WHERE Active = 1";
        var result = SqlScriptDomParser.ParseScript(sql);

        Assert.True(result.Success);
        Assert.NotNull(result.Script);
        Assert.Empty(result.Errors);
        Assert.Single(result.Script.Batches);
        Assert.Single(result.Script.Batches[0].Statements);
    }

    [Fact]
    public void ParseScript_InvalidSql_ReturnsErrors()
    {
        const string sql = "SELECT FROM WHERE";
        var result = SqlScriptDomParser.ParseScript(sql);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void CreateParser_ReturnsConfiguredTSql150Parser()
    {
        var parser = SqlScriptDomParser.CreateParser();
        Assert.NotNull(parser);
        Assert.IsType<TSql150Parser>(parser);
    }
}
