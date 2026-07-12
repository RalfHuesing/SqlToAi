#nullable enable

namespace SqlToAi.Domain;

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
    /// Read-only access to schemas and data queries is allowed, but string data is anonymized.
    /// </summary>
    ReadOnlyAnonymized = 2,

    /// <summary>
    /// Read-only access to schemas and clear-text data queries is allowed (no anonymization).
    /// </summary>
    ReadOnly = 3,

    /// <summary>
    /// Full access is allowed (schema, read, and write operations).
    /// </summary>
    ReadWrite = 4
}
