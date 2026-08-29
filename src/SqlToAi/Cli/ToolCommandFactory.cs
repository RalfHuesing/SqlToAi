#nullable enable

using System.CommandLine;
using SqlToAi.Mcp;

namespace SqlToAi.Cli;

/// <summary>
/// Builds a `query` command tree from the MCP <see cref="ToolRegistry"/>, so every MCP tool
/// can also be invoked directly from the command line. Pure and DI-free: callers supply an
/// execution callback, so this class has no knowledge of <see cref="IToolDispatcher"/> or DI.
/// </summary>
internal static class ToolCommandFactory
{
    /// <summary>
    /// Builds the top-level <c>query</c> command with one subcommand per tool in <paramref name="tools"/>.
    /// </summary>
    /// <param name="tools">The tool definitions to expose, typically <see cref="ToolRegistry.GetAll"/>.</param>
    /// <param name="execute">Invoked with the tool name and parsed arguments when a tool subcommand runs.</param>
    internal static Command BuildQueryCommand(
        IReadOnlyList<ToolDefinition> tools,
        Func<string, Dictionary<string, object?>, CancellationToken, Task<int>> execute)
    {
        var query = new Command("query", "Invokes a single MCP tool directly from the command line, bypassing the MCP protocol.");

        foreach (ToolDefinition tool in tools)
        {
            query.Add(BuildToolCommand(tool, execute));
        }

        return query;
    }

    private static Command BuildToolCommand(
        ToolDefinition tool,
        Func<string, Dictionary<string, object?>, CancellationToken, Task<int>> execute)
    {
        var command = new Command(tool.Name, tool.Description);
        var valueReaders = new List<(string ArgKey, Func<ParseResult, object?> Read)>();

        foreach (KeyValuePair<string, ToolParameterDefinition> property in tool.InputSchema.Properties)
        {
            bool required = tool.InputSchema.Required.Contains(property.Key);
            valueReaders.Add(AddOption(command, property.Key, property.Value, required));
        }

        Func<ParseResult, CancellationToken, Task<int>> action = async (parseResult, cancellationToken) =>
        {
            var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach ((string argKey, Func<ParseResult, object?> read) in valueReaders)
            {
                object? value = read(parseResult);
                if (value != null)
                {
                    arguments[argKey] = value;
                }
            }

            return await execute(tool.Name, arguments, cancellationToken);
        };
        command.SetAction(action);

        return command;
    }

    /// <summary>Adds an option for one tool parameter and returns a reader that extracts its parsed value.</summary>
    private static (string ArgKey, Func<ParseResult, object?> Read) AddOption(
        Command command,
        string argKey,
        ToolParameterDefinition parameterDefinition,
        bool required)
    {
        string optionName = "--" + argKey;

        if (string.Equals(parameterDefinition.Type, "boolean", StringComparison.OrdinalIgnoreCase))
        {
            var option = new Option<bool?>(optionName)
            {
                Description = parameterDefinition.Description,
                Required = required
            };
            command.Add(option);
            return (argKey, parseResult => parseResult.GetValue(option));
        }

        if (string.Equals(parameterDefinition.Type, "integer", StringComparison.OrdinalIgnoreCase))
        {
            var option = new Option<int?>(optionName)
            {
                Description = parameterDefinition.Description,
                Required = required
            };
            command.Add(option);
            return (argKey, parseResult => parseResult.GetValue(option));
        }

        var stringOption = new Option<string>(optionName)
        {
            Description = parameterDefinition.Description,
            Required = required
        };
        command.Add(stringOption);
        return (argKey, parseResult => parseResult.GetValue(stringOption));
    }
}
