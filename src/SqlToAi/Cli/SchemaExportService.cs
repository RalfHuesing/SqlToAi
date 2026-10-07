using System.Text;
using Microsoft.Extensions.Logging;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Cli;

internal sealed class SchemaExportService(ISchemaService schema, ILogger<SchemaExportService> logger)
{
    internal async Task<Result> ExportAsync(string database, string output, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(output))
            return Result.Failure(SqlToAiError.InvalidParameters("Database and output are required."));
        try
        {
            string root = Path.GetFullPath(output);
            if (File.Exists(root) || (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()))
                return Result.Failure(SqlToAiError.InvalidParameters("The output directory must be absent or empty."));
            var discovery = await schema.GetExportObjectsAsync(database, cancellationToken);
            if (discovery.IsFailure) return Result.Failure(discovery.Error);
            var objects = discovery.Value;
            var paths = SchemaExportPaths.Create(objects);
            Directory.CreateDirectory(root);
            foreach (SchemaObject item in objects)
            {
                var document = await ComposeAsync(database, item, paths, cancellationToken);
                if (document.IsFailure) return Result.Failure(document.Error);
                await WriteAsync(root, paths[item.Identity], document.Value, cancellationToken);
            }
            var overview = new StringBuilder().Append("# Database schema: ").AppendLine(OfflineSqlNameFormatter.Text(database)).AppendLine();
            foreach (var group in objects.GroupBy(item => SchemaExportPaths.DirectoryFor(item.Identity.Kind)))
            {
                overview.Append("## ").AppendLine(group.Key).AppendLine();
                foreach (SchemaObject item in group)
                    overview.Append("- ").AppendLine(SchemaRenderingContext.RelativeLink("README.md", paths[item.Identity], item.Identity.DisplayName));
                overview.AppendLine();
            }
            await WriteAsync(root, "README.md", overview.ToString(), cancellationToken);
            return Result.Success();
        }
        catch (ArgumentException exception)
        {
            return Result.Failure(SqlToAiError.InvalidParameters(exception.Message));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            logger.LogError(exception, "Schema export failed for {Database} to {Output}", database, output);
            return Result.Failure(SqlToAiError.InfrastructureError(exception.Message));
        }
    }

    private async Task<Result<string>> ComposeAsync(string database, SchemaObject item,
        IReadOnlyDictionary<SchemaObjectIdentity, string> paths, CancellationToken cancellationToken)
    {
        var identity = item.Identity;
        var context = new SchemaRenderingContext(identity, paths);
        if (identity.Kind == SchemaObjectKind.Trigger)
        {
            var definition = await schema.GetExportTriggerDefinitionAsync(database, item, cancellationToken);
            if (definition.IsFailure) return definition;
            if (item.Parent is null) return Result<string>.Failure(SqlToAiError.InvalidParameters("A trigger must identify its parent."));
            string parent = paths.TryGetValue(item.Parent, out string? parentPath)
                ? SchemaRenderingContext.RelativeLink(paths[identity], parentPath, item.Parent.DisplayName) : OfflineSqlNameFormatter.Text(item.Parent.DisplayName);
            return Result<string>.Success($"# Trigger: {OfflineSqlNameFormatter.Code(identity.DisplayName)}\n\nParent: {parent}\n\n{definition.Value}");
        }
        var primary = await schema.GetExportSchemaAsync(database, context, cancellationToken);
        if (primary.IsFailure) return primary;
        var sections = new StringBuilder(primary.Value);
        var operations = new List<Func<Task<Result<string>>>>();
        if (identity.Kind is SchemaObjectKind.Table or SchemaObjectKind.View)
        {
            operations.Add(() => schema.GetExportSchemaForeignKeysAsync(database, context, cancellationToken));
            operations.Add(() => schema.GetExportSchemaIndexesAsync(database, context, cancellationToken));
            operations.Add(() => schema.GetExportSchemaConstraintsAsync(database, context, cancellationToken));
            operations.Add(() => schema.GetExportObjectReferencesAsync(database, context, cancellationToken));
        }
        else operations.Add(() => schema.GetExportRoutineParametersAsync(database, context, cancellationToken));
        foreach (var operation in operations)
        {
            var result = await operation();
            if (result.IsFailure) return result;
            sections.AppendLine().AppendLine().Append(result.Value);
        }
        return Result<string>.Success(sections.ToString());
    }

    private static async Task WriteAsync(string root, string relative, string content, CancellationToken cancellationToken)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
    }
}
