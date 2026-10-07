using System.Globalization;
using System.Text.Json;
using SqlToAi.Mcp;

namespace SqlToAi.Exploration;

internal sealed class ExplorationContext(
    IToolDispatcher dispatcher, string repositoryRoot, string outputDirectory, CancellationToken cancellationToken)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private int _callNumber;

    internal string RepositoryRoot { get; } = repositoryRoot;
    internal string OutputDirectory { get; } = outputDirectory;
    internal CancellationToken CancellationToken { get; } = cancellationToken;

    internal async Task<ToolCallResult?> CallAsync(string toolName, object parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        // Keep arbitrary tool names in the request, never in filesystem paths.
        var directory = Path.Combine(OutputDirectory, (++_callNumber).ToString("D2", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        var arguments = JsonSerializer.SerializeToElement(parameters, JsonOptions).EnumerateObject()
            .ToDictionary(property => property.Name, property => (object?)property.Value.Clone(), StringComparer.Ordinal);
        var request = new ToolCallParams { Name = toolName, Arguments = arguments };
        await WriteJsonAsync(Path.Combine(directory, "request.json"), request).ConfigureAwait(false);
        ToolCallResult result;
        try
        {
            CancellationToken.ThrowIfCancellationRequested();
            result = await dispatcher.DispatchAsync(request, CancellationToken).ConfigureAwait(false);
            CancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "error.txt"), exception.ToString(), CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"Call {_callNumber:D2}: {toolName} threw; inspect error.txt.");
            return null;
        }

        await WriteJsonAsync(Path.Combine(directory, "response.json"), result).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(directory, "response.txt"),
            string.Join(Environment.NewLine, result.Content.Select(block => block.Text)), CancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Call {_callNumber:D2}: {toolName}, isError={result.IsError}.");
        return result;
    }

    internal Task WriteJsonAsync<T>(string path, T value) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), CancellationToken);
}
