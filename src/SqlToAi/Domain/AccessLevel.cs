#nullable enable

namespace SqlToAi.Domain;

#pragma warning disable CA1069 // Enums should not have duplicate values

/// <summary>
/// Defines the database access levels allowed for a client connection.
/// </summary>
public enum AccessLevel
{
    /// <summary>
    /// All access to the database is blocked.
    /// </summary>
    None = 0,

    /// <summary>
    /// Only database schema queries are allowed. Data queries are blocked.
    /// </summary>
    SchemaOnly = 1,

    /// <summary>
    /// Read-only access to schemas and data queries is allowed.
    /// </summary>
    ReadOnly = 2,

    /// <summary>
    /// Alias for ReadOnly.
    /// </summary>
    ReadData = 2,

    /// <summary>
    /// Full access is allowed (schema, read, and write operations).
    /// </summary>
    ReadWrite = 3
}
