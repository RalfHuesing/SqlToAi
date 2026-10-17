namespace SqlToAi.Domain;

/// <summary>A syntactically valid qualified target before its server alias is resolved.</summary>
public sealed record ParsedDatabaseTarget
{
    internal ParsedDatabaseTarget(string serverAlias, string databaseName)
    {
        ServerAlias = serverAlias;
        DatabaseName = databaseName;
    }

    public string ServerAlias { get; }

    public string DatabaseName { get; }
}
