#nullable enable

using System.Text;
using System.Globalization;
using SqlToAi.Domain;

namespace SqlToAi.Database;

internal static class ScriptExecutionReportRenderer
{
    private const int MinimumFenceLength = 3;

    public static string Render(ScriptExecutionReport report)
    {
        var builder = new StringBuilder();
        AppendHeader(builder, report);
        AppendBatches(builder, report.Batches);
        AppendFailureDiagnostics(builder, report);
        return builder.ToString();
    }

    private static void AppendHeader(StringBuilder builder, ScriptExecutionReport report)
    {
        builder.AppendLine("# SQL Script Execution Report");
        builder.AppendLine();
        AppendMetadata(builder, "script_path", RenderInlineCode(report.ScriptPath));
        AppendMetadata(builder, "encoding", RenderInlineCode(report.Encoding));
        AppendMetadata(builder, "database", RenderInlineCode(report.DatabaseName));
        AppendMetadata(builder, "status", RenderInlineCode(report.Status.ToString()));
        string mode = RenderInlineCode(report.Mode.ToString());
        builder.Append("- transaction_mode: ").Append(mode).Append(" (")
            .Append(FormatMode(report.Mode)).AppendLine(")");
        AppendMetadata(builder, "elapsed_ms", report.ElapsedMs.ToString(CultureInfo.InvariantCulture));
        AppendMetadata(builder, "cpu_time_ms", report.CpuTimeMs.ToString(CultureInfo.InvariantCulture));
        AppendMetadata(builder, "logical_reads", report.LogicalReads.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendMetadata(StringBuilder builder, string name, string value)
    {
        builder.Append("- ").Append(name).Append(": ").AppendLine(value);
    }

    private static void AppendBatches(StringBuilder builder, IReadOnlyList<ScriptBatchReport> batches)
    {
        builder.AppendLine();
        builder.AppendLine("## Batches");
        if (batches.Count == 0)
        {
            builder.AppendLine("No batches were executed.");
            return;
        }

        foreach (ScriptBatchReport batch in batches)
        {
            AppendBatch(builder, batch);
        }
    }

    private static void AppendBatch(StringBuilder builder, ScriptBatchReport report)
    {
        builder.AppendLine();
        builder.Append("### Batch ").Append(report.BatchNumber).AppendLine();
        builder.Append("- source_lines: ").Append(report.Batch.StartLine).Append('-')
            .AppendLine(report.Batch.EndLine.ToString(CultureInfo.InvariantCulture));
        AppendMetadata(builder, "status", RenderInlineCode(report.Status.ToString()));
        AppendMetadata(builder, "repeat_count", report.Batch.RepeatCount.ToString(CultureInfo.InvariantCulture));
        AppendMetadata(builder, "executions", report.Executions.Count.ToString(CultureInfo.InvariantCulture));
        if (report.Error is not null)
        {
            AppendError(builder, report.Error);
        }

        for (int index = 0; index < report.Executions.Count; index++)
        {
            AppendExecution(builder, index + 1, report.Executions[index]);
        }

        if (report.Status == ScriptBatchStatus.Failed)
        {
            builder.AppendLine();
            builder.AppendLine("#### Failing batch SQL");
            builder.AppendLine(RenderCodeBlock("sql", report.Batch.Text));
        }
    }

    private static void AppendExecution(
        StringBuilder builder,
        int executionNumber,
        QueryExecutionResult execution)
    {
        builder.AppendLine();
        builder.Append("#### Execution ").Append(executionNumber).AppendLine();
        AppendMetadata(builder, "row_count", execution.RowCount.ToString(CultureInfo.InvariantCulture));
        AppendMetadata(builder, "elapsed_ms", execution.ElapsedMs.ToString(CultureInfo.InvariantCulture));
        AppendMetadata(builder, "cpu_time_ms", execution.CpuTimeMs.ToString(CultureInfo.InvariantCulture));
        AppendMetadata(builder, "logical_reads", execution.LogicalReads.ToString(CultureInfo.InvariantCulture));
        AppendAnonymizationMetadata(builder, execution);
        if (string.IsNullOrEmpty(execution.Data))
        {
            AppendMetadata(builder, "data", "(empty)");
            return;
        }

        builder.AppendLine();
        builder.AppendLine(RenderCodeBlock("json", execution.Data));
    }

    private static void AppendAnonymizationMetadata(
        StringBuilder builder,
        QueryExecutionResult execution)
    {
        if (!execution.WasAnonymized && execution.AnonymizedColumns.Count == 0
            && execution.SearchableTokenColumns.Count == 0)
        {
            return;
        }

        AppendMetadata(builder, "anonymized", execution.WasAnonymized.ToString().ToLowerInvariant());
        if (execution.AnonymizationMode.Length > 0)
        {
            AppendMetadata(builder, "anonymization_mode", RenderInlineCode(execution.AnonymizationMode));
        }

        AppendColumnMetadata(builder, "anonymized_columns", execution.AnonymizedColumns);
        AppendColumnMetadata(builder, "searchable_token_columns", execution.SearchableTokenColumns);
    }

    private static void AppendColumnMetadata(
        StringBuilder builder,
        string name,
        IReadOnlyList<string> columns)
    {
        if (columns.Count == 0)
        {
            return;
        }

        var values = new StringBuilder();
        for (int index = 0; index < columns.Count; index++)
        {
            if (index > 0)
            {
                values.Append(", ");
            }

            values.Append(RenderInlineCode(columns[index]));
        }

        AppendMetadata(builder, name, values.ToString());
    }

    private static void AppendFailureDiagnostics(
        StringBuilder builder,
        ScriptExecutionReport report)
    {
        ScriptBatchReport? failedBatch = FindFailedBatch(report.Batches);
        if (report.Error is null && failedBatch is null)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Failure diagnostics");
        SqlToAiError? error = failedBatch?.Error ?? report.Error;
        if (error is not null)
        {
            AppendError(builder, error);
        }

        if (failedBatch is null)
        {
            return;
        }

        AppendMetadata(builder, "failed_batch", failedBatch.BatchNumber.ToString(CultureInfo.InvariantCulture));
        builder.Append("- failed_source_lines: ").Append(failedBatch.Batch.StartLine).Append('-')
            .AppendLine(failedBatch.Batch.EndLine.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine();
        builder.AppendLine("#### Failing SQL");
        builder.AppendLine(RenderCodeBlock("sql", failedBatch.Batch.Text));
    }

    private static void AppendError(StringBuilder builder, SqlToAiError error)
    {
        AppendMetadata(builder, "error_code", RenderInlineCode(error.Code));
        AppendMetadata(builder, "error_message", RenderInlineCode(error.Message));
    }

    private static ScriptBatchReport? FindFailedBatch(IReadOnlyList<ScriptBatchReport> batches)
    {
        foreach (ScriptBatchReport batch in batches)
        {
            if (batch.Status == ScriptBatchStatus.Failed)
            {
                return batch;
            }
        }

        return null;
    }

    private static string FormatMode(ScriptTransactionMode mode) => mode switch
    {
        ScriptTransactionMode.ReadWriteAtomic => "ReadWrite atomic",
        ScriptTransactionMode.ReadWriteProviderAutocommit => "ReadWrite provider-autocommit",
        ScriptTransactionMode.ReadOnlyRollback => "ReadOnly rollback",
        ScriptTransactionMode.ReadOnlyAnonymizedRollback => "ReadOnly anonymized rollback",
        ScriptTransactionMode.NotStarted => "not started / preflight",
        _ => mode.ToString()
    };

    private static string RenderInlineCode(string value)
    {
        string normalized = value.Replace('\r', ' ').Replace('\n', ' ');
        int fenceLength = Math.Max(MinimumFenceLength - 2, MaxBacktickRun(normalized) + 1);
        string fence = new('`', fenceLength);
        return fence + normalized + fence;
    }

    private static string RenderCodeBlock(string language, string value)
    {
        int fenceLength = Math.Max(MinimumFenceLength, MaxBacktickRun(value) + 1);
        string fence = new('`', fenceLength);
        string lineBreak = value.EndsWith('\n') ? string.Empty : "\n";
        return fence + language + "\n" + value + lineBreak + fence;
    }

    private static int MaxBacktickRun(string value)
    {
        int maximum = 0;
        int current = 0;
        foreach (char character in value)
        {
            current = character == '`' ? current + 1 : 0;
            maximum = Math.Max(maximum, current);
        }

        return maximum;
    }
}
