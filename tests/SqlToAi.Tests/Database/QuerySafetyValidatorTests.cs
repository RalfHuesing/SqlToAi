using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Single source of truth for the 6-stage guardrail-pipeline tests.
/// </summary>
public sealed class QuerySafetyValidatorTests
{
    private static QuerySafetyValidator BuildValidator(
        bool isAllowed = true,
        AccessLevel accessLevel = AccessLevel.ReadOnly) =>
        new QuerySafetyValidator(
            new FakeSecurityGuard(isAllowed),
            new FakeAccessLevelProvider(accessLevel),
            new ReadOnlyGuard());

    // -------------------------------------------------------------------------
    // Stage 1: empty / whitespace / null database name → InvalidParameters
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateQuerySafetyAsync_EmptyDatabaseName_ReturnsInvalidParameters(string db)
    {
        var v = BuildValidator();
        var result = await v.ValidateQuerySafetyAsync(db, "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task ValidateQuerySafetyAsync_NullDatabaseName_ReturnsInvalidParameters()
    {
        var v = BuildValidator();
        var result = await v.ValidateQuerySafetyAsync(null!, "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task ValidateQuerySafetyAsync_UnqualifiedDatabaseName_ReturnsInvalidParameters()
    {
        var v = BuildValidator();
        var result = await v.ValidateQuerySafetyAsync("UnqualifiedDb", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Stage 2: empty / whitespace query → InvalidParameters
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateQuerySafetyAsync_EmptyQuery_ReturnsInvalidParameters(string query)
    {
        var v = BuildValidator();
        var result = await v.ValidateQuerySafetyAsync("primary/TestDb", query, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Stage 3: database not in whitelist → SafetyCheckFailed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQuerySafetyAsync_DatabaseNotAllowed_ReturnsSafetyCheckFailed()
    {
        var v = BuildValidator(isAllowed: false);
        var result = await v.ValidateQuerySafetyAsync("primary/BlockedDb", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Stage 4a: AccessLevel.None → WriteOperationBlocked
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQuerySafetyAsync_AccessLevelNone_ReturnsWriteOperationBlocked()
    {
        var v = BuildValidator(accessLevel: AccessLevel.None);
        var result = await v.ValidateQuerySafetyAsync("primary/TestDb", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Stage 4b: AccessLevel.SchemaOnly without the allowSchemaOnly flag → WriteOperationBlocked
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQuerySafetyAsync_AccessLevelSchemaOnly_WithoutFlag_ReturnsWriteOperationBlocked()
    {
        var v = BuildValidator(accessLevel: AccessLevel.SchemaOnly);
        var result = await v.ValidateQuerySafetyAsync("primary/TestDb", "SELECT 1", allowSchemaOnly: false, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Stage 4b positive: AccessLevel.SchemaOnly with allowSchemaOnly:true → Success
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQuerySafetyAsync_AccessLevelSchemaOnly_WithFlag_ReturnsSuccess()
    {
        var v = BuildValidator(accessLevel: AccessLevel.SchemaOnly);
        var result = await v.ValidateQuerySafetyAsync("primary/TestDb", "SELECT 1", allowSchemaOnly: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccessLevel.SchemaOnly, result.Value.AccessLevel);
        Assert.False(result.Value.IsWriteAllowed);
    }

    // -------------------------------------------------------------------------
    // Stage 5: mutating query without ReadWrite → WriteOperationBlocked.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("DELETE FROM Customers")]
    [InlineData("DELETE FROM Foo")]
    [InlineData("DROP TABLE Users")]
    public async Task ValidateQuerySafetyAsync_MutatingQuery_WithoutReadWrite_ReturnsWriteOperationBlocked(string query)
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadOnly);
        var result = await v.ValidateQuerySafetyAsync("primary/TestDb", query, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Stage 5: sp_executesql is rejected by the read-only-guard regex
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("sp_executesql N'DELETE FROM Foo'")]
    [InlineData("EXEC sp_executesql N'DELETE FROM dbo.Foo; COMMIT'")]
    [InlineData("sys.sp_executesql N'DELETE FROM Foo'")]
    public async Task ValidateQuerySafetyAsync_SpExecuteSql_WithoutReadWrite_ReturnsWriteOperationBlocked(string query)
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadOnly);
        var result = await v.ValidateQuerySafetyAsync("primary/TestDb", query, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Stage 5 positive: mutating query with ReadWrite → Success (write allowed, no rejection).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQuerySafetyAsync_MutatingQuery_WithReadWrite_ReturnsSuccess()
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadWrite);
        var result = await v.ValidateQuerySafetyAsync("primary/TestDb", "DELETE FROM Customers", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccessLevel.ReadWrite, result.Value.AccessLevel);
        Assert.True(result.Value.IsWriteAllowed);
    }

    // -------------------------------------------------------------------------
    // Stage 6: multi-statement detection — always enforced.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1 ; DROP TABLE Foo")]
    [InlineData("SELECT 'hello'; SELECT 'world'")]
    public async Task ValidateQuerySafetyAsync_MultiStatement_ReturnsMultipleStatementsForbidden(string query)
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadWrite);
        var result = await v.ValidateQuerySafetyAsync("primary/TestDb", query, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Stage 6 parameterised by access level: multi-statement is rejected even at ReadWrite.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.ReadWrite)]
    public async Task ValidateQuerySafetyAsync_MultiStatement_RegardlessOfAccessLevel_ReturnsMultipleStatementsForbidden(AccessLevel level)
    {
        var v = BuildValidator(accessLevel: level);
        var result = await v.ValidateQuerySafetyAsync("primary/TestDb", "SELECT 1; SELECT 2", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Happy path: all 6 stages pass
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQuerySafetyAsync_AllStagesPass_ReturnsResolvedAccessLevelAndWriteFlag()
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadOnlyAnonymized);
        var result = await v.ValidateQuerySafetyAsync("primary/TestDb", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccessLevel.ReadOnlyAnonymized, result.Value.AccessLevel);
        Assert.False(result.Value.IsWriteAllowed);
    }

    // -------------------------------------------------------------------------
    // ReadWrite + simple SELECT: all stages pass with write allowed.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQuerySafetyAsync_ReadWrite_SelectQuery_ReturnsSuccess()
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadWrite);
        var result = await v.ValidateQuerySafetyAsync("primary/TestDb", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccessLevel.ReadWrite, result.Value.AccessLevel);
        Assert.True(result.Value.IsWriteAllowed);
    }

    // -------------------------------------------------------------------------
    // Short-circuit verification: an empty database name must fail before the whitelist is checked
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQuerySafetyAsync_EmptyDatabaseName_ShortCircuitsBeforeWhitelist()
    {
        var v = BuildValidator(isAllowed: false);
        var result = await v.ValidateQuerySafetyAsync("", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // SchemaOnly blocks mutating queries even when allowSchemaOnly:true
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQuerySafetyAsync_AccessLevelSchemaOnly_WithFlag_MutatingQuery_ReturnsWriteOperationBlocked()
    {
        var v = BuildValidator(accessLevel: AccessLevel.SchemaOnly);
        var result = await v.ValidateQuerySafetyAsync("primary/TestDb", "DELETE FROM Customers", allowSchemaOnly: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Fact]
    public async Task ValidateBatchSafetyAsync_ReadWrite_AllowsMultipleStatements()
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadWrite);
        var result = await v.ValidateBatchSafetyAsync("primary/TestDb", "UPDATE Customers SET Name = 'X'; UPDATE Orders SET Flag = 1", TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccessLevel.ReadWrite, result.Value.AccessLevel);
        Assert.True(result.Value.IsWriteAllowed);
    }

    [Fact]
    public async Task ValidateBatchSafetyAsync_ReadOnly_AllowsMultipleReadStatements()
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadOnly);
        var result = await v.ValidateBatchSafetyAsync("primary/TestDb", "SELECT 1; SELECT 2", TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccessLevel.ReadOnly, result.Value.AccessLevel);
        Assert.False(result.Value.IsWriteAllowed);
    }

    [Fact]
    public async Task ValidateBatchSafetyAsync_ReadOnly_RejectsMutatingStatement()
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadOnly);
        var result = await v.ValidateBatchSafetyAsync("primary/TestDb", "SELECT 1; DELETE FROM Customers", TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.ReadWrite)]
    public async Task ValidateQuerySafetyAsync_UseStatement_ReturnsSafetyCheckFailed(AccessLevel accessLevel)
    {
        var v = BuildValidator(accessLevel: accessLevel);
        var result = await v.ValidateQuerySafetyAsync("primary/TestDb", "USE master; SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.ReadWrite)]
    public async Task ValidateBatchSafetyAsync_UseStatement_ReturnsSafetyCheckFailed(AccessLevel accessLevel)
    {
        var v = BuildValidator(accessLevel: accessLevel);
        var result = await v.ValidateBatchSafetyAsync("primary/TestDb", "USE master", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }
}
