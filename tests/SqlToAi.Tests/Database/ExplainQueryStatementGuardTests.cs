using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

public sealed class ExplainQueryStatementGuardTests
{
    private static readonly DatabaseTargetContext AllowedTarget = new("primary", "TestDb");
    private static readonly DatabaseTargetContext ExcludedTarget = new("primary", "ExcludedDb");

    private sealed class TargetFilteringSecurityGuard : ISecurityGuard
    {
        public HashSet<DatabaseTargetContext> AllowedTargets { get; } = [];
        public bool IsTargetAllowed(DatabaseTargetContext target) => AllowedTargets.Contains(target);
    }

    private static (ExplainQueryStatementGuard Guard, TargetFilteringSecurityGuard SecurityGuard, IReadOnlyGuard ReadOnlyGuard) CreateGuard()
    {
        var securityGuard = new TargetFilteringSecurityGuard();
        securityGuard.AllowedTargets.Add(AllowedTarget);

        var realReadOnlyGuard = new ReadOnlyGuard();

        var guard = new ExplainQueryStatementGuard(securityGuard, realReadOnlyGuard);
        return (guard, securityGuard, realReadOnlyGuard);
    }

    // -------------------------------------------------------------------------
    // 1. Accepted Shapes
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("SELECT 1;")]
    [InlineData("SELECT col1, col2 FROM dbo.Orders WHERE Id = 42")]
    [InlineData("SELECT o.Id, c.Name FROM dbo.Orders o INNER JOIN dbo.Customers c ON o.CustomerId = c.Id")]
    [InlineData("WITH cte AS (SELECT 1 AS n UNION ALL SELECT n + 1 FROM cte WHERE n < 5) SELECT n FROM cte OPTION (MAXRECURSION 5);")]
    [InlineData("SELECT 1 UNION ALL SELECT 2")]
    [InlineData("SELECT (SELECT 1) AS Sub")]
    [InlineData("DECLARE @x INT = 1; SELECT @x;")]
    [InlineData("DECLARE @x INT = 1, @y VARCHAR(50) = 'val'; SELECT @x, @y;")]
    [InlineData("DECLARE @x INT; DECLARE @y VARCHAR(50); SELECT @x, @y;")]
    [InlineData("DECLARE @t TABLE (Id INT, Val VARCHAR(50)); SELECT * FROM @t;")]
    [InlineData("DECLARE @t TABLE (Id INT); DECLARE @x INT = 10; SELECT * FROM @t WHERE Id = @x;")]
    public void ValidateStatement_AcceptsValidShapes(string query)
    {
        var (guard, _, _) = CreateGuard();

        var result = guard.ValidateStatement(query);

        Assert.True(result.IsSuccess);
    }

    // -------------------------------------------------------------------------
    // 2. Unparseable SQL fails closed with SQL-AI-0102
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("SELECT")]
    [InlineData("SELECT 1,")]
    [InlineData("SELECT FROM")]
    [InlineData("SELECT * FROM WHERE")]
    [InlineData("FOOBAR NON SENSE ???")]
    [InlineData("SELECT 1; SELECT FROM")]
    public void ValidateStatement_UnparseableSql_FailsWithSqlAi0102(string invalidSql)
    {
        var (guard, _, _) = CreateGuard();

        var result = guard.ValidateStatement(invalidSql);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // 3. Controls anywhere fail with SQL-AI-0104
    // -------------------------------------------------------------------------

    [Theory]
    // SET controls before SELECT
    [InlineData("SET SHOWPLAN_XML ON; SELECT 1;")]
    [InlineData("SET SHOWPLAN_XML OFF; SELECT 1;")]
    [InlineData("SET NOEXEC ON; SELECT 1;")]
    [InlineData("SET NOEXEC OFF; SELECT 1;")]
    [InlineData("SET PARSEONLY ON; SELECT 1;")]
    [InlineData("SET FMTONLY ON; SELECT 1;")]
    [InlineData("SET STATISTICS IO ON; SELECT 1;")]
    [InlineData("SET STATISTICS TIME ON; SELECT 1;")]
    [InlineData("SET STATISTICS XML ON; SELECT 1;")]
    [InlineData("SET @x = 1; SELECT @x;")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL READ COMMITTED; SELECT 1;")]
    [InlineData("SET ROWCOUNT 10; SELECT 1;")]
    [InlineData("SET DEADLOCK_PRIORITY LOW; SELECT 1;")]
    [InlineData("SET ARITHABORT ON; SELECT 1;")]
    [InlineData("SET ANSI_NULLS ON; SELECT 1;")]
    // Transaction controls before SELECT
    [InlineData("BEGIN TRAN; SELECT 1;")]
    [InlineData("BEGIN TRANSACTION; SELECT 1;")]
    // USE before SELECT
    [InlineData("USE master; SELECT 1;")]
    [InlineData("USE [TestDb]; SELECT 1;")]
    // Trailing controls after SELECT
    [InlineData("SELECT 1; SET SHOWPLAN_XML OFF;")]
    [InlineData("SELECT 1; SET NOEXEC OFF;")]
    [InlineData("SELECT 1; SET STATISTICS IO OFF;")]
    [InlineData("SELECT 1; COMMIT;")]
    [InlineData("SELECT 1; COMMIT TRAN;")]
    [InlineData("SELECT 1; ROLLBACK;")]
    [InlineData("SELECT 1; ROLLBACK TRANSACTION;")]
    [InlineData("SELECT 1; USE tempdb;")]
    [InlineData("SELECT 1; SET @x = 2;")]
    [InlineData("DECLARE @x INT; SELECT @x; SET @x = 2;")]
    public void ValidateStatement_Controls_FailWithSqlAi0104(string queryWithControls)
    {
        var (guard, _, _) = CreateGuard();

        var result = guard.ValidateStatement(queryWithControls);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // 4. Mutating statements fail with SQL-AI-0107
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("SELEC 1")]
    [InlineData("INSERT INTO dbo.Orders (Id) VALUES (1);")]
    [InlineData("UPDATE dbo.Orders SET col = 1;")]
    [InlineData("DELETE FROM dbo.Orders WHERE Id = 1;")]
    [InlineData("TRUNCATE TABLE dbo.Orders;")]
    [InlineData("SELECT 1 INTO #TempTable;")]
    [InlineData("SELECT * INTO NewTable FROM dbo.Orders;")]
    [InlineData("EXEC sp_who2;")]
    [InlineData("EXECUTE dbo.CustomProc;")]
    [InlineData("CREATE TABLE dbo.Foo (Id INT);")]
    [InlineData("ALTER TABLE dbo.Foo ADD Bar INT;")]
    [InlineData("DROP TABLE dbo.Foo;")]
    [InlineData("CREATE VIEW dbo.V AS SELECT 1 AS x;")]
    [InlineData("SELECT 1; INSERT INTO dbo.Orders (Id) VALUES (1);")]
    [InlineData("SELECT 1; DROP TABLE dbo.Foo;")]
    public void ValidateStatement_Mutations_FailWithSqlAi0107(string mutatingQuery)
    {
        var (guard, _, _) = CreateGuard();

        var result = guard.ValidateStatement(mutatingQuery);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // 5. Multiple main statements or batches fail with SQL-AI-0101
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("SELECT 1; SELECT 2;")]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1; DECLARE @x INT;")]
    [InlineData("SELECT 1\nGO\nSELECT 2")]
    [InlineData("SELECT 1\nGO\nSELECT 1")]
    [InlineData("DECLARE @x INT = 1;")]
    [InlineData("DECLARE @t TABLE (Id INT);")]
    [InlineData("PRINT 'hello'; SELECT 1;")]
    [InlineData("WAITFOR DELAY '00:00:01'; SELECT 1;")]
    [InlineData("IF 1=1 SELECT 1;")]
    public void ValidateStatement_MultipleStatementsOrBatches_FailWithSqlAi0101(string multiQuery)
    {
        var (guard, _, _) = CreateGuard();

        var result = guard.ValidateStatement(multiQuery);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // 6. Empty / Whitespace query fails with SQL-AI-0001
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void ValidateStatement_EmptyOrWhitespace_FailsWithSqlAi0001(string emptyQuery)
    {
        var (guard, _, _) = CreateGuard();

        var result = guard.ValidateStatement(emptyQuery);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // 7. Access checks: None & excluded targets fail with SQL-AI-0104; others pass
    // -------------------------------------------------------------------------

    [Fact]
    public void ValidateAccess_TargetNotAllowed_FailsWithSqlAi0104()
    {
        var (guard, _, _) = CreateGuard();

        var result = guard.ValidateAccess(ExcludedTarget, AccessLevel.ReadOnly);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    [Fact]
    public void ValidateAccess_AccessLevelNone_FailsWithSqlAi0104()
    {
        var (guard, _, _) = CreateGuard();

        var result = guard.ValidateAccess(AllowedTarget, AccessLevel.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    [Theory]
    [InlineData(AccessLevel.SchemaOnly)]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.ReadOnlyAnonymized)]
    [InlineData(AccessLevel.ReadWrite)]
    [InlineData(AccessLevel.ReadWriteAnonymized)]
    public void ValidateAccess_PermittedAccessLevels_Succeed(AccessLevel level)
    {
        var (guard, _, _) = CreateGuard();

        var result = guard.ValidateAccess(AllowedTarget, level);

        Assert.True(result.IsSuccess);
    }

    // -------------------------------------------------------------------------
    // 8. Full Validate across all access levels including ReadWrite
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(AccessLevel.SchemaOnly)]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.ReadOnlyAnonymized)]
    [InlineData(AccessLevel.ReadWrite)]
    [InlineData(AccessLevel.ReadWriteAnonymized)]
    public void Validate_AcceptedShape_SucceedsAcrossAllPermittedAccessLevels(AccessLevel level)
    {
        var (guard, _, _) = CreateGuard();

        var result = guard.Validate(AllowedTarget, level, "DECLARE @id INT = 1; SELECT * FROM dbo.Orders WHERE Id = @id;");

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(AccessLevel.SchemaOnly)]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.ReadOnlyAnonymized)]
    [InlineData(AccessLevel.ReadWrite)]
    [InlineData(AccessLevel.ReadWriteAnonymized)]
    public void Validate_MutatingQuery_FailsWithSqlAi0107AcrossAllAccessLevelsIncludingReadWrite(AccessLevel level)
    {
        var (guard, _, _) = CreateGuard();

        var result = guard.Validate(AllowedTarget, level, "INSERT INTO dbo.Orders (Id) VALUES (1);");

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Theory]
    [InlineData(AccessLevel.ReadWrite)]
    [InlineData(AccessLevel.ReadWriteAnonymized)]
    public void Validate_SelectInto_FailsWithSqlAi0107AtReadWriteLevels(AccessLevel level)
    {
        var (guard, _, _) = CreateGuard();

        var result = guard.Validate(AllowedTarget, level, "SELECT 1 INTO #Temp;");

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Theory]
    [InlineData(AccessLevel.ReadWrite)]
    [InlineData(AccessLevel.ReadWriteAnonymized)]
    public void Validate_TrailingControl_FailsWithSqlAi0104AtReadWriteLevels(AccessLevel level)
    {
        var (guard, _, _) = CreateGuard();

        var result = guard.Validate(AllowedTarget, level, "SELECT 1; SET SHOWPLAN_XML OFF;");

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }
}
