using System.Buffers;
using System.Text;
using System.Text.Json;
using SqlToAi.Configuration;
using SqlToAi.Domain;

namespace SqlToAi.Database;

public sealed record SchemaBatchResponse(string Json, bool Complete);

internal static class SchemaBatchComposer
{
    internal static Result ValidateRequest(IReadOnlyList<string> names, SchemaBatchOptions options)
    {
        if (options.MaxInputCount <= 0 || options.MaxResponseBytes <= 0)
            return SqlToAiError.InvalidParameters("SchemaBatch limits must be greater than zero.");
        if (names.Count == 0 || names.Count > options.MaxInputCount)
            return SqlToAiError.InvalidParameters($"Schema batches require 1 to {options.MaxInputCount} non-empty input entries before deduplication.");
        var metadata = names.Select(Entry.Omitted).ToArray();
        return Serialize(metadata).Length <= options.MaxResponseBytes
            ? Result.Success()
            : MetadataLimit();
    }

    internal static async Task<Result<SchemaBatchResponse>> ComposeAsync(
        IReadOnlyList<SchemaObjectSelectionEntry> selection, int maxResponseBytes,
        Func<SchemaObject, CancellationToken, Task<Result<string>>> fetch, CancellationToken cancellationToken)
    {
        // Keep omission metadata for every pending input in every candidate envelope.
        var entries = selection.Select(item => Entry.Omitted(item.ObjectName)).ToArray();
        if (Serialize(entries).Length > maxResponseBytes)
            return MetadataLimit();

        var byId = new Dictionary<int, Result<string>>();
        for (int index = 0; index < selection.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = selection[index];
            Result<string> schema;
            if (item.Object.IsFailure)
            {
                schema = item.Object.Error;
            }
            else if (!byId.TryGetValue(item.Object.Value.Identity.ObjectId, out schema!))
            {
                schema = await fetch(item.Object.Value, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                byId.Add(item.Object.Value.Identity.ObjectId, schema);
            }
            entries[index] = schema.IsSuccess
                ? new Entry(item.ObjectName, "ok", Markdown: schema.Value)
                : new Entry(item.ObjectName, "error", Error: schema.Error);

            if (Serialize(entries).Length <= maxResponseBytes) continue;

            for (int remaining = index; remaining < entries.Length; remaining++)
                entries[remaining] = Entry.Omitted(entries[remaining].ObjectName);
            break;
        }

        byte[] json = Serialize(entries);
        return new SchemaBatchResponse(Encoding.UTF8.GetString(json), entries.All(entry => entry.Status == "ok"));
    }

    private static SqlToAiError MetadataLimit() => SqlToAiError.InvalidParameters(
        "Schema batch identifying metadata exceed SchemaBatch.MaxResponseBytes; request fewer or shorter object names, or increase the limit.");

    private static byte[] Serialize(IReadOnlyList<Entry> entries)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("complete", entries.All(entry => entry.Status == "ok"));
            writer.WriteStartArray("results");
            foreach (var entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("object_name", entry.ObjectName);
                writer.WriteString("status", entry.Status);
                if (entry.Markdown is not null) writer.WriteString("markdown", entry.Markdown);
                if (entry.Error is not null)
                {
                    writer.WriteStartObject("error");
                    writer.WriteString("code", entry.Error.Code);
                    writer.WriteString("message", entry.Error.Message);
                    writer.WriteEndObject();
                }
                if (entry.Reason is not null) writer.WriteString("reason", entry.Reason);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    private sealed record Entry(string ObjectName, string Status, string? Markdown = null, SqlToAiError? Error = null, string? Reason = null)
    {
        internal static Entry Omitted(string name) => new(name, "not_returned", Reason: "response_limit");
    }
}
