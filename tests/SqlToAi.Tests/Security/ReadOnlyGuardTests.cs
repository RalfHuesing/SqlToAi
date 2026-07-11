#nullable enable

using SqlToAi.Security;

namespace SqlToAi.Tests.Security;

// @covers SqlToAi.Security.ReadOnlyGuard
public sealed class ReadOnlyGuardTests
{
    private static readonly Type TargetType = typeof(ReadOnlyGuard);

    [Fact]
    public void IsQuerySafe_ShouldReturnFalse_ForEmptyOrNullQuery()
    {
        // Arrange
        var guard = new ReadOnlyGuard();

        // Act & Assert
        Assert.False(guard.IsQuerySafe(""));
        Assert.False(guard.IsQuerySafe("   "));
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Customers")]
    [InlineData("SELECT Name, Email FROM Users WHERE Id = 10")]
    [InlineData("WITH cte AS (SELECT 1 AS X) SELECT X FROM cte")]
    [InlineData("SELECT * FROM dbo.Customers -- this is a comment")]
    [InlineData("SELECT * FROM dbo.Customers /* inline comment */ WHERE Id = 1")]
    [InlineData("SELECT * FROM dbo.Customers -- delete")]
    [InlineData("SELECT * FROM dbo.Customers /* update */")]
    public void IsQuerySafe_ShouldReturnTrue_ForSafeQueries(string query)
    {
        // Arrange
        var guard = new ReadOnlyGuard();

        // Act & Assert
        Assert.True(guard.IsQuerySafe(query));
    }

    [Theory]
    [InlineData("INSERT INTO Customers (Name) VALUES ('Test')")]
    [InlineData("UPDATE Customers SET Name = 'Test' WHERE Id = 1")]
    [InlineData("DELETE FROM Customers WHERE Id = 1")]
    [InlineData("DROP TABLE Customers")]
    [InlineData("ALTER TABLE Customers ADD Age INT")]
    [InlineData("TRUNCATE TABLE Logs")]
    [InlineData("CREATE TABLE Temp (Id INT)")]
    [InlineData("MERGE INTO Target USING Source ON (1=1) WHEN MATCHED THEN UPDATE SET Target.X = 1")]
    [InlineData("EXEC dbo.MyStoredProcedure")]
    [InlineData("EXECUTE dbo.MyStoredProcedure")]
    [InlineData("SELECT * INTO NewTable FROM OldTable")]
    [InlineData("INSERT INTO Logs VALUES (1) -- comment")]
    [InlineData("/* comment */ DELETE FROM Logs")]
    public void IsQuerySafe_ShouldReturnFalse_ForMutatingQueries(string query)
    {
        // Arrange
        var guard = new ReadOnlyGuard();

        // Act & Assert
        Assert.False(guard.IsQuerySafe(query));
    }
}
