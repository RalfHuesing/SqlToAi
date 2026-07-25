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
    [InlineData("SELECT 'DELETE' AS Status")]
    [InlineData("SELECT * FROM Customers WHERE Status = 'UPDATE'")]
    [InlineData("SELECT HAS_PERMS_BY_NAME('T', 'OBJECT', 'EXECUTE') AS CanExec")]
    [InlineData("SELECT 'it''s a delete-like value' AS Note")]
    // step-004/fix-01: harmlose Bracket-Identifier muessen safe bleiben. Bracket-Inhalt wird
    // an die Regex durchgereicht, aber das Wort in den Klammern ist nicht im Mutating-Set.
    [InlineData("SELECT [My Column With Spaces] FROM t")]
    [InlineData("SELECT [Order Date] FROM [Customer Orders]")]
    [InlineData("SELECT * FROM [dbo].[Customers]")]
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
    // sp_executesql bypass (audit finding 2): one contiguous token, so "exec" never appears as
    // its own bounded match inside it, and T-SQL allows it as a batch's sole statement with no
    // EXEC/EXECUTE prefix at all — must be rejected outright, regardless of prefix/case/schema
    // qualifier, and regardless of what the dynamic SQL literal argument contains.
    [InlineData("sp_executesql N'SELECT 1'")]
    [InlineData("EXEC sp_executesql N'SELECT 1'")]
    [InlineData("EXECUTE sp_executesql N'SELECT 1'")]
    [InlineData("sys.sp_executesql N'SELECT 1'")]
    [InlineData("SP_EXECUTESQL N'SELECT 1'")]
    [InlineData("Sp_ExecuteSql N'SELECT 1'")]
    [InlineData("sp_executesql N'DELETE FROM dbo.Customers; COMMIT'")]
    // step-004/fix-01: Bracket-Identifier mit mutating-keyword-aehnlichem Inhalt muessen
    // abgewiesen werden. .NET-Regex-Wortgrenzen \b bilden sich an '[' und ']', sodass insert
    // innerhalb von [insert] als eigenstaendiges Token matcht. Ohne Bracket-Pass-Through
    // waeren diese Queries faelschlich als safe eingestuft worden.
    [InlineData("SELECT [insert] FROM t")]
    [InlineData("SELECT [drop] FROM t")]
    [InlineData("SELECT * FROM [delete]")]
    [InlineData("SELECT [update] FROM t WHERE [truncate] = 1")]
    [InlineData("INSERT INTO [insert] VALUES (1)")]
    public void IsQuerySafe_ShouldReturnFalse_ForMutatingQueries(string query)
    {
        // Arrange
        var guard = new ReadOnlyGuard();

        // Act & Assert
        Assert.False(guard.IsQuerySafe(query));
    }
}
