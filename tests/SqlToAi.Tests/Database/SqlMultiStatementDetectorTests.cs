#nullable enable

using SqlToAi.Database;
using Xunit;

namespace SqlToAi.Tests.Database;

public sealed class SqlMultiStatementDetectorTests
{
    [Theory]
    [InlineData("SELECT * FROM Customers", false)]
    [InlineData("SELECT * FROM Customers;", false)]
    [InlineData("SELECT * FROM Customers;   ", false)]
    [InlineData("SELECT 'hello; world' AS Msg", false)]
    [InlineData("SELECT 'hello; world' AS Msg;", false)]
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
}
