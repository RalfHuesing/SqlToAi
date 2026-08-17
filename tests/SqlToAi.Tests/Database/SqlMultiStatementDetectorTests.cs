#nullable enable

using SqlToAi.Database;
using Xunit;

namespace SqlToAi.Tests.Database;

// @covers SqlToAi.Database.SqlMultiStatementDetector
public sealed class SqlMultiStatementDetectorTests
{
    private static readonly System.Type TargetType = typeof(SqlMultiStatementDetector);

    [Fact]
    public void ContainsMultipleStatements_NullOrWhitespace_ReturnsFalse()
    {
        Assert.False(SqlMultiStatementDetector.ContainsMultipleStatements(""));
        Assert.False(SqlMultiStatementDetector.ContainsMultipleStatements("   "));
        Assert.False(SqlMultiStatementDetector.ContainsMultipleStatements(null!));
    }

    [Theory]
    [InlineData("SELECT * FROM Customers", false)]
    [InlineData("SELECT * FROM Customers;", false)]
    [InlineData("SELECT * FROM Customers;;;   ", false)]
    [InlineData("SELECT 'hello; world' AS Msg", false)]
    [InlineData("SELECT 'hello; world' AS Msg;", false)]
    [InlineData("SELECT ';', ';;;' FROM t WHERE col = ';'", false)]
    [InlineData("SELECT 1 UNION ALL SELECT 2 UNION SELECT 3", false)]
    [InlineData("SELECT 1 EXCEPT SELECT 2", false)]
    [InlineData("SELECT 1 INTERSECT SELECT 2", false)]
    [InlineData("-- comment with ;\nSELECT 1", false)]
    [InlineData("/* multi \n ; \n line */ SELECT 1", false)]
    public void SingleStatement_ReturnsFalse(string query, bool expected)
    {
        bool result = SqlMultiStatementDetector.ContainsMultipleStatements(query);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("SELECT 1; SELECT 2", true)]
    [InlineData("SELECT 1; SELECT 2;", true)]
    [InlineData("UPDATE TableA SET Col = 1; DELETE FROM TableB", true)]
    [InlineData("DECLARE @x INT = 1; SELECT 1; SELECT 2", true)]
    [InlineData("SET @x = 1; SELECT 1; SELECT 2", true)]
    [InlineData("USE [MyDb]; SELECT 1; SELECT 2;", true)]
    [InlineData("SELECT 1\nGO\nSELECT 2", true)]
    public void MultipleMainStatements_ReturnsTrue(string query, bool expected)
    {
        bool result = SqlMultiStatementDetector.ContainsMultipleStatements(query);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("DECLARE @Mandant INT = 3; SELECT * FROM Orders WHERE Mandant = @Mandant", false)]
    [InlineData("DECLARE @Mandant INT = 3; DECLARE @Gruppe VARCHAR(10) = 'AS'; SELECT * FROM Orders WHERE Mandant = @Mandant AND Gruppe = @Gruppe;", false)]
    [InlineData("-- Setup variables\nDECLARE @x INT = 1;\n/* Main Query */\nSELECT @x AS Val;", false)]
    [InlineData("DECLARE @x INT = 1;\nDECLARE @y INT = 2;\nWITH CTE AS (SELECT @x + @y AS SumVal) SELECT * FROM CTE;", false)]
    public void DeclareStatementsPrecedingSingleQuery_ReturnsFalse(string query, bool expected)
    {
        bool result = SqlMultiStatementDetector.ContainsMultipleStatements(query);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("SET NOCOUNT ON; SELECT * FROM Customers", false)]
    [InlineData("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SELECT * FROM Customers;", false)]
    [InlineData("DECLARE @id INT = 5; SET @id = 10; SELECT * FROM Users WHERE Id = @id", false)]
    [InlineData("USE MyDatabase; SELECT * FROM Orders", false)]
    [InlineData("USE [MyDatabase]; SET NOCOUNT ON; DECLARE @Status INT = 1; SELECT * FROM Items WHERE Status = @Status;", false)]
    public void SetAndUseStatementsPrecedingSingleQuery_ReturnsFalse(string query, bool expected)
    {
        bool result = SqlMultiStatementDetector.ContainsMultipleStatements(query);
        Assert.Equal(expected, result);
    }
}
