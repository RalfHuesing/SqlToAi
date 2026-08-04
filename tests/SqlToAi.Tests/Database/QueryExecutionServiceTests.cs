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
/// Uses a test double for the connection factory and real implementations of the guards.
/// Anonymization/tokenization tests live in the second partial-class file
/// <c>QueryExecutionServiceAnonymizationTests.cs</c> — split purely to stay within the project's
/// per-file line-count budget.
/// </summary>
public sealed partial class QueryExecutionServiceTests
{
    // -------------------------------------------------------------------------
    // Helpers: build service with configurable fakes
    // -------------------------------------------------------------------------

    private static QueryExecutionService BuildService(
        AccessLevel accessLevel = AccessLevel.ReadOnly,
        bool isAllowed = true,
        bool readOnlySafe = true,
        string? mockData = null,
        SqlToAiOptions? options = null)
    {
        options ??= new SqlToAiOptions();
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(mockData ?? "Col1\tVal1"));
        var securityGuard = new FakeSecurityGuard(isAllowed);
        var accessLevelProvider = new FakeAccessLevelProvider(accessLevel);
        var readOnlyGuard = new FakeReadOnlyGuard(readOnlySafe);
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());
        return new QueryExecutionService(
            factory, securityGuard, accessLevelProvider, readOnlyGuard,
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
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadWrite),
            new FakeReadOnlyGuard(safe: false), // guard would reject it — must be bypassed
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
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly), // not ReadWrite
            new FakeReadOnlyGuard(safe: true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
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
            new MockQueryConnectionFactory(), new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadWrite),
            new FakeReadOnlyGuard(safe: true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "UPDATE Foo SET X=1; UPDATE Bar SET Y=2", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Tests: row limit enforcement
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_ShouldRespectDefaultRowLimit()
    {
        var options = new SqlToAiOptions { QueryExecution = new QueryExecutionOptions { DefaultRowLimit = 2, MaxRowLimit = 100 } };
        // MockQueryConnectionFactory returns 5 rows; default limit is 2
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(RowCount: 5));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly),
            new FakeReadOnlyGuard(true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        int lineCount = result.Value.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.Equal(2, lineCount);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldCapAtMaxRowLimit()
    {
        var options = new SqlToAiOptions { QueryExecution = new QueryExecutionOptions { DefaultRowLimit = 100, MaxRowLimit = 3 } };
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(RowCount: 10));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly),
            new FakeReadOnlyGuard(true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", 999, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        int lineCount = result.Value.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.Equal(3, lineCount);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldReturnTimeout_WhenTimeoutExceptionOccurs()
    {
        var options = new SqlToAiOptions();
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(ThrowOnExecute: new TimeoutException("Execution timed out")));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly),
            new FakeReadOnlyGuard(true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.TimeoutCode, result.Error.Code);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldReturnInfrastructureError_WhenSocketExceptionOccurs()
    {
        var options = new SqlToAiOptions();
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(ThrowOnExecute: new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused)));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly),
            new FakeReadOnlyGuard(true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InfrastructureErrorCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Tests: STATISTICS IO/TIME (step-002)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_ShouldIssueSetStatisticsCommands_BeforeMainQuery()
    {
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig("Col1\tVal1"));
        var options = new SqlToAiOptions();
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly),
            new FakeReadOnlyGuard(true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains(factory.ExecutedNonQueryCommands, c => string.Equals(c, "SET STATISTICS IO ON", StringComparison.Ordinal));
        Assert.Contains(factory.ExecutedNonQueryCommands, c => string.Equals(c, "SET STATISTICS TIME ON", StringComparison.Ordinal));
        // The fake connection is not a SqlConnection, so the InfoMessage guard never fires —
        // both metrics stay at their 0 default (see step-002 JIT context on testability).
        Assert.Equal(0, result.Value.CpuTimeMs);
        Assert.Equal(0, result.Value.LogicalReads);
    }

    // -------------------------------------------------------------------------
    // Tests: configurable command timeout (audit-hardening step-001) — verifies the
    // hardcoded `CommandTimeout = 0` (unbounded) is gone and the configured
    // QueryExecutionOptions.CommandTimeoutSeconds reaches the actual DbCommand.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_ShouldApplyConfiguredCommandTimeout_ToCommand()
    {
        var options = new SqlToAiOptions { QueryExecution = new QueryExecutionOptions { CommandTimeoutSeconds = 45 } };
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig("Col1\tVal1"));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly),
            new FakeReadOnlyGuard(true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(45, factory.LastConnection?.LastCommand?.CommandTimeout);
    }

    // -------------------------------------------------------------------------
    // Tests: server-side SET ROWCOUNT enforcement (audit-hardening step-002)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_ShouldIssueSetRowCount_WithRequestedRowLimit_BeforeMainQuery()
    {
        var options = new SqlToAiOptions { QueryExecution = new QueryExecutionOptions { DefaultRowLimit = 100, MaxRowLimit = 100 } };
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig("Col1\tVal1"));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly),
            new FakeReadOnlyGuard(true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", 7, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains(factory.ExecutedNonQueryCommands, c => string.Equals(c, "SET ROWCOUNT 7", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldIssueSetRowCount_WithDefaultRowLimit_WhenNoneRequested()
    {
        var options = new SqlToAiOptions { QueryExecution = new QueryExecutionOptions { DefaultRowLimit = 42, MaxRowLimit = 100 } };
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig("Col1\tVal1"));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly),
            new FakeReadOnlyGuard(true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains(factory.ExecutedNonQueryCommands, c => string.Equals(c, "SET ROWCOUNT 42", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldResetRowCountToZero_AfterMainQuery_InCorrectOrder()
    {
        var options = new SqlToAiOptions { QueryExecution = new QueryExecutionOptions { DefaultRowLimit = 5, MaxRowLimit = 100 } };
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig("Col1\tVal1"));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly),
            new FakeReadOnlyGuard(true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        int setRowCountIndex = factory.ExecutedNonQueryCommands.IndexOf("SET ROWCOUNT 5");
        int resetIndex = factory.ExecutedNonQueryCommands.IndexOf("SET ROWCOUNT 0");
        Assert.True(setRowCountIndex >= 0, "SET ROWCOUNT {limit} was not issued.");
        Assert.True(resetIndex > setRowCountIndex, "SET ROWCOUNT 0 reset must come after SET ROWCOUNT {limit}.");
    }

    // Anonymization and tokenization tests continue in the second partial-class file:
    // see QueryExecutionServiceAnonymizationTests.cs.
}
