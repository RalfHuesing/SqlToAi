#nullable enable

using SqlToAi.Database;

namespace SqlToAi.Tests.Database;

// @covers SqlToAi.Database.SqlLiteralScanner
public sealed class SqlLiteralScannerTests
{
    private static string[] ExtractLiterals(string sql) =>
        SqlLiteralScanner.GetLiteralContentRanges(sql)
            .Select(r => sql.Substring(r.Start, r.Length))
            .ToArray();

    [Fact]
    public void GetLiteralContentRanges_ShouldReturnEmpty_WhenNoLiteralsPresent()
    {
        Assert.Empty(ExtractLiterals("SELECT * FROM Customers WHERE Id = 1"));
    }

    [Fact]
    public void GetLiteralContentRanges_ShouldFindSingleLiteral()
    {
        var literals = ExtractLiterals("SELECT * FROM Customers WHERE Name = 'Ralf'");

        Assert.Equal(["Ralf"], literals);
    }

    [Fact]
    public void GetLiteralContentRanges_ShouldFindMultipleLiterals()
    {
        var literals = ExtractLiterals("SELECT * FROM T WHERE A = 'X' AND B = 'Y'");

        Assert.Equal(["X", "Y"], literals);
    }

    [Fact]
    public void GetLiteralContentRanges_ShouldHandleEscapedQuotes_AsLiteralContent()
    {
        var literals = ExtractLiterals("SELECT * FROM T WHERE Name = 'O''Brien'");

        Assert.Equal(["O''Brien"], literals);
    }

    [Fact]
    public void GetLiteralContentRanges_ShouldIgnoreContentInsideLineComments()
    {
        var literals = ExtractLiterals("SELECT 1 -- 'not a literal'\nWHERE A = 'real'");

        Assert.Equal(["real"], literals);
    }

    [Fact]
    public void GetLiteralContentRanges_ShouldIgnoreContentInsideBlockComments()
    {
        var literals = ExtractLiterals("SELECT 1 /* 'not a literal' */ WHERE A = 'real'");

        Assert.Equal(["real"], literals);
    }

    [Fact]
    public void GetLiteralContentRanges_ShouldIgnoreContentInsideBracketIdentifiers()
    {
        var literals = ExtractLiterals("SELECT [Weird'Column] FROM T WHERE A = 'real'");

        Assert.Equal(["real"], literals);
    }

    [Fact]
    public void GetLiteralContentRanges_ShouldReturnEmptyString_ForEmptyLiteral()
    {
        var literals = ExtractLiterals("SELECT * FROM T WHERE A = ''");

        Assert.Equal([""], literals);
    }

    [Fact]
    public void GetLiteralContentRanges_ShouldSupportLikePatternWithWildcards()
    {
        var literals = ExtractLiterals("SELECT * FROM T WHERE A LIKE '%middle%'");

        Assert.Equal(["%middle%"], literals);
    }

    [Fact]
    public void GetLiteralContentRanges_ShouldReturnOffsetsThatRoundTripTheOriginalText()
    {
        const string sql = "SELECT * FROM T WHERE A = 'first' AND B = 'second'";

        var ranges = SqlLiteralScanner.GetLiteralContentRanges(sql);

        Assert.Equal(2, ranges.Count);
        Assert.Equal("first", sql.Substring(ranges[0].Start, ranges[0].Length));
        Assert.Equal("second", sql.Substring(ranges[1].Start, ranges[1].Length));
    }
}
