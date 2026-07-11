#nullable enable

namespace SqlToAi.Metadata;

/// <summary>
/// Provides metadata descriptions for tables and columns to enrich the schemas returned to AI agents.
/// </summary>
public interface IMetadataProvider
{
    /// <summary>
    /// Retrieves the description for a table.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="tableName">The name of the table.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The table description, or null if not found.</returns>
    Task<string?> GetTableDescriptionAsync(string databaseName, string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a dictionary mapping column names to their descriptions for a specific table.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="tableName">The name of the table.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary of column names to descriptions.</returns>
    Task<IReadOnlyDictionary<string, string>> GetColumnDescriptionsAsync(string databaseName, string tableName, CancellationToken cancellationToken = default);
}
