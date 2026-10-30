using SqlToAi.Domain;

namespace SqlToAi.Database;

public interface IShowplanExecutionService
{
    Task<Result<string>> ExecuteShowplanAsync(
        string databaseName,
        string query,
        object? parameters = null,
        CancellationToken cancellationToken = default);

    Task<Result<string>> ExecuteShowplanAsync(
        DatabaseTargetContext target,
        string query,
        object? parameters = null,
        CancellationToken cancellationToken = default);
}
