using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Cli;

internal sealed class SchemaExportService(ISchemaService schema, ILogger<SchemaExportService> logger)
{
    private readonly Func<string, Stream> _openFile = path =>
        new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);

    internal SchemaExportService(ISchemaService schema, ILogger<SchemaExportService> logger, Func<string, Stream> openFile)
        : this(schema, logger) => _openFile = openFile;

    internal async Task<Result> ExportAsync(string database, string output, CancellationToken cancellationToken = default)
    {
        var outcome = await ExportAsync(database, output, SchemaExportScope.All, cancellationToken: cancellationToken);
        if (outcome.Status == SchemaExportStatus.Cancelled)
            throw new OperationCanceledException("Schema export was cancelled.", cancellationToken);
        return outcome.Status == SchemaExportStatus.Completed ? Result.Success() : Result.Failure(outcome.Error!);
    }

    internal async Task<SchemaExportOutcome> ExportAsync(string database, string output, SchemaExportScope scope,
        JsonElement objectName = default, JsonElement objectNames = default, CancellationToken cancellationToken = default)
    {
        string? root = null;
        bool createdOutput = false;
        SchemaExportOutcome Failure(SqlToAiError error) =>
            new(SchemaExportStatus.Failed, root, HasPartialOutput(root, createdOutput), error);
        try
        {
            if (string.IsNullOrWhiteSpace(output))
                return Failure(SqlToAiError.InvalidParameters("Database and output are required."));
            root = Path.GetFullPath(output);
            if (string.IsNullOrWhiteSpace(database))
                return Failure(SqlToAiError.InvalidParameters("Database and output are required."));
            if (scope is not (SchemaExportScope.Selected or SchemaExportScope.All))
                return Failure(SqlToAiError.InvalidParameters("Scope must be selected or all."));
            var names = SchemaObjectSelection.Normalize(objectName, objectNames, requireNames: scope == SchemaExportScope.Selected);
            if (names.IsFailure) return Failure(names.Error);
            if (scope == SchemaExportScope.All && names.Value.Count > 0)
                return Failure(SqlToAiError.InvalidParameters("Full export does not accept an object selection."));
            if (File.Exists(root) || (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()))
                return Failure(SqlToAiError.InvalidParameters("The output directory must be absent or empty."));
            cancellationToken.ThrowIfCancellationRequested();
            var discovery = scope == SchemaExportScope.All
                ? await schema.GetExportObjectsAsync(database, cancellationToken)
                : await schema.GetSelectedExportObjectsAsync(database, names.Value, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (discovery.IsFailure) return Failure(discovery.Error);
            var objects = discovery.Value;
            var paths = SchemaExportPaths.Create(objects);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(root);
            foreach (SchemaObject item in objects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var document = await ComposeAsync(database, item, paths, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (document.IsFailure) return Failure(document.Error);
                await WriteAsync(root, paths[item.Identity], document.Value, () => createdOutput = true, cancellationToken);
            }
            var overview = new StringBuilder().Append("# Database schema: ").AppendLine(OfflineSqlNameFormatter.Text(database)).AppendLine();
            foreach (var group in objects.GroupBy(item => SchemaExportPaths.DirectoryFor(item.Identity.Kind)))
            {
                overview.Append("## ").AppendLine(group.Key).AppendLine();
                foreach (SchemaObject item in group)
                    overview.Append("- ").AppendLine(SchemaRenderingContext.RelativeLink("README.md", paths[item.Identity], item.Identity.DisplayName));
                overview.AppendLine();
            }
            await WriteAsync(root, "README.md", overview.ToString(), () => createdOutput = true, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new(SchemaExportStatus.Completed, root, PartialOutput: false);
        }
        catch (OperationCanceledException)
        {
            return new(SchemaExportStatus.Cancelled, root, HasPartialOutput(root, createdOutput), Reason: "cancelled");
        }
        catch (ArgumentException exception)
        {
            return Failure(SqlToAiError.InvalidParameters(exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Schema export failed for {Database} to {Output}", database, output);
            return Failure(SqlToAiError.InfrastructureError(exception.Message));
        }
    }

    private static bool HasPartialOutput(string? root, bool createdOutput)
    {
        try
        {
            return root is not null && Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Preserve evidence of created entries if a later filesystem failure prevents inspection.
            return createdOutput;
        }
    }

    private async Task<Result<string>> ComposeAsync(string database, SchemaObject item,
        IReadOnlyDictionary<SchemaObjectIdentity, string> paths, CancellationToken cancellationToken)
    {
        var identity = item.Identity;
        cancellationToken.ThrowIfCancellationRequested();
        var context = new SchemaRenderingContext(identity, paths);
        if (identity.Kind == SchemaObjectKind.Trigger)
        {
            var definition = await schema.GetExportTriggerDefinitionAsync(database, item, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (definition.IsFailure) return definition;
            if (item.Parent is null) return Result<string>.Failure(SqlToAiError.InvalidParameters("A trigger must identify its parent."));
            string parent = paths.TryGetValue(item.Parent, out string? parentPath)
                ? SchemaRenderingContext.RelativeLink(paths[identity], parentPath, item.Parent.DisplayName) : OfflineSqlNameFormatter.Text(item.Parent.DisplayName);
            return Result<string>.Success($"# Trigger: {OfflineSqlNameFormatter.Code(identity.DisplayName)}\n\nParent: {parent}\n\n{definition.Value}");
        }
        var primary = await schema.GetExportSchemaAsync(database, context, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
            var result = await operation();
            cancellationToken.ThrowIfCancellationRequested();
            if (result.IsFailure) return result;
            sections.AppendLine().AppendLine().Append(result.Value);
        }
        return Result<string>.Success(sections.ToString());
    }

    private async Task WriteAsync(string root, string relative, string content, Action outputCreated, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = Path.Combine(root, relative);
        string parent = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(parent);
        if (!string.Equals(parent, root, StringComparison.OrdinalIgnoreCase)) outputCreated();
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = _openFile(path);
        outputCreated();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }
}
