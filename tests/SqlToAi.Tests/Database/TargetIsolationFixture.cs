using System.Data.Common;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Shared target-aware test doubles for the M2.3 execution/validation isolation tests: one
/// physical row set per qualified owner, a thread-safe target-recording factory with an optional
/// cross-owner overlap barrier at the connection-open boundary, a target-scoped rule provider and
/// an access-level provider derived from configured grants. Raw database names reach these
/// doubles exactly as SqlClient would see them; qualified identities are only on the context.
/// </summary>
internal sealed record TargetRowSet(string[] Columns, object?[][] Rows);

/// <summary>Releases waiting connections only once at least one call per owner has reached the
/// connection-open boundary, proving genuine cross-owner overlap without sleeps.</summary>
internal sealed class OwnerOverlapBarrier(string? owner1 = null, string? owner2 = null)
{
    private readonly string _owner1 = owner1 ?? TestConstants.QualifiedDatabaseName;
    private readonly string _owner2 = owner2 ?? TestConstants.AlternateQualifiedDatabaseName;
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private bool _owner1Arrived;
    private bool _owner2Arrived;

    public Task Arrive(string ownerQualifiedName)
    {
        lock (_gate)
        {
            if (string.Equals(ownerQualifiedName, _owner1, StringComparison.Ordinal))
            {
                _owner1Arrived = true;
            }
            if (string.Equals(ownerQualifiedName, _owner2, StringComparison.Ordinal))
            {
                _owner2Arrived = true;
            }
            if (_owner1Arrived && _owner2Arrived)
            {
                _release.TrySetResult();
            }
        }
        return _release.Task;
    }

    public bool CrossOwnerOverlapReached => _release.Task.IsCompleted;
}

/// <summary>Target-aware database double. Thread-safe so parallel calls can record concurrently.</summary>
internal sealed class TargetRowsConnectionFactory(
    Dictionary<string, TargetRowSet> rowsByTarget,
    Func<string, string[]>? columnsFromCommand = null) : IDatabaseConnectionFactory
{
    private readonly object _gate = new();
    private readonly List<DatabaseTargetContext> _createdTargets = [];

    internal OwnerOverlapBarrier? OpenBarrier { get; set; }

    public FakeDbConnection? LastConnection { get; private set; }

    public List<DatabaseTargetContext> CreatedTargets
    {
        get { lock (_gate) { return [.. _createdTargets]; } }
    }

    public DbConnection CreateTargetConnection(DatabaseTargetContext target)
    {
        TargetRowSet rowSet = rowsByTarget[target.QualifiedName];
        Func<FakeDbConnection, DbCommand> commandFactory = conn => new FakeDbCommand(conn, new FakeDbCommandHandlers(
            ExecuteScalar: _ => 1,
            ExecuteReader: cmd =>
            {
                conn.LastCommand = cmd;
                string[] columns = columnsFromCommand?.Invoke(cmd.CommandText) ?? rowSet.Columns;
                return new FakeDbDataReader(
                    columns,
                    rowSet.Rows,
                    // Column 0 carries a real origin (ExemptValue), column 1 has none —
                    // so the rule-provider exemption decision only ever applies to ExemptValue.
                    new FakeSchemaTableOrigin("T", "ExemptValue", "dbo"));
            },
            ExecuteNonQuery: _ => 0));
        var options = new FakeDbConnectionOptions(
            Database: target.DatabaseName,
            DataSource: "mock",
            ServerVersion: "16.0",
            BeginTransaction: (transactionConnection, _) => new FakeDbTransaction(transactionConnection));
        OwnerOverlapBarrier? barrier = OpenBarrier;
        if (barrier is not null)
        {
            string owner = target.QualifiedName;
            options = options with { OpenAsyncHook = () => barrier.Arrive(owner) };
        }
        var connection = new FakeDbConnection(commandFactory, options);
        lock (_gate)
        {
            _createdTargets.Add(target);
            LastConnection = connection;
        }
        return connection;
    }

    public Result<DbConnection> CreateCatalogConnection(string? serverAlias) => throw new NotSupportedException();

    public DbConnection CreateConnection(string? databaseName = null) => throw new NotSupportedException();
}

/// <summary>Per-target central rule provider: exempts only the test owner's ExemptValue column
/// and records the owning target identity of every lookup. Thread-safe under parallel calls.</summary>
internal sealed class TargetRuleProvider : IAnonymizationRuleProvider
{
    private readonly object _gate = new();
    private readonly List<string> _owners = [];

    public List<string> Owners { get { lock (_gate) { return [.. _owners]; } } }

    public Task<bool> IsExcludedAsync(DatabaseTargetContext target, string schemaName, string tableName, string columnName, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _owners.Add(target.QualifiedName);
        }
        return Task.FromResult(
            target.QualifiedName == TestConstants.QualifiedDatabaseName
            && string.Equals(columnName, "ExemptValue", StringComparison.OrdinalIgnoreCase));
    }

    public Task<bool> IsExcludedAsync(string databaseName, string schemaName, string tableName, string columnName, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Target-isolated execution must not fall back to the unscoped rule path.");
}

/// <summary>Access levels derived from the configured qualified grant lists in the same
/// restrictive order as the target policy, standing in for the SQL access-level probe.</summary>
internal sealed class GrantsAccessLevelProvider(SqlToAiOptions options) : IAccessLevelProvider
{
    public Task<AccessLevel> GetTargetAccessLevelAsync(DatabaseTargetContext target, CancellationToken cancellationToken = default) =>
        Task.FromResult(Evaluate(target));

    public Task<AccessLevel> GetAccessLevelAsync(string databaseName, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Target-isolated execution must not fall back to the unscoped access probe.");

    private AccessLevel Evaluate(DatabaseTargetContext target)
    {
        foreach ((AccessLevel level, List<string> grants) in new[]
        {
            (AccessLevel.SchemaOnly, options.Databases.SchemaOnly),
            (AccessLevel.ReadOnlyAnonymized, options.Databases.ReadOnlyAnonymized),
            (AccessLevel.ReadOnly, options.Databases.ReadOnly),
            (AccessLevel.ReadWrite, options.Databases.ReadWrite),
        })
        {
            if (grants.Any(grant => Matches(grant, target)))
            {
                return level;
            }
        }
        return AccessLevel.None;
    }

    private static bool Matches(string grant, DatabaseTargetContext target)
    {
        var parsed = DatabaseTargetParser.Parse(grant);
        return parsed.IsSuccess
            && string.Equals(parsed.Value.ServerAlias, target.ServerAlias, StringComparison.Ordinal)
            && string.Equals(parsed.Value.DatabaseName, target.DatabaseName, StringComparison.OrdinalIgnoreCase);
    }
}
