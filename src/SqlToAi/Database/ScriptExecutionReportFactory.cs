#nullable enable

using SqlToAi.Domain;

namespace SqlToAi.Database;

internal sealed record ScriptExecutionReportInput(
    SqlScriptFile ScriptFile,
    string DatabaseName,
    ScriptTransactionMode Mode,
    IReadOnlyList<ScriptBatchReport> Batches,
    SqlToAiError? Error = null);

internal static class ScriptExecutionReportFactory
{
    public static ScriptExecutionReport BuildReport(ScriptExecutionReportInput input)
    {
        var batches = new List<ScriptBatchReport>(input.Batches);
        long elapsedMs = 0;
        long cpuTimeMs = 0;
        long logicalReads = 0;

        foreach (ScriptBatchReport batch in batches)
        {
            foreach (QueryExecutionResult execution in batch.Executions)
            {
                elapsedMs += execution.ElapsedMs;
                cpuTimeMs += execution.CpuTimeMs;
                logicalReads += execution.LogicalReads;
            }
        }

        ScriptExecutionStatus status = input.Error is null && !ContainsFailure(batches)
            ? ScriptExecutionStatus.Success
            : ScriptExecutionStatus.Failed;

        return new ScriptExecutionReport(
            input.ScriptFile.ResolvedPath,
            input.ScriptFile.EncodingName,
            input.DatabaseName,
            status,
            input.Mode,
            elapsedMs,
            cpuTimeMs,
            logicalReads,
            batches,
            input.Error);
    }

    public static ScriptBatchReport BuildSucceededBatch(
        int batchNumber,
        SqlBatch batch,
        IReadOnlyList<QueryExecutionResult> executions) =>
        new(batchNumber, batch, ScriptBatchStatus.Success, executions);

    public static ScriptBatchReport BuildFailedBatch(
        int batchNumber,
        SqlBatch batch,
        IReadOnlyList<QueryExecutionResult> executions,
        SqlToAiError error) =>
        new(batchNumber, batch, ScriptBatchStatus.Failed, executions, error);

    public static ScriptBatchReport BuildNotExecutedBatch(int batchNumber, SqlBatch batch) =>
        new(batchNumber, batch, ScriptBatchStatus.NotExecuted, []);

    public static IReadOnlyList<ScriptBatchReport> BuildFailureBatches(
        IReadOnlyList<SqlBatch> batches,
        int? failedBatchNumber,
        SqlToAiError error)
    {
        var reports = new List<ScriptBatchReport>(batches.Count);
        for (int index = 0; index < batches.Count; index++)
        {
            int batchNumber = index + 1;
            reports.Add(failedBatchNumber == batchNumber
                ? BuildFailedBatch(batchNumber, batches[index], [], error)
                : BuildNotExecutedBatch(batchNumber, batches[index]));
        }

        return reports;
    }

    private static bool ContainsFailure(IReadOnlyList<ScriptBatchReport> batches)
    {
        foreach (ScriptBatchReport batch in batches)
        {
            if (batch.Status == ScriptBatchStatus.Failed)
            {
                return true;
            }
        }

        return false;
    }
}
