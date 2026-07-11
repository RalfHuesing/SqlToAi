#nullable enable

using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Service for querying SQL Server database lists, objects, and schemas, rendering them as Markdown for AI consumption.
/// </summary>
public interface ISchemaService
{
    /// <summary>
    /// Lists all allowed databases on the SQL Server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of database names.</returns>
    Task<Result<IReadOnlyList<string>>> ListDatabasesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists allowed databases matching a search term.
    /// </summary>
    /// <param name="searchTerm">The term to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of matching database names.</returns>
    Task<Result<IReadOnlyList<string>>> SearchDatabasesAsync(string searchTerm, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for objects (tables, views, procedures, triggers, functions) in the target database.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="searchTerm">The term to search for in object names.</param>
    /// <param name="maxResults">Optional maximum results limit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A Markdown string listing the matched objects.</returns>
    Task<Result<string>> SearchObjectsAsync(string databaseName, string searchTerm, int? maxResults = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the primary schema for a table, view, stored procedure, or function.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="objectName">The object name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A Markdown representation of the object's schema.</returns>
    Task<Result<string>> GetSchemaAsync(string databaseName, string objectName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves outgoing and incoming foreign keys for a table.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A Markdown table listing the foreign keys.</returns>
    Task<Result<string>> GetSchemaForeignKeysAsync(string databaseName, string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves indexes for a table.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A Markdown table listing the indexes.</returns>
    Task<Result<string>> GetSchemaIndexesAsync(string databaseName, string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves default and check constraints for a table.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A Markdown table listing the constraints.</returns>
    Task<Result<string>> GetSchemaConstraintsAsync(string databaseName, string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the DDL definition of a specific trigger.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="tableName">The parent table name.</param>
    /// <param name="triggerName">The trigger name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A Markdown code block containing the trigger definition SQL.</returns>
    Task<Result<string>> GetTriggerDefinitionAsync(string databaseName, string tableName, string triggerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves static database references using sys.dm_sql_referencing_entities.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="objectName">The object name (table/view).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A Markdown list of referencing entities.</returns>
    Task<Result<string>> GetObjectReferencesAsync(string databaseName, string objectName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves parameters and return types for a stored procedure or function.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="routineName">The procedure or function name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A Markdown table of parameters.</returns>
    Task<Result<string>> GetRoutineParametersAsync(string databaseName, string routineName, CancellationToken cancellationToken = default);
}
