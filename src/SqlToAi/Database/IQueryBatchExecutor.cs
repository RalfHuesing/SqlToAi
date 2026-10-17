using System.Data.Common;
using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Execution unit shared by single-query and script-batch execution. Every caller resolves the
/// qualified target once at its entry point and passes the immutable context here, so token and
/// rule operations always use the scoped target-aware APIs. <see cref="DatabaseName"/> carries the
/// canonical qualified label for diagnostics and report fields.
/// </summary>
internal sealed record QueryBatchExecutionArgs(
    DbConnection Connection,
    DbTransaction? Transaction,
    string DatabaseName,
    string Query,
    int RowLimit,
    bool Anonymize,
    object? Parameters,
    DatabaseTargetContext Target);

internal interface IQueryBatchExecutor
{
    Task<Result<QueryExecutionResult>> ExecuteBatchAsync(
        QueryBatchExecutionArgs args,
        CancellationToken cancellationToken = default);
}
