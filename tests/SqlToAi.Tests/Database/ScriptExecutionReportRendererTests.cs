#nullable enable

using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Database;

// @covers SqlToAi.Database.ScriptExecutionReportRenderer
public sealed class ScriptExecutionReportRendererTests
{
    [Fact]
    public void RenderSuccessReport_ContainsMetadataMetricsBatchesAndJsonLines()
    {
        var execution = new QueryExecutionResult(
            "{\"id\":1}\n{\"id\":2}",
            true,
            ["CustomerName"],
            "Tokenization",
            ["CustomerName"],
            12,
            2,
            7,
            9);
        var reports = new[]
        {
            ScriptExecutionReportFactory.BuildSucceededBatch(
                1,
                new SqlBatch("SELECT 1", 1, 2, 2),
                [execution]),
            ScriptExecutionReportFactory.BuildSucceededBatch(
                2,
                new SqlBatch("SELECT 2", 4, 4),
                [new QueryExecutionResult("[]", false, [], "ScramblePattern", [], 5, 0, 2, 3)])
        };
        var report = BuildReport(reports, ScriptTransactionMode.ReadWriteAtomic);

        string markdown = ScriptExecutionReportRenderer.Render(report);

        Assert.Contains("# SQL Script Execution Report", markdown, StringComparison.Ordinal);
        Assert.Contains("script_path", markdown, StringComparison.Ordinal);
        Assert.Contains("UTF-8", markdown, StringComparison.Ordinal);
        Assert.Contains("ReportingDb", markdown, StringComparison.Ordinal);
        Assert.Contains("ReadWriteAtomic", markdown, StringComparison.Ordinal);
        Assert.Contains("elapsed_ms: 17", markdown, StringComparison.Ordinal);
        Assert.Contains("cpu_time_ms: 9", markdown, StringComparison.Ordinal);
        Assert.Contains("logical_reads: 12", markdown, StringComparison.Ordinal);
        Assert.Contains("### Batch 1", markdown, StringComparison.Ordinal);
        Assert.Contains("source_lines: 1-2", markdown, StringComparison.Ordinal);
        Assert.Contains("repeat_count: 2", markdown, StringComparison.Ordinal);
        Assert.Contains("#### Execution 1", markdown, StringComparison.Ordinal);
        Assert.Contains("{\"id\":1}\n{\"id\":2}", markdown, StringComparison.Ordinal);
        Assert.Contains("anonymized: true", markdown, StringComparison.Ordinal);
        Assert.Contains("anonymization_mode", markdown, StringComparison.Ordinal);
        Assert.Contains("CustomerName", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderFailureReport_ContainsBatchDiagnosticsAndNotExecutedMarkers()
    {
        var error = SqlToAiError.QueryError("syntax failure with `inline` marker");
        var batches = new[]
        {
            ScriptExecutionReportFactory.BuildSucceededBatch(
                1,
                new SqlBatch("SELECT 1", 1, 1),
                [new QueryExecutionResult("[]", false, [], "ScramblePattern")]),
            ScriptExecutionReportFactory.BuildFailedBatch(
                2,
                new SqlBatch("SELECT ``` AS [value]", 3, 3),
                [],
                error),
            ScriptExecutionReportFactory.BuildNotExecutedBatch(3, new SqlBatch("SELECT 3", 5, 5))
        };
        var report = BuildReport(batches, ScriptTransactionMode.ReadWriteAtomic, error);

        string markdown = ScriptExecutionReportRenderer.Render(report);

        Assert.Contains("## Failure diagnostics", markdown, StringComparison.Ordinal);
        Assert.Contains("error_code", markdown, StringComparison.Ordinal);
        Assert.Contains(SqlToAiError.QueryErrorCode, markdown, StringComparison.Ordinal);
        Assert.Contains("failed_batch: 2", markdown, StringComparison.Ordinal);
        Assert.Contains("failed_source_lines: 3-3", markdown, StringComparison.Ordinal);
        Assert.Contains("SELECT ``` AS [value]", markdown, StringComparison.Ordinal);
        Assert.Contains("````sql", markdown, StringComparison.Ordinal);
        Assert.Contains("NotExecuted", markdown, StringComparison.Ordinal);
        Assert.Contains("inline", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderReportLevelFailure_UsesErrorWithoutInventingBatchContext()
    {
        var error = SqlToAiError.InfrastructureError("connection failed");
        var report = BuildReport(
            ScriptExecutionReportFactory.BuildFailureBatches(
                [new SqlBatch("SELECT 1", 1, 1)],
                null,
                error),
            ScriptTransactionMode.ReadWriteProviderAutocommit,
            error);

        string markdown = ScriptExecutionReportRenderer.Render(report);

        Assert.Contains("connection failed", markdown, StringComparison.Ordinal);
        Assert.Contains("ReadWriteProviderAutocommit", markdown, StringComparison.Ordinal);
        Assert.Contains("NotExecuted", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("failed_batch", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Failing SQL", markdown, StringComparison.Ordinal);
    }

    private static ScriptExecutionReport BuildReport(
        IReadOnlyList<ScriptBatchReport> batches,
        ScriptTransactionMode mode,
        SqlToAiError? error = null) =>
        ScriptExecutionReportFactory.BuildReport(new ScriptExecutionReportInput(
            new SqlScriptFile("C:\\scripts\\report`file.sql", "SELECT 1", "UTF-8"),
            "ReportingDb",
            mode,
            batches,
            error));
}
