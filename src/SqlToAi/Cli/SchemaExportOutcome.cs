using SqlToAi.Domain;

namespace SqlToAi.Cli;

internal enum SchemaExportScope { Selected, All }

internal enum SchemaExportStatus { Completed, Failed, Cancelled }

internal sealed record SchemaExportOutcome(
    SchemaExportStatus Status, string? OutputPath, bool PartialOutput, SqlToAiError? Error = null, string? Reason = null);
