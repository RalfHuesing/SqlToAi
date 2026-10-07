using System.CommandLine;
using SqlToAi.Domain;

namespace SqlToAi.Cli;

internal static class SchemaExportCommand
{
    internal static Command Build(Func<string, string, CancellationToken, Task<Result>> export)
    {
        var command = new Command("export-schema", "Exports database schema as linked Markdown documents.");
        var database = new Option<string>("--database") { Required = true };
        var output = new Option<string>("--output") { Required = true };
        command.Add(database);
        command.Add(output);
        command.SetAction(async (parse, cancellationToken) =>
        {
            var result = await export(parse.GetValue(database)!, parse.GetValue(output)!, cancellationToken);
            if (result.IsSuccess) return 0;
            await Console.Error.WriteLineAsync($"{result.Error.Code}: {result.Error.Message}");
            return 1;
        });
        return command;
    }
}
