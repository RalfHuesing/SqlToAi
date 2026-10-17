using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Metadata;

/// <summary>
/// Provides metadata descriptions for tables and columns to enrich the schemas returned to AI agents.
/// </summary>
public interface IMetadataProvider
{
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
}
