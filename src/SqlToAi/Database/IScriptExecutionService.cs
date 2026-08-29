#nullable enable

using SqlToAi.Domain;

namespace SqlToAi.Database;

internal interface IScriptExecutionService
{
    Task<ScriptExecutionReport> ExecuteAsync(
        ScriptExecutionRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed record ScriptExecutionRequest(
    SqlScriptFile ScriptFile,
    string DatabaseName,
    int? RequestedRowLimit = null,
    object? Parameters = null,
    bool UseTransaction = true);
