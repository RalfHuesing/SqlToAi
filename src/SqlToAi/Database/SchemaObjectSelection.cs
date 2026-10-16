using System.Data.Common;
using System.IO;
using System.Text.Json;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlToAi.Domain;

namespace SqlToAi.Database;

internal sealed record SchemaObjectSelectionEntry(string ObjectName, Result<SchemaObject> Object);

internal static class SchemaObjectSelection
{
    internal static Result<IReadOnlyList<string>> Normalize(
        JsonElement objectName = default, JsonElement objectNames = default, bool requireNames = true)
    {
        var names = new List<string>();
        var scalar = AppendName(objectName, "object_name", names);
        if (scalar.IsFailure)
        {
            return scalar.Error;
        }

        if (objectNames.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            if (objectNames.ValueKind != JsonValueKind.Array)
            {
                return SqlToAiError.InvalidParameters("object_names must be an array of strings or null.");
            }
            foreach (var name in objectNames.EnumerateArray())
            {
                var appended = AppendName(name, "object_names entry", names);
                if (appended.IsFailure)
                {
                    return appended.Error;
                }
            }
        }

        return requireNames && names.Count == 0
            ? SqlToAiError.InvalidParameters("At least one non-empty object name is required.")
            : Result<IReadOnlyList<string>>.Success(names.ToArray());
    }

    private static Result AppendName(JsonElement value, string parameter, List<string> names)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return Result.Success();
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            return SqlToAiError.InvalidParameters($"{parameter} must be a string or null.");
        }
        var name = value.GetString()!.Trim();
        if (name.Length > 0)
        {
            names.Add(name);
        }
        return Result.Success();
    }

    internal static async Task<IReadOnlyList<SchemaObjectSelectionEntry>> ResolveAsync(
        DbConnection connection, IReadOnlyList<string> names, bool forExport, CancellationToken cancellationToken)
    {
        var results = new List<SchemaObjectSelectionEntry>(names.Count);
        var byName = new Dictionary<string, Result<SchemaObject>>(StringComparer.Ordinal);
        var byIdentifier = new Dictionary<SqlObjectName, Result<SchemaObject>>();
        var byId = new Dictionary<int, SchemaObject>();
        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!byName.TryGetValue(name, out var result))
            {
                var parsed = ParseName(name);
                if (parsed.IsFailure)
                {
                    result = parsed.Error;
                }
                else
                {
                    if (!byIdentifier.TryGetValue(parsed.Value, out result))
                    {
                        result = await ResolveNameAsync(connection, parsed.Value, forExport, cancellationToken);
                        byIdentifier.Add(parsed.Value, result);
                    }
                    // Errors identify the original input, even when quoted aliases reuse a lookup.
                    if (result.IsFailure)
                    {
                        result = result.Error.Code == SqlToAiError.ObjectNotFoundCode
                            ? SqlToAiError.ObjectNotFound(name)
                            : result.Error with { Message = $"Object '{name}': {result.Error.Message}" };
                    }
                    else if (byId.TryGetValue(result.Value.Identity.ObjectId, out var existing))
                    {
                        result = existing;
                    }
                    else
                    {
                        byId.Add(result.Value.Identity.ObjectId, result.Value);
                    }
                }
                byName.Add(name, result);
            }
            results.Add(new SchemaObjectSelectionEntry(name, result));
        }
        return results.ToArray();
    }

    private static async Task<Result<SchemaObject>> ResolveNameAsync(
        DbConnection connection, SqlObjectName name, bool forExport, CancellationToken cancellationToken)
    {
        // Compare catalog names on SQL Server: case, accents and other identifier rules belong to the database.
        var rows = (await SchemaObjectDiscovery.FindAsync(connection, name.Schema, name.Name, cancellationToken)).ToArray();
        if (rows.Length == 0)
        {
            return SqlToAiError.ObjectNotFound(name.Name);
        }
        if (rows.Length > 1)
        {
            return SqlToAiError.InvalidParameters("The object name is ambiguous; qualify it with its SQL schema.");
        }
        if (!rows[0].IsSupported(forExport))
        {
            return SqlToAiError.InvalidParameters("This object type is not supported for the requested selection.");
        }
        return rows[0].ToSchemaObject();
    }

    private static Result<SqlObjectName> ParseName(string name)
    {
        using var reader = new StringReader(name);
        var parsed = SqlScriptDomParser.CreateParser().ParseSchemaObjectName(reader, out var errors);
        if (errors.Count != 0 || parsed is null || parsed.Identifiers.Count is < 1 or > 2
            || parsed.ScriptTokenStream.Any(token => token.TokenType is TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment)
            || parsed.Identifiers.Any(identifier => string.IsNullOrEmpty(identifier.Value) || identifier.Value.Length > 128))
        {
            return SqlToAiError.InvalidParameters($"Object '{name}' must be an unqualified or SQL-schema-qualified identifier in the selected database.");
        }
        return new SqlObjectName(parsed.Identifiers.Count == 2 ? parsed.Identifiers[0].Value : null, parsed.BaseIdentifier.Value);
    }

    private sealed record SqlObjectName(string? Schema, string Name);
}
