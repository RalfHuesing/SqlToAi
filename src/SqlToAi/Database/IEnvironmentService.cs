using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Service for querying SQL Server and database environment metadata.
/// </summary>
public interface IEnvironmentService
{
    Task<Result<DatabaseEnvironmentResult>> GetEnvironmentAsync(string databaseName, CancellationToken cancellationToken = default);

    Task<Result<DatabaseEnvironmentResult>> GetEnvironmentAsync(DatabaseTargetContext target, CancellationToken cancellationToken = default);
}
