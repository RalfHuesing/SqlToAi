#nullable enable

using SqlToAi.Domain;

namespace SqlToAi.Database;

internal interface IScriptExecutionService
{
    Task<Result<IReadOnlyList<ScriptBatchExecutionResult>>> ExecuteAtomicallyAsync(
        ScriptExecutionRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed record ScriptExecutionRequest(
    SqlScriptFile ScriptFile,
    string DatabaseName,
    int? RequestedRowLimit = null,
    object? Parameters = null);

internal sealed record ScriptBatchExecutionResult(
    SqlBatch Batch,
    IReadOnlyList<QueryExecutionResult> Executions);
