#nullable enable

using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Unit tests for <see cref="QueryValidationService"/>, covering the guard clauses
/// (empty parameters, blocked database, <see cref="AccessLevel.None"/>) that are exercised
/// nowhere else — the integration tests in
/// <c>QueryValidationServiceIntegrationTests.cs</c> only cover the happy/syntax-error paths
/// against a real SQL Server and never construct the service with a failing guard. Also proves
/// this service always rolls back (it never commits, unlike <see cref="QueryExecutionService"/>)
/// and that the <c>finally</c>-block rollback still runs when validation throws.
/// Reuses <see cref="FakeSecurityGuard"/> and <see cref="FakeAccessLevelProvider"/> from
/// <c>QueryExecutionServiceMockDb.cs</c> (same namespace); the connection/transaction/command
/// fakes below are local to this file since <c>QueryValidationService</c> only ever calls
/// <c>ExecuteNonQueryAsync</c> (never a data reader), which the existing mock DB doesn't model.
/// </summary>
public sealed class QueryValidationServiceTests
{
    private static QueryValidationService BuildService(
        IDatabaseConnectionFactory? factory = null,
        bool isAllowed = true,
        AccessLevel accessLevel = AccessLevel.ReadOnly,
        SqlToAiOptions? options = null,
        IReadOnlyGuard? readOnlyGuard = null)
    {
        options ??= new SqlToAiOptions();
        factory ??= new ValidationMockConnectionFactory();
        return new QueryValidationService(
            factory,
            new FakeSecurityGuard(isAllowed),
            new FakeAccessLevelProvider(accessLevel),
            readOnlyGuard ?? new ReadOnlyGuard(),
            Options.Create(options),
            NullLogger<QueryValidationService>.Instance);
    }

    // -------------------------------------------------------------------------
    // Tests: input validation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQueryAsync_ShouldFail_WhenDatabaseNameIsEmpty()
    {
        var service = BuildService();
        var result = await service.ValidateQueryAsync("   ", "SELECT 1", TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task ValidateQueryAsync_ShouldFail_WhenQueryIsEmpty()
    {
        var service = BuildService();
        var result = await service.ValidateQueryAsync(TestConstants.DatabaseName, "   ", TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Tests: security checks
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQueryAsync_ShouldFail_WhenDatabaseNotAllowed()
    {
        var service = BuildService(isAllowed: false);
        var result = await service.ValidateQueryAsync("BlockedDb", "SELECT 1", TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    [Fact]
    public async Task ValidateQueryAsync_ShouldFail_WhenAccessLevelIsNone()
    {
        var service = BuildService(accessLevel: AccessLevel.None);
        var result = await service.ValidateQueryAsync(TestConstants.DatabaseName, "SELECT 1", TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Tests: read-only guard (audit finding 4) — mirrors QueryExecutionService's layer 4.
    // sql_validate_query previously had no guard at all and relied solely on the unverified
    // assumption that SET PARSEONLY ON prevents any statement from actually executing.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQueryAsync_ShouldFail_WhenQueryIsMutating_AndAccessLevelIsNotReadWrite()
    {
        var factory = new ValidationMockConnectionFactory();
        var service = BuildService(factory: factory, accessLevel: AccessLevel.ReadOnly);

        var result = await service.ValidateQueryAsync(TestConstants.DatabaseName, "DELETE FROM Foo", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
        // The guard must reject before ever touching the database — no connection was created.
        Assert.Null(factory.LastConnection);
    }

    [Fact]
    public async Task ValidateQueryAsync_ShouldNotBlock_MutatingQuery_WhenAccessLevelIsReadWrite()
    {
        // ReadWrite bypasses the guard here exactly as it does in QueryExecutionService — this
        // service still never commits, so the query only ever runs under SET PARSEONLY inside a
        // transaction that always gets rolled back.
        var factory = new ValidationMockConnectionFactory();
        var service = BuildService(factory: factory, accessLevel: AccessLevel.ReadWrite);

        var result = await service.ValidateQueryAsync(TestConstants.DatabaseName, "DELETE FROM Foo", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        Assert.NotNull(factory.LastConnection);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount);
    }

    [Theory]
    [InlineData("sp_executesql N'DELETE FROM Foo'")]
    [InlineData("EXEC sp_executesql N'DELETE FROM dbo.Foo; COMMIT'")]
    [InlineData("sys.sp_executesql N'DELETE FROM Foo'")]
    public async Task ValidateQueryAsync_ShouldReject_SpExecuteSql_BeforeTouchingDatabase(string query)
    {
        // The real ReadOnlyGuard (not a fake) proves the production regex itself closes the
        // sp_executesql bypass (audit finding 2) for this tool too, not just QueryExecutionService.
        var factory = new ValidationMockConnectionFactory();
        var service = BuildService(factory: factory, accessLevel: AccessLevel.ReadOnly, readOnlyGuard: new ReadOnlyGuard());

        var result = await service.ValidateQueryAsync(TestConstants.DatabaseName, query, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
        Assert.Null(factory.LastConnection);
    }

    // -------------------------------------------------------------------------
    // Tests: multi-statement detection (audit finding 4) — always enforced, write-allowed or not.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.ReadWrite)]
    public async Task ValidateQueryAsync_ShouldFail_WhenMultipleStatements_RegardlessOfAccessLevel(AccessLevel accessLevel)
    {
        var factory = new ValidationMockConnectionFactory();
        var service = BuildService(factory: factory, accessLevel: accessLevel);

        var result = await service.ValidateQueryAsync(TestConstants.DatabaseName, "SELECT 1; SELECT 2", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
        Assert.Null(factory.LastConnection);
    }

    // -------------------------------------------------------------------------
    // Tests: transaction handling — this service must NEVER commit
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQueryAsync_ShouldRollBack_NeverCommit_WhenValidationSucceeds()
    {
        var factory = new ValidationMockConnectionFactory();
        var service = BuildService(factory: factory);

        var result = await service.ValidateQueryAsync(TestConstants.DatabaseName, "SELECT 1", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount);
    }

    [Fact]
    public async Task ValidateQueryAsync_ShouldFail_AndStillRollBack_WhenExecutionThrows()
    {
        var executionException = new InvalidOperationException("Syntax error near FROM.");
        var factory = new ValidationMockConnectionFactory(throwOnExecute: executionException);
        var service = BuildService(factory: factory);

        var result = await service.ValidateQueryAsync(TestConstants.DatabaseName, "SELECT FROM", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount);
    }
}

// -------------------------------------------------------------------------
// Minimal DbConnection/DbTransaction/DbCommand fakes for QueryValidationService, built on the
// shared TestSupport plumbing. Unlike QueryExecutionService, this service never reads rows — it
// only issues ExecuteNonQueryAsync calls (SET PARSEONLY ON / the query itself / SET PARSEONLY OFF)
// inside a transaction it always rolls back — so no data-reader dispatch is needed here.
// -------------------------------------------------------------------------

internal sealed class ValidationMockConnectionFactory(Exception? throwOnExecute = null) : IDatabaseConnectionFactory
{
    /// <summary>The most recently created connection — lets tests inspect its transaction.</summary>
    public FakeDbConnection? LastConnection { get; private set; }

    public DbConnection CreateConnection(string? databaseName)
    {
        LastConnection = BuildConnection(throwOnExecute);
        return LastConnection;
    }

    public DbConnection CreateConnection() => CreateConnection(null);

    private static FakeDbConnection BuildConnection(Exception? executeException) =>
        new(
            conn => new FakeDbCommand(conn, new FakeDbCommandHandlers(ExecuteNonQuery: _ => ExecuteNonQuery(executeException))),
            new FakeDbConnectionOptions(
                Database: TestConstants.DatabaseName,
                DataSource: "mock",
                ServerVersion: "16.0",
                BeginTransaction: (transactionConnection, _) => new FakeDbTransaction(transactionConnection)));

    private static int ExecuteNonQuery(Exception? executeException)
    {
        if (executeException != null)
        {
            throw executeException;
        }
        return 0;
    }
}
