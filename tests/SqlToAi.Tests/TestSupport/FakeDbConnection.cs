#nullable enable

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace SqlToAi.Tests.TestSupport;

/// <summary>
/// Fixed identity values a <see cref="FakeDbConnection"/> reports, plus an optional transaction
/// factory. Bundled into a record (see AiNetLinter <c>MaxMethodParameterCount</c>) so the
/// connection constructor stays within the project's parameter-count limit. Defaults match the
/// values used by the majority of the pre-refactor mocks (schema/rule/exclusion/metadata), which
/// never began a transaction; <see cref="BeginTransaction"/> stays <see langword="null"/> there and
/// <see cref="FakeDbConnection.BeginDbTransaction"/> throws <see cref="NotImplementedException"/>,
/// exactly as those mocks did.
/// </summary>
internal sealed record FakeDbConnectionOptions(
    string Database = "MockDb",
    string DataSource = "MockServer",
    string ServerVersion = "1.0",
    Func<FakeDbConnection, IsolationLevel, FakeDbTransaction>? BeginTransaction = null);

/// <summary>
/// Generic <see cref="DbConnection"/> fake shared by every ADO.NET test double in this project.
/// Holds the common plumbing (connection string/identity/state, open/close, transaction tracking)
/// and delegates command creation to a per-test-class factory.
/// </summary>
internal sealed class FakeDbConnection : DbConnection
{
    private readonly Func<FakeDbConnection, DbCommand> _commandFactory;
    private readonly FakeDbConnectionOptions _options;
    private ConnectionState _state = ConnectionState.Closed;

    public FakeDbConnection(Func<FakeDbConnection, DbCommand> commandFactory, FakeDbConnectionOptions? options = null)
    {
        _commandFactory = commandFactory;
        _options = options ?? new FakeDbConnectionOptions();
    }

    [AllowNull]
    public override string ConnectionString { get; set; } = string.Empty;

    public override string Database => _options.Database;
    public override string DataSource => _options.DataSource;
    public override string ServerVersion => _options.ServerVersion;
    public override ConnectionState State => _state;

    /// <summary>The most recently started transaction — lets tests inspect commit/rollback counts.</summary>
    public FakeDbTransaction? LastTransaction { get; private set; }

    /// <summary>
    /// The most recently created command. Unlike <see cref="LastTransaction"/> this is not set
    /// automatically by <see cref="CreateDbCommand"/> — some services (e.g. <c>QueryExecutionService</c>)
    /// issue incidental probe commands (a <c>SELECT @@TRANCOUNT</c> via <c>ExecuteScalar</c>) that
    /// existing tests deliberately must not see here. A command dispatch handler sets this
    /// explicitly only for the command(s) that represent the "real" query, preserving that
    /// distinction.
    /// </summary>
    public FakeDbCommand? LastCommand { get; set; }

    public override void Open() => _state = ConnectionState.Open;

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        _state = ConnectionState.Open;
        return Task.CompletedTask;
    }

    public override void Close() => _state = ConnectionState.Closed;

    public override void ChangeDatabase(string databaseName) { }

    protected override DbCommand CreateDbCommand() => _commandFactory(this);

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        if (_options.BeginTransaction is null)
        {
            throw new NotImplementedException();
        }
        LastTransaction = _options.BeginTransaction(this, isolationLevel);
        return LastTransaction;
    }
}
