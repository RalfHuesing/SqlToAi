#nullable enable

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Unit tests for <see cref="QueryExecutionService"/>.
/// Uses a test double for the connection factory and a <see cref="FakeQuerySafetyValidator"/>
/// that pins the guardrail-pipeline outcome directly â€” the three legacy
/// <c>FakeSecurityGuard</c> / <c>FakeAccessLevelProvider</c> / <c>FakeReadOnlyGuard</c> fakes
/// were removed from <see cref="BuildService"/> in step-002 (item-04) once
/// <see cref="QueryExecutionService"/> started consuming the new
/// <see cref="IQuerySafetyValidator"/> pipeline. Anonymization/tokenization tests live in the
/// second partial-class file <c>QueryExecutionServiceAnonymizationTests.cs</c> â€” split purely to
/// stay within the project's per-file line-count budget.
/// </summary>
public sealed class QueryExecutionServiceTests
{
    // -------------------------------------------------------------------------
    // Helpers: build service with configurable fakes
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps the historical three-fake combination (security guard / access level / read-only
    /// guard) onto a single <see cref="FakeQuerySafetyValidator"/>. Mirrors the legacy
    /// <see cref="FakeReadOnlyGuard"/> semantics — the read-only guard is faked, not real, so
    /// the <c>MultipleStatements</c> Theory can still observe the multi-statement rejection
    /// independently of the read-only guard.
    /// </summary>
    private static FakeQuerySafetyValidator BuildSafetyValidator(
        AccessLevel accessLevel,
        bool isAllowed,
        bool readOnlySafe,
        SqlToAiError? error = null)
    {
        if (error != null)
        {
            return new FakeQuerySafetyValidator(error);
        }
        return new FakeQuerySafetyValidator(
            new FakeSecurityGuard(isAllowed),
            new FakeAccessLevelProvider(accessLevel),
            new FakeReadOnlyGuard(readOnlySafe));
    }

    private static QueryExecutionService BuildService(
        AccessLevel accessLevel = AccessLevel.ReadOnly,
        bool isAllowed = true,
        bool readOnlySafe = true,
        string? mockData = null,
        SqlToAiOptions? options = null,
        SqlToAiError? error = null)
    {
        options ??= new SqlToAiOptions();
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(mockData ?? "Col1\tVal1"));
        var safetyValidator = BuildSafetyValidator(accessLevel, isAllowed, readOnlySafe, error);
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());
        return new QueryExecutionService(
            factory, safetyValidator,
            new AnonymizationDependencies(anonymizer), Options.Create(options), NullLogger<QueryExecutionService>.Instance);
    }

    // -------------------------------------------------------------------------
    // Tests: input validation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_ShouldFail_WhenDatabaseNameIsEmpty()
    {
        var service = BuildService();
        var result = await service.ExecuteQueryAsync("", "SELECT 1", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldFail_WhenQueryIsEmpty()
    {
        var service = BuildService();
        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "   ", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Tests: security checks
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_ShouldFail_WhenDatabaseNotAllowed()
    {
        var service = BuildService(isAllowed: false);
        var result = await service.ExecuteQueryAsync("BlockedDb", "SELECT 1", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    [Theory]
    [InlineData(AccessLevel.None)]
    [InlineData(AccessLevel.SchemaOnly)]
    public async Task ExecuteQueryAsync_ShouldFail_WhenAccessLevelTooLow(AccessLevel level)
    {
        var service = BuildService(accessLevel: level);
        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldFail_WhenQueryIsMutating()
    {
        var service = BuildService(readOnlySafe: false);
        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "DELETE FROM Customers", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Tests: multi-statement detection
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1 ; DROP TABLE Foo")]
    [InlineData("SELECT 'hello'; SELECT 'world'")]
    public async Task ExecuteQueryAsync_ShouldFail_WhenMultipleStatements(string query)
    {
        var service = BuildService();
        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, query, null, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("SELECT 1;")]           // trailing semicolon only â€” allowed
    [InlineData("SELECT 'hello;world'")] // semicolon inside string literal
    [InlineData("SELECT 1 -- note; comment")]
    public async Task ExecuteQueryAsync_ShouldSucceed_WhenSingleStatement(string query)
    {
        var service = BuildService();
        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, query, null, TestContext.Current.CancellationToken);
        // We only verify the multi-statement check passes; actual query execution may return stub data
        Assert.True(result.IsSuccess || result.Error.Code == SqlToAiError.QueryErrorCode);
    }

    // -------------------------------------------------------------------------
    // Tests: ReadWrite access level unlocks mutating statements (and commits them)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_ShouldAllowMutatingQuery_AndCommit_WhenReadWrite()
    {
        var options = new SqlToAiOptions();
        var factory = new MockQueryConnectionFactory();
        var service = new QueryExecutionService(
            factory,
            new FakeQuerySafetyValidator(
                new FakeSecurityGuard(true),
                new FakeAccessLevelProvider(AccessLevel.ReadWrite),
                new FakeReadOnlyGuard(safe: false)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "UPDATE Customers SET Name = 'X'", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.CommitCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.RollbackCount);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldStillRollBack_WhenAccessLevelIsNotReadWrite()
    {
        var options = new SqlToAiOptions();
        var factory = new MockQueryConnectionFactory();
        var service = new QueryExecutionService(
            factory,
            new FakeQuerySafetyValidator(
                new FakeSecurityGuard(true),
                new FakeAccessLevelProvider(AccessLevel.ReadOnly),
                new FakeReadOnlyGuard(safe: true)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldStillForbidMultipleStatements_WhenWriteAllowed()
    {
        var options = new SqlToAiOptions();
        var service = new QueryExecutionService(
            new MockQueryConnectionFactory(),
            new FakeQuerySafetyValidator(
                new FakeSecurityGuard(true),
                new FakeAccessLevelProvider(AccessLevel.ReadWrite),
                new FakeReadOnlyGuard(safe: true)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "UPDATE Foo SET X=1; UPDATE Bar SET Y=2", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

}

