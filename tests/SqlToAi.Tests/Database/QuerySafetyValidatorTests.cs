#nullable enable

using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

// @covers SqlToAi.Database.QuerySafetyValidator
/// <summary>
/// Single source of truth for the 6-stage guardrail-pipeline tests. Replaces 31 individual
/// negative test cases that used to be duplicated across <c>QueryExecutionServiceTests</c>,
/// <c>QueryValidationServiceTests</c>, <c>PerformanceMeasurementServiceTests</c>, and
/// <c>QueryComparisonServiceTests</c> (step-003 / DRY-T3). The service tests now focus on
/// service-specific behaviour (transactions, anonymization, command-timeout source, before-touching
/// database) instead of the pipeline. Service-internal behaviour (e.g. transaction commit/rollback)
/// stays in the service tests.
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
    // Mirrors ShouldFail_WhenDatabaseNameIsEmpty across QE, PM, QC (use "") and QV (uses "   ").
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

    // -------------------------------------------------------------------------
    // Stage 2: empty / whitespace query → InvalidParameters
    // Mirrors ShouldFail_WhenQueryIsEmpty across QE, QV (use "   ") and PM, QC (use "").
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateQuerySafetyAsync_EmptyQuery_ReturnsInvalidParameters(string query)
    {
        var v = BuildValidator();
        var result = await v.ValidateQuerySafetyAsync("TestDb", query, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Stage 3: database not in whitelist → SafetyCheckFailed
    // Mirrors ShouldFail_WhenDatabaseNotAllowed across all four service tests.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQuerySafetyAsync_DatabaseNotAllowed_ReturnsSafetyCheckFailed()
    {
        var v = BuildValidator(isAllowed: false);
        var result = await v.ValidateQuerySafetyAsync("BlockedDb", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Stage 4a: AccessLevel.None → WriteOperationBlocked (regardless of allowSchemaOnly)
    // Mirrors ShouldFail_WhenAccessLevelIsNone (QV) and the None branch of
    // ShouldFail_WhenAccessLevelTooLow (QE), and AccessLevelNone branches in PM and QC.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQuerySafetyAsync_AccessLevelNone_ReturnsWriteOperationBlocked()
    {
        var v = BuildValidator(accessLevel: AccessLevel.None);
        var result = await v.ValidateQuerySafetyAsync("TestDb", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Stage 4b: AccessLevel.SchemaOnly without the allowSchemaOnly flag → WriteOperationBlocked
    // Mirrors the SchemaOnly branch of ShouldFail_WhenAccessLevelTooLow (QE).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQuerySafetyAsync_AccessLevelSchemaOnly_WithoutFlag_ReturnsWriteOperationBlocked()
    {
        var v = BuildValidator(accessLevel: AccessLevel.SchemaOnly);
        var result = await v.ValidateQuerySafetyAsync("TestDb", "SELECT 1", allowSchemaOnly: false, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Stage 4b positive: AccessLevel.SchemaOnly with allowSchemaOnly:true → Success
    // (only QueryValidationService opts into the SchemaOnly flag; the other three services
    // pass false by default. This test pins the QueryValidationService behaviour end-to-end.)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQuerySafetyAsync_AccessLevelSchemaOnly_WithFlag_ReturnsSuccess()
    {
        var v = BuildValidator(accessLevel: AccessLevel.SchemaOnly);
        var result = await v.ValidateQuerySafetyAsync("TestDb", "SELECT 1", allowSchemaOnly: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccessLevel.SchemaOnly, result.Value.AccessLevel);
        Assert.False(result.Value.IsWriteAllowed);
    }

    // -------------------------------------------------------------------------
    // Stage 5: mutating query without ReadWrite → WriteOperationBlocked.
    // Mirrors ShouldFail_WhenQueryIsMutating (QE) using "DELETE FROM Customers",
    // ShouldFail_WhenQueryIsMutating_AndAccessLevelIsNotReadWrite (QV) using "DELETE FROM Foo",
    // MeasurePerformanceAsync_MutatingQuery (PM) and CompareQueriesAsync_MutatingQuery (QC) using
    // "DROP TABLE Users". The four concrete queries are kept as four InlineData so the test
    // reproduces the exact behaviour that each service test pinned — no semantic change.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("DELETE FROM Customers")]
    [InlineData("DELETE FROM Foo")]
    [InlineData("DROP TABLE Users")]
    public async Task ValidateQuerySafetyAsync_MutatingQuery_WithoutReadWrite_ReturnsWriteOperationBlocked(string query)
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadOnly);
        var result = await v.ValidateQuerySafetyAsync("TestDb", query, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Stage 5: sp_executesql is rejected by the read-only-guard regex regardless of the
    // wrapping form. Mirrors ShouldReject_SpExecuteSql_BeforeTouchingDatabase (QV) — the
    // service-level "before touching DB" assertion stays in the service test; here we pin the
    // pipeline's WriteOperationBlocked outcome for all three wrapping forms.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("sp_executesql N'DELETE FROM Foo'")]
    [InlineData("EXEC sp_executesql N'DELETE FROM dbo.Foo; COMMIT'")]
    [InlineData("sys.sp_executesql N'DELETE FROM Foo'")]
    public async Task ValidateQuerySafetyAsync_SpExecuteSql_WithoutReadWrite_ReturnsWriteOperationBlocked(string query)
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadOnly);
        var result = await v.ValidateQuerySafetyAsync("TestDb", query, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Stage 5 positive: mutating query with ReadWrite → Success (write allowed, no rejection).
    // Mirrors ShouldNotBlock_MutatingQuery_WhenAccessLevelIsReadWrite (QV) at the pipeline layer.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQuerySafetyAsync_MutatingQuery_WithReadWrite_ReturnsSuccess()
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadWrite);
        var result = await v.ValidateQuerySafetyAsync("TestDb", "DELETE FROM Customers", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccessLevel.ReadWrite, result.Value.AccessLevel);
        Assert.True(result.Value.IsWriteAllowed);
    }

    // -------------------------------------------------------------------------
    // Stage 6: multi-statement detection — always enforced.
    // Mirrors ShouldFail_WhenMultipleStatements (QE) with three InlineData forms.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1 ; DROP TABLE Foo")]
    [InlineData("SELECT 'hello'; SELECT 'world'")]
    public async Task ValidateQuerySafetyAsync_MultiStatement_ReturnsMultipleStatementsForbidden(string query)
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadWrite);
        var result = await v.ValidateQuerySafetyAsync("TestDb", query, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Stage 6 parameterised by access level: multi-statement is rejected even at ReadWrite.
    // Mirrors ShouldFail_WhenMultipleStatements_RegardlessOfAccessLevel (QV) with two InlineData.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.ReadWrite)]
    public async Task ValidateQuerySafetyAsync_MultiStatement_RegardlessOfAccessLevel_ReturnsMultipleStatementsForbidden(AccessLevel level)
    {
        var v = BuildValidator(accessLevel: level);
        var result = await v.ValidateQuerySafetyAsync("TestDb", "SELECT 1; SELECT 2", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Happy path: all 6 stages pass — pins the AccessLevel plumbing end-to-end at the pipeline
    // layer. Uses ReadOnlyAnonymized because the service tests assert on the resolved
    // AccessLevel for that case (anonymization toggle).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQuerySafetyAsync_AllStagesPass_ReturnsResolvedAccessLevelAndWriteFlag()
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadOnlyAnonymized);
        var result = await v.ValidateQuerySafetyAsync("TestDb", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccessLevel.ReadOnlyAnonymized, result.Value.AccessLevel);
        Assert.False(result.Value.IsWriteAllowed);
    }

    // -------------------------------------------------------------------------
    // ReadWrite + simple SELECT: all stages pass with write allowed. Mirrors the happy-path
    // scenario the service tests use for the commit/rollback/anonymization assertions.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQuerySafetyAsync_ReadWrite_SelectQuery_ReturnsSuccess()
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadWrite);
        var result = await v.ValidateQuerySafetyAsync("TestDb", "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccessLevel.ReadWrite, result.Value.AccessLevel);
        Assert.True(result.Value.IsWriteAllowed);
    }

    // -------------------------------------------------------------------------
    // Short-circuit verification: an empty database name must fail before the whitelist is even
    // checked, so even a database that the whitelist would reject stays at InvalidParameters
    // (this guards against future reorderings of the 6 stages).
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
    // SchemaOnly blocks mutating queries even when allowSchemaOnly:true (the flag only bypasses
    // the access-level check, not the read-only guard's mutating-keyword detection).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQuerySafetyAsync_AccessLevelSchemaOnly_WithFlag_MutatingQuery_ReturnsWriteOperationBlocked()
    {
        var v = BuildValidator(accessLevel: AccessLevel.SchemaOnly);
        var result = await v.ValidateQuerySafetyAsync("TestDb", "DELETE FROM Customers", allowSchemaOnly: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }
}
