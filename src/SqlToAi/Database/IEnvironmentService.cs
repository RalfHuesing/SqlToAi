using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Service for querying SQL Server and database environment metadata.
/// </summary>
public interface IEnvironmentService
{
    /// <summary>
    /// Retrieves environment metadata for the specified qualified database target.
    /// </summary>
    /// <param name="databaseName">The qualified database target (e.g., 'alias/database').</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The environment metadata result or an error.</returns>
    Task<Result<DatabaseEnvironmentResult>> GetEnvironmentAsync(string databaseName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves environment metadata for the specified database target context.
    /// </summary>
    /// <param name="target">The resolved database target context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The environment metadata result or an error.</returns>
    Task<Result<DatabaseEnvironmentResult>> GetEnvironmentAsync(DatabaseTargetContext target, CancellationToken cancellationToken = default);
}
