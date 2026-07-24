#nullable enable

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Tests.Database;

#pragma warning disable CS8765 // Nullability of parameter doesn't match overridden member

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
        SqlToAiOptions? options = null)
    {
        options ??= new SqlToAiOptions();
        factory ??= new ValidationMockConnectionFactory();
        return new QueryValidationService(
            factory,
            new FakeSecurityGuard(isAllowed),
            new FakeAccessLevelProvider(accessLevel),
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
// Minimal DbConnection/DbTransaction/DbCommand fakes for QueryValidationService.
// Unlike QueryExecutionService, this service never reads rows — it only issues
// ExecuteNonQueryAsync calls (SET PARSEONLY ON / the query itself / SET PARSEONLY OFF) inside a
// transaction it always rolls back — so no data-reader plumbing is needed here.
// -------------------------------------------------------------------------

internal sealed class ValidationMockConnectionFactory(Exception? throwOnExecute = null) : IDatabaseConnectionFactory
{
    /// <summary>The most recently created connection — lets tests inspect its transaction.</summary>
    public ValidationMockConnection? LastConnection { get; private set; }

    public DbConnection CreateConnection(string? databaseName)
    {
        LastConnection = new ValidationMockConnection(throwOnExecute);
        return LastConnection;
    }

    public DbConnection CreateConnection() => CreateConnection(null);
}

internal sealed class ValidationMockConnection(Exception? throwOnExecute) : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;

    public override string ConnectionString { get; set; } = string.Empty;
    public override string Database => TestConstants.DatabaseName;
    public override string DataSource => "mock";
    public override string ServerVersion => "16.0";
    public override ConnectionState State => _state;

    /// <summary>The most recently started transaction — lets tests inspect commit/rollback calls.</summary>
    public ValidationMockTransaction? LastTransaction { get; private set; }

    public override void Open() => _state = ConnectionState.Open;
    public override Task OpenAsync(CancellationToken cancellationToken) { _state = ConnectionState.Open; return Task.CompletedTask; }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        LastTransaction = new ValidationMockTransaction(this);
        return LastTransaction;
    }

    protected override DbCommand CreateDbCommand() => new ValidationMockCommand(this, throwOnExecute);

    public override void ChangeDatabase(string databaseName) { }
    public override void Close() => _state = ConnectionState.Closed;
}

internal sealed class ValidationMockTransaction(DbConnection connection) : DbTransaction
{
    public int CommitCount { get; private set; }
    public int RollbackCount { get; private set; }

    protected override DbConnection DbConnection => connection;
    public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
    public override void Commit() => CommitCount++;
    public override void Rollback() => RollbackCount++;
}

internal sealed class ValidationMockCommand(DbConnection connection, Exception? throwOnExecute) : DbCommand
{
    private readonly MockQueryParameterCollectionAdapter _parameters = new();

    public override string CommandText { get; set; } = string.Empty;
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; } = connection;
    protected override DbParameterCollection DbParameterCollection => _parameters;
    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel() { }

    public override int ExecuteNonQuery()
    {
        if (throwOnExecute != null)
        {
            throw throwOnExecute;
        }
        return 0;
    }

    public override object? ExecuteScalar() => null;

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
        throw new NotSupportedException("QueryValidationService never opens a data reader.");

    protected override DbParameter CreateDbParameter() => new MockQueryParameter();
    public override void Prepare() { }
}
