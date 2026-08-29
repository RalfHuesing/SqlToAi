#nullable enable

using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Database;

// @covers SqlToAi.Database.ScriptExecutionReportFactory
public sealed class ScriptExecutionReportFactoryTests
{
    [Fact]
    public void BuildReport_SumsMetricsAcrossBatchRepetitionsAndPreservesDetails()
    {
        var first = new QueryExecutionResult(
            "{\"id\":1}",
            true,
            ["CustomerName"],
            "Tokenization",
            ["CustomerName"],
            11,
            1,
            7,
            13);
        var second = new QueryExecutionResult(
            "{\"id\":2}",
            true,
            ["CustomerName"],
            "Tokenization",
            ["CustomerName"],
            17,
            1,
            5,
            19);
        var third = new QueryExecutionResult(
            "{\"id\":3}",
            false,
            [],
            "ScramblePattern",
            [],
            23,
            1,
            3,
            29);
        var firstBatch = new SqlBatch("SELECT 1", 2, 4, 2);
        var secondBatch = new SqlBatch("SELECT 2", 6, 6);
        var reports = new[]
        {
            ScriptExecutionReportFactory.BuildSucceededBatch(1, firstBatch, [first, second]),
            ScriptExecutionReportFactory.BuildSucceededBatch(2, secondBatch, [third])
        };

        var report = ScriptExecutionReportFactory.BuildReport(new ScriptExecutionReportInput(
            new SqlScriptFile("C:\\scripts\\report.sql", "SELECT 1", "UTF-8"),
            "ReportingDb",
            ScriptTransactionMode.ReadOnlyAnonymizedRollback,
            reports));

        Assert.Equal(ScriptExecutionStatus.Success, report.Status);
        Assert.Equal(ScriptTransactionMode.ReadOnlyAnonymizedRollback, report.Mode);
        Assert.Equal("C:\\scripts\\report.sql", report.ScriptPath);
        Assert.Equal("UTF-8", report.Encoding);
        Assert.Equal("ReportingDb", report.DatabaseName);
        Assert.Equal(51, report.ElapsedMs);
        Assert.Equal(15, report.CpuTimeMs);
        Assert.Equal(61, report.LogicalReads);
        Assert.Equal(2, report.Batches[0].Batch.RepeatCount);
        Assert.Same(first, report.Batches[0].Executions[0]);
        Assert.Equal("CustomerName", report.Batches[0].Executions[0].AnonymizedColumns[0]);
    }

    [Fact]
    public void BuildFailureReport_OrdersFailedAndNotExecutedBatches()
    {
        var error = SqlToAiError.WriteOperationBlocked("second batch rejected");
        var sourceBatches = new[]
        {
            new SqlBatch("SELECT 1", 1, 1),
            new SqlBatch("UPDATE blocked_table SET Value = 1", 3, 3),
            new SqlBatch("SELECT 3", 5, 5)
        };
        var reports = ScriptExecutionReportFactory.BuildFailureBatches(sourceBatches, 2, error);

        var report = ScriptExecutionReportFactory.BuildReport(new ScriptExecutionReportInput(
            new SqlScriptFile("script.sql", "", "UTF-8"),
            "ReportingDb",
            ScriptTransactionMode.NotStarted,
            reports,
            error));

        Assert.Equal(ScriptExecutionStatus.Failed, report.Status);
        Assert.Equal(3, report.Batches.Count);
        Assert.Equal(ScriptBatchStatus.NotExecuted, report.Batches[0].Status);
        Assert.Equal(ScriptBatchStatus.Failed, report.Batches[1].Status);
        Assert.Equal(ScriptBatchStatus.NotExecuted, report.Batches[2].Status);
        Assert.Equal(2, report.Batches[1].BatchNumber);
        Assert.Equal(3, report.Batches[1].Batch.StartLine);
        Assert.Same(error, report.Batches[1].Error);
        Assert.Same(error, report.Error);
    }
}
