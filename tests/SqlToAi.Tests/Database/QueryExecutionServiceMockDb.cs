#nullable enable

using System.Data.Common;
using Microsoft.Extensions.Logging;
using SqlToAi.Anonymization;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

internal sealed class AlwaysExcludeRuleProvider : IAnonymizationRuleProvider
{
    public Task<bool> IsExcludedAsync(string databaseName, string schemaName, string tableName, string columnName, CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}

/// <summary>
/// Test double for <see cref="IAnonymizationRuleProvider"/> that excludes (shows in clear text)
/// only when the resolved schema equals <see cref="ExcludedSchema"/> (case-insensitive) â€” used to
/// verify that <c>QueryExecutionService</c> actually threads the resolved <c>BaseSchemaName</c>
/// through to the rule provider, instead of collapsing same-named tables in different schemas into
/// one decision (see tasks/audit-2026-07-24/02-anonymisierung-tokenisierung.md, Finding
/// "Ausschluss-/Regel-Abgleich ist schema-blind").
/// </summary>
internal sealed class SchemaScopedRuleProvider(string excludedSchema) : IAnonymizationRuleProvider
{
    public Task<bool> IsExcludedAsync(string databaseName, string schemaName, string tableName, string columnName, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Equals(schemaName, excludedSchema, StringComparison.OrdinalIgnoreCase));
}


/// <summary>
/// Minimal <see cref="ILogger{T}"/> test double that captures the last logged message and
/// exception, so tests can assert what actually reaches the log â€” independent of what (if
/// anything) is filtered before being returned to the AI as the tool's error response.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public string? LastMessage { get; private set; }
    public Exception? LastException { get; private set; }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        LastMessage = formatter(state, exception);
        LastException = exception;
    }
}

/// <summary>
/// Test double for <see cref="IQuerySafetyValidator"/>. Replaces the legacy
/// <c>FakeSecurityGuard</c> / <c>FakeAccessLevelProvider</c> / <c>FakeReadOnlyGuard</c> triple
/// (now in <c>TestSupport/LegacySecurityFakes.cs</c>, step-003 / DRY-T1) in the four
/// guardrail-service tests so each test pins the pipeline outcome directly instead of composing
/// three independent fakes. The legacy fakes themselves are still consumed by
/// <c>IndexSuggestionServiceTests</c> and by this class's happy-path constructor below.
/// </summary>
internal sealed class FakeQuerySafetyValidator : IQuerySafetyValidator
{
    private readonly Result<QuerySafetyCheckResult>? _fixedFailure;
    private readonly IQuerySafetyValidator? _delegate;

    /// <summary>Happy-path constructor: delegates to a real <see cref="QuerySafetyValidator"/>
    /// built from the supplied security triples. This keeps the test fake faithful to the
    /// production pipeline (same regex, same multi-statement detector, same access-level
    /// checks) so the service-level tests don't have to re-implement any of the 6 stages.
    /// </summary>
    public FakeQuerySafetyValidator(
        ISecurityGuard securityGuard,
        IAccessLevelProvider accessLevelProvider,
        IReadOnlyGuard readOnlyGuard)
    {
        _delegate = new QuerySafetyValidator(securityGuard, accessLevelProvider, readOnlyGuard);
    }

    /// <summary>Convenience overload for tests that don't care which access level the pipeline
    /// sees, only that it returns the configured success result for any input that the
    /// pipeline stages (empty inputs, multi-statement, read-only guard) accept. Used by tests
    /// that explicitly want to exercise the service's post-pipeline behaviour (commit/rollback,
    /// row limits, anonymization) without coupling them to access-level semantics.
    /// </summary>
    public FakeQuerySafetyValidator(QuerySafetyCheckResult result)
    {
        _delegate = new BypassReadOnlyGuardValidator(result);
    }

    /// <summary>Failure-path constructor: the validator always reports this error and skips
    /// every check — used by tests that need a fixed rejection (e.g. <c>sp_executesql</c>
    /// rejection, schema-mismatch, transaction-timeout) regardless of the input.
    /// </summary>
    public FakeQuerySafetyValidator(SqlToAiError error)
    {
        _fixedFailure = error;
    }

    /// <summary>
    /// Factory method building a <see cref="FakeQuerySafetyValidator"/> with the specified
    /// security parameters, wrapping <see cref="FakeSecurityGuard"/>, <see cref="FakeAccessLevelProvider"/>,
    /// and <see cref="ReadOnlyGuard"/> or fixed error.
    /// </summary>
    public static FakeQuerySafetyValidator Create(
        bool isAllowed = true,
        AccessLevel accessLevel = AccessLevel.ReadOnly,
        SqlToAiError? error = null)
    {
        return error != null
            ? new FakeQuerySafetyValidator(error)
            : new FakeQuerySafetyValidator(
                new FakeSecurityGuard(isAllowed),
                new FakeAccessLevelProvider(accessLevel),
                new ReadOnlyGuard());
    }

    public Task<Result<QuerySafetyCheckResult>> ValidateQuerySafetyAsync(
        string databaseName,
        string query,
        bool allowSchemaOnly = false,
        CancellationToken cancellationToken = default)
    {
        if (_fixedFailure != null)
        {
            return Task.FromResult(_fixedFailure);
        }
        return _delegate!.ValidateQuerySafetyAsync(databaseName, query, allowSchemaOnly, cancellationToken);
    }

    /// <summary>
    /// Minimal pipeline that still runs the input-only stages (empty database name, empty query,
    /// multi-statement detection) but treats the read-only guard as a no-op, so tests that
    /// already control the success outcome via the explicit <see cref="QuerySafetyCheckResult"/>
    /// see a service-level pass for any non-structural input. The delegate is intentionally
    /// narrow: <c>readOnlySafe</c> is the test author's choice, the access level is whatever
    /// the test claims, and the whitelist is always satisfied.
    /// </summary>
    private sealed class BypassReadOnlyGuardValidator(QuerySafetyCheckResult result) : IQuerySafetyValidator
    {
        public Task<Result<QuerySafetyCheckResult>> ValidateQuerySafetyAsync(
            string databaseName,
            string query,
            bool allowSchemaOnly = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                return Task.FromResult(Result<QuerySafetyCheckResult>.Failure(
                    SqlToAiError.InvalidParameters("Database name must not be empty.")));
            }
            if (string.IsNullOrWhiteSpace(query))
            {
                return Task.FromResult(Result<QuerySafetyCheckResult>.Failure(
                    SqlToAiError.InvalidParameters("Query must not be empty.")));
            }
            if (SqlMultiStatementDetector.ContainsMultipleStatements(query))
            {
                return Task.FromResult(Result<QuerySafetyCheckResult>.Failure(
                    SqlToAiError.MultipleStatementsForbidden()));
            }
            return Task.FromResult(Result<QuerySafetyCheckResult>.Success(result));
        }
    }
}

// -------------------------------------------------------------------------
// Connection factory / reader mock
// -------------------------------------------------------------------------

/// <summary>
/// Schema-table origin metadata (<c>BaseSchemaName</c>/<c>BaseTableName</c>/<c>BaseColumnName</c>)
/// a mock reader reports for column 0, or "unavailable" (<see cref="Available"/> false) to simulate
/// a provider without schema-table support. Bundled into its own record (see AiNetLinter
/// <c>MaxConstructorDependencies</c>) so the mock DB constructors stay within the project's
/// parameter-count limit.
/// </summary>
internal sealed record MockSchemaOrigin(string? BaseTableName = null, string? BaseColumnName = null, bool Available = true, string? BaseSchemaName = null);

/// <summary>Bundles all per-test mock DB configuration into one parameter object.</summary>
internal sealed record MockQueryRowConfig(
    string? StringValue = null,
    int RowCount = 1,
    string ColumnName = "Name",
    MockSchemaOrigin? Origin = null,
    MockTranCountSequence? TranCountSequence = null,
    bool ThrowOnRollback = false,
    Exception? ThrowOnExecute = null);

/// <summary>
/// Simulates a sequence of <c>SELECT @@TRANCOUNT</c> probe results, for testing
/// <c>QueryExecutionService</c>'s layer-2 transaction-tampering detection (see
/// <see cref="TransactionIntegrityGuard"/>) without needing an actual mutating keyword. Each call
/// to <see cref="Next"/> returns the next configured value; once exhausted, the last value
/// repeats (only two calls â€” baseline and post-execution â€” are expected per query).
/// </summary>
internal sealed class MockTranCountSequence(params int[] values)
{
    private int _index;

    public int Next()
    {
        int value = values[Math.Min(_index, values.Length - 1)];
        _index++;
        return value;
    }
}

internal sealed class MockQueryConnectionFactory(MockQueryRowConfig? config = null) : IDatabaseConnectionFactory
{
    private readonly MockQueryRowConfig _config = config ?? new MockQueryRowConfig();

    /// <summary>The most recently created connection â€” lets tests inspect its transaction.</summary>
    public FakeDbConnection? LastConnection { get; private set; }

    /// <summary>
    /// Command texts passed to <see cref="DbCommand.ExecuteNonQuery"/> across all commands on the
    /// most recently created connection â€” used to assert that <c>SET STATISTICS ...</c> commands
    /// were issued (see step-002).
    /// </summary>
    public List<string> ExecutedNonQueryCommands { get; } = [];

    public DbConnection CreateConnection(string? databaseName)
    {
        LastConnection = BuildConnection(_config);
        return LastConnection;
    }

    public DbConnection CreateConnection() => CreateConnection((string?)null);

    private FakeDbConnection BuildConnection(MockQueryRowConfig config) =>
        new(
            conn => new FakeDbCommand(conn, new FakeDbCommandHandlers(
                ExecuteScalar: cmd => ExecuteScalar(cmd, config),
                ExecuteReader: cmd => ExecuteReader(cmd, conn, config),
                ExecuteNonQuery: cmd => RecordNonQuery(cmd))),
            new FakeDbConnectionOptions(
                Database: TestConstants.DatabaseName,
                DataSource: "mock",
                ServerVersion: "16.0",
                BeginTransaction: (transactionConnection, _) => new FakeDbTransaction(
                    transactionConnection,
                    config.ThrowOnRollback ? ThrowTransactionAlreadyCompleted : null)));

    /// <summary>
    /// Simulates the real-world case where the underlying transaction is already gone (e.g.
    /// committed server-side by the statement itself) by the time our code tries to roll it back
    /// defensively.
    /// </summary>
    private static void ThrowTransactionAlreadyCompleted() =>
        throw new InvalidOperationException("This transaction has already completed; it is no longer usable.");

    /// <summary>
    /// Returns 1 by default (preserving prior behavior for every unconfigured test). When a
    /// <see cref="MockQueryRowConfig.TranCountSequence"/> is configured and the command text is
    /// the <c>SELECT @@TRANCOUNT</c> probe issued by <see cref="TransactionIntegrityGuard"/>,
    /// returns the next simulated value instead â€” letting tests drive the layer-2
    /// transaction-tampering detection without any real mutating keyword.
    /// </summary>
    private static int ExecuteScalar(FakeDbCommand cmd, MockQueryRowConfig config) =>
        config.TranCountSequence != null && cmd.CommandText.Contains("@@TRANCOUNT", StringComparison.OrdinalIgnoreCase)
            ? config.TranCountSequence.Next()
            : 1;

    /// <summary>Records the command text of every <c>ExecuteNonQuery</c> call (e.g. the <c>SET STATISTICS ...</c> commands issued before the main query).</summary>
    private int RecordNonQuery(FakeDbCommand cmd)
    {
        ExecutedNonQueryCommands.Add(cmd.CommandText);
        return 0;
    }

    /// <summary>
    /// Records the command actually run through a data reader (the real query) on the connection
    /// â€” deliberately NOT every command created on this connection, so an incidental
    /// <c>SELECT @@TRANCOUNT</c> probe (see <see cref="TransactionIntegrityGuard"/>), which only
    /// ever calls <c>ExecuteScalar</c>, never overwrites <see cref="FakeDbConnection.LastCommand"/>
    /// and existing assertions on the resolved query text keep working unchanged.
    /// </summary>
    private static FakeDbDataReader ExecuteReader(FakeDbCommand cmd, FakeDbConnection conn, MockQueryRowConfig config)
    {
        conn.LastCommand = cmd;
        if (config.ThrowOnExecute != null)
        {
            throw config.ThrowOnExecute;
        }
        return BuildReader(config);
    }

    private static FakeDbDataReader BuildReader(MockQueryRowConfig config)
    {
        var rows = Enumerable.Repeat<object?[]>([config.StringValue], config.RowCount).ToList();
        MockSchemaOrigin origin = config.Origin ?? new MockSchemaOrigin();
        bool hasOrigin = origin.Available
            && (origin.BaseTableName != null || origin.BaseColumnName != null || origin.BaseSchemaName != null);
        FakeSchemaTableOrigin? schemaOrigin = hasOrigin
            ? new FakeSchemaTableOrigin(origin.BaseTableName, origin.BaseColumnName, origin.BaseSchemaName)
            : null;
        return new FakeDbDataReader([config.ColumnName], rows, schemaOrigin);
    }
}

