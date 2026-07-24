#nullable enable

using System.Data;
using System.Data.Common;

namespace SqlToAi.Tests.TestSupport;

/// <summary>
/// Generic <see cref="DbTransaction"/> fake shared by every ADO.NET test double in this project.
/// Counts <see cref="Commit"/>/<see cref="Rollback"/> calls (several services under test assert on
/// exactly these counts) and optionally invokes <paramref name="onRollback"/> so a test can
/// simulate a transaction that is already gone by the time defensive rollback runs (mirrors a
/// real-world case where the statement itself committed server-side).
/// </summary>
internal sealed class FakeDbTransaction(DbConnection connection, Action? onRollback = null) : DbTransaction
{
    public int CommitCount { get; private set; }
    public int RollbackCount { get; private set; }

    protected override DbConnection DbConnection => connection;

    public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

    public override void Commit() => CommitCount++;

    public override void Rollback()
    {
        RollbackCount++;
        onRollback?.Invoke();
    }
}
