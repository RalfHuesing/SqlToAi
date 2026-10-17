namespace SqlToAi.Domain;

/// <summary>
/// Identifies one configured server alias and its exact raw SQL database name.
/// </summary>
public sealed record DatabaseTargetContext
{
    internal DatabaseTargetContext(string serverAlias, string databaseName)
    {
        ServerAlias = serverAlias;
        DatabaseName = databaseName;
    }

    public string ServerAlias { get; }

    public string DatabaseName { get; }

    public string QualifiedName => $"{ServerAlias}/{DatabaseName}";

    /// <summary>Compares targets using the access-policy alias and database-name rules.</summary>
    public bool HasSamePolicyIdentity(DatabaseTargetContext other) =>
        string.Equals(ServerAlias, other.ServerAlias, StringComparison.Ordinal)
        && StringComparer.OrdinalIgnoreCase.Equals(DatabaseName, other.DatabaseName);
}
