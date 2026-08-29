#nullable enable

using SqlToAi.Domain;

namespace SqlToAi.Database;

internal enum ScriptExecutionStatus
{
    Success,
    Failed
}

internal enum ScriptBatchStatus
{
    Success,
    Failed,
    NotExecuted
}

internal enum ScriptTransactionMode
{
    ReadWriteAtomic,
    ReadWriteProviderAutocommit,
    ReadOnlyRollback,
    ReadOnlyAnonymizedRollback,
    NotStarted
}

internal sealed record ScriptExecutionReport(
    string ScriptPath,
    string Encoding,
    string DatabaseName,
    ScriptExecutionStatus Status,
    ScriptTransactionMode Mode,
    long ElapsedMs,
    long CpuTimeMs,
    long LogicalReads,
    IReadOnlyList<ScriptBatchReport> Batches,
    SqlToAiError? Error = null);

internal sealed record ScriptBatchReport(
    int BatchNumber,
    SqlBatch Batch,
    ScriptBatchStatus Status,
    IReadOnlyList<QueryExecutionResult> Executions,
    SqlToAiError? Error = null);
