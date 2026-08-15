#nullable enable

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Unit tests for <see cref="QueryExecutionService"/>, focused on the service's own behaviour:
/// transaction commit/rollback, ReadWrite override, multi-statement enforcement at write-allowed
/// levels, and single-statement positive coverage. The pure pipeline outcomes (empty parameters,
/// blocked database, AccessLevel.None/SchemaOnly, sp_executesql/mutating-keyword detection,
/// multi-statement detection) are covered end-to-end in the dedicated
/// <c>QuerySafetyValidatorTests</c> class (step-003 / DRY-T3). Anonymization/tokenization tests
/// live in the second partial-class file <c>QueryExecutionServiceAnonymizationTests.cs</c> —
/// split purely to stay within the project's per-file line-count budget.
/// </summary>
public sealed class QueryExecutionServiceTests
{
    // -------------------------------------------------------------------------
    // Helpers: build service with configurable fakes
    // -------------------------------------------------------------------------

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
    // Tests: multi-statement positive coverage — false-positive guard for the detector.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("SELECT 1;")]           // trailing semicolon only — allowed
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
