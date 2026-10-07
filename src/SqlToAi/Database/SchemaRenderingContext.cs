using System.Collections.Frozen;

namespace SqlToAi.Database;

public sealed class SchemaRenderingContext
{
    private readonly FrozenDictionary<SchemaObjectIdentity, string> _paths;
    private readonly FrozenDictionary<(string Schema, string Name), SchemaObjectIdentity> _objects;
    private readonly FrozenDictionary<int, SchemaObjectIdentity> _triggers;

    public SchemaRenderingContext(SchemaObjectIdentity source, IReadOnlyDictionary<SchemaObjectIdentity, string> paths)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(paths);
        Source = source;
        // Copy the coordinator's mapping so later calls cannot change this call's presentation.
        _paths = paths.ToFrozenDictionary();
        _objects = _paths.Keys.ToFrozenDictionary(identity => (identity.SchemaName, identity.ObjectName));
        _triggers = _paths.Keys.Where(identity => identity.Kind == SchemaObjectKind.Trigger).ToFrozenDictionary(identity => identity.ObjectId);
        if (!_paths.ContainsKey(source)) throw new ArgumentException("The source object must have a document path.", nameof(paths));
    }

    public SchemaObjectIdentity Source { get; }

    internal string ObjectLink(string schemaName, string objectName, string label)
        => _objects.TryGetValue((schemaName, objectName), out var identity) ? ObjectLink(identity, label) : OfflineSqlNameFormatter.Text(label);

    internal string ObjectLink(SchemaObjectIdentity identity, string label)
        => _paths.TryGetValue(identity, out string? target) ? RelativeLink(_paths[Source], target, label) : OfflineSqlNameFormatter.Text(label);

    internal string TriggerLink(int objectId, string label)
        => _triggers.TryGetValue(objectId, out var identity) ? ObjectLink(identity, label) : OfflineSqlNameFormatter.Text(label);

    public static string RelativeLink(string sourcePath, string targetPath, string label)
    {
        string? sourceDirectory = Path.GetDirectoryName(sourcePath);
        string relative = Path.GetRelativePath(string.IsNullOrEmpty(sourceDirectory) ? "." : sourceDirectory, targetPath);
        string url = string.Join('/', relative.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
        string text = OfflineSqlNameFormatter.Text(label);
        return $"[{text}]({url})";
    }
}
