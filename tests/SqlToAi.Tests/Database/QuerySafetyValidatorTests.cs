using SqlToAi.Database;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Shared query and batch safety checks, with target syntax verified at the service entry.
/// </summary>
public sealed class QuerySafetyValidatorTests
{
    private static DatabaseTargetContext Target => TestTargets.ResolveTarget(new SqlToAiOptions().WithNamedServers("primary"), "primary/TestDb");
    private static DatabaseTargetContext BlockedTarget => TestTargets.ResolveTarget(new SqlToAiOptions().WithNamedServers("primary"), "primary/BlockedDb");

    private static QuerySafetyValidator BuildValidator(
        bool isAllowed = true,
        AccessLevel accessLevel = AccessLevel.ReadOnly) =>
        new QuerySafetyValidator(
            new FakeSecurityGuard(isAllowed),
            new FakeAccessLevelProvider(accessLevel),
            new ReadOnlyGuard());

    // -------------------------------------------------------------------------
    // empty / whitespace query → InvalidParameters
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateTargetQuerySafetyAsync_EmptyQuery_ReturnsInvalidParameters(string query)
    {
        var v = BuildValidator();
        var result = await v.ValidateTargetQuerySafetyAsync(Target, query, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // database not in whitelist → SafetyCheckFailed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateTargetQuerySafetyAsync_DatabaseNotAllowed_ReturnsSafetyCheckFailed()
    {
        var v = BuildValidator(isAllowed: false);
        var result = await v.ValidateTargetQuerySafetyAsync(BlockedTarget, "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // AccessLevel.None → WriteOperationBlocked
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateTargetQuerySafetyAsync_AccessLevelNone_ReturnsWriteOperationBlocked()
    {
        var v = BuildValidator(accessLevel: AccessLevel.None);
        var result = await v.ValidateTargetQuerySafetyAsync(Target, "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // AccessLevel.SchemaOnly without the allowSchemaOnly flag → WriteOperationBlocked
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateTargetQuerySafetyAsync_AccessLevelSchemaOnly_WithoutFlag_ReturnsWriteOperationBlocked()
    {
        var v = BuildValidator(accessLevel: AccessLevel.SchemaOnly);
        var result = await v.ValidateTargetQuerySafetyAsync(Target, "SELECT 1", allowSchemaOnly: false, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Stage 4b positive: AccessLevel.SchemaOnly with allowSchemaOnly:true → Success
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateTargetQuerySafetyAsync_AccessLevelSchemaOnly_WithFlag_ReturnsSuccess()
    {
        var v = BuildValidator(accessLevel: AccessLevel.SchemaOnly);
        var result = await v.ValidateTargetQuerySafetyAsync(Target, "SELECT 1", allowSchemaOnly: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccessLevel.SchemaOnly, result.Value.AccessLevel);
        Assert.False(result.Value.IsWriteAllowed);
    }

    // -------------------------------------------------------------------------
    // mutating query without ReadWrite → WriteOperationBlocked.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("DELETE FROM Customers")]
    [InlineData("DELETE FROM Foo")]
    [InlineData("DROP TABLE Users")]
    public async Task ValidateTargetQuerySafetyAsync_MutatingQuery_WithoutReadWrite_ReturnsWriteOperationBlocked(string query)
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadOnly);
        var result = await v.ValidateTargetQuerySafetyAsync(Target, query, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // sp_executesql is rejected by the read-only-guard regex
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("sp_executesql N'DELETE FROM Foo'")]
    [InlineData("EXEC sp_executesql N'DELETE FROM dbo.Foo; COMMIT'")]
    [InlineData("sys.sp_executesql N'DELETE FROM Foo'")]
    public async Task ValidateTargetQuerySafetyAsync_SpExecuteSql_WithoutReadWrite_ReturnsWriteOperationBlocked(string query)
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadOnly);
        var result = await v.ValidateTargetQuerySafetyAsync(Target, query, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Stage 5 positive: mutating query with ReadWrite → Success (write allowed, no rejection).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateTargetQuerySafetyAsync_MutatingQuery_WithReadWrite_ReturnsSuccess()
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadWrite);
        var result = await v.ValidateTargetQuerySafetyAsync(Target, "DELETE FROM Customers", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccessLevel.ReadWrite, result.Value.AccessLevel);
        Assert.True(result.Value.IsWriteAllowed);
    }

    // -------------------------------------------------------------------------
    // multi-statement detection — always enforced.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1 ; DROP TABLE Foo")]
    [InlineData("SELECT 'hello'; SELECT 'world'")]
    public async Task ValidateTargetQuerySafetyAsync_MultiStatement_ReturnsMultipleStatementsForbidden(string query)
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadWrite);
        var result = await v.ValidateTargetQuerySafetyAsync(Target, query, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Stage 6 parameterised by access level: multi-statement is rejected even at ReadWrite.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.ReadWrite)]
    public async Task ValidateTargetQuerySafetyAsync_MultiStatement_RegardlessOfAccessLevel_ReturnsMultipleStatementsForbidden(AccessLevel level)
    {
        var v = BuildValidator(accessLevel: level);
        var result = await v.ValidateTargetQuerySafetyAsync(Target, "SELECT 1; SELECT 2", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Happy path: all 6 stages pass
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateTargetQuerySafetyAsync_AllStagesPass_ReturnsResolvedAccessLevelAndWriteFlag()
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadOnlyAnonymized);
        var result = await v.ValidateTargetQuerySafetyAsync(Target, "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccessLevel.ReadOnlyAnonymized, result.Value.AccessLevel);
        Assert.False(result.Value.IsWriteAllowed);
    }

    // -------------------------------------------------------------------------
    // ReadWrite + simple SELECT: all stages pass with write allowed.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateTargetQuerySafetyAsync_ReadWrite_SelectQuery_ReturnsSuccess()
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadWrite);
        var result = await v.ValidateTargetQuerySafetyAsync(Target, "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccessLevel.ReadWrite, result.Value.AccessLevel);
        Assert.True(result.Value.IsWriteAllowed);
    }

    // -------------------------------------------------------------------------
    // Short-circuit verification: an empty database name must fail before the whitelist is checked
    // -------------------------------------------------------------------------

    // -------------------------------------------------------------------------
    // SchemaOnly blocks mutating queries even when allowSchemaOnly:true
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateTargetQuerySafetyAsync_AccessLevelSchemaOnly_WithFlag_MutatingQuery_ReturnsWriteOperationBlocked()
    {
        var v = BuildValidator(accessLevel: AccessLevel.SchemaOnly);
        var result = await v.ValidateTargetQuerySafetyAsync(Target, "DELETE FROM Customers", allowSchemaOnly: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Fact]
    public async Task ValidateTargetBatchSafetyAsync_ReadWrite_AllowsMultipleStatements()
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadWrite);
        var result = await v.ValidateTargetBatchSafetyAsync(Target, "UPDATE Customers SET Name = 'X'; UPDATE Orders SET Flag = 1", TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccessLevel.ReadWrite, result.Value.AccessLevel);
        Assert.True(result.Value.IsWriteAllowed);
    }

    [Fact]
    public async Task ValidateTargetBatchSafetyAsync_ReadOnly_AllowsMultipleReadStatements()
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadOnly);
        var result = await v.ValidateTargetBatchSafetyAsync(Target, "SELECT 1; SELECT 2", TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccessLevel.ReadOnly, result.Value.AccessLevel);
        Assert.False(result.Value.IsWriteAllowed);
    }

    [Fact]
    public async Task ValidateTargetBatchSafetyAsync_ReadOnly_RejectsMutatingStatement()
    {
        var v = BuildValidator(accessLevel: AccessLevel.ReadOnly);
        var result = await v.ValidateTargetBatchSafetyAsync(Target, "SELECT 1; DELETE FROM Customers", TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.ReadWrite)]
    public async Task ValidateTargetQuerySafetyAsync_UseStatement_ReturnsSafetyCheckFailed(AccessLevel accessLevel)
    {
        var v = BuildValidator(accessLevel: accessLevel);
        var result = await v.ValidateTargetQuerySafetyAsync(Target, "USE master; SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.ReadWrite)]
    public async Task ValidateTargetBatchSafetyAsync_UseStatement_ReturnsSafetyCheckFailed(AccessLevel accessLevel)
    {
        var v = BuildValidator(accessLevel: accessLevel);
        var result = await v.ValidateTargetBatchSafetyAsync(Target, "USE master", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("UnqualifiedDb")]
    [InlineData("/TestDb")]
    [InlineData("bad alias/TestDb")]
    [InlineData("unknown/TestDb")]
    public async Task ServiceEntry_InvalidTargetShortCircuitsBeforeDeniedPolicyAndConnections(string? database)
    {
        var options = Options.Create(new SqlToAiOptions().WithNamedServers("primary"));
        var factory = new TargetRowsConnectionFactory([]);
        var service = new QueryValidationService(factory, BuildValidator(isAllowed: false), options,
            NullLogger<QueryValidationService>.Instance);
        var result = await service.ValidateQueryAsync(database!, "SELECT 1", TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Empty(factory.CreatedTargets);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulSafetyResult_RetainsExactCallerContext(bool batch)
    {
        DatabaseTargetContext target = Target;
        var validator = BuildValidator();
        var result = batch
            ? await validator.ValidateTargetBatchSafetyAsync(target, "SELECT 1; SELECT 2", TestContext.Current.CancellationToken)
            : await validator.ValidateTargetQuerySafetyAsync(target, "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Same(target, result.Value.Target);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAtAccessCheck_PreservesCallerToken(bool batch)
    {
        var configuration = new SqlToAiOptions().WithNamedServers("primary");
        configuration.Databases.ReadOnly.Add("primary/TestDb");
        var options = Options.Create(configuration);
        var validator = new QuerySafetyValidator(new SecurityGuard(options), new AccessLevelProvider(options), new ReadOnlyGuard());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => batch
            ? validator.ValidateTargetBatchSafetyAsync(Target, "SELECT 1", cancellation.Token)
            : validator.ValidateTargetQuerySafetyAsync(Target, "SELECT 1", cancellationToken: cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }
}
