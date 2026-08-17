#nullable enable

using SqlToAi.Security;
using Xunit;

namespace SqlToAi.Tests.Security;

// @covers SqlToAi.Security.ReadOnlyGuard
public sealed class ReadOnlyGuardTests
{
    private static readonly System.Type TargetType = typeof(ReadOnlyGuard);

    [Fact]
    public void IsQuerySafe_ShouldReturnFalse_ForEmptyOrNullQuery()
    {
        var guard = new ReadOnlyGuard();

        Assert.False(guard.IsQuerySafe(""));
        Assert.False(guard.IsQuerySafe("   "));
        Assert.False(guard.IsQuerySafe(null!));
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
    [InlineData("SELECT [My Column With Spaces] FROM t")]
    [InlineData("SELECT [Order Date] FROM [Customer Orders]")]
    [InlineData("SELECT * FROM [dbo].[Customers]")]
    // Bracket-Identifier mit Schlüsselwort-Namen sind im echten AST reguläre Identifier (Spalten-/Tabellennamen)
    [InlineData("SELECT [insert] FROM t")]
    [InlineData("SELECT [drop] FROM t")]
    [InlineData("SELECT * FROM [delete]")]
    [InlineData("SELECT [update] FROM t WHERE [truncate] = 1")]
    // EXECUTE AS impersonation ist keine modifizierende Procedure-Execution
    [InlineData("EXECUTE AS USER = 'ReadOnlyUser'")]
    [InlineData("EXECUTE AS CALLER")]
    public void IsQuerySafe_ShouldReturnTrue_ForSafeQueries(string query)
    {
        var guard = new ReadOnlyGuard();
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
    [InlineData("SELECT Id INTO #Temp FROM Users")]
    [InlineData("INSERT INTO Logs VALUES (1) -- comment")]
    [InlineData("/* comment */ DELETE FROM Logs")]
    [InlineData("sp_executesql N'SELECT 1'")]
    [InlineData("EXEC sp_executesql N'SELECT 1'")]
    [InlineData("EXECUTE sp_executesql N'SELECT 1'")]
    [InlineData("sys.sp_executesql N'SELECT 1'")]
    [InlineData("SP_EXECUTESQL N'SELECT 1'")]
    [InlineData("Sp_ExecuteSql N'SELECT 1'")]
    [InlineData("sp_executesql N'DELETE FROM dbo.Customers; COMMIT'")]
    [InlineData("INSERT INTO [insert] VALUES (1)")]
    [InlineData("GRANT SELECT ON Customers TO GuestUser")]
    [InlineData("REVOKE SELECT ON Customers FROM GuestUser")]
    [InlineData("BACKUP DATABASE db TO DISK = 'c:\\backup.bak'")]
    [InlineData("DBCC CHECKDB")]
    public void IsQuerySafe_ShouldReturnFalse_ForMutatingQueries(string query)
    {
        var guard = new ReadOnlyGuard();
        Assert.False(guard.IsQuerySafe(query));
    }
}
