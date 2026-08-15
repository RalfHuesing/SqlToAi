#nullable enable

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Transaction integrity and bypass rejection tests for <see cref="QueryExecutionService"/>.
/// </summary>
public sealed class QueryExecutionServiceTransactionTests
{
    // -------------------------------------------------------------------------
    // Tests: sp_executesql bypass (audit finding 2, layer 1) — the REAL ReadOnlyGuard is used
    // here (not FakeReadOnlyGuard) to prove the production regex itself closes the documented
    // bypass end-to-end through the service, and that rejection happens before the database is
    // ever touched.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("sp_executesql N'DELETE FROM Customers'")]
    [InlineData("EXEC sp_executesql N'DELETE FROM dbo.Customers; COMMIT'")]
    [InlineData("sys.sp_executesql N'DELETE FROM Customers'")]
    public async Task ExecuteQueryAsync_ShouldReject_SpExecuteSql_BeforeTouchingDatabase(string query)
    {
        var options = new SqlToAiOptions();
        var factory = new MockQueryConnectionFactory();
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly),
            new ReadOnlyGuard(), // the real guard, not a fake — proves the regex itself blocks this
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, query, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
        // The connection factory was never invoked — the guard fired before any DB connection.
        Assert.Null(factory.LastConnection);
    }

    // -------------------------------------------------------------------------
    // Tests: transaction-integrity guard (audit finding 2, layer 2) — defense in depth,
    // independent of sp_executesql or any other specific keyword. Simulates a hypothetical
    // future bypass by making the mock's SELECT @@TRANCOUNT probe report a changed value after
    // "execution", without any mutating keyword ever being involved.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_ShouldRejectResult_WhenTransactionStateChangedDuringExecution()
    {
        var options = new SqlToAiOptions();
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(TranCountSequence: new MockTranCountSequence(1, 0)));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly),
            new FakeReadOnlyGuard(safe: true), // the guard sees nothing wrong — this is layer 2's job
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount); // defensive rollback attempted
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount); // never committed by us
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldSwallowRollbackFailure_AfterTransactionIntegrityViolation()
    {
        // The defensive rollback after a detected violation can itself fail (the underlying
        // transaction is often already gone by then) — this must not crash or replace the
        // violation error with a confusing "no transaction" exception.
        var options = new SqlToAiOptions();
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(
            TranCountSequence: new MockTranCountSequence(1, 0), ThrowOnRollback: true));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly),
            new FakeReadOnlyGuard(safe: true),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount); // rollback was attempted...
        // ...and its InvalidOperationException was swallowed rather than propagating or masking
        // the violation error above (the assertions above already prove nothing crashed and the
        // correct error code still came back).
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldSucceed_WhenTransactionStateUnchanged_NoRegression()
    {
        // Regression guard for layer 2 in isolation: when the trancount probe reports the same
        // value before and after execution (the normal case), behavior must be identical to
        // before this change — success, with a plain rollback for a non-write-allowed database.
        var options = new SqlToAiOptions();
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(TranCountSequence: new MockTranCountSequence(1, 1)));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly),
            new FakeReadOnlyGuard(safe: true),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount);
    }
}
