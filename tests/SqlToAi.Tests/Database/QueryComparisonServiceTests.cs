#nullable enable

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Service-level tests for <see cref="QueryComparisonService.CompareQueriesAsync"/>.
/// Pins the 2-query identity of the service: pre-pipeline argument validation
/// (<see cref="QueryComparisonService.ValidateArgs"/>), short-circuit-on-first-failure
/// across the two <see cref="IQuerySafetyValidator"/> invocations, and 2-query-specific
/// branching (mutating/multi-statement in either QueryA or QueryB). The pure 6-stage
/// guardrail pipeline itself is covered end-to-end in <c>QuerySafetyValidatorTests</c>
/// (step-003 / DRY-T3); the service tests below target behaviour that the pipeline
/// tests cannot see (calling the validator twice with the right arguments, propagating
/// only the first error).
/// </summary>
public sealed class QueryComparisonServiceTests
{
    private static QueryComparisonService BuildService(
        bool isAllowed = true,
        AccessLevel accessLevel = AccessLevel.ReadOnly,
        SqlToAiError? error = null)
    {
        var options = new SqlToAiOptions();

        return new QueryComparisonService(
            new ValidationMockConnectionFactory(),
            FakeQuerySafetyValidator.Create(isAllowed, accessLevel, error),
            Options.Create(options),
            NullLogger<QueryComparisonService>.Instance);
    }

    // ---- Pre-pipeline: empty arguments are rejected before the validator runs ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CompareQueriesAsync_EmptyDatabase_ReturnsInvalidParameters(string db)
    {
        var service = BuildService();
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(db, "SELECT 1", "SELECT 1"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_EmptyQueryA_ReturnsInvalidParameters()
    {
        var service = BuildService();
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "", "SELECT 1"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_EmptyQueryB_ReturnsInvalidParameters()
    {
        var service = BuildService();
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1", ""),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    // ---- Pipeline stages 3-4: whitelist + access level (both queries get the same probe) ----

    [Fact]
    public async Task CompareQueriesAsync_DatabaseNotAllowed_ReturnsSafetyCheckFailed()
    {
        var service = BuildService(isAllowed: false);
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1", "SELECT 2"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_AccessLevelNone_ReturnsWriteOperationBlocked()
    {
        var service = BuildService(accessLevel: AccessLevel.None);
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1", "SELECT 2"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    // ---- 2-Query-specific: Mutating / Multi-Statement in QueryA vs. QueryB ----

    [Fact]
    public async Task CompareQueriesAsync_MutatingQueryInQueryA_ReturnsError()
    {
        // ReadOnly pipeline; QueryA is mutating, QueryB is clean. Service must fail on
        // QueryA and never call the validator for QueryB (short-circuit proof).
        var service = BuildService(accessLevel: AccessLevel.ReadOnly);
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "DROP TABLE Users", "SELECT 1"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_MutatingQueryInQueryB_ReturnsError()
    {
        // ReadOnly pipeline; QueryA is clean, QueryB is mutating. Service must validate
        // QueryA (passes), then validate QueryB (fails). This proves the validator is
        // called for both queries in the expected order.
        var service = BuildService(accessLevel: AccessLevel.ReadOnly);
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1", "DROP TABLE Users"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_MultipleStatementsInQueryA_ReturnsError()
    {
        // ReadWrite pipeline (multi-statement is enforced at every access level).
        var service = BuildService(accessLevel: AccessLevel.ReadWrite);
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1; SELECT 2", "SELECT 3"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    [Fact]
    public async Task CompareQueriesAsync_MultipleStatementsInQueryB_ReturnsError()
    {
        var service = BuildService(accessLevel: AccessLevel.ReadWrite);
        var result = await service.CompareQueriesAsync(
            new QueryComparisonArgs(TestConstants.DatabaseName, "SELECT 1", "SELECT 2; SELECT 3"),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }
}
