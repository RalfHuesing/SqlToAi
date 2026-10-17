using SqlToAi.Database;

namespace SqlToAi.Tests.Database;

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

    [Theory]
    [InlineData("SELECT ''''", "''")]
    [InlineData("SELECT ''''''", "''''")]
    [InlineData("SELECT N'O''Brien'", "O''Brien")]
    [InlineData("SELECT n'O''Brien'", "O''Brien")]
    public void GetLiteralContentRanges_ShouldPreserveRawQuoteEscapesAndNationalPrefixes(string sql, string expected)
    {
        Assert.Equal([expected], ExtractLiterals(sql));
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
    public void GetLiteralContentRanges_ShouldIgnoreEscapedBracketIdentifiersAndDoubleQuotedIdentifiers()
    {
        var literals = ExtractLiterals("SELECT 1 AS [a]]'hidden'] , \"a\"\"'hidden'\" , 'real'");

        Assert.Equal(["real"], literals);
    }

    [Fact]
    public void GetLiteralContentRanges_ShouldIgnoreNestedBlockComments()
    {
        var literals = ExtractLiterals("SELECT 1 /* outer /* 'hidden' */ 'also hidden' */ WHERE A = 'real'");

        Assert.Equal(["real"], literals);
    }

    [Theory]
    [InlineData("SELECT 1 AS [unterminated 'hidden'")]
    [InlineData("SELECT 1 AS \"unterminated 'hidden'")]
    [InlineData("SELECT 1 /* outer /* 'hidden' */")]
    public void GetLiteralContentRanges_ShouldIgnoreMalformedTails(string sql)
    {
        Assert.Empty(ExtractLiterals(sql));
    }

    [Theory]
    [InlineData("SELECT 'real' AS 'hidden'")]
    [InlineData("SELECT 'real' 'hidden'")]
    [InlineData("SELECT 'hidden' = 'real'")]
    [InlineData("SELECT N'hidden' = N'real'")]
    public void GetLiteralContentRanges_ShouldExcludeSingleQuotedAliases(string sql)
    {
        Assert.Equal(["real"], ExtractLiterals(sql));
    }

    [Theory]
    [InlineData("SELECT 1 -- 'hidden'\rWHERE A = 'real'")]
    [InlineData("SELECT 1 -- 'hidden'\nWHERE A = 'real'")]
    [InlineData("SELECT 1 -- 'hidden'\r\nWHERE A = 'real'")]
    public void GetLiteralContentRanges_ShouldEndLineCommentsAtLineBreaks(string sql)
    {
        Assert.Equal(["real"], ExtractLiterals(sql));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-- 'hidden'")]
    [InlineData("SELECT 'unterminated")]
    [InlineData("SELECT 'complete', 'unterminated")]
    public void GetLiteralContentRanges_ShouldReturnNoRangesForEmptyOrUnparseableInput(string sql)
    {
        Assert.Empty(ExtractLiterals(sql));
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
