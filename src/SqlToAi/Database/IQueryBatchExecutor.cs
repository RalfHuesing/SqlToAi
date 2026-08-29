#nullable enable

using System.Data.Common;
using SqlToAi.Domain;

namespace SqlToAi.Database;

internal sealed record QueryBatchExecutionArgs(
    DbConnection Connection,
    DbTransaction Transaction,
    string DatabaseName,
    string Query,
    int RowLimit,
    bool Anonymize,
    object? Parameters);

internal interface IQueryBatchExecutor
{
    Task<Result<QueryExecutionResult>> ExecuteBatchAsync(
        QueryBatchExecutionArgs args,
        CancellationToken cancellationToken = default);
}
