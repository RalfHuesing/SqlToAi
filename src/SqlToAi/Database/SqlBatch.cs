#nullable enable

namespace SqlToAi.Database;

internal sealed record SqlBatch(string Text, int StartLine, int EndLine, int RepeatCount = 1);
