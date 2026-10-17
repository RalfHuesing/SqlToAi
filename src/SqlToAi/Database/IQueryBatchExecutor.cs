using System.Data.Common;
using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Execution unit shared by single-query and script-batch execution. <see cref="Target"/> is the
/// resolved immutable target context set by migrated callers; the legacy raw
/// <see cref="DatabaseName"/> string remains the script executor's contract until M2.4 and is
/// removed with it (M4.1). When <see cref="Target"/> is set, token and rule operations use the
/// scoped target-aware APIs; otherwise they use the legacy unscoped paths.
/// </summary>
internal sealed record QueryBatchExecutionArgs(
    DbConnection Connection,
    DbTransaction? Transaction,
    string DatabaseName,
    string Query,
    int RowLimit,
    bool Anonymize,
    object? Parameters,
    DatabaseTargetContext? Target = null);

internal interface IQueryBatchExecutor
{
    Task<Result<QueryExecutionResult>> ExecuteBatchAsync(
        QueryBatchExecutionArgs args,
        CancellationToken cancellationToken = default);
}
