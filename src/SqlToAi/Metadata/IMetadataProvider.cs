using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Metadata;

/// <summary>
/// Provides metadata descriptions for tables and columns to enrich the schemas returned to AI agents.
/// </summary>
public interface IMetadataProvider
{
    Task<string?> GetTableDescriptionAsync(string databaseName, SchemaObjectIdentity identity, CancellationToken cancellationToken = default)
        => GetTableDescriptionAsync(databaseName, identity.DisplayName, cancellationToken);

    Task<IReadOnlyDictionary<string, string>> GetColumnDescriptionsAsync(string databaseName, SchemaObjectIdentity identity, CancellationToken cancellationToken = default)
        => GetColumnDescriptionsAsync(databaseName, identity.DisplayName, cancellationToken);

    /// <summary>Retrieves the table description using the target's owning server provider settings.</summary>
    Task<string?> GetTableDescriptionAsync(DatabaseTargetContext target, SchemaObjectIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>Retrieves the column descriptions using the target's owning server provider settings.</summary>
    Task<IReadOnlyDictionary<string, string>> GetColumnDescriptionsAsync(DatabaseTargetContext target, SchemaObjectIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>Retrieves the table description using the target's owning server provider settings.</summary>
    Task<string?> GetTableDescriptionAsync(DatabaseTargetContext target, string tableName, CancellationToken cancellationToken = default)
        => GetTableDescriptionAsync(target, new SchemaObjectIdentity(0, "dbo", tableName, SchemaObjectKind.Table), cancellationToken);

    /// <summary>Retrieves the column descriptions using the target's owning server provider settings.</summary>
    Task<IReadOnlyDictionary<string, string>> GetColumnDescriptionsAsync(DatabaseTargetContext target, string tableName, CancellationToken cancellationToken = default)
        => GetColumnDescriptionsAsync(target, new SchemaObjectIdentity(0, "dbo", tableName, SchemaObjectKind.Table), cancellationToken);

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
