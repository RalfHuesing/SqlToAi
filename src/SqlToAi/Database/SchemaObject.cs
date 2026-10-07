namespace SqlToAi.Database;

public enum SchemaObjectKind
{
    Table,
    View,
    Procedure,
    ScalarFunction,
    InlineTableValuedFunction,
    TableValuedFunction,
    Trigger
}

public sealed record SchemaObjectIdentity(int ObjectId, string SchemaName, string ObjectName, SchemaObjectKind Kind)
{
    public string DisplayName => $"{SchemaName}.{ObjectName}";

    public string QualifiedName => $"[{SchemaName.Replace("]", "]]", StringComparison.Ordinal)}].[{ObjectName.Replace("]", "]]", StringComparison.Ordinal)}]";
}

public sealed record SchemaObject(SchemaObjectIdentity Identity, SchemaObjectIdentity? Parent = null);
