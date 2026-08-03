#nullable enable

using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Validates a SQL query syntactically and semantically (via PARSEONLY) without executing it.
/// </summary>
public interface IQueryValidationService
{
    /// <summary>
    /// Validates a SQL query against the specified database using <c>SET PARSEONLY ON</c>.
    /// </summary>
    /// <param name="databaseName">The target database name.</param>
    /// <param name="query">The SQL query to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> containing a success message on valid syntax,
    /// or a structured error on failure.
    /// </returns>
    Task<Result<string>> ValidateQueryAsync(
        string databaseName,
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a SQL query against the specified database with optional parameter binding using <c>SET PARSEONLY ON</c>.
    /// </summary>
    /// <param name="databaseName">The target database name.</param>
    /// <param name="query">The SQL query to validate.</param>
    /// <param name="parameters">Optional parameter container.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> containing a success message on valid syntax,
    /// or a structured error on failure.
    /// </returns>
    Task<Result<string>> ValidateQueryAsync(
        string databaseName,
        string query,
        object? parameters,
        CancellationToken cancellationToken = default);
}
