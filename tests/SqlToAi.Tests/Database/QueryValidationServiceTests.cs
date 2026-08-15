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
/// Unit tests for <see cref="QueryValidationService"/>, covering the service-level behaviour
/// (rollback-immer, command-timeout source, <c>sp_executesql</c> -> <c>WriteOperationBlocked</c>
/// before touching the database, timeout/socket-exception mapping, TD-001 timeout-source pinning).
/// The pure pipeline outcomes (empty parameters, blocked database, <see cref="AccessLevel.None"/>,
/// <c>sp_executesql</c> detection, mutating-keyword detection, multi-statement detection) are
/// covered end-to-end in the dedicated <c>QuerySafetyValidatorTests</c> class
/// (step-003 / DRY-T3). Here the pipeline is pinned via <see cref="FakeQuerySafetyValidator"/>
/// to keep each test focused on the service's own behaviour.
/// </summary>
public sealed class QueryValidationServiceTests
{
    private static QueryValidationService BuildService(
        IDatabaseConnectionFactory? factory = null,
        bool isAllowed = true,
        AccessLevel accessLevel = AccessLevel.ReadOnly,
        SqlToAiOptions? options = null,
        SqlToAiError? error = null)
    {
        options ??= new SqlToAiOptions();
        factory ??= new ValidationMockConnectionFactory();

        IQuerySafetyValidator safetyValidator = error != null
            ? new FakeQuerySafetyValidator(error)
            : new FakeQuerySafetyValidator(
                new FakeSecurityGuard(isAllowed),
                new FakeAccessLevelProvider(accessLevel),
                new ReadOnlyGuard());

        return new QueryValidationService(
            factory,
            safetyValidator,
            Options.Create(options),
            NullLogger<QueryValidationService>.Instance);
    }

    // -------------------------------------------------------------------------
    // Tests: read-only guard (audit finding 4) — mirrors QueryExecutionService's layer 4.
    // sql_validate_query previously had no guard at all and relied solely on the unverified
    // assumption that SET PARSEONLY ON prevents any statement from actually executing. After
    // step-002 the production regex lives in the IQuerySafetyValidator pipeline, so these
    // service-level tests use FakeQuerySafetyValidator(error) to pin the WriteOperationBlocked
    // outcome; the regex itself is exercised end-to-end by QuerySafetyValidatorTests.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQueryAsync_ShouldFail_WhenQueryIsMutating_AndAccessLevelIsNotReadWrite()
    {
        var factory = new ValidationMockConnectionFactory();
        var service = BuildService(
            factory: factory,
            accessLevel: AccessLevel.ReadOnly,
            error: SqlToAiError.WriteOperationBlocked("The query contains mutating SQL keywords and was rejected."));

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
        // After step-002 the production regex (audit finding 2) lives in the
        // IQuerySafetyValidator pipeline, so this test pins the pipeline's
        // WriteOperationBlocked error directly via the FakeQuerySafetyValidator instead of
        // binding a real ReadOnlyGuard. The regex itself is now exercised end-to-end by
        // dedicated QuerySafetyValidator tests (EPIC-03 / DRY-T3).
        var factory = new ValidationMockConnectionFactory();
        var service = BuildService(
            factory: factory,
            accessLevel: AccessLevel.ReadOnly,
            error: SqlToAiError.WriteOperationBlocked("The query contains mutating SQL keywords and was rejected."));

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
        var service = BuildService(
            factory: factory,
            accessLevel: accessLevel,
            error: SqlToAiError.MultipleStatementsForbidden());

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

    [Fact]
    public async Task ValidateQueryAsync_ShouldReturnTimeout_WhenExecutionTimesOut()
    {
        var timeoutException = new TimeoutException("Operation timed out.");
        var factory = new ValidationMockConnectionFactory(throwOnExecute: timeoutException);
        var service = BuildService(factory: factory);

        var result = await service.ValidateQueryAsync(TestConstants.DatabaseName, "SELECT 1", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.TimeoutCode, result.Error.Code);
    }

    [Fact]
    public async Task ValidateQueryAsync_ShouldReturnInfrastructureError_WhenSocketExceptionOccurs()
    {
        var socketException = new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused);
        var factory = new ValidationMockConnectionFactory(throwOnExecute: socketException);
        var service = BuildService(factory: factory);

        var result = await service.ValidateQueryAsync(TestConstants.DatabaseName, "SELECT 1", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InfrastructureErrorCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Tests: command timeout source (TD-001) — CommandTimeout on the SET NOEXEC ON/query/
    // SET NOEXEC OFF commands must come from QueryExecutionOptions.CommandTimeoutSeconds
    // (command-execution timeout), not SqlServerOptions.ConnectTimeoutSeconds (connection-open
    // timeout). Standard appsettings.json has both at 30, so the two options must be given
    // deliberately different values here to make a regression to the wrong source visible.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateQueryAsync_ShouldUseQueryExecutionCommandTimeout_NotConnectTimeout()
    {
        var options = new SqlToAiOptions
        {
            SqlServer = new SqlServerOptions { ConnectTimeoutSeconds = 99 },
            QueryExecution = new QueryExecutionOptions { CommandTimeoutSeconds = 42 },
        };
        var factory = new ValidationMockConnectionFactory();
        var service = BuildService(factory: factory, options: options);

        var result = await service.ValidateQueryAsync(TestConstants.DatabaseName, "SELECT 1", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        Assert.Equal(3, factory.ObservedCommandTimeouts.Count);
        Assert.All(factory.ObservedCommandTimeouts, timeout => Assert.Equal(42, timeout));
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

    /// <summary>
    /// <see cref="DbCommand.CommandTimeout"/> observed on every command at the moment it executes
    /// (SET NOEXEC ON, the query itself, SET NOEXEC OFF, in that order) — lets tests verify which
    /// options source fed <c>CommandTimeout</c> without depending on internals of the service.
    /// </summary>
    public List<int> ObservedCommandTimeouts { get; } = [];

    public DbConnection CreateConnection(string? databaseName)
    {
        LastConnection = BuildConnection(throwOnExecute);
        return LastConnection;
    }

    public DbConnection CreateConnection() => CreateConnection(null);

    private FakeDbConnection BuildConnection(Exception? executeException) =>
        new(
            conn => new FakeDbCommand(conn, new FakeDbCommandHandlers(ExecuteNonQuery: cmd => ExecuteNonQuery(cmd, executeException))),
            new FakeDbConnectionOptions(
                Database: TestConstants.DatabaseName,
                DataSource: "mock",
                ServerVersion: "16.0",
                BeginTransaction: (transactionConnection, _) => new FakeDbTransaction(transactionConnection)));

    private int ExecuteNonQuery(FakeDbCommand cmd, Exception? executeException)
    {
        ObservedCommandTimeouts.Add(cmd.CommandTimeout);
        if (executeException != null)
        {
            throw executeException;
        }
        return 0;
    }
}
