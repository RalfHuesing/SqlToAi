using SqlToAi.Domain;

namespace SqlToAi.Database;

public interface IExplainQueryService
{
    Task<Result<ExplainQueryResult>> ExplainQueryAsync(
        string databaseName,
        string query,
        object? parameters = null,
        CancellationToken cancellationToken = default);

    Task<Result<ExplainQueryResult>> ExplainQueryAsync(
        DatabaseTargetContext target,
        string query,
        object? parameters = null,
        CancellationToken cancellationToken = default);
}
