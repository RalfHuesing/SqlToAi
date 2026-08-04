#nullable enable

using System.Data.Common;
using SqlToAi.Database;

namespace SqlToAi.Tests.TestSupport;

internal sealed record DmvColumn(int ColumnId, string ColumnUsage);

internal sealed record DmvRow(
    string Statement,
    long IndexHandle,
    long UserSeeks,
    long UserScans,
    DateTime? LastUserSeek,
    double AvgTotalUserCost,
    double AvgUserImpact,
    IReadOnlyList<DmvColumn> Columns);

/// <summary>
/// A <see cref="DbConnection"/> fake that returns the given DMV rows from a single
/// reader. If <see cref="throwOnExecuteReader"/> is set, it is thrown on
/// <c>ExecuteReaderAsync</c> to simulate server-side failures (e.g. permission errors).
/// <paramref name="serverVersion"/> feeds <see cref="FakeDbConnection.ServerVersion"/>
/// so tests can exercise IndexSuggestionService's version-dependent DMV query selection
/// (TD-004 / step-006/fix-01) without a real SQL Server.
/// </summary>
internal sealed class DmvMockConnectionFactory(
    IReadOnlyList<DmvRow> rows,
    Exception? throwOnExecuteReader,
    string serverVersion = "16.0") : IDatabaseConnectionFactory
{
    public FakeDbConnection? LastConnection { get; private set; }

    /// <summary>The most recent <see cref="FakeDbCommand"/> passed to <c>ExecuteReader</c> —
    /// lets tests inspect the bound parameters (Dapper prefixes the names with <c>@</c>, but
    /// tests strip that prefix when looking up by property name).</summary>
    public FakeDbCommand? LastReaderCommand { get; private set; }

    public DbConnection CreateConnection(string? databaseName)
    {
        var conn = new FakeDbConnection(
            c => new FakeDbCommand(
                c,
                new FakeDbCommandHandlers(
                    ExecuteReader: cmd => ExecuteReader(cmd, c))),
            new FakeDbConnectionOptions(
                Database: TestConstants.DatabaseName,
                DataSource: "mock",
                ServerVersion: serverVersion,
                BeginTransaction: (connection, _) => new FakeDbTransaction(connection)));
        conn.LastCommand = null;
        LastConnection = conn;
        return conn;
    }

    public DbConnection CreateConnection() => CreateConnection(null);

    private FakeDbDataReader ExecuteReader(FakeDbCommand cmd, FakeDbConnection conn)
    {
        LastReaderCommand = cmd;
        if (throwOnExecuteReader != null)
        {
            throw throwOnExecuteReader;
        }

        string[] columns =
        [
            "Statement", "IndexHandle", "UserSeeks", "UserScans", "LastUserSeek",
            "AvgTotalUserCost", "AvgUserImpact", "ColumnId", "ColumnUsage",
        ];
        var raw = new List<object?[]>();
        foreach (var r in rows)
        {
            foreach (var c in r.Columns)
            {
                raw.Add([r.Statement, r.IndexHandle, r.UserSeeks, r.UserScans, r.LastUserSeek,
                    r.AvgTotalUserCost, r.AvgUserImpact, c.ColumnId, c.ColumnUsage]);
            }
        }
        return new FakeDbDataReader(columns, raw);
    }
}
