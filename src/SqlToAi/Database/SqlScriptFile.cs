#nullable enable

namespace SqlToAi.Database;

internal sealed record SqlScriptFile(string ResolvedPath, string Text, string EncodingName);
