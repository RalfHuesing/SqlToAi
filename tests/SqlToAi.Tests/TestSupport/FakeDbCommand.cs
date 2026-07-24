#nullable enable

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace SqlToAi.Tests.TestSupport;

/// <summary>
/// Per-command dispatch behavior for a <see cref="FakeDbCommand"/>. Each test file's mock
/// connection supplies its own delegates here — the dispatch logic (which SQL text returns which
/// rows, which parameters are expected, etc.) is genuinely test-specific and is deliberately not
/// unified; only the ADO.NET plumbing around it is shared. Unconfigured handlers fall back to the
/// most common default across the pre-refactor mocks (0 rows affected, no scalar, "no reader
/// configured").
/// </summary>
internal sealed record FakeDbCommandHandlers(
    Func<FakeDbCommand, DbDataReader>? ExecuteReader = null,
    Func<FakeDbCommand, object?>? ExecuteScalar = null,
    Func<FakeDbCommand, int>? ExecuteNonQuery = null);

/// <summary>
/// Generic <see cref="DbCommand"/> fake shared by every ADO.NET test double in this project.
/// Holds the common plumbing (<see cref="CommandText"/>, parameters, transaction, timeout) and
/// forwards the three <c>Execute*</c> entry points to the per-test <see cref="FakeDbCommandHandlers"/>
/// supplied at construction, so each mock connection only has to author the dispatch logic that is
/// actually specific to the service under test.
/// </summary>
internal sealed class FakeDbCommand(DbConnection? connection, FakeDbCommandHandlers? handlers = null) : DbCommand
{
    private readonly FakeDbCommandHandlers _handlers = handlers ?? new FakeDbCommandHandlers();
    private readonly FakeDbParameterCollection _parameters = new();

    [AllowNull]
    public override string CommandText { get; set; } = string.Empty;

    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }

    [AllowNull]
    protected override DbConnection? DbConnection { get; set; } = connection;

    protected override DbParameterCollection DbParameterCollection => _parameters;
    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel() { }
    public override void Prepare() { }

    public override int ExecuteNonQuery() => _handlers.ExecuteNonQuery?.Invoke(this) ?? 0;

    public override object? ExecuteScalar() => _handlers.ExecuteScalar?.Invoke(this);

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
        _handlers.ExecuteReader?.Invoke(this)
        ?? throw new NotSupportedException("This fake command has no ExecuteReader handler configured.");

    protected override DbParameter CreateDbParameter() => new FakeDbParameter();
}
