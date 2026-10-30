namespace SqlToAi.Mcp;

internal static class ExecutionToolDescriptions
{
    public const string ValidateQuery = "Validates SQL using NOEXEC compilation without executing the query. Caller NOEXEC, PARSEONLY and FMTONLY settings are rejected. Deferred name resolution means object existence and later execution success are not guaranteed.";
    public const string ExportData = "Executes a single read-only SQL query and streams results directly to an absolute local Windows/UNC file path in CSV or JSONL format via an atomic .tmp write, returning compact JSON metadata with column names/types and execution metrics.";
    public const string ExecuteFile = "Executes a local .sql file with multi-batch support and returns a structured Markdown report with transaction mode, metrics, results, and diagnostics. Server-managed transactions reject explicit transaction control before any batch runs. Procedures, dynamic SQL and triggers can still alter transaction state; atomicity is not guaranteed for such code.";
    public const string UseTransaction = "Whether ReadWrite and ReadWriteAnonymized batches share a server-managed transaction. Defaults to true; protected read-only modes always roll back. Atomicity requires executed code to preserve that transaction, including procedures, dynamic SQL and triggers.";
}
